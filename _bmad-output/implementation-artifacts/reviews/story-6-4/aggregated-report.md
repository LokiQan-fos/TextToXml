# Review report — Story 6.4

Range : bef7a555ef3c5f8860f63bef75307f07d6d10373^..HEAD (bef7a55) + SVN MicroServices r540 + 4 fichiers de test non versionnés
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-4-robustesse-commune-workers-gpao.md
Date : 2026-09-29
Verdict : REFUSÉ
Findings : D=2 P=7 F=5 R=21  (Decision, Patch, Defer, Rejetés)
Mise à jour 2026-09-29 : décisions de l'utilisateur, d'où les décomptes effectifs D=0 P=9 F=5 R=21.
- D-1 → option 1, reclassé en patch P-8 : note de renégociation datée dans le bloc figé (ReadConfig pré-vérifié, toute exception de la factory réessayée) ; le `catch` du pré-check est élargi pour que rien ne s'échappe de la tâche fire-and-forget ; le texte du ledger est aligné.
- D-2 → option 1, reclassé en patch P-9 : l'arrêt après épuisement est conservé, avec une note de renégociation datée dans le bloc figé.

P-1..P-9 sont appliqués dans l'arbre de travail (option « tout appliquer »). Rien n'est commité, ni dans git ni dans SVN.
- P-1 : `svn add` des 4 fichiers de test (statut `A`). Le commit SVN reste à faire par l'utilisateur.
- P-2 : AC-FR11-8 est réécrit (`AscoLSI` seule) avec une note datée dans `PRD.md`. La reformulation d'`epics.md` est alignée ; les descriptions de tâches historiques d'`epics.md` (≈851, 1263) sont laissées telles quelles.
- P-3 : nouveau test `RunTickCore_OnlyThePurgeThrows_ReturnsFalse_AcFr25_5` (P60 uniquement : le `RunTickCore` de P89 n'a pas de purge).
- P-4 : deux nouveaux tests, `RunTick_ExecuteThrows_StopsThePublisher` et `RunTick_ExecuteAndStopThrow_ContainsBothAndResetsTheGuard_AcFr25_1` (ce dernier avec un `ThrowingStopPublisher`).
- P-5 : nouveau test `Actions_ErrorAfterDispose_LogsNothing_AcFr25_3` dans les deux workers, via le hook `SpyClient.DisposeOnConnect`.
- P-6 : commentaire de `Publisher.cs:16` corrigé (CC-2).
- P-7 : texte du ledger aligné (RESOLVED AC-FR25-6, « Dispose » au lieu de « Stop/Dispose », mentions 6.4 des entrées UNPLANNED requalifiées).
- P-8 : `catch (Exception)` sur le pré-check `ReadConfig` des deux `WorkerService` ; note « Code review D-1 » ajoutée au Spec Change Log.
- P-9 : note « Code review D-2 » ajoutée au Spec Change Log.

Hors patches, `sprint-status.yaml` passe `6-4-robustesse-commune-workers-gpao` à `done`.

Vérification :
- `dotnet build MicroServices.sln -warnaserror` : 0 avertissement, 0 erreur.
- Tests MicroServices au vert : MicroService 39, ImportP60 46, ConvertP89 52, Laminoir 33, Zumbach 13.
- `TextToXml.sln` Unit au vert : 192 + 48 + 21 + 917.
- Mutations, toutes rouges puis restaurées :
  - retrait de `succeeded = false` dans le `catch` de purge ;
  - retrait de la garde `_disposed` d'`OnTickError`, en P60 comme en P89 ;
  - retrait de `await Stop()` ;
  - retrait du `try/catch` autour de `Stop()`.

F-1..F-5 restent à consigner dans `deferred-work.md` par `/commit-review`.

Le range git ne contient que la documentation (spec, PRD, ledger, sprint-status). Le code de la story vit dans MicroServices (SVN). Sur accord de l'utilisateur au CHECKPOINT, `{diff_output}` agrège :
- le diff git ;
- `svn diff -c 540` ;
- le contenu complet des 4 fichiers de test non versionnés (`ClientRobustnessTests.cs` et `WorkerServiceTests.cs`, pour P60 comme pour P89).

Chemins MicroServices relatifs à `C:\Users\Administrateur\Documents\MicroServices`.

## 1. Verdict

**REFUSÉ.** Sévérités (après triage) : high = 3 (D-1, P-1, P-2), medium = 6 (D-2, P-3, P-4, P-5, F-1, F-4), low = 5 (P-6, P-7, F-2, F-3, F-5).

Motifs du refus :
- Deux écarts au bloc `<frozen-after-approval>` (matrice I/O, lignes « Standalone config error » et « Standalone transient error ») n'ont aucune note de renégociation datée (D-1, D-2).
- Une déviation CC-2 : un commentaire commence par un chiffre au lieu d'une capitale (P-6).
- CC-1 : l'attestation cite des tests absents de toute révision commitée (P-1).

Le reste du bloc figé est respecté :
- la garde `Interlocked` du `Publisher` ;
- `Connect` dans le `try` ;
- `_disposed` à l'entrée d'`Actions`, avant `Publish` et dans `OnTickError` ;
- `RunTickCore` → `bool` ;
- `ReadConfig` évalué dans l'argument `this(...)` ;
- le retrait de `MQTTnetServices` des JSON workers ;
- les Never : pas de `Dispose` bloquant, pas de validation du sink Logs, et les 5 items reclassés ne sont pas touchés.

## 2. Findings Decision — à trancher par l'humain

### D-1 — `WorkerService` : une `InvalidOperationException` de la factory est réessayée, contre la matrice figée
- **Sources :** acceptance-auditor, blind-hunter, edge-case-hunter, verification-gap.
- **Localisation :** `GPAO/ImportP60/WorkerService.cs:48-83` (jumeau `GPAO/ConvertP89/WorkerService.cs:48-83`). Test associé : `WorkerServiceTests.StartClient_TransientErrors_RetriesAndDisposesTheFailedClient_AcFr25_6` (P60 et P89).
- **Constat :** la matrice figée (spec l.39) dit « `Client` factory throws `InvalidOperationException` → one attempt, message, `StopApplication()`, no retry », et le Code Map (l.54) le répète. Le code procède autrement :
  - il pré-valide par `Client.ReadConfig(_configuration)` hors boucle ;
  - il réessaie ensuite toute exception de la factory, `InvalidOperationException` comprise ;
  - le test fige ce comportement (« an InvalidOperationException from the Client constructor included »).
- **Aucune note datée dans la spec.** Le texte RESOLVED de `deferred-work.md` affirme de son côté que le worker « stops the host on a factory `InvalidOperationException` », ce qui est inexact.
- **Effets de bord du pré-check :**
  - `ReadConfig` tourne deux fois (pré-check puis constructeur).
  - Une `ArgumentException` du constructeur `AscoLsiFichierJournal` dont le `ParamName` n'est pas `initiatingServer` n'est pas filtrée par le `catch (InvalidOperationException)` du pré-check. Elle s'échappe alors de la tâche jetée (`_ = StartClientAsync()`) et l'hôte reste inerte, sans worker et sans arrêt.
- **Options :**
  1. Renégocier : ajouter une note datée au bloc figé (« config pre-checked via ReadConfig; every factory exception retried, since an InvalidOperationException from the AbstractService SQL sink is transient »), corriger la ligne du ledger, et élargir le `catch` du pré-check (ou le mettre sous un `catch (Exception)`) pour qu'aucune exception ne s'échappe.
  2. Se conformer au bloc figé : `catch (InvalidOperationException)` autour de `_createClient()` dans la boucle, avec message, `StopApplication()` et sans retry. Supprimer le pré-check et adapter le test. Risque : une `InvalidOperationException` transitoire du sink SQL arrêterait alors l'hôte à froid.
- **État :** à trancher par l'humain.

### D-2 — `StopApplication()` après épuisement des 10 tentatives : le chemin transitoire n'est plus « unchanged »
- **Source :** acceptance-auditor.
- **Localisation :** `GPAO/ImportP60/WorkerService.cs:77-83` (jumeau P89). Test associé : `StartClient_EveryAttemptFails_StopsTheHost_AcFr25_6`.
- **Constat :** la matrice figée (l.40) marque le chemin « Standalone transient error » `unchanged` : même retry 10 × 3 s, avec comme seul ajout le dispose du `Client` en échec. Le code ajoute un `catch` final qui arrête l'hôte, et aucune note datée ne le couvre.
- **Options :**
  1. Garder l'arrêt (plus sûr qu'un hôte inerte) et ajouter une note datée au bloc figé.
  2. Retirer l'arrêt et le test.
