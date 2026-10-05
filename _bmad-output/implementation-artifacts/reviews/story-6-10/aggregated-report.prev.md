# Review report — Story 6.10

Range : ae08655^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-10-renvoi-of-existant-remplacement-ou-refus.md
Date : 2026-10-05
Verdict : REFUSÉ (D-1 tranché → P-9 ; P-1..P-3, P-5..P-9 appliqués le 2026-10-05, P-4 à la clôture, voir §7)
Findings : D=1 P=8 F=2 R=27  (Decision, Patch, Defer, Rejetés) — D-1 tranché 2026-10-05 → P-9

## 1. Verdict

**REFUSÉ.** Trois raisons :

- une Decision reste ouverte (D-1) ;
- deux Patch de gravité medium restent ouverts : P-1, la suppression des lignes obsolètes au remplacement n'est pas testée, et P-8, le critère de mutation de la spec n'a aucune preuve ;
- un écart CC-1 : l'attestation du commit omet AC-FR21-2 (P-4).

Le cœur D34 est conforme :

- la branche est placée après D22 et le contrôle de Coulée froide, avant A-5/B-5/C-4 ;
- le message de refus a la forme demandée (`Block.File`, `BusinessRuleViolation`, une entrée REJETÉ) ;
- le remplacement se fait en un seul `SaveChanges()` ;
- les 3 tables de précondition ne sont que lues ;
- la production n'est lue que par `SELECT` avec `ApplicationIntent=ReadOnly` ;
- aucun *Never* n'est exécuté, aucun AD n'est violé ;
- l'écart avec le bloc gelé (fusion EF en UPDATE au lieu d'un « deleted ») est couvert par l'entrée datée du Spec Change Log (2026-10-05).

| Gravité | Nombre | IDs |
|---|---|---|
| high | 0 | — |
| medium | 3 | D-1, P-1, P-8 |
| low | 6 | P-2..P-7 |

## 2. Decision (à trancher par l'humain)

### D-1 — Le remplacement par UPDATE garde les colonnes non mappées de l'ancien OF (medium) — à trancher par l'humain

- **Localisation :** `src/Kape22Importer/Persistence/Kape22Persister.cs:214-222`, `RemoveDownstreamRows` (`:347-358`).
- **Sources :** edge-case-hunter.
- **Constat :** EF fusionne la paire Deleted + Added de même clé en un UPDATE.
  - Cela vaut pour `L_D_ORDRE_FABRICATION`, et aussi pour chaque ligne `L_D_SECTIONCHARGE_*` / `L_D_CONSIGNES` que le nouveau Fichier reproduit à l'identique.
  - Cet UPDATE n'écrit que les colonnes mappées par l'entité.
  - Toute colonne de production absente de l'entité (database-first minimal) garde donc la valeur de l'ancien OF.
  - Le legacy `DeleteOF` + insert la remettait à sa valeur par défaut.
