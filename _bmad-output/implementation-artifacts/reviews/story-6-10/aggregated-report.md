# Review report — Story 6.10

Range : ae08655^..HEAD + working tree (review patches of the first pass, uncommitted — user option C, 2026-10-05)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-10-renvoi-of-existant-remplacement-ou-refus.md
Date : 2026-10-05
Verdict : REFUSÉ
Findings : D=1 P=5 F=2 R=25  (Decision, Patch, Defer, Rejetés) — D-1 tranché 2026-10-05 → Defer (sous D-2)

> Second pass. The first-pass report (D-1→P-9, P-1..P-9, F-1, F-2) is preserved in
> `aggregated-report.prev.md` in this folder; its open items (P-4 attestation, F-1, F-2 ledger entries)
> are still owed to `/commit-review` and are carried over below where relevant.

## 1. Verdict

**REFUSÉ.** Trois raisons :

- **D-1 (medium) est ouverte.** La clause « FK de production dans le miroir » du P-9 de la première passe n'a pas été livrée, et rien ne protège l'ordre des `DELETE` du nouveau `DeleteOf`.
- **P-1 est un écart CC-4.** Dans `PersistenceSmokeTests`, les champs statiques ne sont pas dans l'ordre alphabétique.
- **La documentation contredit le code livré.** P-2 (spine AD-1), P-3 (spec hors bloc gelé) et P-4 (en-tête de `OfResendIntegrationTests`) décrivent encore le mécanisme d'avant P-9.

Le cœur P-9 est conforme :

- `DeleteOf` reproduit le `DeleteOF` legacy, tables filles d'abord, `L_D_ORDRE_FABRICATION` en dernier.
- Une transaction explicite est jointe par le `SaveChanges()`. Le verification-gap a confirmé que la retirer fait échouer REPLACE_SQL_FAIL.
- La renégociation du bloc gelé est datée dans le Spec Change Log.
- Aucun *Never* n'est exécuté et aucun AD n'est violé. La production n'est lue que par `SELECT` avec `ApplicationIntent=ReadOnly`, et aucun `EnableRetryOnFailure` n'est configuré (grep : 0 occurrence).

| Gravité | Nombre | IDs |
|---|---|---|
| high | 0 | — |
| medium | 1 | D-1 |
| low | 7 | P-1..P-5, F-1, F-2 |

## 2. Decision (à trancher par l'humain)

### D-1 — Rien ne protège l'ordre des DELETE du remplacement contre les FK de production (medium) — à trancher par l'humain

- **Localisation :** `src/Kape22Importer/Persistence/Kape22Persister.cs:353-376` (`DeleteOf`) ; `scripts/schema/01-ascolsi-tables.sql` (aucune `FOREIGN KEY`) ; `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs` (`ResetData` en `TRUNCATE`).
- **Sources :** verification-gap.
- **Constat :** le miroir ne déclare aucune FK et les tests ne vérifient que l'état final des tables.
  - Déplacer `OrdreFabricationRows…ExecuteDelete()` en tête de `DeleteOf` laisserait les 5 tests de remplacement au vert.
  - En production, ce même changement ferait échouer en violation de FK tout renvoi d'OF qui a des sections. L'OF serait alors rejeté (PersistenceError) au lieu d'être remplacé.
  - La décision P-9 de la première passe prévoyait : « Ajouter aussi les FK de production … dans le miroir, pour que les tests attrapent un mauvais ordre. Cela solde D-2. » Ce point n'a pas été livré. `deferred-work.md:1489` (D-2) justifie l'abandon par le `TRUNCATE` du fixture.
- **Options :**
  - **(a)** Ajouter au miroir les FK de production : 7 `L_D_SECTIONCHARGE_*`, `L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PLANS_FOURS`, `L_D_PSO` → `L_D_ORDRE_FABRICATION`. `ResetData` doit alors passer en `DELETE` dans l'ordre enfants → parent. Conséquence : l'ordre des INSERT (F-1 de la première passe) devient testé lui aussi, et peut échouer si EF ne trie pas.
  - **(b)** Écrire un test Integration qui crée ces FK dans `AscoLSI_Test` le temps d'un remplacement, puis les supprime dans un `finally`. Coût minimal, et `TRUNCATE` reste inchangé.
  - **(c)** Accepter le report sous D-2 (`deferred-work.md:1488`) et retirer formellement la clause FK du P-9.
