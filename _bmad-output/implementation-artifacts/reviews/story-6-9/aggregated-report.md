# Review report — Story 6.9

Range : 2130d41^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-9-controle-coulee-froide-typeconsigne-12.md
Date : 2026-10-02
Verdict : REFUSÉ (patches P-1..P-7 appliqués le 2026-10-02, P-8 à la clôture, voir §7)
Findings : D=0 P=8 F=4 R=19  (Decision, Patch, Defer, Rejetés)

> Écart de ciblage (ÉTAPE 1) : la règle littérale `chore(story-N.M)` désigne 6.8, déjà revue et clôturée (0dad5c4). La story 6.9 n'a qu'un seul commit, `fix(story-6.9)` (2130d41), et elle est en `review` dans sprint-status. L'utilisateur a confirmé la cible 6.9 au CHECKPOINT.

## 1. Verdict

**REFUSÉ** : un Patch de gravité medium reste ouvert. P-1 : la limite déclarée dans la clause *Always* de la spec, « null/empty `CodeConsignePits` is hot (no exception) », n'est couverte par aucun test.

Le correctif lui-même est conforme :

- `Kape22Persister.cs:98` applique le prédicat `StartsWith(ColdConsignePits, Ordinal)`, null-safe, identique au golden example des Design Notes.
- La forme du rejet AC-FR20-5 est inchangée.
- Les tests REAL_* (407/408, 428→430, 412) et la théorie sur le code complet couvrent la matrice I/O.
- Aucune violation de CC-2, CC-4 ni CC-7, aucune violation d'AD, aucun *Never* exécuté.
- La modification des assertions `L_D_COULEE` sur 6 tests existants est couverte par l'entrée step-04 du Spec Change Log, arbitrée par l'utilisateur.

Le seul commentaire hors du frozen block resté « hot » concerne `Kape22ImportBundleMapperTests` (P-3).

| Gravité | Nombre | IDs |
|---|---|---|
| high | 0 | — |
| medium | 1 | P-1 |
| low | 7 | P-2..P-8 |

## 2. Decision (à trancher par l'humain)

Aucune. L'unique Decision proposée par l'acceptance-auditor (réécriture des commentaires de deux tests « "1" » existants) est rejetée, voir R-1.

## 3. Patch

### P-1 — La clause « null/empty CodeConsignePits is hot » n'a aucun test (medium)

- **Localisation :** `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:419-420` (théorie `Persist_FullCodeConsignePits_ColdOnlyOnFirstCharacter_AcFr20_5`). Prédicat : `src/Kape22Importer/Persistence/Kape22Persister.cs:98`.
- **Sources :** verification-gap, edge-case-hunter, blind-hunter, acceptance-auditor (out_of_mandate)
- **Constat :** la clause *Always* de la spec impose « null/empty `CodeConsignePits` is hot (no exception) ». Le champ n'est pas obligatoire (`Kape22File.cs:154`, défaut `string.Empty`), donc un code vide atteint le persister. Pourtant, toutes les valeurs testées sont non vides : `"1"`, `"1 207 00 000"`, `"3 148 00 740"`, `'9'×30`. Une réécriture plausible comme `CodeConsignePits[0] == '1'` ou `Substring(0, 1) == ColdConsignePits` (la forme de `ConsignesMapper.cs:73`) lèverait une exception sur un code vide, et la suite resterait verte.
- **Action corrective :**
  - Ajouter `[InlineData("", true)]` à la théorie : Fichier accepté, ligne Coulée insérée, aucune exception.
  - Si le mapper normalise `""` en `null`, adapter l'assertion `Assert.Equal(codeConsignePits, bundle.Kape22!.CodeConsignePits)` pour ce cas, ou muter directement `bundle.Kape22.CodeConsignePits = null`.
  - Si `ConsignesMapper` rejette un code PC1 vide, le cas doit le démontrer et la clause doit alors être vérifiée par mutation directe du bundle.

### P-2 — `AssertOnlyTheSeededReferenceCoulee` ne vérifie pas tous les champs « unchanged » (low)

- **Localisation :** `tests/Kape22Importer.Tests/TestSupport.cs:107-114`
- **Sources :** blind-hunter, edge-case-hunter
- **Constat :** l'entrée step-04 du Spec Change Log promet que chaque test « asserts the remaining row is exactly the seeded one (IdCoulee, fields unchanged) ». Le helper ne compare pourtant que `IdCoulee`, `Nuance`, `DateReception` et `DerniereModif`. Une modification de `EtatReception`, `NbLingotRestantARefroidir` ou `Externe` par un import rejeté ou annulé passerait inaperçue.
- **Action corrective :** ajouter `Assert.Equal(seeded.EtatReception, row.EtatReception)`, ainsi que les mêmes assertions pour `NbLingotRestantARefroidir` et `Externe`.