- **Ce qui manque :** personne n'a encore vérifié que de telles colonnes existent. `SchemaModelParityTests` compare l'entité au miroir, pas à la production.
- **Options :**
  - (a) vérifier tout de suite `sys.columns` en production contre les 9 entités, puis patcher si un écart a un sens métier ;
  - (b) accepter et documenter, puisque les colonnes non mappées sont hors du périmètre P60 ;
  - (c) différer, en le liant à D-2 (le miroir n'a pas les FK de production).
- **Tranché le 2026-10-05 par l'utilisateur :** « Il faut conserver le legacy DeleteOF + insert », avec la portée **DeleteOF complet**. D-1 devient donc **P-9** (§3).

### P-9 — Remplacement par un vrai DeleteOF legacy complet + insert (medium, issu de D-1)

- **Localisation :** `src/Kape22Importer/Persistence/Kape22Persister.cs:211-222`, `:344-358`. Référence legacy : `Desktop/kape22/OrdreFabricationController.cs:577-864` (`DeleteOF`).
- **Décision :** remplacer la fusion EF en UPDATE par un vrai DELETE suivi d'un INSERT, avec la même portée que `DeleteOF`. Les colonnes non mappées reviennent ainsi à leur valeur par défaut, comme avec le legacy.
- **Ce que la décision implique :**
  - **Renégociation du bloc gelé.** La clause *Never* (« deleting from tables outside the 9 (`L_D_OF_SUIVI`, `L_D_REBUT`, …) ») et l'*Ask First* sur `ExecuteDelete` / une seconde transaction sont levées par l'utilisateur. Il faut une note datée dans le Spec Change Log, et la Decision KEEP du 2026-10-05 sur la fusion UPDATE est révoquée.
  - **Suppressions en plus des 9 tables :**
    - `L_D_OF_SUIVI` : supprimer la ligne de l'OF, puis décrémenter le `Rang` de **toutes** les lignes de rang supérieur. Le legacy le fait sans filtre sur l'OF.
    - `L_D_REBUT` : supprimer toutes les lignes de l'OF.
  - **Sans effet ici :** les branches `L_D_PLANS_FOURS`, `L_D_PSO` et `L_D_FOURS` de `DeleteOF` ne servent à rien sur ce chemin, puisqu'un OF présent dans ces tables est refusé avant d'arriver là.
  - **Ordre des commandes :** les tables filles d'abord, `L_D_ORDRE_FABRICATION` ensuite, puis les insertions avec le parent avant les filles. Le modèle ne déclare aucune relation (AD-7) et EF ne sait donc pas ordonner. Il faut des `ExecuteDelete` dans une transaction explicite qui englobe le `SaveChanges()` des insertions, pour que tout soit atomique (AC-FR21-2).
  - **Nouvelles entités et nouveaux miroirs :** `L_D_OF_SUIVI` (`OF`, `Rang`, avec la clé lue dans `sys.columns` de production) et `L_D_REBUT` (`OF` + clé). Ajouter aussi les FK de production vers `L_D_ORDRE_FABRICATION` dans le miroir, pour que les tests attrapent un mauvais ordre. Cela solde D-2.
  - **Tests rouges d'abord :** un remplacement doit supprimer les lignes `L_D_OF_SUIVI` / `L_D_REBUT` de l'OF et renuméroter les rangs suivants. Une colonne non mappée doit revenir à sa valeur par défaut. Un échec SQL doit laisser intactes les 11 tables, `L_D_OF_SUIVI` et ses rangs compris.
- **Ampleur :** c'est un changement de comportement sur le bloc gelé, plus large qu'un patch ordinaire. On peut le mener dans cette revue ou en story 6.10-bis via `/build-story`.

## 3. Patch

### P-1 — La suppression des lignes obsolètes au remplacement n'est pas testée (medium)

- **Localisation :** `tests/Kape22Importer.Tests/OfResendIntegrationTests.cs:113-138` (REPLACE) et `:70-86` (ANNULEE). Code concerné : `Kape22Persister.cs:347-358`.
- **Sources :** verification-gap + blind-hunter.
- **Constat :** 445 et 448 ne diffèrent que par le NumeroFichier et un champ numérique. `Resent()` ne touche ni les sections ni les consignes. Chaque ligne ancienne est donc réécrite par une nouvelle ligne de même clé.
- **Risque :** les mutations suivantes laisseraient tous les tests verts :
  - retirer le `RemoveRange` d'une table (par exemple `SectionChargeSvtRows`) ;
  - restreindre la suppression aux clés réinsérées.

  Les lignes d'une section disparue resteraient alors en base, ce qui viole AC-FR21-6 (« 9 tables hold exactly the new rows »).
- **Action :** ajouter un test d'intégration dans `OfResendIntegrationTests`.
  1. Persister le bundle de référence.
  2. Le renvoyer via `Resent(d => …)` en retirant une section et des consignes.
  3. Vérifier que `DownstreamSnapshot()` est égal au snapshot du bundle réduit persisté seul.

### P-2 — Le refus à plusieurs raisons (séparateur ` ; `, ordre) n'est pas testé (low)

- **Localisation :** `Kape22Persister.cs:132-136` et `:318-342` ; tests dans `OfResendIntegrationTests.cs`.
- **Sources :** verification-gap + blind-hunter.
- **Constat :** chaque test de refus ne construit qu'une seule raison. Or la spec (*Always*) impose le séparateur ` ; ` et l'ordre « état puis tables ».
- **Action :** ajouter un cas avec `SetEtat(2, …)` et des inserts dans `L_D_FOURS` et `L_D_PSO`. Message exact attendu :
  `OF '<of>' : l'OF existe déjà et ne peut pas être remplacé (état EVC (2) ; présent dans L_D_FOURS ; présent dans L_D_PSO).`

### P-3 — La théorie legacy teste la raison générique avant les raisons spécifiques (low)

- **Localisation :** `tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs:45-54`, `:124-125`.
- **Sources :** blind-hunter + edge-case-hunter.
- **Constat :** le commentaire dit « matched in this order », mais l'ordre réel est alphabétique. Résultat : `nombre d'OF sauvés : 0`, une ligne de bilan générique du legacy, est testée avant `pas de consignes pour la répartition` (FR20-2) et `sans consignes d'enfournement` (FR20-4).
- **Risque :** un Fichier FR20-2 ou FR20-4 dont la trace contient aussi ce bilan serait classé FR20-6. Les 6 cas actuels passent, mais la théorie se veut générique.
- **Action :** placer `("nombre d'OF sauvés : 0", "AC-FR20-6")` en dernier, avec un commentaire du type « specific reasons first, the generic summary last ». Ce tableau de tuples relève de la dérogation CC-4 R-7.

### P-4 — CC-1 : l'attestation du commit omet AC-FR21-2 (low)

- **Localisation :** message du commit `ae08655`.
- **Sources :** acceptance-auditor.
- **Constat :** les Tasks de la spec annoncent AC-FR21-2, et `Persist_ReplaceThatFailsOnSqlServer_LeavesThePreviousOfIntact_AcFr21_6` porte `[Trait("AC","FR21-2")]`. Pourtant le commit n'a aucune ligne `AC-FR21-2 → …`.
- **Action :** dans le commit de clôture, ajouter la ligne
  `AC-FR21-2 → OfResendIntegrationTests.Persist_ReplaceThatFailsOnSqlServer_LeavesThePreviousOfIntact_AcFr21_6`.

### P-5 — Le commentaire d'en-tête du persister dit « deleted », le code fait un UPDATE (low)

- **Localisation :** `src/Kape22Importer/Persistence/Kape22Persister.cs:28-30`.
- **Sources :** blind-hunter.
- **Constat :** l'en-tête dit « its rows in the 9 downstream tables are deleted ». Le commentaire de `:214-218` du même fichier précise au contraire que la ligne `L_D_ORDRE_FABRICATION` n'est jamais supprimée. C'est ce comportement qui protège les FK de production.
- **Action :** reformuler l'en-tête : « replaced (EF merges the same-key Deleted + Added pair into an UPDATE, see PersistMapped) ».

### P-6 — Les commentaires de `L_D_PSO` donnent la clé dans le mauvais ordre (low)

- **Localisation :** `src/Kape22Importer/Persistence/L_D_PSO.cs:4` ; `scripts/schema/01-ascolsi-tables.sql:475`.
- **Sources :** blind-hunter.
- **Constat :** les deux commentaires écrivent « keyed on (Coulee, NumeroLingot) ». La PK réelle (`01-ascolsi-tables.sql:483`, `AscoLsiDbContext`, et la production vérifiée) est `(NumeroLingot, Coulee)`. Le patch P-6 du step-04 n'a corrigé que la PK.
- **Action :** écrire `(NumeroLingot, Coulee)` dans les deux commentaires.

### P-7 — La disposition de W-1 (6.8) n'est pas mise à jour (low)

- **Localisation :** `_bmad-output/implementation-artifacts/deferred-work.md:1454-1457` ; l'affirmation se trouve dans `epic-6-context.md:55`.
- **Sources :** blind-hunter.
- **Constat :** `epic-6-context.md` affirme que 6.10 résout W-1. Le ledger indique toujours `disposition: PLANNED 2026-10-02 — Story 6.10`.
- **Action :** passer la disposition à « RESOLVED 2026-10-05 by Story 6.10 (D34 replace) », en précisant que la preuve sera le prochain double run standalone du script E2E.

### P-8 — Le critère « D34 branch removed → REFUSE_* and REPLACE fail » n'a aucune preuve (medium)

- **Localisation :** spec, section *Acceptance Criteria*, 3ᵉ puce.
- **Sources :** blind-hunter.
- **Constat :** ce critère est une vérification par mutation. Ni le commit, ni le Spec Change Log, ni la section Verification n'en gardent la trace.
- **Action :** retirer temporairement la branche D34, lancer `OfResendIntegrationTests` (`-m:1 --blame-hang-timeout 2m`), constater les échecs REFUSE_* et REPLACE, restaurer le fichier, puis consigner le résultat daté dans la spec. Attention au piège mtime de `Copy-Item` à la restauration.

## 4. Defer

### F-1 — L'ordre des commandes EF ne connaît pas les FK de production (medium, pré-existant)

- **Localisation :** `src/Kape22Importer/Persistence/AscoLsiDbContext.cs` (aucune relation déclarée, AD-7).
- **Sources :** blind-hunter.
- **Constat :** sans relation dans le modèle, EF ne trie pas les commandes selon les FK de production `L_D_SECTIONCHARGE_*` → `L_D_ORDRE_FABRICATION`.
- **Justification du report :** la situation existe depuis Story 4.6 sur le chemin d'insertion. Elle relève du même chantier que D-2 (le miroir n'a pas les FK de production). Les workers ne sont jamais déployés.

