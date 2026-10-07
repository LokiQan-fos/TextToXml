# Review report — Story 6.11

Range : 9b8b351^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-11-ordre-fk-production-garde-par-tests.md
Date : 2026-10-07
Verdict : REFUSÉ
Findings : D=1 P=3 F=4 R=20  (Decision, Patch, Defer, Rejetés) — D-1 tranché 2026-10-07 → P-4 ; F-2 promu en P-5 (2026-10-07, utilisateur) ; F-1, F-3, F-4 promus en P-6, P-7, P-8 (2026-10-07, utilisateur)

> The range also holds `2480785` (raw P89 sample Fichiers 331-373, unrelated to the story). With the
> user's agreement at the step-01 checkpoint, `P89/raw/` was excluded from the diff sent to the lenses.

## 1. Verdict

**REFUSÉ.** Deux raisons :

- **P-1 (high à la revue, requalifié low le 2026-10-07 : tables legacy non alimentées, voir §3).** La garde ne couvre pas 4 des 20 FK. Le Fichier de référence n'a pas de section PoidsMetrique ni SVT : `CodeOpePoidMetrique` et `CodeOpeSVT` sont vides, et `Kape22ImportBundleMapperTests.cs:46,48` le vérifie. Aucune ligne n'est donc écrite ni supprimée dans `L_D_SECTIONCHARGE_POIDSMETRIQUE` ni dans `L_D_SECTIONCHARGE_SVT` pendant que les FK sont actives. Si `DeleteOf` supprimait l'OF avant ces deux tables, le test resterait vert.
- **D-1 (medium, CC-1).** Le test de FK résiduelle a été ajouté pendant la revue du build, après le correctif qu'il garde. Aucune exécution rouge n'est enregistrée pour ce test.

Ce qui est conforme :

- Les 20 FK ont les noms et colonnes relus dans la note datée de la spec. La renégociation D-1/D-2 du 2026-10-07 est datée dans le Spec Change Log.
- Les FK sont supprimées dans un `finally`. La fixture supprime toutes les FK avant les tables.
- Aucune FK dans `scripts/schema/`. Aucun changement dans `src/`. Aucune écriture en production : `SELECT` seul avec `ApplicationIntent=ReadOnly` (CC-7).
- AD-7 respecté, aucune relation EF. AD-3 respecté.
- CC-2 : commentaires en anglais. CC-4 : `CodeOperations`, `ForeignKeys`, `ProfilProduits`, `ReferencedTables` et `ForeignKeyParentTables` sont triés.

| Gravité | Nombre | IDs |
|---|---|---|
| high | 0 | — |
| medium | 2 | D-1, P-2 |
| low | 6 | P-1 (requalifié), P-3, P-5 (ex-F-2), P-6 (ex-F-1), P-7 (ex-F-3), P-8 (ex-F-4) |

## 2. Decision (à trancher par l'humain)

### D-1 — Le test de FK résiduelle n'a jamais été vu rouge (CC-1, medium) — tranché 2026-10-07 : option 1 → P-4

- **Localisation :** `tests/Kape22Importer.Tests/PersistenceSmokeTests.cs:284` (`SchemaApplies_OverAForeignKeyLeftByAKilledRun_DropsItAndStaysAvailable_AcFr21_7`) ; `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs:251-253`.
- **Sources :** acceptance-auditor.
- **Constat :** le test vient du P-5 de la revue step-04 du build. La suppression des FK dans `DropExistingUserTables` existait déjà à ce moment-là. Le commit ne mentionne qu'un seul rouge, « missing L_P_* tables », celui du test principal. Le chemin de repli littéral D-2 n'a pas non plus de rouge enregistré.
- **Options :**
  1. Rouge a posteriori : retirer les 3 lignes `sys.foreign_keys` de la fixture, lancer le test et constater l'échec (`Available` false), puis restaurer. Faire de même pour le repli D-2 en vidant `CodeOperations` (échec attendu de l'Ask First). Consigner les deux rouges dans le Spec Change Log et dans le message de commit de clôture.
  2. Exemption CC-1 explicite, consignée dans le Spec Change Log.

