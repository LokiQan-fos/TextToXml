# Review report — Story 6.7

Range : ce3f5aa^..HEAD (git TextToXml) — the MicroServices worker side (SVN, uncommitted) was read for context, not reviewed as diff
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-7-plafond-reessai-par-fichier.md
Date : 2026-10-01
Verdict : REFUSÉ (patches P-1..P-5 appliqués le 2026-10-01, voir §7)
Findings : D=0 P=5 F=2 R=12  (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**REFUSÉ.** Le code de production respecte la spec gelée : matrice I/O couverte ligne par ligne, Boundaries tenues (pas de `error/`, aucun nouveau type public hors `Frozen` et les deux `MaxAttempts`, aucun message existant modifié), CC-2, CC-4 et CC-7 conformes, `ReadConfig` refuse `< 1` dans les deux workers. Le refus repose sur un écart CC-3 (P-1) : le commentaire de `IsPersistenceFailure` est désormais détaché de sa méthode. Quatre autres patches mineurs suivent.

| Gravité | Nombre | IDs |
|---|---|---|
| high | 0 | — |
| medium | 0 | — |
| low | 7 | P-1..P-5, F-1, F-2 |

## 2. Decision (à trancher par l'humain)

Aucun.

## 3. Patch

### P-1 — CC-3 : commentaire de `IsPersistenceFailure` détaché de sa méthode (low)

- **Localisation :** `src/Kape22Importer/InboxScanner.cs:446-456`
- **Sources :** acceptance-auditor
- **Constat :** le nouveau helper `IsPersistenceError` a été inséré entre le bloc de commentaire AC-FR15-3 (« a returned result carrying a File-level PersistenceError… ») et `IsPersistenceFailure`. `IsPersistenceFailure` se retrouve sans commentaire, alors que `:293` renvoie encore « (see IsPersistenceFailure) ». C'est une violation de la préservation CC-3.
- **Action :** remettre le bloc existant directement au-dessus de `IsPersistenceFailure`, sans le modifier. Déclarer `IsPersistenceError` après, avec son propre commentaire d'une ligne (AC-FR25-8 : the File-level PersistenceError signal, shared by the outcome test and the retained-cause message).

### P-2 — Le comparer `OrdinalIgnoreCase` du compteur P60 n'est imposé que par un commentaire (low)

- **Localisation :** `src/Kape22Importer/InboxScanner.cs:22-32` (prune `:73-80`, `IsFrozen` `:136-140`, `Attempt` `:145-170`)
- **Sources :** edge-case-hunter + blind-hunter + acceptance-auditor
- **Constat :** le prune ignore la casse (`HashSet` en `OrdinalIgnoreCase`), mais la lecture, l'incrément et le `Remove` utilisent le comparer du dictionnaire fourni par l'appelant. Un `new Dictionary<string,int>()` ferait diverger les comptes entre variantes de casse, ce que la spec interdit (§Boundaries Always : « Name matching is OrdinalIgnoreCase »). Le seul appelant de production (`ImportP60/Client.cs:54`) est conforme, d'où la gravité low.
- **Action :** dans `RunTick`, avant le prune, lever `ArgumentException` (nommant `attempts`) si `attempts is not null && !ReferenceEquals(attempts.Comparer, StringComparer.OrdinalIgnoreCase)`. Ajouter un test `_AcFr25_8` sur un compteur Ordinal. Reformuler le commentaire de l'en-tête pour décrire le guard au lieu de la convention.

### P-3 — Le message `Frozen` P89 affiche le nom de la clé au lieu du nombre de tentatives (low)

- **Localisation :** `MicroServices/GPAO/ConvertP89/Client.cs:332-337` (SVN), test `GPAO/ConvertP89.Tests/LogOutcomeTests.cs:58`
- **Sources :** verification-gap + acceptance-auditor (hors mandat)
- **Constat :** le message est « …after P89:MaxAttempts consecutive deferrals… ». La Code Map de la spec demande « after <n> attempts » avec `n` = valeur configurée, et le message P60 affiche bien le nombre (`{Attempts}`). Le test vérifie seulement le nom du Fichier et les raisons, il ne voit donc pas l'écart.
- **Action :** passer `int maxAttempts` à `LogOutcome` (paramètre, sans nouveau type) et écrire « …after {maxAttempts} consecutive deferrals (P89:MaxAttempts)… ». Faire porter l'assertion de `LogOutcomeTests` sur le nombre. Le commit SVN est à la charge de l'utilisateur.

### P-4 — Note de design « un tick annulé ne remet pas le compte à zéro » sans test (low)

- **Localisation :** spec §Design Notes « Consecutive », code `src/Kape22Importer/InboxScanner.cs:82-88` et `src/P89Converter/P89FolderConverter.cs:84-88`
- **Sources :** blind-hunter
- **Constat :** le comportement est juste (le prune s'appuie sur le listing, pas sur le parcours), mais aucun test `_AcFr25_8` ne fige une annulation avant le Fichier compté.
- **Action :** ajouter un test P60 : MaxAttempts = 3, deux ticks retenus, un tick avec token annulé, puis un tick retenu. Assert : Error au 4ᵉ tick effectif et processeur appelé 3 fois.

### P-5 — Cosmétique documentaire (low)

- **Localisation :** `src/Kape22Importer/ImportOptions.cs:9-11`, `_bmad-output/implementation-artifacts/deferred-work.md:1438-1439`
- **Sources :** blind-hunter
- **Constat :** l'en-tête de `ImportOptions` enchaîne deux « , and … » (« …and XmlExportPath, …, and MaxAttempts… »). Dans `deferred-work.md`, la nouvelle rubrique story-6.7 n'a pas de ligne vide sous le titre, et son `source_spec` n'a pas l'étiquette `(id, gravité)` que portent les entrées voisines.
- **Action :** réécrire l'énumération de l'en-tête en une seule liste. Ajouter la ligne vide et l'étiquette.

## 4. Defer

### F-1 — Un Fichier gelé, remplacé sur place sous le même nom, reste gelé jusqu'au redémarrage (low)

- **Localisation :** `src/P89Converter/P89FolderConverter.cs:72-101`, `src/Kape22Importer/InboxScanner.cs:73-97`
- **Sources :** edge-case-hunter
- **Justification :** la clé est le nom, et « until restart » est la règle de la spec gelée. Le chemin opérateur P60 normal (dépôt dans l'inbox) remet bien le compte à zéro (`:128-130`, test `RunTick_InboxFichierReplacingAFrozenOne_StartsAfresh_AcFr25_8`). Le cas ne se produit que si quelqu'un réécrit directement `processing/` ou le dossier source P89. Pour le couvrir, il faudrait associer `LastWriteTimeUtc` à la clé, ce qui demande une renégociation de la spec.

### F-2 — Le câblage `_attempts` du Client P60 n'est pas testé au niveau instance (low)

- **Localisation :** `MicroServices/GPAO/ImportP60/Client.cs:54`, `:378-386`
- **Sources :** verification-gap
- **Justification :** `RunTickCoreTests` fournit son propre dictionnaire. Une mutation qui recrée un compteur à chaque tick passerait tous les tests. Le paramètre obligatoire (P-4 interne) oblige à fournir un dictionnaire, et le champ fait une ligne. Un test instance demande le harnais Client de la Story 6.5 (MicroServices, `Gpao.IntegrationTests`), hors du périmètre unitaire de la story.

## 5. Rejetés (bruit)

- **R-1 — Fichier disparu entre le listing et la lecture, compté comme retenu** (edge-case-hunter) : il faudrait que la disparition tombe exactement sur le N-ième tick. Le prune du tick suivant supprime la clé, et le Warning existant formule déjà la même affirmation.
- **R-2 — Gel sans Error si `MaxAttempts` descend sous un compte en cours** (`==` contre `>=`, blind-hunter) : les options sont lues une seule fois dans `Configure` (`_configured.Options`), sans rechargement. Cas inatteignable.
- **R-3 — L'Error P60 ne nomme pas `Import:MaxAttempts`** (blind-hunter) : la matrice gelée exige « Error naming Fichier + last cause », ce qui est fait.
- **R-4 — Le compteur incrémente encore quand le plafond est désactivé** (blind-hunter) : le prune borne la mémoire, et le comportement observable est celui d'aujourd'hui, comme la spec le demande.
- **R-5 — L'insertion de `Frozen` décale l'ordinal de `Rejected`** (blind-hunter) : aucun consommateur n'utilise la valeur entière (grep sur GPAO : seulement `switch`/`Assert.Equal` par membre). L'ordre alphabétique est imposé par CC-4 et la Code Map.
- **R-6 — Le « ticks never overlap » n'est pas rappelé dans le code** (blind-hunter) : il l'est, à `ImportP60/Client.cs:52-53`. Le convertisseur P89 n'a qu'un appelant séquentiel.
- **R-7 — Spec `done` / sprint `review` / `review_loop_iteration: 0` / « RESOLVED » avant clôture** (blind-hunter + acceptance-auditor hors mandat) : c'est la même convention que la Story 6.6 (`668b38f`). `/commit-review` ferme la boucle.
- **R-8 — L'échappement non-I/O est différé sans route** (blind-hunter) : il est déjà consigné dans `deferred-work.md` par la story. C'est un chemin préexistant.
- **R-9 — Les numéros de ligne de la Code Map datent d'avant le changement** (blind-hunter) : la Code Map est écrite avant l'implémentation, par construction. Le Suggested Review Order utilise les numéros post-changement.
- **R-10 — Les changements MicroServices sont absents du diff** (blind-hunter) : l'acceptance-auditor les a vérifiés dans SVN (`ReadConfig` < 1, champ, JSON, tests). Le commit SVN reste dû par l'utilisateur.
- **R-11 — Le corps du commit liste 7 tests sur 15, sans attestation CC-1** (acceptance-auditor hors mandat) : le message de commit est hors diff, et son format (« + N other ») suit les commits 6.6.
- **R-12 — Tests supplémentaires demandés** (blind-hunter) : Fichier gelé parmi des voisins P89, casse P89, `Rejected` qui supprime la clé, nombre d'appels au journal, jointure multi-erreurs. Les branches en jeu sont uniques (`Status != Deferred`, même comparer que P60 déjà testé, `string.Join`). Ce serait du rembourrage de couverture sans risque identifié.

## 6. Auto-vérifications

- **Lentilles lancées : 4/4**, toutes revenues. Aucune `failed_layers`.
  - blind-hunter (diff seul)
  - edge-case-hunter (accès projet)
  - verification-gap (accès projet + MicroServices)
  - acceptance-auditor (`project-profile.md` + spec + `epic-6-context.md`)
- **Diff stats :** 10 fichiers, +684 / −24, 966 lignes.
  - Production : `ImportOptions.cs` (8), `InboxScanner.cs` (84), `P89FolderConverter.cs` (48), `P89Options.cs` (5)
  - Tests : `InboxScannerTests.cs` (245), `P89FolderConverterTests.cs` (136), `TestSupport.cs` (14)
  - Artefacts : spec (153), `deferred-work.md` (11), `sprint-status.yaml` (4)
- **Code relu au-delà des hunks :**
  - `InboxScanner.cs` en entier
  - `P89FolderConverter.cs:1-140`
  - `ImportP60/Client.cs:50-58`, `:370-392`
  - `ConvertP89/Client.cs:312-345`
  - grep des usages de `P89FichierStatus` dans GPAO
  - conventions de statut du commit 6.6
- **Traits :** 15 tests `_AcFr25_8` dans le diff (8 P60, 7 P89), plus les tests worker côté SVN.
- **Contrôles de format :**
  - CC-4 vérifié : `ImportOptions`, `P89Options`, enum `Converted/Deferred/Frozen/Rejected`, `RecordingJournal`.
  - CC-2 vérifié : commentaires en anglais.
  - CC-3 : en défaut (P-1).

## 7. Application des patches (2026-10-01)

Choix utilisateur : option 1, tous les patches appliqués. Non commité.

- **P-1 :** `InboxScanner.cs` : le commentaire AC-FR15-3 est de nouveau au-dessus de `IsPersistenceFailure`. `IsPersistenceError` est déplacé dessous, avec son propre commentaire.
- **P-2 :** `InboxScanner.RunTick` lève `ArgumentException(nameof(attempts))` si le compteur n'est pas en `OrdinalIgnoreCase`. Le commentaire d'en-tête est réécrit. Nouveau test `RunTick_CounterNotOrdinalIgnoreCase_Throws_AcFr25_8`, rouge sans le guard (vérifié), vert avec.
- **P-3 (MicroServices, SVN) :**
  - `ConvertP89/Client.cs` : `LogOutcome` reçoit `int maxAttempts`, alimenté par le champ `_maxAttempts` renseigné dans `Configure`. Nouveau message : « …after {n} consecutive deferrals (P89:MaxAttempts)… ».
  - `LogOutcomeTests` vérifie le nombre (rouge à la compilation, puis vert).
- **P-4 :** nouveau test `RunTick_CancelledTick_KeepsTheCount_AcFr25_8`. Il passe d'emblée, car il fige un comportement existant décrit dans la note de design.
- **P-5 :** l'en-tête de `ImportOptions` est réécrit en une seule énumération. La rubrique story-6.7 de `deferred-work.md` a sa ligne vide et son étiquette.

Vérification :

| Commande | Résultat |
|---|---|
| `dotnet build TextToXml.sln -warnaserror` | 0 warning, 0 erreur |
| `dotnet test TextToXml.sln --filter Category=Unit` | 1 204 tests verts (Kape22Importer 935, TextToXml 192, P89Converter 56, AscoLsiJournal 21) |
| `dotnet build MicroServices.sln -warnaserror` | 0 warning, 0 erreur |
| `GpaoConvertP89.Tests` | 68 tests verts |
| `GpaoImportP60.Tests` | 70 tests verts |

Restent pour `/commit-review` :

- Inscrire F-1 et F-2 dans `deferred-work.md`.
- Passer la story à `done` dans `sprint-status.yaml`.
- Faire le commit git.
- Le commit SVN de P-3 reste à la charge de l'utilisateur.

## Clôture (2026-10-01)

- P-1 → appliqué (`InboxScanner.cs` : commentaire AC-FR15-3 rattaché à `IsPersistenceFailure`, `IsPersistenceError` commenté dessous).
- P-2 → appliqué (guard `ArgumentException(nameof(attempts))` dans `RunTick` + `RunTick_CounterNotOrdinalIgnoreCase_Throws_AcFr25_8`).
- P-3 → appliqué côté MicroServices (`ConvertP89/Client.cs`, `LogOutcomeTests`) ; hors du dépôt git, commit SVN à la charge de l'utilisateur.
- P-4 → appliqué (`RunTick_CancelledTick_KeepsTheCount_AcFr25_8`).
- P-5 → appliqué (en-tête `ImportOptions`, ligne vide + étiquette dans `deferred-work.md`).
- F-1 → tracé dans `deferred-work.md` (non planifié ; demande une renégociation de la règle « until restart »).
- F-2 → tracé dans `deferred-work.md` (non planifié ; harnais Client Story 6.5, MicroServices).

Vérification finale :

| Commande | Résultat |
|---|---|
| `dotnet build TextToXml.sln -warnaserror` | 0 warning, 0 erreur |
| `dotnet test TextToXml.sln --filter Category=Unit` | 1 204 verts (Kape22Importer 935, TextToXml 192, P89Converter 56, AscoLsiJournal 21) |
| `dotnet test TextToXml.sln --filter Category=Integration -m:1` | 1 253 verts, 18 ignorés (Kape22Importer 1 251 + 18 skipped, AscoLsiJournal 2) |

Statut : story 6-7 → done ; épic 6 reste in-progress (6-8 en backlog). Commit de clôture = celui qui ajoute cette section.
