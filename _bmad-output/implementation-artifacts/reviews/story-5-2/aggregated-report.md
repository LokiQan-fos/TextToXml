# Review report — Story 5.2

Range : 1ca2e195f83e371e17eed1ce170960410ab92eae^..HEAD (1ca2e19) + SVN MicroServices r534 (code du worker, commité par l'utilisateur)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-5-2-worker-gpaoconvertp89-launcher.md
Date : 2026-09-28
Verdict : ACCEPTÉ
Findings : D=0 P=7 F=5 R=16  (Decision, Patch, Defer, Rejetés)
Mise à jour 2026-09-28 : P-1..P-7 appliqués dans les arbres de travail (option « tout appliquer ») :
- MicroServices (SVN, non commité) : `Client.cs`, `RunTickCoreTests.cs`, propriété `svn:global-ignores` = `bin`/`obj` sur les deux dossiers.
- TextToXml (git, non commité) : spec (Spec Change Log), `epic-5-context.md`, `deferred-work.md:1305-1306` réduit à P60.

Vérifications :
- `dotnet build MicroServices.sln -warnaserror` : 0 warning, 0 erreur.
- `GpaoConvertP89.Tests` : 34/34.
- `Launcher.Tests` : 4/4.

F-1..F-5 restent à consigner par `/commit-review`, qui passera aussi sprint-status à `done`.

## 1. Verdict

**ACCEPTÉ**, à une condition : appliquer P-1..P-7 dans le commit de clôture (`/commit-review`). Ce verdict suit le précédent de la Story 5.1, qui avait été acceptée sous conditions avec des findings medium.

Constats qui fondent le verdict :
- **Recâblage conforme au bloc figé.**
  - `P89FolderConverter(P89Options, IFichierJournal, TimeProvider)` est construit avec un `AscoLsiFichierJournal`.
  - `GpaoConvertP89` ne référence que `AscoLsiJournal`, `MicroService` et `P89Converter`, sans `Kape22Importer`.
  - L'`ArgumentException` de la bibliothèque est renommée pour citer la clé complète `AscoLsiJournal:InitiatingServer`.
- **Matrice couverte.** Chaque ligne de configuration a un test `ReadConfig_*_AcFr22_8`. Chaque ligne de tick a un test `RunTickCore_*`.
- **Frontières respectées.** `src/` de TextToXml n'est pas modifié. Aucun nouveau package. Pas de Docker, pas de `P60/tmp/`.
- **Critères transverses.** Aucune déviation CC-2/CC-3 (commentaires en anglais, au-dessus du bloc). CC-4 est respecté : clés de dossiers triées dans `ReadConfig`, `ProjectReference` dans l'ordre alphabétique. CC-7 : aucun chemin ni chaîne de connexion en dur.
- **Écart principal (P-1).** L'invariant figé « `Actions` never throws » (§ Boundaries, Always) n'est pas tenu : l'appel `Connect()` est hors du `try`. La story l'a elle-même consigné comme defer au lieu de le corriger, alors que la correction tient en une ligne.

| Sévérité | Nombre |
|---|---|
| high | 0 |
| medium | 3 (P-1, P-2, F-1) |
| low | 9 (P-3..P-7, F-2..F-5) |

## 2. Findings Decision — à trancher par l'humain

Aucun.

## 3. Findings Patch

### P-1 — `Actions` peut lever une exception : `Connect()` est hors du `try` (medium)

- **Localisation :** `MicroServices/GPAO/ConvertP89/Client.cs:158-161`
- **Source :** acceptance-auditor + blind-hunter + edge-case-hunter.
- **Constat :**
  - Le bloc figé impose « `Actions` never throws » (§ Boundaries, Always). Le commentaire de la ligne 154 le promet aussi.
  - Pourtant, `await Connect()` est appelé avant le `try`. Une erreur de connexion au broker remonte donc jusqu'au callback du timer du Publisher, ce qui arrête le worker.
  - `deferred-work.md:1305` a consigné ce point comme defer partagé avec P60, sans note de renégociation datée dans le spec.
- **Action corrective :**
  1. Déplacer `if (!IsConnected) { await Connect(); }` et `if (!IsConnected) return;` à l'intérieur du `try` de `Actions`.
  2. Réduire l'entrée `deferred-work.md:1304-1306` au seul `GPAO/ImportP60/Client.cs:137`, que la story ne couvre pas.

### P-2 — `svn:global-ignores = *` masque tout futur fichier source (medium)

- **Localisation :** propriétés SVN des dossiers `MicroServices/GPAO/ConvertP89` et `MicroServices/GPAO/ConvertP89.Tests` (r534).
- **Source :** blind-hunter + edge-case-hunter + verification-gap + acceptance-auditor.
- **Constat :**
  - `svn proplist -v` donne `*` pour les deux dossiers, alors que `GPAO/ImportP60` a `bin` et `obj`.
  - La propriété est héritée. Un `.cs` ajouté plus tard n'apparaîtra donc ni dans `svn status` ni dans un `svn add` récursif.
  - La Code Map ne demandait que de déversionner `bin` et `obj`.
- **Action corrective :** exécuter `svn propset svn:global-ignores` avec la valeur `bin` + saut de ligne + `obj` sur les deux dossiers, comme pour `GPAO/ImportP60`. Le commit SVN reste à faire par l'utilisateur.

### P-3 — Matrice figée étendue sans note de renégociation (low)

- **Localisation :**
  - `spec-5-2-worker-gpaoconvertp89-launcher.md:28-41` (matrice) ;
  - `MicroServices/GPAO/ConvertP89/Client.cs:111-136` (`NormalizedFolder`) ;
  - `_bmad-output/planning-artifacts/PRD.md:1053-1059`.
- **Source :** acceptance-auditor.
- **Constat :**
  - Le code refuse aussi les dossiers relatifs ou mal formés (point P5 de la revue interne de bmad-build), et AC-FR22-8 a été élargi dans le PRD en conséquence.
  - La matrice figée n'a pas de ligne pour ces deux cas, et le spec n'a pas de note datée.
  - Le comportement lui-même est correct.
- **Action corrective :** ajouter au spec une note de renégociation datée du 2026-09-28. Elle doit rattacher les lignes « dossier relatif » et « dossier mal formé » (tests `ReadConfig_RelativeFolder_*` et `ReadConfig_MalformedFolder_*`) au périmètre d'AC-FR22-8.

### P-4 — `epic-5-context.md` décrit l'état d'avant la Story 5.2 (low)

- **Localisation :** `_bmad-output/implementation-artifacts/epic-5-context.md:25`, `:36`, `:41-42`
- **Source :** blind-hunter + edge-case-hunter.
- **Constat :** le fichier a été réécrit dans ce même commit, mais il est déjà périmé :
  - la ligne 41 dit « 5.2 is next » ;
  - la ligne 42 dit que le worker « no longer compiles » ;
  - la ligne 36 liste comme ouverts les éléments reportés au worker, alors que `deferred-work.md` les marque RESOLVED ou KEPT ;
  - la règle Worker (ligne 25) ne reflète pas l'AC-FR22-8 étendu (dossiers absolus et distincts, AscoLSI, longueur d'InitiatingServer).