### F-2 — Les raisons KAP22 de la trace legacy ne sont filtrées que par plage d'`Id` (low)

- **Localisation :** `tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs:198-200`.
- **Sources :** blind-hunter + edge-case-hunter.
- **Constat :** si un autre Fichier était traité en même temps, ses lignes seraient attribuées à celui-ci.
- **Justification du report :** le legacy traite les Fichiers un par un, et les 6 cas de référence sont corrects. Un filtre sur l'OF reste à ajouter si un cas réel le contredit. Le code de test sera retiré à la bascule.

## 5. Rejetés (bruit)

- **R-1** — Lignes downstream orphelines sans `L_D_ORDRE_FABRICATION` → violation de PK brute. C'est le comportement voulu : la ligne NEW_OF de la matrice et le Spec Change Log 2026-10-05 (seed orphelin de `RejectionAtomicityIntegrationTests`) l'arbitrent ainsi.
- **R-2** — Un `Etat` inconnu (6, 7…) est remplacé en silence. La spec le dit explicitement : « GPAO (0) and any other value allow the replace » (Suggested Review Order, Intent).
- **R-3** — Les tables de précondition ne sont lues que si l'OF existe. La ligne NEW_OF dit « Unchanged insert path ». De plus, `L_D_PLANS_FOURS` et `L_D_PSO` ont une FK de production vers `L_D_ORDRE_FABRICATION`.
- **R-4** — Rien ne distingue un remplacement d'une première insertion. La spec ne demande qu'une entrée de succès (ligne REPLACE) ; un nouveau signal serait une extension non demandée.
- **R-5** — Les 3 `DbSet` sont inscriptibles. La contrainte « never added to a DbSet write path » est respectée : aucun `Add` ni `Remove` ne les touche.
- **R-6** — `ProtectedEtats` est un `Dictionary` mutable. Le champ est `private static`, jamais muté ; c'est du style, sans effet.
- **R-7** — Pas de test NEW_OF dédié. Le chemin d'insertion n'a pas changé et toute la suite existante le couvre.
- **R-8** — Le test ANNULEE ne vérifie que l'OF et `L_D_KAPE22`. Le test REPLACE couvre déjà le snapshot complet des 9 tables ; le manque réel est P-1.
- **R-9** — REPLACE_SQL_FAIL ne vérifie pas `Success == false`. `Assert.Single(result.Errors)` avec `PersistenceError` suffit à l'impliquer.
- **R-10** — Sans configuration de production, la théorie est ignorée en CI. La matrice LEGACY_THEORY prévoit explicitement le `Skip`, et le critère vaut « production configured ».
- **R-11** — `ErrorFichiers` vide → théorie sans données. `P60/error/` est versionné et ne peut pas être vide.
- **R-12** — Messages de test mêlant anglais et français. CC-2 vise les commentaires ; les messages destinés à l'opérateur restent en français, comme dans le reste du projet.
- **R-13** — `[Trait("AC","6.10")]` sur `SchemaModelParityTests`. Même convention que les `"2.1"` et `"4.1"` existants pour les tests de schéma.
- **R-14** — La spec dit `status: done` alors que sprint-status dit `review`. C'est la séquence normale build → review ; la clôture alignera les deux.
- **R-15** — `review_loop_iteration: 0`. Métadonnée du workflow, sans effet.
- **R-16** — `epic-6-context.md` est condensé. Les contraintes de 6.1 à 6.8 restent dans leurs specs respectives, et 6.10 est la dernière story de l'épic.
- **R-17** — La suppression du brouillon `ZzLegacyRejectionReplayTests.cs` ne se voit pas dans le diff. L'acceptance-auditor a constaté le fichier absent.
- **R-18** — L'entrée différée `L_D_OF_SUIVI` n'a pas de déclencheur de déploiement. Les workers GPAO n'ont jamais été déployés ; le déploiement est lui-même le déclencheur.
- **R-19** — CC-2 : `ponytail:` en minuscule au milieu d'une phrase (`LegacyRejectionParityTests.cs:30`). C'est un jeton-marqueur outillé (comme `TODO:`) avec un précédent (`EndToEndPerformanceTests.cs:123`).
- **R-20** — CC-3 : la note Q-3 réécrite au-dessus de `CompleteAlreadyImported`. Le Code Map de la spec approuvée par l'humain le demandait (« stale business note Q-3 … to update »), et la note était devenue fausse à cause de D34.
- **R-21** — `CopyFromProduction` interpole `table` et `where`. Ce ne sont que des constantes internes au test.
- **R-22** — Position de `SMALLINT` dans le switch de `SqlTableSchema.ClrTypeFor`. Le switch n'avait déjà aucun ordre.
- **R-23** — La cause obtenue dépend de l'ordre de `ObtainedCauses`. Chaque refus produit une seule erreur, donc aucune ambiguïté n'est atteignable.
- **R-24** — Le mapper rejette, donc l'état de l'OF n'est pas reconstruit. L'assert nomme alors les deux causes, l'échec est lisible.
- **R-25** — Une colonne du miroir absente de la production ferait échouer le `SELECT`. L'échec serait bruyant, sur du code de test seulement.
- **R-26** — Horloge de la théorie basée sur `TimeZoneInfo.Local`. Les tests tournent sur le serveur, dans le fuseau de production ; l'heure ambiguë du changement d'heure ne concerne aucun des cas.
- **R-27** — Le comportement « jamais réellement supprimé » (UPDATE) n'a pas de test. C'est déjà tracé comme D-2 (FK du miroir) dans `deferred-work.md`.