- **Tranché le 2026-10-05 par l'utilisateur : option (c), report sous D-2.**
  - **Preuve en production :** le script opérateur de suppression d'un OF (`DELETE` de `L_D_SECTIONCHARGE_LINGOT`, `_CHUTAGE`, `_DECOUPE`, `_REFROIDISSOIRS`, `_PITS`, `L_D_CONSIGNES`, puis `L_D_ORDRE_FABRICATION`) fonctionne avec l'ordre « tables filles d'abord, OF en dernier ». C'est l'ordre que suit `DeleteOf`.
  - **Clause abandonnée :** la clause « FK de production dans le miroir » du P-9 de la première passe est retirée. Des FK dans le miroir bloqueraient le `TRUNCATE` de `ResetData`.
  - **À la clôture :** `/commit-review` complète l'entrée D-2 de `deferred-work.md` avec cette preuve et cette décision.

## 3. Patch

### P-1 — CC-4 : champs statiques de `PersistenceSmokeTests` hors ordre alphabétique (low)

- **Localisation :** `tests/Kape22Importer.Tests/PersistenceSmokeTests.cs:38`, `:47`.
- **Sources :** acceptance-auditor.
- **Constat :** l'ordre actuel est `DownstreamTables`, `PreconditionTables`, `LegacyDeleteOfTables`, `ReferenceTables`. Les deux champs qui existaient avant étaient en ordre alphabétique.
- **Action :** déplacer `LegacyDeleteOfTables` (avec son commentaire) avant `PreconditionTables`.

### P-2 — La note D34 de l'AD-1 dans le spine d'architecture date d'avant P-9 (low)

- **Localisation :** `_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md:70-74`.
- **Sources :** acceptance-auditor.
- **Constat :** la note dit encore « suppression … dans `L_D_ORDRE_FABRICATION`, les 7 `L_D_SECTIONCHARGE_*` et `L_D_CONSIGNES`, puis insertion, dans ce même `SaveChanges()` ».
- **Action :** ajouter une note datée du 2026-10-05 (décision D-1 → P-9). Elle doit dire :
  - le remplacement est un vrai `DeleteOF` legacy sur 13 tables, `L_D_OF_SUIVI` renuméroté, puis l'insertion ;
  - il s'exécute en `ExecuteDelete` dans une transaction explicite que le `SaveChanges()` rejoint, tout ou rien ;
  - elle remplace la formulation « même `SaveChanges()` » de la note du 2026-10-02.

### P-3 — Les sections de la spec hors bloc gelé décrivent encore le `RemoveRange` / la fusion UPDATE (low)

- **Localisation :** spec `:49`, `:62`, `:122`, `:125`, `:130`, `:169`.
- **Sources :** blind-hunter + acceptance-auditor.
- **Constat :** voici ce qui reste faux.
  - Code Map `:49` et Suggested Review Order `:169` disent « ResetData truncates the 3 new tables » ; il y en a 7.
  - La tâche `:62` parle de « load the OF's tracked rows in the 9 tables, `RemoveRange` ».
  - `:122` dit « EF merges same-key Deleted+Added into UPDATE ». Ce KEEP est révoqué par le Change Log du 2026-10-05.
  - `:125` parle de « Tracked removal … 9 tables » et `:130` de « Three DbSets ».
- **Action :** réécrire ces lignes pour décrire `DeleteOf`, sans toucher au bloc `<frozen-after-approval>`.
  - `ExecuteDelete` sur 13 tables, enfants d'abord, dans une transaction explicite.
  - 7 `DbSet` et 7 tables tronquées par `ResetData`.
  - Recaler les ancres de ligne.

### P-4 — L'en-tête de `OfResendIntegrationTests` est périmé (low)

- **Localisation :** `tests/Kape22Importer.Tests/OfResendIntegrationTests.cs:21-23`.
- **Sources :** blind-hunter + acceptance-auditor (hors mandat).
- **Constat :** l'en-tête dit « its rows in the 9 downstream tables are replaced by the new ones in the same single SaveChanges ».
- **Action :** remplacer par « its rows are deleted the way the legacy DeleteOF does (13 tables, L_D_OF_SUIVI re-ranked) and the new ones inserted, in one explicit transaction around the single SaveChanges ».

### P-5 — CC-1 : l'attestation de clôture doit couvrir AC-FR21-2 et les tests P-9 (low, reporté de la première passe P-4)