## 3. Patch

### P-1 — Sections PoidsMetrique et SVT jamais écrites : 4 FK sur 20 ne gardent rien (high → low, requalifié 2026-10-07) — APPLIQUÉ 2026-10-07, conservé par décision utilisateur

> **Requalification (2026-10-07, utilisateur) :** `L_D_SECTIONCHARGE_SVT` et `L_D_SECTIONCHARGE_POIDSMETRIQUE` sont des tables legacy que le GPAO n'alimente plus. Production, lecture seule : SVT a 416 lignes, dernier OF `7038000` ; PoidsMetrique a 4 lignes ; le dernier code SVT dans `L_D_KAPE22` est l'Id 96167 sur 124 430 ; aucun code PoidsMetrique. Le risque en production est donc latent, pas réel. Le patch est conservé, car le mapper sait toujours écrire ces sections si le GPAO les renvoyait.

- **Localisation :** `tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs:81-82,128-137` ; FK concernées aux lignes 44, 46, 56 et 58.
- **Sources :** verification-gap + blind-hunter (aucun contrôle des lignes de section après CREATE).
- **Constat :**
  - `Hot()` ne modifie que `CodeConsignePits`, `Coulee` et `NumeroFichier`, donc `SectionChargePoidsMetrique` et `SectionChargeSvt` restent `null`.
  - `AssertReferenceCodesPresent` filtre avec `OfType<string>()`, donc ces deux sections nulles sont ignorées sans message.
  - La mutation manuelle n'a démontré que `FK_ConsignesChutageOrdreFabrication`.
  - En production, remplacer un OF qui a des sections SVT ou PoidsMetrique peut violer `FK_ConsignesSVTOrdreFabrication` ou `FK_ConsignesPoidsMetriqueOrdreFabrication` alors que la suite reste verte.
- **Action :**
  - Dans `Hot()`, ajouter `SetChamp` sur `CodeOpePoidMetrique`/`RangOpePoidMetrique` et `CodeOpeSVT`/`RangOpeSVT`, avec des codes de la liste du 2026-10-06 et les champs que leurs mappers exigent.
  - Avant d'ajouter les FK, vérifier que les 7 `SectionCharge*` du bundle sont non nulles. Le CREATE insère alors les 7 sections et le REPLACE les supprime, donc l'ordre d'insertion et l'ordre de suppression sont gardés pour les 20 FK.
  - Si aucune combinaison valide n'existe sans toucher `src/`, cela relève de l'Ask First de la spec.

### P-2 — `epic-6-context.md` contredit D-1/D-2 et fusionne deux puces (medium) — APPLIQUÉ 2026-10-07

- **Localisation :** `_bmad-output/implementation-artifacts/epic-6-context.md:28` et `:50`.
- **Sources :** acceptance-auditor + blind-hunter.
- **Constat :**
  - Ligne 28 : le texte, réécrit dans ce même commit, dit encore « the 13 production FKs » et « seeds both `L_P_*` tables from production (`SELECT` only) ».
  - Ligne 50 : la puce « Integration tests use local SQL Server… » est collée à la fin de la puce 6.12 (« …by the user.- Integration tests… »). La contrainte Integration est donc rendue comme faisant partie de 6.12.
- **Action :**
  - Ligne 28 : remplacer « 13 » par « 20 (2 on `L_D_ORDRE_FABRICATION`, 11 child `OF`, 7 `CodeOperation`) ». Ajouter « or, without production access (CI), the codes read on 2026-10-06 » après la clause de copie.
  - Ligne 50 : insérer le saut de ligne manquant.

### P-3 — Le test de FK résiduelle peut contaminer la collection (low) — APPLIQUÉ 2026-10-07