### P-3 — Commentaire et nom de test « hot » périmés dans `Kape22ImportBundleMapperTests` (low)

- **Localisation :** `tests/Kape22Importer.Tests/Kape22ImportBundleMapperTests.cs:181-187`
- **Sources :** verification-gap
- **Constat :** le commentaire dit « the reference Fichier is hot (CodeConsignePits is a real 12-char consigne code, never literally "1") ». Le test s'appelle `Map_UnmutatedReferenceFichier_HotExternalCoulee_Succeeds`. Depuis la 6.9, le code de référence `"1 205 00 999"` est froid, comme le dit désormais `TestSupport.cs:66-68`. La tâche de la spec demandait de corriger les commentaires « hot » périmés, et celui-ci a été oublié. Le test reste vrai, car le mapper ne juge pas le froid ; seule la justification est fausse.
- **Action corrective :**
  - Réécrire le commentaire : le mapper n'applique aucun contrôle de froid, la référence est froide (6.9) avec une Coulée externe `165718`, et le contrôle AC-FR20-5 relève du persister.
  - Renommer le test en `Map_UnmutatedReferenceFichier_ExternalCoulee_Succeeds`.

### P-4 — NFR-1/NFR-2 ne prouvent pas que les ticks insèrent au lieu de rejeter (low)

- **Localisation :** `tests/Kape22Importer.Tests/EndToEndPerformanceTests.cs:100-113`, `:141`
- **Sources :** edge-case-hunter
- **Constat :** le commentaire 6.9 affirme que le seed permet au tick de mesurer « a real insert rather than a cold-Coulee rejection ». Aucune assertion ne le vérifie. `Assert.Empty(source.Names(InboxRoot))` passe aussi quand tous les Fichiers partent en `error/`, et NFR-1 n'a aucune assertion de résultat. Si le seed ratait, la mesure porterait silencieusement sur des rejets.
- **Action corrective :** après les ticks, vérifier qu'aucun Fichier n'est dans `error/` (par exemple `Assert.Empty(source.ListRecursive("error"))`, ou l'équivalent sur le dossier d'erreur des `Options()`), pour chaque scanner NFR-1 et pour le scanner NFR-2.

### P-5 — REAL_COLD_PRESENT ne vérifie ni la Coulée seedée ni l'absence de ré-insertion (low)

- **Localisation :** `tests/Kape22Importer.Tests/ColdCouleeRealFichierTests.cs:69-76`
- **Sources :** blind-hunter
- **Constat :** le retour de `SeedCouleeOf("P60_847_682_412")` est ignoré. Le test ne vérifie pas non plus que `L_D_COULEE` contient toujours une seule ligne après l'import, donc la branche « cold + present ⇒ accepted, no re-insert » n'est pas épinglée sur données réelles. Les deux autres tests REAL_* font cette vérification (`Assert.Equal("063196", SeedCouleeOf(...))`).
- **Action corrective :** remplacer l'appel par `Assert.Equal("063241", SeedCouleeOf("P60_847_682_412"))`, et après l'import ajouter `Assert.Single(verify.CouleeRows.AsNoTracking())`.

### P-6 — Script E2E : `$coulee` non échappé, commentaire de fidélité exagéré (low)

- **Localisation :** `scripts/e2e-worker-import.ps1:164-166` (commentaire), `:178` (`$coulee`)
- **Sources :** blind-hunter, edge-case-hunter
- **Constat :** `$nuance` est échappé (`Replace("'", "''")`), pas `$coulee`, alors que les deux sont interpolés dans le même SQL. Par ailleurs, le commentaire « the row CouleeMapper builds » est inexact : le script insère `GETDATE()` pour les deux dates et des états à zéro, pas les valeurs du mapper.
- **Action corrective :** appliquer `.Replace("'", "''")` à `$coulee` dans la requête, sans toucher la valeur gardée dans `$seededCoulees` ni le `DELETE` du `finally`, qui doit lui aussi être échappé. Reformuler le commentaire : « a minimal Coulee row (IdCoulee, Nuance, current timestamps, zero states) ».

### P-7 — Retours à la ligne irréguliers dans les commentaires réécrits (low)

