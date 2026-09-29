# Review report — Story 6.2

Range : 9cad6884a4a0b835915a4042a447d803d6bdae09^..HEAD (9cad688)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-2-garde-stabilite-fichiers-p60-p89.md
Date : 2026-09-29
Verdict : ACCEPTÉ
Findings : D=1 P=7 F=1 R=13  (Decision, Patch, Defer, Rejetés)
Mise à jour 2026-09-29 : D-1 tranché (option 2), reclassé en defer F-2 ; décomptes effectifs D=0 P=7 F=2 R=13.

P-1..P-7 sont appliqués dans l'arbre de travail (option « tout appliquer »), sans commit :
- P-1 : `P89FolderConverter.RunTick` énumère avec `DirectoryInfo.GetFiles` et lit `FileInfo.LastWriteTimeUtc`, fourni par l'énumération. Il n'y a plus d'appel de métadonnées par Fichier. Pas de test rouge possible : la faute réseau entre le listing et le contrôle n'est pas reproductible de façon déterministe. Le comportement reste couvert par les tests AC-FR22-9.
- P-2 : nouveau test `DirectoryFileSourceTests.List_ReportsTheFileLastWriteTimeUtc_AcFr12_5`. Il a été vu rouge en sabotant `ToEntry`, puis l'adaptateur a été restauré.
- P-3 : commentaires de `ImportOptions` et `P89Options`.
- P-4 : en-tête de `ImportOptions`.
- P-5 : `README.md:8`.
- P-6 : `deferred-work.md:338` et `:1248` marqués RESOLVED.
- P-7 : `sprint-status.yaml` passé à `done` (le spec était déjà `done`).

Vérification :
- `dotnet build TextToXml.sln -warnaserror` : 0 erreur.
- Unit : 192 + 47 + 21 + 904 tests au vert.
- `MicroServices/GPAO/ConvertP89.Tests` : 34 tests au vert.
- Les tests d'intégration n'ont pas été relancés.

F-2 reste à consigner dans `deferred-work.md` par `/commit-review`.

## 1. Verdict

**ACCEPTÉ**, sous deux conditions pour le commit de clôture (`/commit-review`) :
- appliquer P-1..P-7 ;
- trancher D-1.

Aucune déviation CC ni AD n'a été relevée. Les 2 patches medium (P-1 et P-2) ne remettent pas en cause la règle de la garde, qui est conforme au bloc figé. Ils corrigent :
- P-1 : une régression de journalisation sur un chemin de faute du partage, côté P89 ;
- P-2 : un trou de vérification côté P60.

Constats qui fondent le verdict :
- **Bloc figé respecté** (Acceptance Auditor, 0 finding bloquant).
  - La règle d'âge est `now - LastWriteTimeUtc < quiet` ⇒ skip, pour P60 comme pour P89.
  - L'horloge est le `TimeProvider` injecté.
  - Le défaut de 10 s est dans l'initialiseur de propriété.
  - Un horodatage dans le futur est ignoré (skip).
  - L'inbox est listée une seule fois par tick. `processing/` n'a pas de garde.
- **Always / Never / Ask First respectés.**
  - La double sonde et `MarkUnstableOnce` sont supprimés.
  - Les seeds sont antidatés, sans période nulle.
  - Aucun nouveau membre `IFileSource`, aucun sleep, aucune validation au démarrage.
  - Aucun nouveau `P89FichierStatus` ni log.
  - `src/TextToXml` n'est pas touché. Dans MicroServices, seuls les tests changent.
- **CC-4.** L'ordre des propriétés est `RetentionDays` < `StabilityQuietPeriod` et `SourcePath` < `StabilityQuietPeriod` < `XmlPath`.
- **CC-1.** Chaque nouveau test porte `[Trait("AC", "FR12-5")]` ou `[Trait("AC", "FR22-9")]` avec le suffixe `_AcFr12_5` / `_AcFr22_9`.
- **Matrice I/O couverte** pour P60 comme pour P89 : cas jeune, borne écoulée, inbox mixte, futur, période configurée, défaut.

| Sévérité | Nombre |
|---|---|
| high | 0 |
| medium | 3 (D-1, P-1, P-2) |
| low | 6 (P-3..P-7, F-1) |

## 2. Findings Decision — à trancher par l'humain

### D-1 — Un Fichier ignoré par la garde ne laisse aucune trace, même en Debug (medium)
- Source : blind-hunter + edge-case-hunter
- Localisation : `src/Kape22Importer/InboxScanner.cs:137`, `src/P89Converter/P89FolderConverter.cs:75`
- Constat : un Fichier retenu par la garde n'est tracé à aucun niveau de log. Deux cas bloquent alors l'inbox (P60) ou le dossier source (P89) sans aucun indice pour l'exploitant :
  - une période mal configurée, par exemple une valeur nue `10`, lue comme 10 jours (piège documenté dans le README) ;
  - un partage dont l'horloge avance de plusieurs heures : chaque Fichier est retardé de tout ce décalage, sans limite.

  La matrice du spec interdit seulement un log de niveau ≥ Warning. En revanche, la section « Ask First » range tout « logged outcome for a skipped Fichier » dans les points à faire valider par l'humain.
