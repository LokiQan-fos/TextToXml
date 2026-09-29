# Review report — Story 6.3

Range : 0ba63b4f4467a4b0575ac7d1fe44f448684ec089^..HEAD (0ba63b4, 48a7b09)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-3-export-xml-p60-dossier-dedie.md
Date : 2026-09-29
Verdict : ACCEPTÉ
Findings : D=1 P=4 F=3 R=27  (Decision, Patch, Defer, Rejetés)
Mise à jour 2026-09-29 : D-1 tranché (option 2), reclassé en patch P-5 ; décomptes effectifs D=0 P=5 F=3 R=27.

P-1..P-5 sont appliqués dans l'arbre de travail (option « tout appliquer »), sans commit :
- P-1 : nouveau test `Tick_ExportUndeletableAfterAFailedFiling_WarnsAndFilesTheNextFichier_AcFr26_6`. `InMemoryFileSource` reçoit un hook `MoveHook`. Le test a été vu rouge en restreignant le filtre de `TryDelete` : l'`IOException` remonte de `RunTick`. Le code a ensuite été restauré.
- P-2 : le message du `Warning` de `TryDelete` est reformulé.
- P-3 : le commentaire d'en-tête et le commentaire de `XmlExportPath` dans `ImportOptions` sont corrigés.
- P-4 : `e2e-worker-import.ps1` vide `$exportPath` au démarrage.
- P-5 : une note datée « Code review D-1 » est ajoutée au Spec Change Log.

Hors patches, `sprint-status.yaml` passe `6-3-export-xml-p60-dossier-dedie` à `done`.

Vérification :
- `dotnet build TextToXml.sln -warnaserror` : 0 erreur.
- Unit : 48 + 192 + 21 + 917 tests au vert.
- Les tests d'intégration et ceux de MicroServices n'ont pas été relancés : aucun code worker n'a été modifié.

F-1..F-3 restent à consigner dans `deferred-work.md` par `/commit-review` : F-1 en PLANNED Story 6.4, F-2 et F-3 à rattacher à 6.4.

## 1. Verdict

**ACCEPTÉ**, sous deux conditions pour le commit de clôture (`/commit-review`) :
- appliquer P-1..P-4 ;
- trancher D-1.

Aucune déviation CC ni AD n'a été relevée, et l'Acceptance Auditor ne rend aucun finding :
- **Bloc figé :** il est respecté. Le seul écart, la place de `XmlExportPath` après `StabilityQuietPeriod`, est couvert par la note de renégociation D-1 du 2026-09-29.
- **Always :**
  - le suffixe utilise l'heure de Paris et une seule lecture de l'horloge, partagée avec `Archive` ;
  - l'export est écrit en `CreateNew` ;
  - un échec passe par le catch de classement, le Fichier reste dans `processing/` et un `Warning` est journalisé ;
  - l'export est supprimé au mieux si le classement échoue ensuite ;
  - une clé vide désactive l'export au niveau de la bibliothèque ;
  - `PurgeRetention` n'est pas modifié.
- **Ask First :** il n'y a aucun export sur `PersistenceError`, et `IFileSource`, `src/P89Converter/` et `src/TextToXml/` ne sont pas modifiés.
- **Never :** l'export ne passe pas par `IFileSource` et le dossier d'export n'est jamais purgé.
- **CC :**
  - CC-1 : chaque AC FR26-1..6 et FR16-5 a un test tracé, avec `[Trait("AC", …)]` et le suffixe `_AcFr26_n` correspondant, et le commit 0ba63b4 l'atteste.
  - CC-2 et CC-3 : les commentaires sont en anglais et placés au-dessus du bloc.
  - CC-4 : `ImportOptions` et les initialiseurs sont en ordre alphabétique.
  - CC-7 : la clé est obligatoire côté worker.
- **Worker (SVN, en lecture seule) :** `Client.ReadConfig` valide la clé, `GpaoImportP60.json` la contient, et les tests AC-FR26-5 existent dans `ClientConfigurationTests.cs`.

Les findings retenus concernent tous le chemin d'échec de l'export :
- deux trous de vérification, P-1 et D-1 ;
- un message d'exploitation trompeur, P-2 ;
- deux points d'hygiène, P-3 et P-4 ;
- trois limites de conception, reportées en F-1..F-3.