- **Localisation :** message du commit de clôture.
- **Sources :** acceptance-auditor (hors mandat) + rapport précédent P-4.
- **Constat :** aucune trace rouge-d'abord n'est visible pour `Persist_ExistingReplaceableOf_DeletesItsRowsInTheOtherLegacyDeleteOfTables_AcFr21_6`, `Persist_ResentOfWithFewerRows_LeavesNoStaleRow_AcFr21_6` et `Persist_ExistingOfWithSeveralRefusalReasons_NamesThemAllInOrder_AcFr20_6`. La ligne `AC-FR21-2 → …` est toujours due.
- **Action :** dans le commit de clôture (`/commit-review`), ajouter :
  - `AC-FR21-2 → OfResendIntegrationTests.Persist_ReplaceThatFailsOnSqlServer_LeavesThePreviousOfIntact_AcFr21_6` ;
  - les 3 tests P-1/P-2/P-9 sous AC-FR21-6 et AC-FR20-6, avec leur statut rouge-d'abord tel que consigné au §7 du rapport précédent.

## 4. Defer

### F-1 — La vérification de l'OF existant est lue hors de la transaction de remplacement (low)

- **Localisation :** `src/Kape22Importer/Persistence/Kape22Persister.cs:130-144` contre `:220`.
- **Sources :** blind-hunter.
- **Constat :** l'`Etat` et les 3 tables de précondition sont lus avant `BeginTransaction`. Si le MCC fait passer l'OF en ENC dans l'intervalle (quelques millisecondes), l'OF est supprimé quand même.
- **Justification du report :** le legacy a la même fenêtre (`AddRange2` vérifie avant `DeleteOF`). Le worker traite un Fichier à la fois et n'est jamais déployé. Pour corriger : ouvrir la transaction avant la lecture, avec `UPDLOCK, HOLDLOCK`.

### F-2 — La théorie legacy reconstruit l'historique de l'OF sans borne basse et seulement en GPAO/ENC (low)

- **Localisation :** `tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs:243-260`.
- **Sources :** blind-hunter.
- **Constat :**
  - Les recherches `Création d'un OF` / `ENC` ne bornent que `Id < @start`. Un numéro d'OF recyclé des années plus tôt compterait comme existant.
  - L'`Etat` reconstruit est 0 ou 1, alors que 446/447/449 ont rencontré un OF EVC (2). Les cas passent parce que la théorie ne compare que la cause.
- **Justification du report :** c'est du code de test à retirer à la bascule. Le plafond est déclaré par le commentaire `ponytail:` (`:30`), et les 6 cas réels sont corrects. Il faudra borner sur la dernière suppression de l'OF si un cas réel le contredit. Même famille que le F-2 de la première passe.

## 5. Rejetés (bruit)