- **Localisation :** `tests/Kape22Importer.Tests/PersistenceSmokeTests.cs:288-298`.
- **Sources :** edge-case-hunter + blind-hunter.
- **Constat :** le test pose `FK_ConsignesSVTOrdreFabrication` sur la base partagée, sans `finally`. Si `new SqlServerIntegrationFixture()` lève avant de supprimer la FK, chaque `ResetData` suivant de la collection échoue sur le `TRUNCATE` de `L_D_ORDRE_FABRICATION`.
- **Action :** envelopper le test dans `try { … } finally { IF OBJECT_ID(N'dbo.FK_ConsignesSVTOrdreFabrication', N'F') IS NOT NULL ALTER TABLE dbo.L_D_SECTIONCHARGE_SVT DROP CONSTRAINT [FK_ConsignesSVTOrdreFabrication]; }`.

### P-4 — Rouge a posteriori pour le test de FK résiduelle et le repli littéral (issu de D-1, medium) — APPLIQUÉ 2026-10-07

- **Localisation :** `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs:251-253` ; `tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs:28-32`.
- **Action :** retirer temporairement la suppression `sys.foreign_keys` de la fixture, lancer `SchemaApplies_OverAForeignKeyLeftByAKilledRun_…` et constater l'échec, puis restaurer. Vider temporairement `CodeOperations`, lancer le test principal sans production configurée et constater l'échec Ask First, puis restaurer. Consigner les deux rouges dans le Spec Change Log et le message du commit de clôture.

### Résultat de l'application (2026-10-07)

- **P-1 :** `Hot()` remplit `CodeOpePoidMetrique`/`RangOpePoidMetrique` (`XP9`/`160`) et `CodeOpeSVT`/`RangOpeSVT` (`XVP`/`170`). Le test vérifie que les 7 sections sont présentes avant d'ajouter les FK. Test vert avec la copie production et vert avec les codes littéraux. **Mutation** (une fois) : `DeleteOf` supprime l'OF avant PoidsMetrique et SVT, et REPLACE échoue sur `FK_ConsignesPoidsMetriqueOrdreFabrication`. La mutation est annulée par `git checkout`, `src/` est intact.
- **P-2 :** `epic-6-context.md:28` dit maintenant 20 FK et mentionne le repli littéral ; ligne 50, le saut de ligne est inséré.
- **P-3 :** un `finally` supprime `FK_ConsignesSVTOrdreFabrication` si elle existe encore.
- **P-4 :**
  - Rouge 1 : sans la suppression des `sys.foreign_keys` dans la fixture, le test échoue avec « SQL Server test instance not reachable: Could not drop object 'dbo.L_D_ORDRE_FABRICATION'… ». Il n'échoue que lorsque `L_D_ORDRE_FABRICATION` précède `L_D_SECTIONCHARGE_SVT` dans `sys.tables`. **Cet ordre tourne d'une table à chaque construction de fixture** : 4 runs verts d'affilée, puis 2 rouges. **F-2 est donc confirmé et plus grave qu'estimé** : la garde ne voit pas la régression dans environ 9 positions sur 34.
  - Rouge 2 : production désactivée par une variable d'environnement vide et `CodeOperations` vidé, le test échoue sur l'Ask First (`CodeOperation 'XC1' is missing`). Restauré, il passe au vert.
  - Les deux rouges sont consignés dans le Spec Change Log.
- **Vérification :** `dotnet build -warnaserror` : 0 warning, 0 erreur. Unit : 1 260 réussis, 0 échec. Integration (`-m:1 --blame-hang-timeout 2m`) : 1 428 réussis, 0 échec, 20 ignorés. Les 20 ignorés sont des cas de `Kape22ProductionDataParityTests` sans ligne de production pour leur OF, sans lien avec cette story.
- **P-5 (ex-F-2, promu le 2026-10-07 sur proposition de l'utilisateur : « fixer définitivement l'ordre ») — APPLIQUÉ :** `DropExistingUserTables` concatène les `DROP TABLE` avec `STRING_AGG … WITHIN GROUP (ORDER BY s.name, t.name)`. Un `ORDER BY` sur `SELECT @sql +=` n'est pas garanti par SQL Server. Dans cet ordre, `L_D_ORDRE_FABRICATION` passe toujours avant `L_D_SECTIONCHARGE_SVT`. Preuve : sans la suppression des `sys.foreign_keys`, le test de FK résiduelle est rouge sur 5 runs consécutifs sur 5 ; restauré, il est vert sur 2 runs. Le correctif à base de FK croisées entre tables `L_P_*` est abandonné : l'utilisateur a fait remarquer qu'elles n'ont aucun lien avec les OF.