- **Localisation :** `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:362`, `:808-812` (bloc A-4)
- **Sources :** blind-hunter, acceptance-auditor
- **Constat :** la ligne 362 dépasse nettement la largeur des lignes voisines (« … no downstream entity, one REJETÉ log row »). Dans le bloc A-4, une ligne s'arrête court (« … back to a private duplicate »).
- **Action corrective :** refaire le retour à la ligne des deux blocs à la largeur habituelle (~110 colonnes) sans changer le texte.

### P-8 — `last_updated` de sprint-status recule (low)

- **Localisation :** `_bmad-output/implementation-artifacts/sprint-status.yaml:33`
- **Sources :** blind-hunter, acceptance-auditor
- **Constat :** la valeur passe de `10-02-2026 17:00` à `10-02-2026 15:23`. Le fichier de suivi paraît plus ancien que sa version précédente.
- **Action corrective :** à la clôture (`/commit-review`), fixer `last_updated` à l'horodatage réel de clôture, au format `MM-DD-YYYY HH:MM`.

## 4. Defer

- **F-1 — Nom du test A-4 périmé.** `TransactionalPersistenceTests.cs:808` : `ColdConsignePits_IsTheOneSharedConstantBothCollaboratorsReference_A4` affirme que deux collaborateurs référencent la constante. Seul le persister le fait, et c'était déjà le cas avant la 6.9 (l'erreur du commentaire a été corrigée dans cette story, pas le nom). Le défaut est antérieur à la story ; renommer touche à la traçabilité A-4. *Justification :* préexistant, cosmétique.
- **F-2 — Aucun test de niveau Unit pour le prédicat froid.** Tous les nouveaux tests sont en Integration et sont ignorés sans l'instance SQL locale. Un run Unit seul ne prouve donc rien sur le correctif. *Justification :* convention du projet (AR-12, tests d'intégration sur SQL Server local) ; rejoint l'action item de la rétro Épic 4 sur les assertions downstream au niveau Unit.
- **F-3 — Modification MicroServices non revue.** `GPAO/ImportP60.Tests/EndToEndSmokeTests.cs` est hors diff (SVN) et n'a pas été revu ici ; le commit SVN reste à faire par l'utilisateur. Vérifié pendant la revue : `Gpao.IntegrationTests` (contenu « not a valid Fichier »), `ClientRobustnessTests` (`[0xFF]`) et `RunTickCoreTests` (octets en mémoire) n'importent aucune référence froide, donc la clause « if affected » de la tâche est satisfaite. *Justification :* hors dépôt git.
- **F-4 — Câblage de test dupliqué.** `ColdCouleeRealFichierTests.Counts()` (liste des 11 tables) et `Import()` (`ImportOptions` + configuration du processor) dupliquent `RejectionAtomicityIntegrationTests.AssertNoDispatchRows`, les listes en ligne de `TransactionalPersistenceTests` et les `Processor()` d'autres classes. *Justification :* nettoyage de test hors du périmètre 6.9, sans impact fonctionnel.

## 5. Rejetés (bruit)