- **R-1** — OF absent de `L_D_ORDRE_FABRICATION` mais présent dans `L_D_FOURS.OFEnCours` (edge-case + blind). La ligne NEW_OF de la matrice prévoit le chemin d'insertion inchangé. Déjà rejeté en R-3 lors de la première passe.
- **R-2** — Lignes `L_D_MAM_QUAL` / `L_D_PRODUITS_OUTIL` orphelines pour un OF inexistant. Ce cas suit NEW_OF ; le legacy n'appelle `DeleteOF` que pour un OF existant.
- **R-3** — `L_D_LOG_COMMANDE.Message` NULL → `SqlNullValueException` dans la théorie. C'est du code de test qui échoue bruyamment, comme R-25 de la première passe.
- **R-4** — `P60/error/` vide → « No data found ». Le dossier est versionné, comme R-11 de la première passe.
- **R-5** — Heure ambiguë au changement d'heure / `FixedClock` sur `TimeZoneInfo.Local`. Les tests tournent sur le serveur, dans le fuseau de production (R-26 de la première passe).
- **R-6** — Conflit entre `BeginTransaction` et `EnableRetryOnFailure`. Aucune stratégie d'exécution n'est configurée (grep : 0 occurrence). C'est spéculatif.
- **R-7** — Renumérotation `L_D_OF_SUIVI` en cas de trous ou de doublons de `Rang`, ou si l'OF est en queue. L'instruction est la même que celle du legacy. La PK sur `OF` garantit `SingleOrDefault`. Le trigger de production est hors miroir et noté dans le Change Log.
- **R-8** — Pas de test d'échec au milieu de `DeleteOf`. La même transaction couvre les deux phases, et le verification-gap confirme que le rollback est couvert.
- **R-9** — `ProtectedEtats` est un `Dictionary` mutable. Il est privé et jamais muté (R-6 de la première passe).
- **R-10** — `ValueGeneratedOnAdd()` sur `L_D_REBUT.Id`. C'est explicite et inoffensif, et cela reflète l'identité de production.
- **R-11** — Les `DbSet` en lecture seule ne le sont que par commentaire. Aucun `Add`/`Remove` ne les touche (R-5 de la première passe).
- **R-12** — `Snapshot()` omet les tables de précondition. Aucun code de l'importeur n'écrit dans ces tables.
- **R-13** — `[Trait("AC","6.10")]` dans `SchemaModelParityTests`. Même convention que `"2.1"` et `"4.1"` (R-13 de la première passe).
- **R-14** — `CopyFromProduction` ouvre plusieurs connexions et interpole le `WHERE`. Le code de test utilise des constantes internes (R-21 de la première passe).
- **R-15** — Le bloc gelé n'est ni barré ni accompagné de pointeurs. La règle demande une note datée dans le Change Log, et l'auditor l'a constatée.
- **R-16** — `review_loop_iteration: 0`. Métadonnée de workflow (R-15 de la première passe).
- **R-17** — W-1 est RESOLVED « to confirm ». Formulation décidée au P-7 de la première passe.
- **R-18** — Dans `deferred-work.md`, la résolution est portée par `source_spec` et il n'y a pas de `disposition:`. C'est la convention du ledger, comme pour les entrées voisines.
- **R-19** — Le `summary` de l'entrée « D34 scope » décrit l'état d'avant P-9. Le ledger garde le summary d'origine et porte la résolution dans `source_spec`.
- **R-20** — Dans `epic-6-context.md`, « delicate part … fixed at the spec checkpoint » et `:45`. Le contexte de planification est historique ; `:27` décrit déjà le remplacement P-9.
- **R-21** — Dans `RejectionAtomicityIntegrationTests`, un OF orphelin est désormais remplacé sans bruit. L'échange du seed est consigné dans le Change Log, et un OF existant prend par conception le chemin de remplacement.
- **R-22** — Plus de 12 allers-retours SQL par remplacement. C'est la parité legacy, pour un seul OF par Fichier.
- **R-23** — Le message d'échec pour une raison legacy inconnue ne cite que la raison legacy. La cause obtenue n'est pas encore calculée à ce stade ; l'auditor juge le comportement conforme.
- **R-25** — *(ex-F-3, rejeté le 2026-10-05 par l'utilisateur)* `L_D_MAM_QUAL.[OF]` / `L_D_PRODUITS_OUTIL.[OF]` en `NVARCHAR(12)` : une valeur non paddée survivrait au remplacement. Sans objet : ces tables sont alimentées après l'ENC (l'enregistrement de l'ordre de production, qui consomme le P60). Un OF remplaçable n'a donc jamais atteint l'ENC, et une ligne restée orpheline après la suppression de son OF n'a plus aucun intérêt, puisqu'on ne peut plus en retrouver la source.
- **R-24** — L'ordre des INSERT n'est pas trié par FK. C'est le F-1 de la première passe, déjà reporté.

## 6. Auto-vérifications

- **4 lentilles lancées en parallèle, toutes revenues** (`failed_layers` vide) :
  - blind-hunter : 23 findings ;
  - edge-case-hunter : 5 ;
  - verification-gap : 1, plus la liste des comportements couverts ;
  - acceptance-auditor : 3, plus 4 hors mandat.
- **Diff :** `ae08655^` contre le working tree, plus les 4 entités non suivies. 22 fichiers, +1377 / −49 sur les fichiers suivis, plus 4 nouveaux fichiers. Le diff complet fait 1786 lignes.
- **Code lu avant classement :**
  - `Kape22Persister.cs:55-70, 120-150, 200-265, 315-385` ;
  - `PersistenceSmokeTests.cs:20-60` ;
  - `OfResendIntegrationTests.cs:15-30` ;
  - `LegacyRejectionParityTests.cs:25-30, 243-260` ;
  - `01-ascolsi-tables.sql` (FK, `L_D_OF_SUIVI`, `L_D_MAM_QUAL`, `L_D_PRODUITS_OUTIL`) ;
  - `ARCHITECTURE-SPINE.md:68-75` ;
  - spec `:57-84` et grep des lignes périmées ;
  - `deferred-work.md:1457, 1479-1489` ;
  - `epic-6-context.md` (grep) ;
  - `project-profile.md` (CC).
- **Recherches :** `EnableRetryOnFailure|ExecutionStrategy` dans TextToXml et MicroServices/GPAO : 0 occurrence. `FOREIGN KEY|REFERENCES` dans le miroir : 0 contrainte.
- **Non exécuté :** aucun build ni run de tests. La revue est en lecture seule, et les tests vident `AscoLSI_Test`.

## 7. Application des patchs (2026-10-05)

- **Décisions de l'utilisateur :**
  - D-1 : option (c), report sous D-2. Le script opérateur de suppression d'OF sert de preuve en production (§2).
  - F-3 : rejeté, devient R-25.
- **Patchs :** l'utilisateur a choisi l'option 1, appliquer tous les patchs sans commit.

| ID | État | Ce qui a été fait |
|---|---|---|
| P-1 | appliqué | `PersistenceSmokeTests.cs` : `LegacyDeleteOfTables` est déplacé avant `PreconditionTables`. L'ordre devient Downstream, LegacyDeleteOf, Precondition, Reference. |
| P-2 | appliqué | `ARCHITECTURE-SPINE.md` : une note AD-1 datée du 2026-10-05 remplace la mécanique de la note du 2026-10-02. Elle décrit le `DeleteOF` sur 13 tables, avec la transaction explicite que le `SaveChanges()` rejoint. |
| P-3 | appliqué | Spec, hors du bloc gelé : Code Map (`ResetData`, 7 tables), tâche Persister (`DeleteOf`), Suggested Review Order (transaction `:220`, `DeleteOf` `:353`, sept DbSets, `ResetData` `:117`). |
| P-4 | appliqué | L'en-tête de `OfResendIntegrationTests.cs` décrit maintenant le DeleteOF legacy sur 13 tables et la transaction explicite. |
| P-5 | dû | Attestation CC-1 (AC-FR21-2 et les tests P-1/P-2/P-9) à mettre dans le commit de clôture (`/commit-review`). |

- **Reste dû à `/commit-review` :**
  - les Defer F-1 et F-2 de cette passe, à inscrire dans `deferred-work.md`, ainsi que les F-1 et F-2 de la première passe ;
  - l'entrée D-2 à compléter avec la décision D-1 ;
  - la synchronisation de `sprint-status.yaml` (6-10 est encore à `review`).
- **Vérification :** `dotnet build TextToXml.sln -warnaserror` donne 0 avertissement et 0 erreur. Aucun test n'a été lancé : P-1 ne fait que réordonner deux déclarations, et les autres patchs ne touchent que des commentaires ou de la documentation.

## Clôture (2026-10-05)

Cette clôture solde les deux passes : la première (`aggregated-report.prev.md`) et la seconde (ce rapport).

**Première passe**
- D-1 → tranché (DeleteOF legacy complet + insert), devenu P-9 → appliqué.
- P-1, P-2, P-3, P-5, P-6, P-7, P-8, P-9 → appliqués (voir §7 du rapport précédent).
- P-4 → appliqué : attestation AC-FR21-2 dans le message du commit de clôture.
- F-1 → tracé dans `deferred-work.md` (lié à D-2, non planifié).
- F-2 → tracé dans `deferred-work.md` (non planifié).

**Seconde passe**
- D-1 → tranché, option (c) : report sous D-2. L'entrée D-2 de `deferred-work.md` est complétée avec la preuve du script opérateur et l'abandon de la clause « FK de production dans le miroir ».
- P-1, P-2, P-3, P-4 → appliqués (§7).
- P-5 → appliqué : attestation CC-1 dans le message du commit de clôture.
- F-1 → tracé dans `deferred-work.md` (même fenêtre que la D-1 du step-04, non planifié).
- F-2 → tracé dans `deferred-work.md` (non planifié).
- F-3 → rejeté par l'utilisateur (R-25).

**Vérification finale**
- `dotnet build TextToXml.sln -warnaserror` : 0 avertissement, 0 erreur.
- `Category=Unit` : 1260 réussis, 0 échec (192 + 991 + 56 + 21).
- `Category=Integration -m:1 --blame-hang-timeout 2m` : 1426 réussis, 0 échec, 20 ignorés (1424 Kape22Importer + 2 AscoLsiJournal ; les 20 Skip sont les Fichiers de `Kape22ProductionDataParityTests` sans ligne en production).

**Statut :** story 6-10 → done ; epic-6 → done (rétrospective en attente). Le commit de clôture est celui qui ajoute cette section.