## 4. Defer

- ~~**F-1**~~ → **promu en P-6, voir ci-dessous.** **F-1 — Le critère 2 de la spec n'est vérifié qu'à la main** (`ProductionForeignKeyOrderIntegrationTests.cs:113-120`). Ce critère exige que `sys.foreign_keys` de `AscoLSI_Test` soit vide après le test, qu'il passe ou échoue. Aucun test automatique ne le lit après le `finally`. La seule preuve est l'exécution manuelle de la mutation. *Justification :* le chemin de récupération est couvert par la fixture et par P-3 ; une assertion post-`finally` exigerait un test enveloppe pour peu de gain. (verification-gap, blind-hunter)
- ~~**F-2**~~ → **promu en P-5, voir §3.** **F-2 — Le test de FK résiduelle dépend de l'ordre de `sys.tables`** (`PersistenceSmokeTests.cs:284` / `SqlServerIntegrationFixture.cs` drop query). Il ne détecte la régression que si `L_D_ORDRE_FABRICATION` est supprimée avant `L_D_SECTIONCHARGE_SVT`. Rien ne garantit cet ordre (aucun `ORDER BY`), mais il est favorable aujourd'hui (object_id 1194903774 < 1482904800). *Justification :* la régression est détectée sur l'instance actuelle ; durcir le test demande une FK croisée artificielle. (verification-gap)
- ~~**F-3**~~ → **promu en P-7, voir ci-dessous.** **F-3 — `Persist` et `AssertAccepted` sont dupliqués** (`ProductionForeignKeyOrderIntegrationTests.cs:123,197`). La Code Map demande de les réutiliser depuis `OfResendIntegrationTests`, où ils sont `private`. *Justification :* deux helpers de 3 lignes ; à factoriser dans `TestSupport` au 3e consommateur. (blind-hunter, acceptance-auditor out_of_mandate)
- ~~**F-4**~~ → **promu en P-8, voir ci-dessous.** **F-4 — Les 20 FK codées en dur ne sont pas comparées à la production quand elle est configurée** (`ProductionForeignKeyOrderIntegrationTests.cs:38-60`). Une FK ajoutée ou modifiée en production passerait inaperçue. *Justification :* la clause Always fige les FK « exactly as read 2026-10-06 » ; un contrôle de dérive est une évolution, pas un défaut de 6.11. (blind-hunter)

## 5. Rejetés (bruit)