| # | Source | Constat | Justification du rejet |
|---|---|---|---|
| R-1 | acceptance-auditor (Decision) | Commentaires réécrits sur deux tests « "1" » existants sans entrée au Change Log | La clause *Always* « tests stay unchanged » vise le comportement (setup, assertions), qui est inchangé. Les anciens commentaires citaient `CodeConsignePits == ColdConsignePits`, un code qui n'existe plus ; les garder aurait laissé un commentaire faux. Aucun CC enfreint. Le retour à la ligne est traité en P-7. |
| R-2 | blind-hunter, acceptance-auditor | Spec `status: 'done'` alors que sprint-status indique `review` | Convention bmad-build (spec 6.8 identique) ; `/commit-review` passe sprint-status à `done`. |
| R-3 | blind-hunter, acceptance-auditor | Matrice I/O « L_D_KAPE22 + 9 downstream tables », alors que les tests en comptent 10 | Texte du bloc frozen ; les tests vérifient les 11 tables, ce qui est plus strict. Aucun défaut de comportement. |
| R-4 | blind-hunter, acceptance-auditor | `P60/error/P60_847_682_{446,447,449}` commités sans test | Décision utilisateur step-01, consignée dans le Change Log (step-04) : les 6 Fichiers `P60/error/` sont commités et serviront à la 6.10. |
| R-5 | blind-hunter | Le persister réimplémente la tranche TypeConsigne 12 au lieu de la partager avec `ConsignesMapper` | L'*Approach* de la spec impose un correctif d'une ligne dans le persister, et la constante est partagée (A-4). Aller plus loin relève du *Ask First* (« change outside the cold predicate »). |
| R-6 | blind-hunter | Le caractère froid des fixtures 407/408/430 n'est épinglé par aucune assertion | Captures réelles commitées, immuables. Le « red before fix » a été vérifié au build (AC de la spec). |
| R-7 | blind-hunter | `logRows[^1]` suppose un ordre d'insertion | `SqlServerIntegrationFixture.LogRows()` trie par `Id` (`SqlServerIntegrationFixture.cs:147`). |
| R-8 | blind-hunter, edge-case-hunter | `SeedCoulee` compare `IdCoulee` sans `Trim` | Le seed et le persister consomment la même sortie du mapper. Sur SQL Server, la comparaison ignore le padding final. Aucun chemin réel ne produit de doublon. |
| R-9 | blind-hunter | `InMemoryContextFactory` seed toujours, sans opt-out | YAGNI : aucun test Unit actuel n'a besoin d'une `L_D_COULEE` vide. À ajouter quand un test l'exigera. |
| R-10 | blind-hunter | NFR-1/2 ne chronomètrent plus la construction des scanners | La construction ne fait pas partie du pipeline mesuré. Le changement est documenté dans le Suggested Review Order et les budgets sont inchangés. |
| R-11 | blind-hunter | La branche acceptée de la théorie ne vérifie que des comptes ; la branche rejetée ne vérifie pas que le journal nomme la Coulée | Le message d'erreur exact est vérifié. Le contenu du journal est couvert par le helper REAL_* (`ColdCouleeRealFichierTests.cs:98`). |
| R-12 | blind-hunter | Le seed du script code en dur `-S localhost -d AscoLSI_Test` | `Invoke-Sql` (`e2e-worker-import.ps1:121`) cible exactement la même base : aucune divergence possible. |
| R-13 | blind-hunter | Offsets du Detail codés en dur dans le script | Le harnais PowerShell n'a pas accès au mapper C#. Les positions sont tracées depuis `Templates/P60.xml`, et un écart ferait échouer le run E2E, pas le masquer. |
| R-14 | blind-hunter | `[Encoding]::Latin1` indisponible sous PowerShell 5.1 | Le harnais est lancé sous pwsh 7 (tests E2E, voir story 6.4-bis). |
| R-15 | blind-hunter, edge-case-hunter | Les Coulées seedées restent en base avec `-KeepArtifacts` | Comportement voulu de `-KeepArtifacts`. `AscoLSI_Test` est réinitialisée par les runs de tests. |
| R-16 | edge-case-hunter | Un `INSERT` validé suivi d'un échec de `ConvertFrom-SqlScalar` laisse une ligne non suivie | Cas théorique. Base de test réinitialisée ; aucune perte de données. |
| R-17 | edge-case-hunter | Coulée vide dans le script | Le mapper rejette de toute façon un Fichier sans Coulée (champ obligatoire). Le run E2E échouerait de manière visible. |
| R-18 | edge-case-hunter | Instances `L_D_COULEE` partagées (`Lazy`) entre plusieurs contextes | Chaque contexte est disposé après `SaveChanges`, sur une base in-memory distincte : pas de suivi d'entité partagé. |
| R-19 | blind-hunter | `Gpao.IntegrationTests` (« if affected ») non traité | Vérifié : aucun test MicroServices hors `EndToEndSmokeTests` n'importe une référence froide (voir F-3). |

## 6. Auto-vérifications

- **Lentilles lancées :** 4/4 (blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor), en sous-agents parallèles. Aucune n'a échoué (`failed_layers` vide).
- **Diff stats :** `git diff 2130d41^..HEAD`, 25 fichiers, +658 / −51, 1146 lignes de diff. Un seul commit : 2130d41.
- **Contexte chargé :** spec 6.9, `epic-6-context.md`, `project-profile.md`.
- **Vérifications de code faites au tri :** `SqlServerIntegrationFixture.LogRows` (tri par Id), `TestSupport` (helpers de seed), `EndToEndPerformanceTests` (le contenu seedé correspond au contenu importé), `ConsignesMapper.cs:55-78`, `e2e-worker-import.ps1:120` (`Invoke-Sql`), tests MicroServices `ImportP60.Tests` et `Gpao.IntegrationTests`, statut de la spec 6.8 (convention `done`).
- **Traçabilité AC :** les nouveaux tests portent `[Trait("AC","FR20-5")]` (et `FR11-5` pour le rollback) ; le commit liste les 5 tests AC.
- **Non exécuté :** aucun `dotnet test` pendant la revue (revue statique). Le commit indique une suite verte.

## 7. Patches appliqués (2026-10-02)

