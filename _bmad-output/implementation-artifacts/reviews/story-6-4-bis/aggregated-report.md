# Review report — Story 6.4-bis

Range : c9ea60714421672770b50ad5a145b8f544b8be1f^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-4-bis-realigner-harnais-e2e-worker-p60-config-6-4.md
Date : 2026-10-01
Verdict : REFUSÉ
Findings : D=2 P=5 F=2 R=13  (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**REFUSÉ.** Le script ne prouve pas l'AC-2 tel qu'il est rédigé (« *its* logs », c'est-à-dire ceux du worker, doivent arriver dans `MQTTnetServices_Test`). L'assertion compte n'importe quelle nouvelle ligne `Logs`, et le defer D-1 du même commit reconnaît que les lignes du worker n'y arrivent jamais. Le spec ne contient aucune note de renégociation datée. Le verdict sera revu une fois D-1 tranché.

Sévérités après triage : high 0 · medium 2 (D-1, P-3) · low 7.

## 2. Findings Decision (à trancher par l'humain)

### D-1 — L'AC-2 « worker logs → MQTTnetServices_Test » n'est pas prouvé (medium)
- **Source :** acceptance-auditor + blind-hunter + edge-case-hunter
- **Localisation :** `scripts/e2e-worker-import.ps1:206` ; spec §Tasks & Acceptance, AC-2
- **Description :** la boucle d'attente fait `SELECT COUNT(*) FROM dbo.Logs WHERE Id > $logsIdBefore`, sans filtre sur la source. Les lignes Broker / CopyDataToDb du Launcher suffisent à la satisfaire. Le `deferred-work.md` du même commit (D-1 6.4-bis) dit que les lignes « Fichier ... processed » de GpaoImportP60 n'atteignent jamais la table : le sink est batché (5 s) et n'est jamais flushé, car le Launcher est tué par `Stop-Process -Force`. La story est marquée done sans note de renégociation datée.
- **Options :**
  - (a) Renégocier l'AC-2 : ajouter dans le spec une note datée qui réduit l'AC aux « lignes du logger partagé du Launcher pendant ce run » et renvoie au defer D-1.
  - (b) Corriger maintenant : arrêter le Launcher proprement ou forcer un flush, puis filtrer l'assertion sur la source GpaoImportP60. Cela sort probablement du périmètre script-only de la story.
  - (c) Laisser la story en `in-progress` jusqu'à la résolution de D-1.
- **État :** à trancher par l'humain.

### D-2 — `epic-6-context.md` modifié hors du périmètre « Ask First » (low)
- **Source :** acceptance-auditor
- **Localisation :** `_bmad-output/implementation-artifacts/epic-6-context.md:15,40`
- **Description :** la frontière « Ask First » vise « any change outside `scripts/e2e-worker-import.ps1` ». Le commit ajoute 6.4-bis à la liste des stories et réécrit la phrase sur l'ordre strict, sans gate humain enregistré. Les autres fichiers hors script (`sprint-status.yaml`, `deferred-work.md`, le spec) relèvent du suivi BMAD standard.
- **Options :**
  - (a) Ratifier : les artefacts de suivi BMAD sont hors du périmètre de la frontière. Aucune action.
  - (b) Revenir sur la modification de `epic-6-context.md`.
- **État :** à trancher par l'humain.

## 3. Findings Patch

### P-1 — La boucle d'attente ne gère pas une table `Logs` absente (low)
- **Source :** blind-hunter
- **Localisation :** `scripts/e2e-worker-import.ps1:206`
- **Description :** la requête de baseline gère l'absence de la table (`IF OBJECT_ID('dbo.Logs') IS NULL SELECT 0`), pas la requête d'attente. Avec `-b`, une table encore absente fait lever « The Logs check query failed. » dès la première itération, au lieu du message « No Logs row was written ».
- **Action corrective :** reprendre la même garde dans la requête d'attente : `IF OBJECT_ID('dbo.Logs') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM dbo.Logs WHERE Id > $logsIdBefore`.

### P-2 — `Server` et `Database` ne sont pas validés après le parsing (low)
- **Source :** edge-case-hunter + blind-hunter + acceptance-auditor (hors mandat)
- **Localisation :** `scripts/e2e-worker-import.ps1:127`
- **Description :** `DbConnectionStringBuilder` ne résout pas les synonymes. Avec `Data Source=` / `Initial Catalog=` (la syntaxe déjà utilisée par l'entrée `AscoLSI_Production` du même fichier), sqlcmd reçoit un `-S` ou un `-d` vide et l'erreur qui en résulte est confuse.
- **Action corrective :** juste après le parsing, lever une erreur si `$logsDb['Server']` ou `$logsDb['Database']` est vide. Le message doit dire que `ConnectionStrings:MQTTnetServices` doit utiliser `Server=` / `Database=`.

### P-3 — Aucune garde n'empêche une cible Logs de production (medium)
- **Source :** edge-case-hunter
- **Localisation :** `scripts/e2e-worker-import.ps1:72` / `:127`
- **Description :** la garde n'écarte que la valeur vide. Une valeur `MQTTnetServices` qui pointe vers une base de production passe, ce qui contredit la frontière « Always » du spec : « the Logs sink of the Launcher run targets `MQTTnetServices_Test`, never production ».
- **Action corrective :** lever une erreur avant l'étape 0 si `$logsDb['Database']` ne correspond pas à `*_Test`. Pour cela, parser la chaîne de connexion à côté de la garde de la ligne 72, avant toute action ayant un effet de bord.

### P-4 — Une sortie sqlcmd vide devient 0 sans erreur (low)
- **Source :** edge-case-hunter + blind-hunter + acceptance-auditor (hors mandat)
- **Localisation :** `scripts/e2e-worker-import.ps1:130`, `:208`
- **Description :** `[long]($x | Select-Object -First 1)` convertit `$null` en 0. Une baseline vide à 0 ferait compter les anciennes lignes comme nouvelles, et l'assertion passerait à tort.
- **Action corrective :** prendre la première ligne entièrement numérique (`-match '^\s*\d+\s*$'`) et lever une erreur si aucune ne l'est.

### P-5 — Les Design Notes et les Tasks du spec ne décrivent pas la vérification `Logs` (low)
- **Source :** blind-hunter
- **Localisation :** spec §Design Notes (ligne 59) et §Tasks & Acceptance
- **Description :** les Design Notes disent encore « the fix is a deletion plus a fail-fast guard ». La baseline `Logs.Id` et la boucle d'attente (P-1/P-2 du build) ne figurent ni dans les Tasks ni dans le Code Map.
- **Action corrective :** ajouter une ligne aux Tasks et une phrase aux Design Notes pour décrire la baseline et l'attente `Logs` (sections non gelées).

## 4. Findings Defer

### F-1 — sqlcmd ignore les identifiants de la chaîne de connexion (low)
- **Source :** edge-case-hunter + blind-hunter
- **Localisation :** `scripts/e2e-worker-import.ps1:127`
- **Justification :** l'authentification Windows est la convention existante du script (`Invoke-Sql`, ligne 77, code `-S localhost` en dur), et la chaîne de test utilise `Trusted_Connection=True`. Ce n'est pas introduit par ce changement, et l'aligner demanderait de revoir toutes les invocations sqlcmd du script.

### F-2 — `Id > baseline` compte aussi les lignes d'autres processus (low)
- **Source :** edge-case-hunter + blind-hunter
- **Localisation :** `scripts/e2e-worker-import.ps1:206`
- **Justification :** un Launcher orphelin d'un run précédent (problème connu) ou un autre écrivain peut satisfaire l'assertion. Le correctif naturel est de filtrer sur la source GpaoImportP60, ce qui dépend de la résolution de D-1. Rien n'est actionnable avant.

## 5. Findings rejetés (bruit)

| # | Finding | Source | Justification du rejet |
|---|---------|--------|------------------------|
| R-1 | Spec `status: 'done'` alors que sprint-status est à `review` | blind + auditor | Bookkeeping que `/commit-review` aligne à la clôture |
| R-2 | `review_loop_iteration: 0` périmé | blind | Métadonnée cosmétique |
| R-3 | La commande de vérification du spec filtre sur `TimeStamp`, le script sur `Id` | blind | Vérification manuelle ; les deux méthodes sont valides |
| R-4 | Références de lignes du Code Map périmées | blind | Cosmétique ; le Code Map décrit l'état d'avant le changement par construction |
| R-5 | Aucun résultat consigné pour les runs d'acceptance | blind | Le format du spec ne l'exige pas ; story déjà validée en session (mémoire 6.4-bis) |
| R-6 | La garde LOG_TARGET_MISSING n'a pas de test automatisé | verification-gap + blind | Décision humaine déjà prise le 2026-09-29 (option [b], contrôle manuel consigné dans le spec) |
| R-7 | Variables d'environnement non restaurées dans `finally` | blind | Pré-existant ; le script tourne via `pwsh -File` (processus enfant), donc rien ne fuit |
| R-8 | Délai d'attente de 15 s non justifié | blind | 3 périodes de batch de 5 s ; sans conséquence |
| R-9 | L'en-tête ne dit pas que la base `MQTTnetServices_Test` doit exister | blind | La base existe déjà (prérequis Epic 6, fixture d'intégration) |
| R-10 | D-2 (export FR-26) devrait être corrigé dans cette story | blind | Hors du périmètre du spec ; déjà suivi dans deferred-work |
| R-11 | Puce de dépendances surchargée dans epic-6-context | blind | Rédactionnel |
| R-12 | `dbo.Logs` en dur, alors que la table est surchargeable (`MICROSERVICE_LOG_TABLE_NAME`) | edge + blind | Le harnais ne pose pas cette surcharge ; `Logging:TableName` = `Logs` dans `microservice.settings.json` ; schéma par défaut `dbo` |
| R-13 | Pas de timeout de login ou de requête pour sqlcmd | edge | SQL Server local ; même convention que le reste du script ; `--blame-hang-timeout 2m` borne le run |

## 6. Auto-vérifications

- **Lentilles lancées (4/4, aucun échec) :** blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor.
- **Diff stats :** 5 fichiers, +148 / −8. Code : `scripts/e2e-worker-import.ps1` (+41/−8). Le reste est de la documentation BMAD (spec +98, deferred-work +10, epic-6-context +3/−1, sprint-status +4/−2).
- **Code relu hors diff :** `SharedLogger.cs` (`AutoCreateSqlTable = true`, table surchargeable), `MicroServiceSettings.LogTableName`, `Launcher/microservice.settings.json` (`TableName: Logs`), `appsettings.Test.json` (syntaxe `Server=` / `Database=` pour `MQTTnetServices`).
- **Critères transverses :** CC-2 respecté (commentaires en anglais). CC-7 : aucun secret ajouté par le diff. Frontière « Never » respectée (aucun code de production modifié, aucune modification du checkout MicroServices).

## 7. Résolution (2026-10-01)

- **D-1** → option 1 : AC-2 renégocié (note datée dans le spec, limité aux lignes du logger partagé du Launcher ; preuve des lignes worker = defer D-1 6.4-bis).
- **D-2** → option 1 : ratifié, les artefacts de suivi BMAD sont hors frontière « Ask First ». Aucune action.
- **P-1..P-5** → appliqués (option 1) :
  - `scripts/e2e-worker-import.ps1` : chaîne Logs parsée en amont (avant l'étape 0), `Server=`/`Database=` exigés, base `*_Test` exigée ; requête d'attente gardée par `OBJECT_ID` ; `ConvertFrom-SqlScalar` remplace les casts `[long]`/`[int]` silencieux.
  - Spec : une tâche et une phrase des Design Notes décrivent la baseline + l'attente `Logs`.
- **F-1, F-2** → ajoutés à `deferred-work.md`.
- **Vérification :** `pwsh -File scripts/e2e-worker-import.ps1 -SkipProductionCompare` → exit 0 en 12 s, 2 lignes `Logs` ; `dotnet test ... --filter FullyQualifiedName~GpaoImportP60WorkerEndToEndTests` → 1/1 vert (21 s). Un premier run avec `--blame-hang-timeout 2m` avait été interrompu (dépassement d'inactivité), sans orphelin, et la config worker était intacte ; non reproduit.
- **Statut final :** story `done` (sprint-status synchronisé). Verdict initial REFUSÉ levé par les résolutions ci-dessus.

## Clôture (2026-10-01)

- D-1 → tranché (option a) : AC-2 renégocié, note datée dans le spec ; preuve des lignes worker = defer D-1 6.4-bis.
- D-2 → tranché (option a) : ratifié, aucune action.
- P-1 → appliqué : garde `OBJECT_ID` sur la requête d'attente `Logs`.
- P-2 → appliqué : `Server=` / `Database=` exigés après parsing de la chaîne Logs.
- P-3 → appliqué : base Logs `*_Test` exigée, contrôle avant l'étape 0.
- P-4 → appliqué : `ConvertFrom-SqlScalar` remplace les casts silencieux `[long]` / `[int]`.
- P-5 → appliqué : tâche + phrase des Design Notes dans le spec.
- F-1 → tracé dans deferred-work.md (non planifié).
- F-2 → tracé dans deferred-work.md (non planifié, dépend de D-1 6.4-bis).
- Vérification finale : build `-warnaserror` 0 warning / 0 erreur ; Unit 1178/1178 (192 + 48 + 21 + 917) ; Integration 1253 passés, 18 ignorés, 0 échec (1251/1269 Kape22Importer + 2/2 AscoLsiJournal).
- Statut : story 6-4-bis → done ; epic-6 reste in-progress (6-5 en backlog) ; commit de clôture = celui qui ajoute cette section.
