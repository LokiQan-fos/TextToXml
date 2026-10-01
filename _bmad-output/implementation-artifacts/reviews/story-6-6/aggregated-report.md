# Review report — Story 6.6

Range : 668b38f^..HEAD (git TextToXml) + SVN r545 (MicroServices)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-6-validation-config-panne-partage-workers-gpao.md
Date : 2026-10-01
Verdict : REFUSÉ (findings tous traités le 2026-10-01, voir §7)
Findings : D=1 P=4 F=0 R=13  (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**REFUSÉ** : un finding Decision est ouvert, quatre Patch restent à appliquer, et le PRD n'est pas réconcilié (`AC-FR25-10`). Le code de production respecte la spec et les règles AC-FR25-9/10 sont couvertes par des tests. Le refus porte sur la configuration livrée et sur la documentation.

Gravité des findings conservés :

| Gravité | Nombre | IDs |
|---|---|---|
| high | 0 | — |
| medium | 2 | D-1, P-1 |
| low | 3 | P-2, P-3, P-4 |

Note sur la cible : le commit git `b76d5ab feat(story-6.6)` a été reformulé en `668b38f chore(story-6.6)` (amend accepté par l'utilisateur) pour que `/run-review` le détecte. Le code des workers vit dans MicroServices (SVN r545). Il est inclus dans le diff relu.

## 2. Decision (à trancher par l'humain)

### D-1 — `GpaoConvertP89.json` livré avec les valeurs de test (medium) — à trancher par l'humain

- **Localisation :** `MicroServices/GPAO/ConvertP89/GpaoConvertP89.json:6-14`
- **Sources :** blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor
- **Constat :** r545 remplace les valeurs P89 vides par `Server=localhost;Database=AscoLSI_Test;…` et par les dossiers `C:\Temp\GpaoConvertP89_E2E`, `…\done`, `…\error` et `C:\Temp\GpaoConvertP89_E2E_Export`. La Code Map de la spec dit : « has empty values (filled per deployment) … Nothing to change ». Aucune note datée n'explique l'écart. Le fichier est copié en sortie (`GpaoConvertP89.csproj:31`) et c'est lui que le Launcher charge (`ConfigPath`).
- **Atténuation :** `GpaoImportP60.json` contient déjà des valeurs du même type, et les workers GPAO n'ont jamais été déployés. Pas de risque immédiat en production.
- **Options :**
  1. Revenir aux valeurs vides, comme la spec le prévoit. Un démarrage local se fait alors avec un JSON surchargé.
  2. Garder les valeurs (alignement sur P60, poste de dev) et ajouter à la spec une note datée qui le justifie.
- **Décision humaine (2026-10-01) : option 2.** Valeurs conservées. Note datée ajoutée sous la Code Map de la spec (« Amended 2026-10-01 (review D-1, human decision) »). D-1 est résolu.

## 3. Patch

### P-1 — `AC-FR25-10` non réconcilié : « Warning inchangé » vs `Error` conservé (medium)

- **Localisation :** `_bmad-output/planning-artifacts/PRD.md:1173-1176`, `_bmad-output/implementation-artifacts/epic-6-context.md:31`
- **Sources :** acceptance-auditor (out_of_mandate), blind-hunter
- **Constat :**
  - Le PRD dit : un dossier de réception injoignable fait échouer le tick, avec « log `Warning` inchangé (`AC-FR15-2`) ». L'epic context reprend « same `Warning` as before ».
  - La spec approuvée (Design Notes, « Log Warning unchanged ») lit la règle comme « aucun niveau de log ne change ». Le dossier absent garde l'`Error` de la Story 6.4 via `OnTickError`. Seul l'échec de listing reste un `Warning`. Le code suit cette lecture.
  - L'epic context dit aussi que la forme du signal est « decided at spec checkpoint ». Elle est décidée : `bool` renvoyé par `InboxScanner.RunTick`.
  - La règle projet impose de réconcilier un AC modifié dans le même commit.
- **Action corrective :**
  - Réécrire `AC-FR25-10` dans le PRD : « niveaux de log inchangés : `Warning` de listing (`AC-FR15-2`), `Error` d'un dossier absent (`AC-FR25-5`) ; aucun heartbeat ; … ».
  - Aligner `epic-6-context.md:31` et y noter la décision (`RunTick` → `bool`).

### P-2 — README des workers exigés, alors que la spec interdit d'en créer (low)

- **Localisation :** `_bmad-output/implementation-artifacts/epic-6-context.md:34`, `_bmad-output/planning-artifacts/epics.md:3101`
- **Sources :** acceptance-auditor (out_of_mandate), blind-hunter
- **Constat :** les deux fichiers demandent de documenter les clés « in the worker READMEs » (« README des workers à jour »). Or aucun README n'existe, et la spec range la création de README dans ses Never.
- **Action corrective :** remplacer par « documented in the worker JSON files (no worker README exists) » dans les deux fichiers.

### P-3 — Le test P89 « base nommée acceptée » n'asserte rien (low)

- **Localisation :** `MicroServices/GPAO/ConvertP89.Tests/ClientConfigurationTests.cs:221`
- **Source :** blind-hunter
- **Constat :** le test appelle seulement `Client.ReadConfig(...)`. Son jumeau P60 (`ImportP60.Tests/ClientConfigurationTests.cs:257`) vérifie la chaîne de connexion renvoyée. D32 demande le même traitement dans les deux workers.
- **Action corrective :** `Assert.Equal(connectionString, Client.ReadConfig(Config(values)).<connection string property>);`, avec la propriété équivalente de la configuration P89.

### P-4 — Annulation dans la boucle `processing/` non couverte (low)

- **Localisation :** `src/Kape22Importer/InboxScanner.cs:69-72` (branche), `tests/Kape22Importer.Tests/InboxScannerTests.cs:662`
- **Source :** blind-hunter
- **Constat :** `RunTick_NoListingFault_ReturnsTrue_AcFr25_10` annule le jeton seulement avec un Fichier dans l'inbox. La branche `return true` de la boucle `processing/` n'est donc jamais exécutée, et une mutation qui la passerait à `false` survivrait.
- **Action corrective :** ajouter au même test un cas avec un Fichier dans `processing/` et un jeton annulé, puis `Assert.True`.

## 4. Defer

Aucun nouveau. D-1 (`Import:Commande` vide) et D-2 (assertion sur le nom de lambda généré), différés pendant la revue de build de 6.6, figurent déjà dans `deferred-work.md:1428-1436`.

## 5. Rejetés (bruit)

| ID | Constat | Source | Motif du rejet |
|---|---|---|---|
| R-1 | `Import:Commande` vide ou blanc accepté | edge-case, blind | Déjà différé (D-1 de la revue de build, `deferred-work.md:1430`). |
| R-2 | Assertion fragile sur `<Actions>b__` | blind, auditor | Déjà différé (D-2, `deferred-work.md:1434`). |
| R-3 | `AttachDbFilename` sans `Initial Catalog` refusé | edge-case | Configuration non utilisée par ces workers. `AC-FR25-9` exige explicitement `Initial Catalog` / `Database`. |
| R-4 | Jeton déjà annulé avant `Directory.Exists` | edge-case | La vérification tourne dans `Task.Run`. `Stop()` attend au plus `ShutdownBudget` (`Task.WhenAny`), donc une latence de partage ne bloque pas l'arrêt. |
| R-5 | La purge tourne après une panne de partage | blind | `PurgeRetention` contient les `IOException` en `Warning` sans `onError` (`InboxScanner.cs:175`), dans la tâche du tick. La spec maintient explicitement la purge. |
| R-6 | `RetentionDays` / `StabilityQuietPeriod` non numériques non testés | blind | L'exception du binder nomme déjà le chemin de la clé. Comportement antérieur commun à toutes les clés liées, hors du périmètre d'AC-FR25-9. |
| R-7 | Deux messages différents pour `RetentionDays` | blind | Choix délibéré (absent / hors bornes). Les deux nomment la clé, comme la spec l'exige. |
| R-8 | Ordre des vérifications et constantes dupliquées entre P60 et P89 | blind | D32 porte sur les règles, pas sur l'ordre. La duplication entre deux workers indépendants est assumée. |
| R-9 | La vérification n'exécute pas l'Integration de TextToXml | blind | Seul le type de retour de `RunTick` change (les appelants qui l'ignorent compilent). L'AC de la spec inclut `Gpao.IntegrationTests`. |
| R-10 | Changement de `src/` sans Ask First (`deferred-work` L49) | blind | `InboxScanner.cs` figure dans la Code Map approuvée au checkpoint de la spec. |
| R-11 | Spec `done` vs sprint-status `review` | blind, auditor | État normal avant revue. La section 6 du workflow synchronise. |
| R-12 | `AC-FR12-9` (≤ 0 = purge off) non réconcilié | blind | La bibliothèque est inchangée. Le PRD `AC-FR25-9` borne déjà `RetentionDays` à 1..3650 pour `GpaoImportP60`. |
| R-13 | Code Map : mauvais fichier et mauvaises lignes pour `RunTickCore_WhenTheFileSourceThrows` | blind | Faux positif. La Code Map cite bien `RunTickCoreTests.cs`, et ses numéros de ligne correspondent à la baseline. |

## 6. Auto-vérifications

- **Lentilles lancées (4/4, aucune en échec) :**
  - blind-hunter : 16 constats.
  - edge-case-hunter : 4 constats.
  - verification-gap : aucun écart de vérification, plus 1 constat hors catégorie.
  - acceptance-auditor : 1 constat, plus 4 hors mandat.
- **Diff git `668b38f^..HEAD` :** 6 fichiers, +227 / −16. Fichiers : `InboxScanner.cs`, `InboxScannerTests.cs`, spec, `deferred-work.md`, `epic-6-context.md`, `sprint-status.yaml`.
- **Diff SVN r545 :** 8 fichiers, +300 / −38.

  | Fichier | Lignes |
  |---|---|
  | `ImportP60/Client.cs` | +60 / −23 |
  | `ImportP60.Tests/ClientConfigurationTests.cs` | +126 |
  | `ImportP60.Tests/ClientRobustnessTests.cs` | +21 / −1 |
  | `ImportP60.Tests/RunTickCoreTests.cs` | +9 / −7 |
  | `ConvertP89/Client.cs` | +12 / −2 |
  | `ConvertP89.Tests/ClientConfigurationTests.cs` | +56 |
  | `ConvertP89.Tests/RunTickCoreTests.cs` | +11 |
  | `ConvertP89/GpaoConvertP89.json` | +5 / −5 |

- **Diff combiné :** 1 046 lignes, sans découpage.
- **Vérifié dans le code avant classement :**
  - `ImportP60/Client.cs` : `ReadConfig`, `ValidAscoLsiConnectionString`, `Actions` / `Task.Run`, `RunTickCore`.
  - `InboxScanner.RunTick` et `PurgeRetention`.
  - Le jumeau P60 du test d'acceptation.
  - L'historique SVN de `GpaoConvertP89.json` (r534, r540, r545).
  - PRD `AC-FR25-9/10`, epics 6.6, `deferred-work.md` D-1/D-2.
- **Constats écrits dans la spec :** section `### Review Findings`.

## 7. Application des findings (2026-10-01)

Décision de l'utilisateur : option 1, appliquer tous les patchs.

| ID | État | Changement |
|---|---|---|
| D-1 | résolu (option 2) | Valeurs conservées ; note datée ajoutée sous la Code Map de la spec. |
| P-1 | appliqué | PRD `AC-FR25-10` : « niveaux de log inchangés : `Warning` de listing (`AC-FR15-2`), `Error` de dossier absent (`AC-FR25-5`) », note datée. `epic-6-context.md:31` aligné, forme du signal consignée (`RunTick` → `false`). `epics.md:3099` complété de la même façon. |
| P-2 | appliqué | `epic-6-context.md:34` et `epics.md:3101` : clés documentées dans les JSON des workers, aucun README n'existe. |
| P-3 | appliqué, avec un écart | `ConvertP89.Tests/ClientConfigurationTests.cs:221` : `Assert.Null(Record.Exception(() => Client.ReadConfig(...)))`. Le `WorkerConfiguration` de P89 n'expose pas de chaîne de connexion, donc l'assertion du jumeau P60 ne peut pas être reprise sans changer le code de production. Fichier modifié dans MicroServices : commit SVN à faire par l'utilisateur. |
| P-4 | appliqué | `InboxScannerTests.cs` : un Fichier dans `processing/` avec un jeton annulé → `true`. Mutation `return true` → `return false` sur la première branche d'annulation de `RunTick` : le test échoue (rouge), puis le code est restauré. |

Vérifications :

- `dotnet test tests/Kape22Importer.Tests --filter InboxScannerTests` : 33/33 verts.
- `dotnet test GPAO/ConvertP89.Tests --filter ClientConfigurationTests` : 43/43 verts.

Statut final : story `done` (spec et `sprint-status.yaml`). Aucun finding high ou medium ouvert.

## Clôture (2026-10-01)

- D-1 → tranché (option 2) : valeurs de dev conservées dans `GpaoConvertP89.json`, note datée sous la Code Map de la spec.
- P-1 → appliqué : PRD `AC-FR25-10`, `epic-6-context.md`, `epics.md` réconciliés (niveaux de log inchangés, `RunTick` → `false`).
- P-2 → appliqué : `epic-6-context.md` et `epics.md` renvoient aux JSON des workers (aucun README).
- P-3 → appliqué avec écart (assertion `Record.Exception` nulle, P89 n'expose pas la chaîne de connexion) ; fichier MicroServices, commit SVN à faire par l'utilisateur.
- P-4 → appliqué : cas `processing/` + jeton annulé dans `InboxScannerTests.cs`.
- Defers : aucun nouveau (D-1/D-2 de la revue de build déjà dans `deferred-work.md`).
- Vérification finale : `dotnet build TextToXml.sln -warnaserror` 0 warning ; Unit 1181/1181 verts (192 + 21 + 920 + 48) ; Integration 1253 verts, 18 ignorés, 0 échec (1251/18 + 2).
- Statut : story 6-6 → done ; epic-6 reste in-progress (6.7, 6.8 en backlog) ; commit de clôture = celui qui ajoute cette section.