Répartition par sévérité : high = 0, medium = 3 (P-1, F-1, F-2), low = 5 (D-1, P-2, P-3, P-4, F-3).

## 2. Decision (à trancher par l'humain)

- **D-1** (low) — Le nettoyage d'un export dont l'écriture échoue n'est pas testé. Source : verification-gap + blind-hunter.
  - Localisation : `src/Kape22Importer/InboxScanner.cs:331-335`, dans le `catch { TryDelete(path); throw; }` d'`Export`.
  - Constat : les tests AC-FR26-6 échouent soit avant le `CreateNew` (dossier invalide ou nom déjà pris), soit après le retour d'`Export` (`MoveFault`). Aucun test ne fait échouer `stream.Write` ou le flush à la fermeture du flux. On peut supprimer ce `catch` sans qu'aucun test ne passe au rouge.
  - Conséquence si ce chemin régresse : un XML tronqué reste dans le dossier transmis aux tiers, à côté du XML complet écrit par le retraitement.
  - Options :
    1. Ajouter un point d'injection de faute d'écriture dans `Export`, par exemple un `Func<string, Stream>` interne, avec un test rouge puis vert.
    2. Accepter ce chemin comme non vérifié, sans nouveau point d'injection. Il suit le même motif que `P89FolderConverter.Accept`, lui non plus non testé sur ce point. Il faut alors le consigner dans le Spec Change Log.
  - État : tranché le 2026-09-29 par l'option 2, reclassé en P-5.

## 3. Patch

- **P-1** (medium) — L'échec de `TryDelete` n'a aucun test. Source : verification-gap + blind-hunter + acceptance-auditor (hors mandat).
  - Localisation : `src/Kape22Importer/InboxScanner.cs:343-356` (filtre `when` du `catch` de `TryDelete`), appelé depuis `:264-267`.
  - Constat : dans tous les tests, la suppression réussit ou n'a pas lieu. Si l'on retire ou restreint le filtre, aucun test ne passe au rouge. Or une exception levée par `File.Delete` depuis le `catch` de classement remonterait hors de `ProcessFromProcessing` et interromprait tout le tick.
  - Action corrective : ajouter à `InboxScannerTests.cs`, à côté de `Tick_FilingFailsAfterTheExport_DeletesTheExportAndRetriesCleanly_AcFr26_6` (`:555`), un test `[Trait("AC", "FR26-6")]` construit ainsi :
    - un premier Fichier dont l'export est verrouillé (`FileShare.None`) pendant l'échec de `Move`, via un hook dans `InMemoryFileSource` ou un verrou posé depuis le fake ;
    - un second Fichier dans le même tick.
    - Assertions : le tick se termine ; le premier Fichier reste dans `processing/` ; le second est traité ; un `Warning` nomme le chemin de l'export.
    - Cycle rouge→vert (CC-1) : saboter le filtre, constater le rouge, restaurer.

- **P-2** (low) — Le `Warning` de `TryDelete` minimise la conséquence. Source : blind-hunter.
  - Localisation : `src/Kape22Importer/InboxScanner.cs:352`.
  - Constat : le message « a retry will export it again » ne dit pas qu'un doublon reste désormais dans le dossier transmis aux tiers.
  - Action corrective : reformuler, par exemple « Export {Path} could not be deleted after a failed filing ({Reason}); it stays in the export folder and the retry will write a second one - remove it manually. »

- **P-3** (low) — Deux défauts de commentaire dans `ImportOptions`. Source : blind-hunter.
  - Localisation : `src/Kape22Importer/ImportOptions.cs:10` et `:46`.
  - Constat :
    - la ligne 46 fait 120 colonnes, alors que le reste du fichier est coupé vers 105 ;
    - l'en-tête parle d'une clé « with no default » alors que la propriété vaut `string.Empty` par défaut, ce qui signifie que l'export est désactivé.
  - Action corrective :
    - recouper la ligne 46 ;
    - remplacer « with no default » par « empty by default (export off at library level) ».