- **État :** à trancher par l'humain.

## 3. Findings Patch

### P-1 — Les 4 fichiers de test AC-FR25-2/3/5/6 ne sont pas versionnés (CC-1)
- **Sources :** acceptance-auditor, blind-hunter.
- **Localisation :**
  - `GPAO/ImportP60.Tests/ClientRobustnessTests.cs`
  - `GPAO/ImportP60.Tests/WorkerServiceTests.cs`
  - `GPAO/ConvertP89.Tests/ClientRobustnessTests.cs`
  - `GPAO/ConvertP89.Tests/WorkerServiceTests.cs`
- **Constat :** `svn status` les montre en `?`. La r540 contient le code sans ces tests, alors que le corps de bef7a55 les cite.
- **Action :** l'utilisateur fait `svn add` des 4 fichiers puis les commite. La révision SVN est ensuite citée dans le commit de clôture.

### P-2 — AC-FR11-8 exige toujours `MQTTnetServices` dans la configuration
- **Source :** acceptance-auditor.
- **Localisation :** `_bmad-output/planning-artifacts/PRD.md:706`.
- **Constat :** la décision du 2026-09-29 a retiré la clé des JSON workers, mais seul AC-FR25-4 a été réconcilié. AC-FR11-8 dit toujours « chaînes de connexion (`AscoLSI`, `MQTTnetServices`) lues de la configuration ».
- **Action :** réécrire AC-FR11-8 (« `AscoLSI` lue de la configuration ; le sink Logs via `Logging:ConnectionString` / `SharedLogger` ») avec une note datée 2026-09-29, et vérifier les éventuelles restatements dans `epics.md`.