Choix utilisateur : « Apply every patch ». Patches non commités, à clôturer via `/commit-review`.

| ID | État | Changement |
|---|---|---|
| P-1 | appliqué | `[InlineData("", true)]` ajouté à la théorie ; l'assertion sur la valeur mappée tolère `null` (`?? string.Empty`). Le mapper ne rejette pas un code PC1 vide (`ConsignesMapper.AddSection` l'ignore), le cas est accepté et la Coulée insérée. |
| P-2 | appliqué | `AssertOnlyTheSeededReferenceCoulee` compare aussi `EtatReception`, `Externe`, `NbLingotRestantARefroidir`. |
| P-3 | appliqué | Commentaire réécrit (référence froide, mapper sans contrôle de froid) ; test renommé `Map_UnmutatedReferenceFichier_ExternalCoulee_Succeeds`. Le test homonyme `RejectionAtomicityIntegrationTests.Import_HotExternalCoulee_SucceedsRatherThanRejects_AcFr20_3` garde son nom (lié au suffixe AC-FR20-3, hors périmètre P-3). |
| P-4 | appliqué | NFR-1 : sources gardées, `Assert.All(sources, … Assert.Empty(source.ListRecursive("error")))` ; NFR-2 : `Assert.Empty(source.ListRecursive("error"))`. Helper `SingleFichierScanner` → `SingleFichierSource`. |
| P-5 | appliqué | `Assert.Equal("063241", SeedCouleeOf(...))` et `Assert.Single(verify.CouleeRows.AsNoTracking())` après l'import. |
| P-6 | appliqué | `$couleeSql` échappé dans le seed, `DELETE` du `finally` échappé, commentaire « minimal row ». Script parsé sans erreur (`Parser.ParseFile`). |
| P-7 | appliqué | Retours à la ligne refaits (ligne 362 et bloc A-4), texte inchangé. |
| P-8 | à la clôture | `last_updated` sera fixé par `/commit-review` en même temps que le passage à `done`. |

Vérification :
- `dotnet build TextToXml.sln -warnaserror` : 0 warning, 0 erreur.
- `dotnet test --filter Category=Unit` : 1253 passés, 0 échec (Kape22Importer 984, TextToXml 192, P89Converter 56, AscoLsiJournal 21).
- `dotnet test --filter Category=Integration -m:1 --blame-hang-timeout 2m` : 1405 passés, 0 échec, 20 ignorés (`Kape22ProductionDataParityTests`, dépendants de la production, préexistant).

Defer F-1..F-4 : à reporter dans `deferred-work.md` par `/commit-review`.

## Clôture (2026-10-02)

- P-1 → appliqué (`[InlineData("", true)]`, code vide accepté, Coulée insérée)
- P-2 → appliqué (`AssertOnlyTheSeededReferenceCoulee` compare `EtatReception`, `Externe`, `NbLingotRestantARefroidir`)
- P-3 → appliqué (commentaire réécrit, test renommé `Map_UnmutatedReferenceFichier_ExternalCoulee_Succeeds`)
- P-4 → appliqué (NFR-1/NFR-2 vérifient qu'aucun Fichier ne part en `error/`)
- P-5 → appliqué (Coulée seedée `063241` vérifiée, `Assert.Single` sur `L_D_COULEE` après import)
- P-6 → appliqué (`$coulee` échappé dans le seed et le `DELETE`, commentaire « minimal row »)
- P-7 → appliqué (retours à la ligne refaits, texte inchangé)
- P-8 → appliqué (`last_updated` fixé à l'horodatage réel de clôture, `10-02-2026 15:53`)
- F-1 → tracé dans deferred-work.md (non planifié)
- F-2 → tracé dans deferred-work.md (non planifié, rejoint l'action item de la rétro Épic 4)
- F-3 → tracé dans deferred-work.md (commit SVN dû par l'utilisateur)
- F-4 → tracé dans deferred-work.md (non planifié)

Vérification finale :
- `dotnet build TextToXml.sln -warnaserror` : 0 warning, 0 erreur.
- `dotnet test --filter Category=Unit` : 1253 passés, 0 échec (Kape22Importer 984, TextToXml 192, P89Converter 56, AscoLsiJournal 21).
- `dotnet test --filter Category=Integration -m:1 --blame-hang-timeout 2m` : 1405 passés, 0 échec, 20 ignorés (`Kape22ProductionDataParityTests`, préexistant).

Statut : story 6-9 → done ; épic 6 reste in-progress (6.10 en backlog). Commit de clôture = celui qui ajoute cette section.