| # | Finding | Source | Justification du rejet |
|---|---|---|---|
| R-1 | Une exception dans le `finally` masque l'échec réel et laisse des lignes `L_P_*` | edge-case, blind | Les `DROP` sont protégés par `IF OBJECT_ID`. `FillReferencedTables` vide les `L_P_*` au début et la fixture supprime toutes les FK. Il ne reste que le cas d'une perte de connexion. |
| R-2 | Production configurée mais injoignable : pas de repli littéral | edge-case, blind | Une production configurée est censée répondre ; un échec bruyant est voulu. C'est le même comportement que les autres tests production. D-2 ne vise que la CI non configurée. |
| R-3 | `Contains` ordinal contre une collation insensible à la casse ou aux espaces | edge-case | Les codes `NCHAR(3)` font tous 3 caractères en majuscules, en littéral comme en production. |
| R-4 | `ProfilProduit` null : fausse alerte Ask First | edge-case | La propriété est un `string` non nullable (`string profil = …`). |
| R-5 | La fixture `fresh` n'est jamais libérée | edge-case, blind | `SqlServerIntegrationFixture` n'implémente pas `IDisposable`. |
| R-6 | `ProductionConnectionString` reconstruit la configuration à chaque accès ; proposition de `Lazy` | edge-case, blind | Choix délibéré P-3 de la revue du build (lecture à la demande) ; coût négligeable. |
| R-7 | `CopyFromProduction` insère `where` et le nom de table bruts dans le SQL | blind | Helper de test, arguments constants, connexion production en lecture seule. |
| R-8 | `sys.columns` vide : erreur SQL opaque | edge-case | Les tables viennent toujours de `scripts/schema/` ; le test échoue de toute façon. |
| R-9 | `ResetData` ne supprime pas les FK | edge-case | La clause Always de la spec garde `ResetData` inchangé ; le `finally` et le constructeur de la fixture couvrent le cas. |
| R-10 | `FK_PlanFourOrdreFabrication` et `FK_OrdreFabricationPSO` ne sont jamais exercées | blind, verification-gap | Voulu : D34 refuse ces OF et D35 est accepté. |
| R-11 | `using Microsoft.Extensions.Configuration` inutilisé dans `LegacyRejectionParityTests` | blind | Encore utilisé : `ConfigurationBuilder` à la ligne 250. |
| R-12 | REPLACE ne vérifie ni `L_D_KAPE22` ni le journal | blind | Déjà couvert par `OfResendIntegrationTests` (6.10) ; ce test garde l'ordre des FK. |
| R-13 | Le cas résiduel ne couvre qu'une FK vers `L_D_ORDRE_FABRICATION` | blind | La fixture supprime toutes les `sys.foreign_keys`, quelle que soit la table référencée. |
| R-14 | `epic-6-context.md` a perdu des contraintes (`DownstreamOf.Pad`, database-first…) | blind | Contraintes propres à 6.10 (done) et hors du périmètre de 6.12 ; leurs specs les conservent. |
| R-15 | Spec `done` alors que sprint-status est en `review` | auditor, blind | Convention du projet : `/commit-review` clôt le sprint-status. |
| R-16 | `review_loop_iteration: 0`, lignes de la Code Map périmées, Tasks incomplètes | blind | Cosmétique, hors bloc gelé. |
| R-17 | `deferred-work.md` « RESOLVED 2026-10-06 » prématuré | blind | Cosmétique ; date de l'implémentation. |
| R-18 | La note de décision est placée dans le Given de `epics.md` et la PRD n'est pas synchronisée | blind | La PRD (`PRD.md:1034`) ne cite aucun nombre de FK, donc elle est déjà cohérente ; le placement de la note est une question de style. |
| R-19 | Commentaire du schéma sur `ResetData` inexact | blind | `FillReferencedTables` vide les tables d'abord ; le commentaire reste exact. |
| R-20 | `sprint-change-proposal-2026-10-06.md` dit encore 13 FK | auditor | Document historique ; la correction D-1 est datée dans la spec et dans `epics.md`. |

## 6. Auto-vérifications

- **Lentilles lancées (4/4, aucune en échec) :** blind-hunter (22 findings), edge-case-hunter (9), verification-gap (1 principal + 1 secondaire), acceptance-auditor (4 + 4 out_of_mandate).
- **Diff stats :** 56 fichiers, +664 / −133 sur le range complet. Diff soumis aux lentilles : 13 fichiers, +535 / −133, 980 lignes (`P89/raw/` exclu).
- **Vérifications faites par le parent :**
  - `Kape22ImportBundleMapperTests.cs:46,48` confirme que PoidsMetrique et SVT sont nulles (P-1).
  - `SqlServerIntegrationFixture` n'est pas `IDisposable` (R-5).
  - `LegacyRejectionParityTests.cs:250` utilise encore `ConfigurationBuilder` (R-11).
  - `PRD.md:1034` ne contient aucun nombre de FK (R-18).
  - `epic-6-context.md:28,50` confirmé (P-2).