## 6. Auto-vérifications

- **4 lentilles lancées en parallèle, toutes revenues :** blind-hunter (25 findings), edge-case-hunter (9), verification-gap (3 + 1 annexe), acceptance-auditor (3 + 5 hors mandat). Aucune n'a échoué (`failed_layers` vide).
- **Diff stats :** 18 fichiers, +1097 / −48.
  - Production : `Kape22Persister.cs`, `AscoLsiDbContext.cs`, 3 entités, `01-ascolsi-tables.sql`.
  - Tests : 2 nouvelles classes et 6 fichiers ajustés.
  - Artefacts : spec, epic-6-context, deferred-work, sprint-status.
- **Code lu avant classement :** `Kape22Persister.cs:1-80, 100-380`, `OfResendIntegrationTests.cs:60-200`, `LegacyRejectionParityTests.cs:25-205`, `L_D_PSO.cs`, `01-ascolsi-tables.sql:475-486`, `deferred-work.md:1450-1462`, le message du commit `ae08655`, `project-profile.md` (CC).
- **Non exécuté pendant la revue :** aucun build ni run de tests, la revue est en lecture seule. La mutation P-8 est à exécuter au moment des patchs.

## 7. Application des patchs (2026-10-05)

L'utilisateur a choisi l'option 1 : appliquer tous les patchs, sans commit.