- **Action corrective :** mettre à jour ces lignes pour décrire l'état en fin de Story 5.2.

### P-5 — La réécriture d'`epic-5-context.md` a supprimé trois énoncés (low)

- **Localisation :** `epic-5-context.md`. Les lignes supprimées correspondent à ces trois énoncés du diff `1ca2e19` :
  - « Move to done happens after the XML write… » ;
  - « at least 3 real fixtures… » ;
  - « the project is reopened for this epic and re-closes at Story 5.2 closure ».
- **Source :** edge-case-hunter (lentille de suppression).
- **Constat :** on ne retrouve plus nulle part dans le fichier l'invariant d'ordre en cas de crash, le plancher de fixtures, ni la règle de réouverture et refermeture du projet. Cette dernière règle s'applique précisément à la clôture de la Story 5.2.
- **Action corrective :** rétablir les trois énoncés, sous « Technical Decisions » et « Cross-Story Dependencies ».

### P-6 — Tests du 2ᵉ AC non tagués (low)

- **Localisation :** `MicroServices/GPAO/ConvertP89.Tests/RunTickCoreTests.cs:37-38`
- **Source :** blind-hunter + acceptance-auditor.
- **Constat :**
  - Le 2ᵉ AC du spec (tick sans exception, le tick suivant s'exécute) n'a pas d'identifiant.
  - Son test, `RunTickCore_MissingSourceFolder_ReportsTheErrorOnEveryTickWithoutThrowing`, n'a ni `[Trait("AC", …)]` ni suffixe `_AcFr…`, contrairement à la règle de l'epic (« tests tagged `[Trait("AC", "FR2x-…")]` », `epic-5-context.md:26`).
- **Action corrective :** ajouter `[Trait("AC", "FR22-8")]` et renommer le test avec le suffixe `_AcFr22_8`. Le rattachement à AC-FR22-8 s'explique ainsi : ce test est la preuve citée pour le defer « RunTick guard » de 5.1, résolu au titre de ce même AC.

### P-7 — Assertion morte dans le test du journal en échec (low)

- **Localisation :** `MicroServices/GPAO/ConvertP89.Tests/RunTickCoreTests.cs:66` et `:81`
- **Source :** verification-gap.
- **Constat :**
  - La fixture est un fichier UTF-8 invalide, qui suit le chemin Reject.
  - Ce chemin n'écrit jamais de XML (`src/P89Converter/P89FolderConverter.cs:117-149`). `Assert.Empty(… "xml")` ne peut donc pas échouer.
  - Le commentaire affirme pourtant qu'il vérifie « no XML is left behind ».
- **Action corrective :** reformuler le commentaire de la ligne 66 pour renvoyer la couverture de la suppression du XML à `tests/P89Converter.Tests/P89FolderConverterTests.cs:245`, et retirer l'assertion de la ligne 81.

## 4. Findings Defer

### F-1 — Câblage d'instance du `Client` testé seulement via ses seams statiques (medium)

- **Localisation :** `MicroServices/GPAO/ConvertP89/Client.cs:33-41`, `:156-188`, `:244`
- **Source :** verification-gap (s'y ajoute un point hors mandat de l'acceptance-auditor).
- **Constat :** aucun test ne construit un vrai `Client`. Les points suivants ne sont donc pas vérifiés :
  - `Frequency = FrequencyFor(...)` ;
  - `Actions`, qui confine l'erreur ;
  - `Stop()`, qui annule le token du tick ;
  - le forwarder `LogOutcome` (Deferred → Warning).
- **Justification :** ce test demande `AscoLSI_Test`, à cause du puits SQL de `AbstractService`, alors que le spec impose des tests unitaires sans base. La même dette existe pour P60 (test `Client.Actions` dû dans `MicroServices.sln`). À traiter par un test d'intégration commun aux deux workers.

### F-2 — Chaînes de connexion validées seulement « non vides » (low)

- **Localisation :** `MicroServices/GPAO/ConvertP89/Client.cs:58-63` ; `GPAO/ConvertP89/GpaoConvertP89.json:6`
- **Source :** blind-hunter + edge-case-hunter.
- **Constat :**
  - Une valeur `ConnectionStrings:AscoLSI` mal formée passe la construction, puis met chaque Fichier en Deferred indéfiniment.
  - `ConnectionStrings:MQTTnetServices` n'est pas contrôlée par `ReadConfig`.
- **Justification :** ce cas n'est pas dans la matrice figée (seulement « Blank AscoLSI »). `MQTTnetServices` est lue par `AbstractService`, que P60 partage. À traiter avec P60.

### F-3 — Le heartbeat publie « vivant » même après un tick en échec (low)

- **Localisation :** `MicroServices/GPAO/ConvertP89/Client.cs:172-173`
- **Source :** blind-hunter + edge-case-hunter.
- **Justification :** c'est le même schéma que `GPAO/ImportP60/Client.cs:170`. Remonter l'état « dernier tick en échec » est une décision de supervision commune aux workers du Launcher, hors Epic 5.

### F-4 — Hôte autonome `WorkerService` : relance 10 fois une erreur de configuration et ne libère pas le `Client` (low)

- **Localisation :** `MicroServices/GPAO/ConvertP89/WorkerService.cs:21-37`
- **Source :** blind-hunter + edge-case-hunter.
- **Justification :** le fichier est identique à `GPAO/ImportP60/WorkerService.cs`, noms mis à part (vérifié par `diff`). Il ne sert qu'à un `dotnet run` autonome, pas sous le Launcher. Préexistant, à corriger avec P60.

### F-5 — Une `ReadConfig` qui lève après le constructeur de base peut laisser fuir le puits SQL d'`AbstractService` (low)

- **Localisation :** `MicroServices/GPAO/ConvertP89/Client.cs:33-35`
- **Source :** edge-case-hunter.
- **Justification :** l'ordre des constructeurs est le même dans `GPAO/ImportP60/Client.cs`. La fuite ne se produit que sur une configuration invalide, qui bloque déjà le démarrage. À traiter avec P60.

## 5. Findings rejetés (bruit)

| ID | Constat | Raison du rejet |
|---|---|---|
| R-1 | Le `using Microsoft.EntityFrameworkCore` contredirait la Code Map (« drop EF usings ») | La Code Map prescrit elle-même `UseSqlServer(cs)` pour `AscoLsiJournalDbContext`. Le « drop » visait `Kape22Importer.Persistence`. |
| R-2 | `ParamName == "initiatingServer"` dépend du nom de paramètre de la bibliothèque | Un renommage fait échouer `ReadConfig_InitiatingServerTooLong_ThrowsNamingTheFullKey_AcFr22_8`, donc la régression est détectée. |
| R-3 | `FrequencyFor` : troncature à la seconde et débordement pour un très grand intervalle | La limite du timer (~49 j) et un intervalle fractionnaire en secondes ne sont pas des configurations réalistes. |
| R-4 | Un `P89:PollingInterval` mal formé n'est pas testé | `ConfigurationBinder` lève déjà une `InvalidOperationException` qui cite le chemin `P89:PollingInterval`. |
| R-5 | Le `switch` de `LogOutcome` n'a pas de `default` | L'enum n'a que 3 valeurs dans une bibliothèque sœur. Pure spéculation. |
| R-6 | `Version=` explicites contre CPM | Identique à `GPAO/ImportP60/*.csproj`. La mention CPM du spec concerne la résolution transitive. |
| R-7 | Versions des packages de test anciennes | Identiques à `GPAO/ImportP60.Tests`. |
| R-8 | `onOutcome` qui lève en cours d'énumération abandonne le reste du tick | Le puits de log ne lève pas en pratique. Le catch journalise et le tick suivant reprend, car chaque Fichier est atomique. |
| R-9 | `outcome.Reasons` pourrait être null | La valeur est initialisée à `[]` (`P89FolderConverter.cs:220`). |
| R-10 | Alias de chemins (UNC, lecteur mappé, lien symbolique) non détectés | Le spec délimite explicitement la comparaison : casse et séparateur final. |
| R-11 | `Actions` ou `Stop` après `Dispose` | Le cycle de vie est piloté par `WorkerAdapter`. Le cas du dépassement du budget d'arrêt est déjà dans `deferred-work.md:1308`. |
| R-12 | Le 3ᵉ AC (0 warning, pas de `Kape22Importer`) n'a pas de test automatisé | La section Verification du spec en fait une commande de build (`-warnaserror`). La référence est lisible dans le csproj. |
| R-13 | `ReadConfig_BlankInitiatingServer_*` ne vérifie pas le nom de machine | C'est un comportement de la bibliothèque, couvert par `AscoLsiFichierJournalTests.cs:56` (`AcFr23_2`). |
| R-14 | Spec `status: 'done'` contre sprint-status `review`, `review_loop_iteration: 0` | Convention du workflow (précédent 5.1). `/commit-review` synchronise. |
| R-15 | Le README ne mentionne ni le repli sous-seconde ni `MQTTnetServices` | Même niveau de détail que la section P60. Sans conséquence pour l'exploitant. |
| R-16 | Débordement du budget d'arrêt dans `Stop`/`Dispose` | Déjà consigné (`deferred-work.md:1308-1310`). |

## 6. Auto-vérifications

- **Lentilles lancées : 4/4**, aucune en échec :
  - blind-hunter (20 constats) ;
  - edge-case-hunter (18) ;
  - verification-gap (3) ;
  - acceptance-auditor, surcharge projet (4 + 6 hors mandat).
- **Diff git `1ca2e19^..HEAD` :** 6 fichiers, +199 / −32. Fichiers : `README.md`, `PRD.md`, `deferred-work.md`, `epic-5-context.md`, `sprint-status.yaml`, le spec.
- **Diff SVN r534 (MicroServices) :** 16 entrées, soit 14 fichiers et 2 propriétés de dossier, pour environ +816 / −17 :
  - `GPAO/ConvertP89/*` (5) ;
  - `GPAO/ConvertP89.Tests/*` (4) ;
  - `Launcher/{Launcher.csproj, WorkerRegistry.cs, workers.json}` ;
  - `Launcher.Tests/WorkerRegistryTests.cs` ;
  - `MicroServices.sln`.
- **Diff combiné relu par les lentilles :** 1 283 lignes.
- **Vérifications de triage (lecture du code) :**
  - `Client.cs` lu en entier ;
  - `WorkerService.cs` comparé par `diff` à celui de P60 ;
  - csproj, `svn proplist -v` et `deferred-work.md:1302-1310` vérifiés ;
  - `P89FichierOutcome.Reasons` et le test du nom de machine dans `AscoLsiJournal.Tests` vérifiés.
- **Non exécuté pendant la revue :** build et tests. À faire après application des patches :
  - `dotnet build MicroServices.sln -warnaserror` ;
  - `dotnet test` sur `GPAO/ConvertP89.Tests` et `Launcher.Tests`.
- Le spec n'a pas de section Tasks/Subtasks, donc aucun finding n'a été ajouté au spec (précédent Epic 4+). Les defers F-1..F-5 sont à consigner par `/commit-review`.