- **P-4** (low) — Le script e2e ne vide pas le dossier d'export au démarrage. Source : blind-hunter.
  - Localisation : `scripts/e2e-worker-import.ps1:93`, avec la liste des exports à `:150`.
  - Constat : l'inbox est vidée au démarrage (`:93`), mais pas `$exportPath`. Après une exécution avec `-KeepArtifacts`, l'exécution suivante liste les exports précédents comme s'ils venaient d'elle.
  - Action corrective : ajouter `if (Test-Path -LiteralPath $exportPath) { Remove-Item -LiteralPath $exportPath -Recurse -Force }` à côté de la ligne 93.

- **P-5** (low) — Documenter que le nettoyage d'un export dont l'écriture échoue n'est pas vérifié. Issu de D-1, option 2.
  - Localisation : `_bmad-output/implementation-artifacts/spec-6-3-export-xml-p60-dossier-dedie.md`, § Spec Change Log ; code visé : `src/Kape22Importer/InboxScanner.cs:331-335`.
  - Action corrective : ajouter au Spec Change Log une note datée du 2026-09-29 (revue D-1, option 2) disant que le chemin `catch { TryDelete(path); throw; }` d'`Export` est accepté comme non vérifié. Motifs à reprendre dans la note :
    - on ne peut pas provoquer de faute d'écriture après `CreateNew` sans ajouter un point d'injection ;
    - c'est le même motif que `P89FolderConverter.Accept`.
  - Contenu de la section KEEP de la note : le `catch` de nettoyage.

## 4. Defer

- **F-1** (medium) — Une panne du partage d'export bloque toute la chaîne P60 sans limite de tentatives. Source : blind-hunter + acceptance-auditor (hors mandat).
  - Localisation : `src/Kape22Importer/InboxScanner.cs:241-274`, ainsi que `Kape22FichierProcessor.cs:147-186` (journal).
  - Constat :
    - Dossier d'export injoignable : chaque Fichier reste dans `processing/`, et chaque tick relance tout le processeur contre `AscoLSI`.
    - Succès déjà commité : une ligne Logs `AlreadyImported` est écrite à chaque tick.
    - Rejet : le persister réécrit l'entrée de journal « REJETÉ » et une ligne `ImportRejected` à chaque tick.
    - Le Design Note (« le garde D22 empêche un second insert ») ne couvre pas ces répétitions.
  - Justification du report : AC-FR26-6 impose ce comportement (le Fichier reste dans `processing/`), et la limite de tentatives par Fichier est déjà prévue dans la Story 6.4 (F-1 de la Story 6.1). Le mécanisme de retraitement existait avant cette story ; 6.3 y ajoute seulement un nouveau déclencheur. À consigner dans `deferred-work.md` comme PLANNED Story 6.4.

- **F-2** (medium) — La publication de l'export n'est pas atomique. Source : edge-case-hunter + blind-hunter.
  - Localisation : `src/Kape22Importer/InboxScanner.cs:241-248` et `:323-329`.
  - Constat :
    - Le XML est écrit directement sous son nom final : un tiers qui interroge le dossier peut lire un fichier partiel.
    - Si le processus s'arrête entre `Export` et le déplacement vers `archive/` ou `error/`, le `catch` ne s'exécute pas. Le retraitement écrit alors un second `<nom>_<ts>.xml`, et la promesse « un seul XML » d'AC-FR26-6 ne tient que pour les exceptions managées.
  - Justification du report : le spec (Always) impose explicitement le motif de `P89FolderConverter.Accept`, qui a le même comportement. La correction (écrire en `.tmp` puis renommer après le classement) touche les deux formats, et `src/P89Converter/` relève d'Ask First. À traiter en commun dans le durcissement 6.4, ou dans une story dédiée.

- **F-3** (low) — Un chemin d'export mal saisi crée silencieusement un dossier local. Source : blind-hunter.
  - Localisation : `src/Kape22Importer/InboxScanner.cs:320`.
  - Constat : `Directory.CreateDirectory(options.XmlExportPath)` s'exécute à chaque export. Un chemin mal saisi mais absolu passe la validation du worker, crée un dossier que personne ne lit, et l'export passe inaperçu.
  - Justification du report : `P89FolderConverter` se comporte de la même façon (`CreateDirectory(XmlPath)`). Vérifier l'existence du dossier au démarrage relève de la validation de configuration des workers, prévue dans la Story 6.4 (AC-FR25-4).