- **Tests non relancés pendant la revue.** Le commit annonce Unit et Integration verts.

### Defers promus et appliqués (2026-10-07, à la demande de l'utilisateur)

Tous les Defer de la revue sont traités : il n'en reste plus aucun.

- **P-6 (ex-F-1) :** après le `finally`, le test principal vérifie `Assert.Equal(0, ForeignKeyCount(...))`, ce qui couvre le critère 2 de la spec dans le cas où le test passe. Preuve : sans la suppression des FK dans le `finally`, le test échoue. Les 20 FK laissées par cette mutation ont été supprimées par la fixture au run suivant.
- **P-7 (ex-F-3) :** `AssertAccepted`, `Persist(fixture, bundle)` et `ForeignKeyCount` sont déplacés dans `TestSupport`. Les copies privées sont supprimées dans `OfResendIntegrationTests`, `ProductionForeignKeyOrderIntegrationTests` et `PersistenceSmokeTests`.
- **P-8 (ex-F-4) :** nouveau test `ProductionForeignKeys_TouchingTheDispatch_AreExactlyTheGuardedList_AcFr21_7`, ignoré si la production n'est pas configurée. Il lit en production les FK qui touchent une table du dispatch (15 tables, `DispatchTables`) et vérifie qu'elles sont exactement les 20 de la liste, `NO_ACTION`, activées et de confiance. Exception : les FK d'une autre table vers `L_D_COULEE`, que le dispatch ne fait qu'insérer. Il y en a 5 en production : `L_D_ANOMALIES`, `L_D_PLANS_FOURS`, `L_D_PSO`, `L_D_REBUT.molding_id`, `L_D_STOCK_PSO`. Le commentaire de `ForeignKeys`, qui n'en citait que 3, est corrigé. Preuve : avec une FK renommée dans la liste, le test échoue. Les noms et types sont composés en C#, car la concaténation côté SQL provoque un conflit de collation (`Latin1_General_CI_AS_KS_WS` / `French_CI_AS`).
- **Vérification finale (après P-6..P-8) :** `dotnet build -warnaserror` : 0 warning. Unit : 1 260 réussis. Integration : 1 429 réussis, 0 échec, 20 ignorés (parité sans ligne de production). `sys.foreign_keys` de `AscoLSI_Test` est vide après le run.

## Clôture (2026-10-07)

- D-1 → tranché (option 1) : devient P-4, appliqué.
- P-1 → appliqué (sections PoidsMetrique et SVT renseignées, requalifié low, conservé par décision de l'utilisateur).
- P-2 → appliqué (`epic-6-context.md` : 20 FK, repli littéral, puces séparées).
- P-3 → appliqué (`finally` qui supprime la FK résiduelle).
- P-4 → appliqué (deux rouges a posteriori consignés dans le Spec Change Log).
- P-5 (ex-F-2) → appliqué (la fixture supprime les tables dans l'ordre des noms, via `STRING_AGG … WITHIN GROUP`).
- P-6 (ex-F-1) → appliqué (assertion `ForeignKeyCount == 0` après le `finally`).
- P-7 (ex-F-3) → appliqué (`AssertAccepted`, `Persist` et `ForeignKeyCount` déplacés dans `TestSupport`).
- P-8 (ex-F-4) → appliqué (contrôle de dérive des FK de production qui touchent le dispatch).
- Defer : aucun. F-1..F-4 ont été promus en patchs, rien n'est ajouté à `deferred-work.md`.
- Vérification finale : `dotnet build TextToXml.sln -warnaserror` : 0 warning, 0 erreur. Unit : 1 260 réussis (192 + 991 + 21 + 56). Integration (`-m:1 --blame-hang-timeout 2m`) : 1 429 réussis, 0 échec, 20 ignorés (parité sans ligne de production).
- Statut : story 6-11 → done. Epic 6 reste in-progress (6-12 en backlog). Le commit de clôture est celui qui ajoute cette section.