| ID | État | Ce qui a été fait |
|---|---|---|
| P-9 | appliqué | `Kape22Persister.DeleteOf` fait la même chose que le `DeleteOF` legacy. Il exécute des `ExecuteDelete` / `ExecuteUpdate` dans l'ordre suivant : `L_D_OF_SUIVI` d'abord (avec renumérotation des `Rang` supérieurs), puis `L_D_CONSIGNES`, `L_D_MAM_QUAL`, `L_D_PRODUITS_OUTIL`, `L_D_REBUT` et les 7 `L_D_SECTIONCHARGE_*`, et enfin `L_D_ORDRE_FABRICATION`. Le tout tourne dans une transaction explicite, que le `SaveChanges()` des insertions rejoint. La recherche de l'OF existant ne charge plus que son `Etat`, sans tracking. Il y a 4 nouvelles entités minimales et 4 miroirs (`L_D_MAM_QUAL`, `L_D_OF_SUIVI`, `L_D_PRODUITS_OUTIL`, `L_D_REBUT`), lus dans `sys.columns` en production. S'y ajoutent les DbSets, le `ResetData` et 4 tests de parité. Le test `Persist_ExistingReplaceableOf_DeletesItsRowsInTheOtherLegacyDeleteOfTables_AcFr21_6` a d'abord été rouge, puis vert. REPLACE_SQL_FAIL vérifie maintenant aussi ces 4 tables. Une note datée de renégociation est ajoutée au Spec Change Log et `epic-6-context.md` est mis à jour. |
| P-1 | appliqué | Nouveau test `Persist_ResentOfWithFewerRows_LeavesNoStaleRow_AcFr21_6`. Le bundle réduit perd 2 sections et la moitié de ses Consignes. Le test garde le comportement actuel : il est vert avant et après le changement, et tombe à la mutation. |
| P-2 | appliqué | Nouveau test `Persist_ExistingOfWithSeveralRefusalReasons_NamesThemAllInOrder_AcFr20_6`. Il attend le message exact `état EVC (2) ; présent dans L_D_FOURS ; présent dans L_D_PSO`. |
| P-3 | appliqué | Dans `LegacyReasons`, la raison générique `nombre d'OF sauvés : 0` passe en dernier, et un commentaire explique cet ordre. |
| P-4 | dû | L'attestation AC-FR21-2 sera ajoutée dans le commit de clôture (`/commit-review`). |
| P-5 | appliqué | Le commentaire d'en-tête et celui du bloc D34 du persister sont réécrits pour le vrai DELETE. Le commentaire sur la fusion en UPDATE est supprimé, car il n'est plus vrai. |
| P-6 | appliqué | `L_D_PSO.cs:4` et le commentaire SQL donnent maintenant `(NumeroLingot, Coulee)`. |
| P-7 | appliqué | `deferred-work.md` passe W-1 (6.8) en RESOLVED. L'entrée 6.10 « D34 scope » passe aussi en RESOLVED par P-9. L'entrée D-2 est révisée : l'ordre des DELETE est maintenant explicite, l'ordre des INSERT reste F-1. |
| P-8 | appliqué | Mutation exécutée en forçant l'OF existant à `null` : 14 des 15 cas de `OfResendIntegrationTests` échouent, et seul REPLACE_SQL_FAIL reste vert (c'est voulu). Le fichier a été restauré avec `cp` + `touch`. Le résultat est consigné dans le Spec Change Log. |

Les Defer F-1 et F-2 sont à tracer par `/commit-review`.

Vérifications :
- `dotnet build TextToXml.sln -warnaserror` : 0 avertissement.
- `Category=Unit` : 1260 tests, 0 échec (192 + 56 + 21 + 991).
- `Category=Integration -m:1 --blame-hang-timeout 2m`, premier run : 1 échec, `PersistenceSmokeTests.SchemaApplies_CreatesExactlyTheHarnessTables`. Ce test compte les tables du miroir, et les 4 tables `LegacyDeleteOfTables` y ont été ajoutées.
- Second run de `PersistenceSmokeTests` + `Kape22ProductionDataParityTests` : 1348 tests, 0 échec. Les 20 Skip viennent de données de production absentes (« aucune ligne … en production »). Ils existaient avant cette revue.
- Les 6 cas de `LegacyRejectionParityTests` passent, aucun n'est ignoré : 407/408/430 → AC-FR20-5, 446/447/449 → AC-FR20-6.
- Le reste de la suite Integration (1423 tests Kape22Importer + 2 tests AscoLsiJournal) passe.
- `PersistenceSmokeTests.cs` a été ajouté à la liste des fichiers touchés par P-9.