## 5. Rejetés (bruit)

- R-1 — `ArgumentException`/`NotSupportedException` sur un `XmlExportPath` malformé échappent au catch (edge-case). Le worker refuse un chemin malformé au démarrage (`Client.ReadConfig`) ; une bibliothèque consommée hors worker n'existe pas.
- R-2 — Exception non-IO levée par `Archive`/`Reject` après l'export (edge-case). `IFileSource` ne lève que des IO ; toute autre exception est un défaut qui remonte au loop worker (Story 3.5).
- R-3 — `XmlExportPath` sous l'inbox (edge-case). Le worker refuse l'égalité inbox/`processing/` ; le scan de l'inbox n'est pas récursif.
- R-4 — `XmlExportPath` relatif au niveau bibliothèque (edge-case, blind). Validé côté worker (AC-FR26-5), seul hôte.
- R-5 — `NormalizedXml` vide → export de 0 octet (edge-case). Le Converter ne produit jamais une chaîne vide sur un Fichier converti.
- R-6 — Succès avec `NormalizedXml` null archivé sans export (blind). Inatteignable : un succès implique une conversion.
- R-7 — Les `Warning` ne loguent que `exception.Message` (blind). Convention existante du scanner (même forme pour l'échec de persistance).
- R-8 — Encodage de l'export non documenté pour les tiers (blind). Octets UTF-8 sans BOM, déclaration `encoding="utf-8"` émise par `NormalizedXmlBuilder` (AC-FR5-10) : le fichier s'auto-décrit ; même encodage que le sidecar `archive/`.
- R-9 — `File.ReadAllText` ne vérifie pas l'absence de BOM (blind). Hors contrat du spec ; même `Utf8NoBom` que le sidecar déjà couvert.
- R-10 — Le cas « d22-guard » de la théorie AC-FR26-1 est simulé par un succès avec Warning (blind). À la couture `IFichierProcessor`, un passage par la garde D22 est indiscernable d'un succès (`Process` ne transmet pas `AlreadyImported`) ; le scanner ne branche pas dessus.
- R-11 — La théorie AC-FR26-1 ne couvre que le rejet mapper (blind). Le scanner ne branche pas sur le code d'erreur ; tout rejet est `Errors` + XML à cette couture.
- R-12 — `Tick_NoExportFolderConfigured_ArchivesWithoutExport_AcFr26_1` passe toujours (blind). Le test prouve la garde `IsNullOrWhiteSpace` : sans elle, `Directory.CreateDirectory("")` lève `ArgumentException` hors du catch et le tick échoue.
- R-13 — Champ `exportFolder` et `Dispose()` en bas de classe (blind). Placés avec les helpers de test ; CC-4 ne régit pas l'ordre des membres.
- R-14 — En-tête de classe « no disk » non réconcilié (blind). Réconcilié : `InboxScannerTests.cs:21-22` ajoute la mention Story 6.3.
- R-15 — Tests `Unit` écrivant dans `%TEMP%` (blind). Précédent établi (`P89FolderConverterTests`, `Unit`, dossiers temporaires).
- R-16 — `[26] = 6` compterait FR26-5 malgré `KnownExceptions` (blind). Le gate filtre `!KnownExceptions.Contains(ac)` (`AcCoverageCompletenessTests.cs`).
- R-17 — Spec `status: done` vs sprint-status `review` (blind, auditor). Convention établie (spec 6.2 au même état à la revue) ; la clôture passe sprint-status à `done`.
- R-18 — Fichiers worker (SVN) absents du diff (blind). Hors git par construction ; vérifiés en lecture seule par l'Acceptance Auditor.
- R-19 — Le texte figé « between RetentionDays and StabilityQuietPeriod » n'a pas de renvoi en ligne (blind). Le bloc figé ne se modifie pas ; le Spec Change Log est le mécanisme prévu.
- R-20 — Références de ligne du Code Map périmées (verification-gap). Le Code Map précède l'implémentation ; le Suggested Review Order porte les lignes à jour.
- R-21 — Le script e2e n'asserte pas un export par Fichier et masque un dossier absent (blind). Script de rapport destiné à l'inspection humaine, pas un gate ; AC-FR26 couverts par les tests Unit.
- R-22 — README : collision de nom « auto-résolutive » non expliquée (blind). « retraité au tick suivant » est déjà écrit ; le second suivant produit un autre nom.
- R-23 — README : fuseau horaire P89 non précisé (blind). Hors périmètre 6.3 (P89 inchangé).
- R-24 — Cellule `README.md:8` qui s'allonge (blind). Cosmétique, cohérent avec les autres entrées de la table.
- R-25 — Fixtures `P89/raw/LP89_682_617_267..293` hors story, non lues par un test (blind, verification-gap, auditor). Commit distinct `48a7b09` (`chore(fixtures)`) entré dans le range par position ; même convention que `f6b18b5` ; pas de code.
- R-26 — Fixtures : encodage non déclaré, blancs de fin menacés par `autocrlf` (blind). `.gitattributes` déclare `P89/** -text`.
- R-27 — Fixtures quasi dupliquées, données de production (blind). Données de référence ajoutées délibérément par l'utilisateur, hors revue de la story.

## 6. Auto-vérifications

- Lentilles lancées : 4/4, toutes ont rendu un résultat (`failed_layers` vide).
  - blind-hunter : 28 constats bruts ;
  - edge-case-hunter : 8 constats bruts ;
  - verification-gap : 2 trous principaux et 3 constats annexes ;
  - acceptance-auditor : 0 finding et 5 points hors mandat.
- Dédoublonnage :
  - P-1 fusionne verification-gap, blind-hunter et auditor (hors mandat) ;
  - F-2 fusionne quatre constats edge-case et blind-hunter : lecture partielle, crash, publication par un tiers, puis retraitement ;
  - F-1 fusionne trois constats blind-hunter et auditor.
- Diff :
  - range complet : 36 fichiers, +575 / −19 ;
  - dont Story 6.3 (`0ba63b4`) : 9 fichiers, +494 / −19 ;
  - dont fixtures (`48a7b09`) : 27 fichiers, +81.
- Code relu avant la notation :
  - `InboxScanner.cs:200-360` ;
  - `ImportOptions.cs` ;
  - `Kape22FichierProcessor.cs:90-230` ;
  - `P89FolderConverter.cs:50-160` ;
  - `InboxScannerTests.cs` (en-tête, théorie FR26-1, tests FR26-6 et « sans dossier ») ;
  - `AcCoverageCompletenessTests.cs` ;
  - `e2e-worker-import.ps1` ;
  - `README.md:96-118` ;
  - `.gitattributes`.
- Tests : non relancés pendant la revue. Le spec atteste 0 warning au build et des tests Unit, `ImportP60.Tests` et `ConvertP89.Tests` au vert.

## Clôture (2026-09-29)

- D-1 → tranché (option 2), reclassé en P-5.
- P-1 → appliqué : test `Tick_ExportUndeletableAfterAFailedFiling_WarnsAndFilesTheNextFichier_AcFr26_6` et hook `MoveHook` dans `InMemoryFileSource`.
- P-2 → appliqué : message du `Warning` de `TryDelete` reformulé.
- P-3 → appliqué : en-tête et commentaire de `XmlExportPath` corrigés dans `ImportOptions` ; la ligne trop longue, déplacée mais non recoupée à la revue, est recoupée à la clôture.
- P-4 → appliqué : `e2e-worker-import.ps1` vide `$exportPath` au démarrage.
- P-5 → appliqué : note « Code review D-1 » au Spec Change Log.
- F-1 → tracé dans deferred-work.md (PLANNED Story 6.4).
- F-2 → tracé dans deferred-work.md (PLANNED Story 6.4).
- F-3 → tracé dans deferred-work.md (PLANNED Story 6.4).
- Vérification finale : build `-warnaserror` 0 erreur ; Unit 192 + 48 + 917 + 21 au vert ; Integration Kape22Importer.Tests 1250 au vert (18 ignorés), AscoLsiJournal.Tests 2 au vert. L'unique échec du run complet (`GpaoImportP60WorkerEndToEndTests`, port 5050 tenu par un Launcher orphelin d'un run interrompu) est au vert en relance isolée, une fois le port libéré (1 au vert).
- Statut : story 6-3 → done ; commit de clôture = celui qui ajoute cette section.