- Options :
  1. Ajouter une trace `Debug` par Fichier ignoré (nom et âge), côté P60 dans `InboxScanner`. P89 n'a pas de logger dans la bibliothèque, donc pas de trace côté P89, ou alors seulement via le worker. Cette option exige une renégociation « Ask First » datée dans le spec.
  2. Garder le silence voulu par le spec. Consigner en defer le risque « blocage silencieux (mauvaise configuration, décalage d'horloge) » pour la Story 6.4 (robustesse des workers).
  3. Rejeter : le README documente déjà le piège `hh:mm:ss`, et le décalage d'horloge est accepté par le spec.
- État : **tranché le 2026-09-29, option 2**. D-1 est reclassé en defer (voir F-2). Aucun changement de code.

## 3. Findings Patch

### P-1 — P89 : `File.GetLastWriteTimeUtc` levé hors de la gestion de faute par Fichier (medium)
- Source : blind-hunter + edge-case-hunter
- Localisation : `src/P89Converter/P89FolderConverter.cs:75`
- Constat :
  - Le contrôle d'âge est fait dans `RunTick`, avant `Process`, donc en dehors de son `catch (IOException or UnauthorizedAccessException)` (`:178`).
  - Sur un partage réseau qui décroche en cours de tick, `GetLastWriteTimeUtc` lève (`ERROR_NETNAME_DELETED`, `ERROR_BAD_NETPATH`, accès refusé après repli). L'exception sort de `RunTick`.
  - `RunTickCore` du worker (`MicroServices/GPAO/ConvertP89/Client.cs:204-214`) l'attrape en `onError`. La liste des issues n'est alors jamais retournée, et les issues des Fichiers déjà traités dans le tick ne sont jamais journalisées dans les logs du worker (`LogOutcome`), y compris l'Error d'un rejet. Seules les entrées `L_D_LOG_COMMANDE` subsistent.
  - Avant la Story 6.2, la même faute passait par `Process` et donnait une issue `Deferred` pour ce seul Fichier. Le test `RunTick_SourceUnreadable_DefersWithoutEntry` fixe ce comportement.
- Action : entourer le contrôle d'âge du même filtre `catch (Exception e) when (e is IOException or UnauthorizedAccessException)`, qui ajoute une issue `P89FichierStatus.Deferred` (message `Fichier : {e.Message}`, comme `:180`) puis fait `continue`.

  Variante équivalente, qui supprime en plus l'appel de métadonnées par Fichier et la fenêtre de course : énumérer avec `new DirectoryInfo(SourcePath).GetFiles("LP89_*")` et lire `FileInfo.LastWriteTimeUtc`, déjà renseigné par l'énumération.

  Ajouter un test rouge d'abord. Aucun nouveau membre de statut n'est nécessaire.

### P-2 — P60 : aucun test ne vérifie que `DirectoryFileSource` renvoie la vraie date de dernière écriture (medium)
- Source : verification-gap
- Localisation : `src/Kape22Importer/DirectoryFileSource.cs:75`, `tests/Kape22Importer.Tests/DirectoryFileSourceTests.cs`
- Constat :
  - La garde P60 repose désormais uniquement sur `FichierEntry.LastWriteTimeUtc`, que `DirectoryFileSource.ToEntry` remplit en production (`GpaoImportP60/Client.cs:186`).
  - Les tests AC-FR12-5 tournent tous sur `InMemoryFileSource`, et `DirectoryFileSourceTests` ne vérifie jamais `LastWriteTimeUtc`.
  - Si ce champ était mal lu ou laissé à sa valeur par défaut, `MinValue` ferait passer tous les Fichiers, et la troncature que corrige la Story 6.2 reviendrait sans qu'aucun test ne passe au rouge.
- Action : ajouter à `DirectoryFileSourceTests` un test qui écrit un fichier, fixe sa date avec `File.SetLastWriteTimeUtc(path, t)`, puis vérifie `List("").Single().LastWriteTimeUtc == t`, avec le trait AC-FR12-5 et le suffixe `_AcFr12_5`.

### P-3 — Commentaires des options : une valeur nulle ou négative désactive la garde, sans que ce soit documenté (low)
- Source : blind-hunter (+ edge-case-hunter, qui proposait une validation interdite par le spec)
- Localisation : `src/Kape22Importer/ImportOptions.cs:40-42`, `src/P89Converter/P89Options.cs:24-26`
- Constat : avec `StabilityQuietPeriod <= 0`, tout passe, y compris les horodatages dans le futur. Le spec interdit la validation au démarrage (« Never »), donc ce comportement doit au moins être écrit. `RetentionDays` le fait déjà : « zero or less disables the purge ».
- Action : compléter les deux commentaires, par exemple « Zero or less disables the gate, future timestamps included. »

### P-4 — En-tête de `ImportOptions` : l'énumération des valeurs issues de la configuration est incomplète (low)
- Source : blind-hunter
- Localisation : `src/Kape22Importer/ImportOptions.cs:5-6`
- Constat : l'en-tête dit « every path, the polling interval, the initiating server and the retention window come from IConfiguration ». La période de stabilité, liée elle aussi depuis la section `Import`, manque à cette liste. Seule la phrase sur les défauts a été mise à jour.
- Action : ajouter « the stability quiet period » à l'énumération.

### P-5 — README, ligne P60 : l'avertissement « valeur nue `10` = 10 jours » manque (low)
- Source : blind-hunter
- Localisation : `README.md:8`
- Constat : le paragraphe P89 (`README.md:88`) avertit qu'une valeur nue `10` vaut 10 jours. La ligne P60, qui a le même piège, ne le dit pas.
- Action : ajouter « une valeur nue `10` vaut 10 jours » après `défaut `00:00:10`` dans la ligne P60.

### P-6 — `deferred-work.md` : les deux entrées résolues par la Story 6.2 ne sont pas marquées RESOLVED (low, administratif)
- Source : blind-hunter + acceptance-auditor
- Localisation : `_bmad-output/implementation-artifacts/deferred-work.md:337-340` (double sonde de `InboxScanner`), `:1248` (P89 sans garde, encore « PLANNED 2026-09-28: Story 6.2 »)
- Constat : le Code Map du spec demande de marquer ces deux entrées RESOLVED à la clôture.
- Action : dans le commit de clôture, les préfixer par « **RESOLVED 2026-09-29: Story 6.2 (9cad688)** ».

### P-7 — Statut du spec (`done`) incohérent avec sprint-status (`review`) (low, administratif)
- Source : blind-hunter + acceptance-auditor
- Localisation : `spec-6-2-garde-stabilite-fichiers-p60-p89.md:5`, `sprint-status.yaml:97`
- Constat : le frontmatter du spec dit `status: 'done'` alors que `sprint-status.yaml` place la story en `review`.
- Action : aligner les deux à la clôture (`done` partout une fois les patches appliqués).

## 4. Findings Defer

### F-1 — L'antidatage de `RunTickCoreTests.cs` côté MicroServices n'est pas encore commité dans SVN (low)
- Source : blind-hunter
- Localisation : `MicroServices/GPAO/ConvertP89.Tests/RunTickCoreTests.cs:104` (hors dépôt git)
- Justification : la tâche du spec est cochée, mais le changement vit dans SVN et c'est l'utilisateur qui le commite. Rien à corriger dans le range git. Il reste à confirmer que le commit SVN des tests MicroServices a bien été fait.

### F-2 — Un Fichier retenu par la garde ne laisse aucune trace : blocage silencieux en cas de mauvaise configuration ou de décalage d'horloge (medium, issu de D-1)
- Source : blind-hunter + edge-case-hunter
- Localisation : `src/Kape22Importer/InboxScanner.cs:137`, `src/P89Converter/P89FolderConverter.cs:75`
- Justification :
  - L'humain a tranché le 2026-09-29 (option 2) : le silence est voulu par le spec (matrice « nothing logged ≥ Warning », « Ask First » sur tout log d'un Fichier ignoré).
  - Le risque reste réel. Une valeur nue `10`, lue comme 10 jours, ou un partage dont l'horloge avance bloque l'inbox ou le dossier source sans aucun signe pour l'exploitant.
  - À traiter dans la Story 6.4 (robustesse commune des workers), par exemple avec une trace `Debug` ou un signal côté worker quand un Fichier reste retenu longtemps.