### P-3 — Le chemin « purge en échec → tick en échec » de `RunTickCore` n'est pas testé
- **Source :** verification-gap.
- **Localisation :** `GPAO/ImportP60/Client.cs:401-409` (`catch` de `PurgeRetention`) et son jumeau P89.
- **Constat :** aucun test ne fait lever `PurgeRetention` vers `onError`. La preuve : supprimer `succeeded = false;` du `catch` de purge laisse tous les tests verts, et la moitié « purge » d'AC-FR25-5 n'est donc pas couverte.
- **Action :** ajouter dans `RunTickCoreTests` (P60 et P89) un test avec un file source dont `List` renvoie vide et `ListRecursive` lève `InvalidOperationException`, puis asserter `false` (`_AcFr25_5`, `[Trait("AC","FR25-5")]`).

### P-4 — `Publisher.RunTickAsync` : `Stop()` après un `Execute` qui lève, et la containment d'un `Stop()` qui lève, ne sont pas couverts
- **Source :** verification-gap.
- **Localisation :** `MicroService/Publish/Publisher.cs:109-122`. Tests : `MicroService.Tests/PublisherLifecycleTests.cs:91-108`.
- **Constat :**
  - Le cas `previousThrew: true` ré-arme `IsRunning` lui-même, si bien que retirer `await Stop();` ne casse rien.
  - Aucun test ne construit de `Publisher` dont le `Stop()` lève : le correctif P-2 de la 1re boucle de revue (pas d'échappement hors du callback `async void`) n'est pas épinglé.
- **Action :** ajouter 2 tests :
  - `Start()` puis un `Execute` qui lève et `await RunTickAsync()` → `_timer` est null ;
  - une sous-classe dont `Stop()` lève → `Record.ExceptionAsync(publisher.RunTickAsync)` est null, et un tick suivant exécute bien `Execute` (flag réinitialisé).

### P-5 — La garde `_disposed` d'`OnTickError` n'est exercée dans aucun worker
- **Source :** verification-gap.
- **Localisation :** `GPAO/ImportP60/Client.cs:412-416` et `GPAO/ConvertP89/Client.cs:322-326`.
- **Constat :** aucun test n'envoie d'exception à `OnTickError` après `Dispose`, et retirer `if (_disposed) return;` laisse tout vert. C'est pourtant une des trois gardes exigées par le Code Map (AC-FR25-3).
- **Action :** dans chaque `ClientRobustnessTests`, surcharger `Connect` pour qu'il dispose le spy puis lève, et asserter qu'aucun `LogError:` n'apparaît dans `Calls` après `Dispose` (`_AcFr25_3`).

### P-6 — Déviation CC-2 : un commentaire commence par un chiffre
- **Source :** acceptance-auditor.
- **Localisation :** `MicroService/Publish/Publisher.cs:16`.
- **Constat :** `/// 1 while a tick runs, 0 otherwise; see RunTickAsync.` CC-2 exige une capitale en tête de phrase.
- **Action :** remplacer par `/// Set to 1 while a tick runs and 0 otherwise; see RunTickAsync.`

### P-7 — Le texte du ledger est faux ou périmé pour la 6.4
- **Source :** blind-hunter.
- **Localisation :** `_bmad-output/implementation-artifacts/deferred-work.md`.
- **Constat :**
  - L'entrée RESOLVED AC-FR25-6 affirme l'arrêt sur une `InvalidOperationException` de la factory (voir D-1).
  - L'entrée RESOLVED cite `ClientRobustnessTests` pour « Stop/Dispose », alors qu'aucun de ces tests n'appelle `Stop()`.
  - Les 6 entrées reclassées UNPLANNED gardent un texte d'évidence qui les rattache à « Story 6.4 ».
- **Action :** aligner le texte RESOLVED selon l'issue de D-1, remplacer « Stop/Dispose » par « Dispose », et préfixer l'évidence des entrées UNPLANNED par « (historique) » ou retirer la mention 6.4.

## 4. Findings Defer

### F-1 — `StopAsync` pendant la boucle de retry laisse un `Client` orphelin
- **Sources :** edge-case-hunter, blind-hunter.
- **Localisation :** `GPAO/*/WorkerService.cs:36-83`.
- **Constat :** `StartClientAsync` est fire-and-forget, sans token. Un arrêt pendant les ~30 s de retry voit `_client` à null et rend la main ; la boucle peut ensuite créer et démarrer un `Client` jamais disposé.
- **Justification :** ce schéma fire-and-forget est antérieur à la 6.4 et ne concerne que l'hôte standalone (`dotnet run`), pas le Launcher. Le corriger demande un `CancellationTokenSource` et une tâche à attendre, ce qui dépasse FR-25.

### F-2 — Une chaîne `AscoLSI` sans `Initial Catalog` passe `ReadConfig`
- **Sources :** edge-case-hunter, blind-hunter.
- **Localisation :** `GPAO/ImportP60/Client.cs:811-839` et `GPAO/ConvertP89/Client.cs:383-411` (lignes du diff).
- **Constat :** `Server=x;` est accepté et cible la base par défaut du login.
- **Justification :** la matrice figée couvre « absent / blank / malformed ». Exiger le catalogue est un choix produit, puisqu'un login peut légitimement porter une base par défaut. À planifier si l'on veut durcir.

### F-3 — Un tick bloqué fait ignorer silencieusement tous les ticks suivants
- **Source :** edge-case-hunter.
- **Localisation :** `MicroService/Publish/Publisher.cs:98-126`.
- **Justification :** « a skipped tick is dropped » est voulu (Always). Un watchdog ou un Warning sur tick trop long est une fonctionnalité nouvelle, qui touche aussi Laminoir, Video et Zumbach.

### F-4 — Une panne de partage (`IOException`) est contenue en Warning et le heartbeat continue
- **Source :** blind-hunter.
- **Localisation :** `InboxScanner` (bibliothèque `src/Kape22Importer`). Le commentaire du test est à diff:1193-1204.
- **Justification :** ce comportement vient de la bibliothèque, dont toute modification de `src/` est en Ask First. AC-FR25-5 n'est que partiellement tenu pour ce cas. À router vers une story qui décide si une panne de partage est un tick en échec.

### F-5 — `Directory.Exists` sur un partage SMB bloqué s'exécute hors de `_tick`
- **Source :** edge-case-hunter.
- **Localisation :** `GPAO/ImportP60/Client.cs:303`.
- **Justification :** le budget de 4 s de `Stop()` ne couvre pas ce blocage (thread du timer). Il faudrait déplacer la vérification dans le `Task.Run`, avec un impact faible. Le cas est rare et ne cause pas de perte de données.

## 5. Findings rejetés (bruit)

| # | Source | Constat | Motif du rejet |
|---|---|---|---|
| R-1 | edge-case-hunter | Double `Dispose` concurrent → `ObjectDisposedException` sur `Cancel()` | Le Launcher et l'hôte appellent `Dispose` une seule fois ; aucun appelant concurrent. |
| R-2 | edge-case-hunter, blind-hunter | `Cancel()` qui lève via un callback enregistré → fuite du CTS et du client MQTT | Aucun callback n'est enregistré sur le token : `InboxScanner` lit `IsCancellationRequested` et `Task.Run` ne reçoit pas le token. Chemin inatteignable. |
| R-3 | edge-case-hunter | Un `Dispose` pendant `LogInformation` ou `Directory.Exists` laisse démarrer un tick | Fenêtre résiduelle documentée dans le commentaire de `Dispose`. Le token annulé arrête le scanner à la 1re frontière, et la fermer demanderait un `Dispose` bloquant (Never). |
| R-4 | edge-case-hunter | Une annulation à l'arrêt lève `OperationCanceledException`, journalisée en Error | `InboxScanner` teste `IsCancellationRequested` (InboxScanner.cs:67, 82) et ne lève pas. |
| R-5 | edge-case-hunter | `StopAsync` appelé deux fois | L'hôte générique n'appelle `StopAsync` qu'une fois par service. |
| R-6 | acceptance-auditor, blind-hunter | Spec `status: 'done'` alors que sprint-status vaut `review` | Écart normal avant revue, réaligné par `/commit-review`. |
| R-7 | blind-hunter | `review_loop_iteration: 0` alors que des patches de revue (P-2, P-8…) sont cités | Ces patches viennent de la boucle interne de `bmad-build`, pas de cette revue. Champ sans effet. |
| R-8 | blind-hunter | Tâche `epics.md` cochée sans diff | `epics.md` ne reformule pas AC-FR25-4 (grep `FR25-4` vide) ; rien à modifier. |
| R-9 | blind-hunter | Inbox manquante = tick en échec non reflété dans le PRD ni dans la définition de la spec | La matrice figée liste explicitement « Failed tick \| inbox / source folder missing ». C'est cohérent. |
| R-10 | blind-hunter | `ValidAscoLsiConnectionString` dupliqué dans les deux workers | D32 et l'Approach imposent le même correctif dans chaque `Client`. Le mutualiser toucherait une lib partagée (Ask First). |
| R-11 | blind-hunter | Test P89 « disposed during tick » sans `DoesNotContain("Publish")` | `Assert.Equal("Dispose", Calls.Last())` exclut déjà tout `Publish` après `Dispose`. |
| R-12 | blind-hunter | Re-check `_disposed` après `Connect()` non testé | Couvert par P-5 (un `Dispose` dans `Connect`). Pas de finding distinct. |
| R-13 | blind-hunter, edge-case-hunter | Comparaison du compteur du cache `SharedLogger` fragile en parallèle | Seule `ClientConfigurationTests` construit des sinks `SharedLogger` dans chaque assembly. Aucun concurrent identifié. |
| R-14 | blind-hunter | Test en timer réel de 2,5 s, dépendant du timing | Complément voulu (P-11 de build). La garde elle-même est testée de façon déterministe via `RunTickAsync`. |
| R-15 | blind-hunter | `Assert.Equal(10, attempts)` en dur | `MaxAttempts` est une constante privée. Cosmétique. |
| R-16 | blind-hunter | Le `Logger` du `SpyClient` n'est pas disposé | Test uniquement, sans effet observable. |
| R-17 | blind-hunter | L'entrée defer 6.4 n'a ni chemin de rapport ni sévérité | Elle sera complétée par `/commit-review` avec les F-n de ce rapport. |
| R-18 | blind-hunter | « five » items dans Never contre six entrées UNPLANNED | Un item correspond à deux entrées du ledger. La liste nommée est exacte. |
| R-19 | blind-hunter | Retrait de `MQTTnetServices` sans vérification du Launcher ni des overlays | La clé présente dans `Launcher/microservice.settings.json` est celle du Launcher (`WorkerSettings`), pas celle des workers. Le retrait côté worker est sans effet. |
| R-20 | acceptance-auditor | Les tests Video absents de §Verification | Video n'a pas de projet de test. Le build `-warnaserror` de la solution le couvre. |
| R-21 | blind-hunter | `ReadConfig` appelé deux fois | Fusionné dans D-1 : il disparaît avec l'option 2 et reste bénin avec l'option 1. |

## 6. Auto-vérifications

- **4 lentilles lancées en parallèle, toutes revenues** (aucune en `failed_layers`) :
  - blind-hunter : 23 findings ;
  - edge-case-hunter : 12 findings ;
  - verification-gap : 3 findings + 1 autre ;
  - acceptance-auditor : 5 findings + 3 hors mandat.
- **Après dédoublonnage :** 35 findings → D=2, P=7, F=5, R=21.
- **Diff stats :**
  - git `bef7a55^..HEAD` : 4 fichiers, +185 / −20 (doc uniquement) ;
  - SVN r540 : 13 fichiers modifiés ;
  - 4 fichiers de test non versionnés ;
  - `{diff_output}` agrégé : 2010 lignes.
- **Vérifications faites dans le code source (hors diff) :**
  - `WorkerService.StartClientAsync` et le diff r539→r540 ;
  - `Client.Actions`, `Dispose`, `RunTickCore` et `OnTickError` ;
  - `Publisher.RunTickAsync` ;
  - `ReadConfig` (types d'exception) ;
  - `InboxScanner` (pas d'`OperationCanceledException`) ;
  - `PRD.md:706` et `epics.md` ;
  - grep de `MQTTnetServices` dans MicroServices ;
  - test P89 dispose-during-tick.
- **Non vérifié :** aucun `dotnet build` ni `dotnet test` relancé pour cette revue.

## Clôture (2026-09-29)

- D-1 → tranché (option 1) puis appliqué comme P-8 ;
- D-2 → tranché (option 1) puis appliqué comme P-9 ;
- P-1 → appliqué (`svn add` des 4 fichiers de test ; commit SVN à faire par l'utilisateur) ;
- P-2 → appliqué (AC-FR11-8 dans `PRD.md`, `epics.md` aligné) ;
- P-3, P-4, P-5 → appliqués (nouveaux tests) ;
- P-6 → appliqué (commentaire `Publisher.cs`, CC-2) ;
- P-7 → appliqué (texte du ledger) ;
- F-1..F-5 → tracés dans `deferred-work.md` (aucun planifié) ;
- Vérification finale :
  - build `TextToXml.sln -warnaserror` : 0 avertissement, 0 erreur ;
  - Unit : 48 + 192 + 21 + 917 verts ;
  - Integration : 1250 passés, 18 ignorés, **1 échec** : `GpaoImportP60WorkerEndToEndTests.WorkerEndToEnd_ImportsArchivesAndMatchesProduction`. Le script `scripts/e2e-worker-import.ps1` (lignes 65 et 106) assigne la clé `ConnectionStrings:MQTTnetServices`, retirée des JSON workers par la 6.4. Effet de bord hors rapport ; traité par la **Story 6.4-bis**, bloquante avant la 6.5 ;
- Statut : story 6-4 → done ; commit de clôture = celui qui ajoute cette section. Prochain jalon : Story 6.4-bis, puis 6.5.