- Consignation dans `deferred-work.md` : par `/commit-review`, sous « **PLANNED 2026-09-29: Story 6.4** ».

## 5. Findings rejetés (bruit)

| # | Source | Constat | Justification du rejet |
|---|---|---|---|
| R-1 | blind-hunter + edge-case-hunter + acceptance-auditor (hors mandat) | P89 : un Fichier supprimé entre `GetFiles` et `GetLastWriteTimeUtc` renvoie 1601-01-01 et franchit la garde | `Process` lève alors `FileNotFoundException` (une `IOException`), ce qui donne l'issue `Deferred` : c'est exactement le comportement d'avant la Story 6.2 pour cette course. Pas de régression. La variante de P-1 (`FileInfo` issu de l'énumération) supprime aussi la fenêtre. |
| R-2 | edge-case-hunter | Une copie qui conserve la date d'origine, ou qui cale au-delà de la période, laisse passer un Fichier à moitié copié | Déjà consigné dans `deferred-work.md` par le commit (section « Deferred from: code review of story-6.2 »). |
| R-3 | edge-case-hunter | Valider au démarrage une période négative ou hors bornes | Interdit par le spec (« Never: Startup validation of the quiet period »). La part documentaire est couverte par P-3. |
| R-4 | blind-hunter | Un appel de métadonnées de plus par Fichier côté P89 | Coût négligeable. Absorbé par la variante de P-1. |
| R-5 | blind-hunter | Cellule P60 du README surchargée | Question de style. Pas de défaut. |
| R-6 | blind-hunter | Pas de test pour « inbox listée une seule fois par tick » | L'AC est formulé « when inspected » : c'est une vérification par inspection, faite par l'Acceptance Auditor. |
| R-7 | blind-hunter | Borne « juste en dessous » (9,999 s) non testée | Le test de borne exacte (âge == période ⇒ traité) et le test à −5 s font déjà échouer toute inversion `>=`/`>` ou `<`/`<=`. |
| R-8 | blind-hunter | Les tests P60 « futur » et « configuré » ne vérifient pas l'absence de Warning | Ils passent par le même prédicat, sans log possible. L'absence de Warning est déjà vérifiée par le test du cas jeune. |
| R-9 | blind-hunter | Le test P89 du cas jeune ne vérifie pas que `error/` est vide | Il vérifie déjà Source == [name], ainsi que Xml, Done et le journal vides. Un déplacement vers `error/` qui laisserait une copie dans la source n'est pas plausible. |
| R-10 | blind-hunter | Le test de période configurée P89 construit le convertisseur sans passer par le helper `Converter(...)` | Question de style. Le helper ne prend pas d'options. |
| R-11 | blind-hunter | La nouvelle entrée defer n'a pas de story cible | Convention du fichier : un defer neuf n'est pas planifié tant qu'aucun correct-course ne l'a routé. |
| R-12 | blind-hunter | `Drop` antidate tous les dépôts par défaut | Choix explicite du spec (Code Map, `TestSupport.cs`). Le paramètre optionnel rend l'intention visible là où elle compte. |
| R-13 | acceptance-auditor (hors mandat) | Une assertion entre deux Act dans `Tick_ProcessesFichierOnceTheQuietPeriodHasElapsed_AcFr12_5` | Cette assertion intermédiaire prouve que le premier tick ne traite rien, et le jumeau P89 fait de même avec `Assert.Single`. L'écart avec la convention AAA est assumé et lisible. |

## 6. Auto-vérifications

- **Lentilles lancées : 4 sur 4, toutes rendues.**
  - blind-hunter : 19 constats
  - edge-case-hunter : 7 constats
  - verification-gap : 1 constat
  - acceptance-auditor : 2 findings et 2 hors mandat, sans CC ni AD violé
- Lentilles en échec : aucune.
- Diff : 14 fichiers, +313 / −59 (611 lignes de diff).
  - Production : `ImportOptions.cs`, `InboxScanner.cs`, `P89FolderConverter.cs`, `P89Options.cs`
  - Tests : `InboxScannerTests`, `InboxScannerTestSupport`, `P89FolderConverterTests`, `TestSupport`, `EndToEndPerformanceTests`, `ErrorsReportReadabilityTests`
  - Documentation et suivi : `README.md`, `deferred-work.md`, spec, `sprint-status.yaml`
- Code lu hors diff pour le tri :
  - `P89FolderConverter.cs:47-82,162-198` (portée du catch)
  - `MicroServices/GPAO/ConvertP89/Client.cs:198-214` (`RunTickCore`)
  - `DirectoryFileSource.cs:69-79`
  - `DirectoryFileSourceTests.cs` (aucune vérification de `LastWrite`)
  - `deferred-work.md:337-340,1248`
- Build et tests : non relancés par la revue. Le spec indique la suite Unit au vert au commit 9cad688.

## Clôture (2026-09-29)

- P-1..P-7 → appliqués, commités dans 39e76d6 (`chore(story-6.2): apply review patches`).
- D-1 → tranché, option 2, reclassé en F-2.
- F-1 → tracé dans `deferred-work.md` (commit SVN des tests MicroServices à confirmer).
- F-2 → tracé dans `deferred-work.md`, PLANNED Story 6.4.
- R-1..R-13 → rejetés, rien à faire.
- Vérification finale : build `-warnaserror` 0 erreur ; Unit 192 + 21 + 47 + 904 au vert ; Integration 1251 au vert (18 ignorés) + 2 au vert.
- Statut : story 6-2 → done ; épic 6 reste in-progress (6.3, 6.4, 6.5 en backlog).
