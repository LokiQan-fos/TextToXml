---
title: 'Story 3.4 — worker GpaoImportP60 (Client Launcher)'
type: 'feature'
created: '2026-09-09'
status: 'done'
review_loop_iteration: 1
baseline_commit: 'eeb84f96f9dbfe329d156e06983e7653400958e0'
context:
  - _bmad-output/implementation-artifacts/epic-3-context.md
  - _bmad-output/implementation-artifacts/spec-3-0-repositionnement-structurel-lib.md
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

<!-- Renegotiated 2026-09-09 (code review D1, ratified by the user): the cooperative stop between -->
<!-- Fichiers is delivered in this story, not deferred. InboxScanner.RunTick takes a -->
<!-- CancellationToken and Client wires it from an override Stop(). The paragraphs below are amended -->
<!-- to match; epics.md l.1408-1411 and PRD AC-FR14-6 already reflect it. -->

## Intent

**Problem:** La lib `Kape22Importer` (TextToXml.sln, `eeb84f9`) est prête mais rien
ne l'exécute. Cette story fusionne l'objectif B de la Story 3.0 (créer le `Client`),
la Story 3.4 réécrite (boucle `Actions` + FR‑8 + arrêt) et le seam de journalisation
de la Story 3.3 (`ILogger` → `SharedLogger`).

**Approach:** Ajouter à `MicroServices.sln` un worker mince
**`class Client : Publisher, IPublisher, IService`** (projet `GpaoImportP60`,
dossier de solution `GPAO`, modèle `Laminoir/OrdresFabricationSync`) et l'enregistrer
dans le `Launcher` (`WorkerRegistry` + `workers.json`). L'arrêt coopératif entre
Fichiers est livré : `InboxScanner.RunTick(CancellationToken)` vérifie le jeton
entre deux Fichiers (jamais mi‑Fichier, jamais passé à `processor.Process`), et
`Client.override Stop()` annule un `CancellationTokenSource` puis attend le tick
en vol dans le budget d'arrêt avant de disposer le timer.

## Boundaries & Constraints

**Always:**
- **CC‑1 test‑first**, CC‑2/CC‑3 (commentaires anglais, une phrase capitalisée
  finissant par un point, au‑dessus du code, pas de trailing), CC‑4 (ordre
  alphabétique des propriétés/initialiseurs, insensible à la casse), CC‑5
  (vocabulaire glossaire), **CC‑7** (aucune chaîne de connexion / secret en dur —
  tout de `IConfiguration`).
- Le `Client` réutilise la lib telle quelle : `InboxScanner`,
  `Kape22FichierProcessor`, `DirectoryFileSource`, `ImportOptions`,
  `AscoLsiDbContext`, `StartupCompatibilityCheck.Verify`, `EmbeddedDescriptor.Xml`.
  Seule modif de lib autorisée (renégociée 2026‑09‑09) : `InboxScanner.RunTick`
  gagne un `CancellationToken` optionnel vérifié entre Fichiers.
- Modèle `OrdresFabricationSync` respecté : `Actions` **ne lève jamais** (corps
  dans un `try/catch` → `LogError`, on continue) ; sinon le callback du timer de
  `Publisher` appelle `Stop()` et tue le worker.
- FR‑8 : `StartupCompatibilityCheck.Verify` tourne dans `CreateAsync` **avant**
  `Start()` ; `StartupCompatibilityException` → `LogError` + `return` sans démarrer
  la boucle (défaut de déploiement, `AC-FR8-1..4`).
- Chaînes `AscoLSI` + `MQTTnetServices` et section `Import:*` : lues de
  `GpaoImportP60.json`, **jamais en dur**. `ConnectionStrings:AscoLSI` vide/absent
  → `InvalidOperationException` au message clair (reprend la garde de
  l'ex‑`AddAscoLsiPersistence`).
- MicroServices.sln : `dotnet build` + `dotnet test` verts (79 tests existants
  intacts + les nouveaux).

**Ask First:**
- Toucher la lib `Kape22Importer` ou un worker existant de `MicroServices.sln`
  (`OrdresFabricationSync`, `CopyDataToDb`, …) ou `MicroService.csproj`.
- Le bump complet des workers prod à net10/EF10 (AR‑13) — reste ouvert.

**Never:**
- Pas de dépendance ASP.NET Core dans `GpaoImportP60`. Pas de `WorkerStatus` /
  endpoint HTTP côté worker (le `Launcher` fournit `WorkerAdapter`).
- Pas d'abstraction `IImportJournal` — le pont `ILogger` est
  `Serilog.Extensions.Logging` (`new SerilogLoggerFactory(Logger)`).
- Ne pas commiter la copie SVN de `MicroServices.sln` (l'utilisateur commite).
- Ne pas modifier `InboxScanner` au‑delà du `CancellationToken` de `RunTick`
  (renégocié 2026‑09‑09) : le reste de la lib reste intouché.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Démarrage nominal | config valide, broker joignable, descripteur ⟺ table compatibles | `CreateAsync` connecte, FR‑8 passe, `Start()` arme le timer (`Frequency` = `Import:PollingInterval` en secondes) | — |
| FR‑8 incompatible | modèle `AscoLsiDbContext` / descripteur incompatibles | `LogError` (event dédié), la boucle **ne démarre pas**, `IsConnected` reste vrai | `StartupCompatibilityException` capturée dans `CreateAsync` |
| Tick nominal | 3 Fichiers dans l'inbox | `RunTickCore` : `InboxScanner.RunTick()` + `PurgeRetention()` ; puis `Publish()` d'un message de statut | exception par Fichier déjà contenue dans `InboxScanner.ProcessFromProcessing` |
| Exception inattendue dans `Actions` | `DirectoryFileSource` lève (dossier injoignable) | capturée dans `Actions`, `LogError`, le worker **continue** (timer non tué) | `try/catch` autour du corps de `Actions` |
| `Stop()` du Launcher | `WorkerAdapter.StopAsync` → `Client.Stop()` | `Stop()` annule le jeton (le tick s'arrête entre deux Fichiers), attend le tick en vol (budget `< 5 s`), puis `base.Stop()` dispose le timer ; pas de demi‑insertion (garanti par la transaction de `Kape22Persister`) | — |
| Config connexion vide | `ConnectionStrings:AscoLSI` absent/vide | `InvalidOperationException` au message explicite, à la construction du `Client` | pas d'échec différé opaque |

</frozen-after-approval>

## Code Map

**Modèle de référence** (lire d'abord) : `C:\Users\Administrateur\Documents\MicroServices\Laminoir\OrdresFabricationSync\` — `Client.cs` (ctor `IConfiguration`, `CreateAsync`, `Actions`, `internal static SyncCoreAsync`), `Program.cs`, `WorkerService.cs`, `OrdresFabricationSync.csproj`, `OrdresFabricationSync.json`, et `OrdresFabricationSync.Tests/` (EF InMemory, teste les méthodes `*Core`, jamais `Client`).

**Contrats Launcher** : `MicroServices/Launcher/WorkerRegistry.cs` (`Factories` dict, 4 lignes existantes), `Launcher/workers.json`, `Launcher/Adapters/WorkerAdapter.cs` (`WorkerAdapter<TClient> where TClient : AbstractService`), `MicroServices/MicroService/Logging/SharedLogger.cs`, `MicroService/Service/AbstractService.cs` (`protected Serilog.Core.Logger Logger`), `MicroService/Publish/Publisher.cs` (`Frequency`, `Execute`, `_timer`, `Start`/`Stop`).

**API lib consommée** (TextToXml.sln, toutes `public`) :
- `Kape22Importer.InboxScanner(IFileSource, IFichierProcessor, ImportOptions, TimeProvider, ILogger<InboxScanner>)` — `RunTick()`, `PurgeRetention()`.
- `Kape22Importer.Kape22FichierProcessor(Func<AscoLsiDbContext> newContext, IConfiguration, ImportOptions, TimeProvider, ILogger<Kape22FichierProcessor>)` — implémente `IFichierProcessor`.
- `Kape22Importer.DirectoryFileSource(string rootPath)`.
- `Kape22Importer.ImportOptions` (`SectionName = "Import"`) ; `Kape22Importer.Persistence.AscoLsiDbContext(DbContextOptions<AscoLsiDbContext>)`.
- `Kape22Importer.StartupCompatibilityCheck.Verify(IModel model, string descriptorXml)` + `StartupCompatibilityException` ; `Kape22Importer.EmbeddedDescriptor.Xml`.

**Fichiers créés / modifiés :**

- `GPAO/ImportP60/GpaoImportP60.csproj` — **nouveau**. `Microsoft.NET.Sdk`,
  `<OutputType>Exe</OutputType>`, `net10.0`, `ImplicitUsings`/`Nullable` enable.
  `ProjectReference` : `..\..\MicroService\MicroService.csproj` **et** la lib
  `Kape22Importer.csproj` (chemin relatif cross‑dépôt vers `Documents\TextToXml\src\Kape22Importer`,
  ajuster au 1er build — précédent : la réf conditionnelle `PortalSharedLibrary`
  dans `Kape22Importer.csproj`). `PackageReference` :
  `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Configuration.Json`,
  `Serilog.Extensions.Logging` (versions inline, alignées MicroService : Serilog 4.x).
  `None Update` copie `GpaoImportP60.json`.
- `GPAO/ImportP60/Globals.cs` — `public const string ProjectName = "GpaoImportP60"`.
- `GPAO/ImportP60/Program.cs` + `WorkerService.cs` — copiés de `OrdresFabricationSync`
  (hôte standalone `dotnet run` ; le Launcher n'utilise que `Client`).
- `GPAO/ImportP60/GpaoImportP60.json` — `ConnectionStrings:{AscoLSI,MQTTnetServices}`
  (placeholders vides), `Import:{InboxPath, ArchiveFolder, ErrorFolder,
  ProcessingFolder, PollingInterval, RetentionDays, InitiatingServer, Commande}`
  (défauts repris de `TextToXml/src/Kape22Importer/appsettings.json`).
- `GPAO/ImportP60/Client.cs` — **le cœur**. Champs privés depuis `IConfiguration`
  (`_ascoLsi`, `_mqtt`, `ImportOptions` via `configuration.GetSection(ImportOptions.SectionName).Get<ImportOptions>()`).
  Ctor : valide `_ascoLsi` non vide ; `Frequency = Math.Max(1, (int)options.PollingInterval.TotalSeconds)` ;
  `Topic = Name = Globals.ProjectName` ; `Execute = Actions`.
  - `CreateAsync()` : `if (!IsConnected) await ConnectWithRetryAsync(); if (!IsConnected) return this;`
    puis `try { using var ctx = NewAscoLsiContext(); StartupCompatibilityCheck.Verify(ctx.Model, EmbeddedDescriptor.Xml); }`
    `catch (StartupCompatibilityException ex) { LogError(AppLogEvents.<x>, ex, "..."); return this; }`
    puis `await Start(); return this;`.
  - `Actions()` : `if (!IsConnected) await Connect(); if (!IsConnected) return;`
    puis `try { RunTickCore(NewFileSource(), NewProcessor(), _options, TimeProvider.System, LoggerFactory()); }`
    `catch (Exception ex) { LogError(..., ex, "..."); }` puis `PayLoad = DateTime.Now.ToString(...); await Publish();`.
  - `internal static void RunTickCore(IFileSource fileSource, IFichierProcessor processor,
    ImportOptions options, TimeProvider timeProvider, ILoggerFactory loggerFactory)` —
    `var scanner = new InboxScanner(fileSource, processor, options, timeProvider, loggerFactory.CreateLogger<InboxScanner>()); scanner.RunTick(); scanner.PurgeRetention();`.
  - Helpers privés : `NewAscoLsiContext()`, `NewFileSource()` (`new DirectoryFileSource(_options.InboxPath)`),
    `NewProcessor()` (`new Kape22FichierProcessor(NewAscoLsiContext, _configuration, _options, TimeProvider.System, LoggerFactory().CreateLogger<Kape22FichierProcessor>())`),
    `LoggerFactory()` (`new SerilogLoggerFactory(Logger)`).
- `Launcher/Launcher.csproj` — `ProjectReference` vers `GpaoImportP60.csproj`.
- `Launcher/WorkerRegistry.cs` — entrée `Factories["GpaoImportP60"]` (modèle des
  lignes existantes, `WorkerAdapter<GpaoImportP60.Client>`).
- `Launcher/workers.json` — `{ "Name": "GpaoImportP60", "Type": "GpaoImportP60", "ConfigPath": "GpaoImportP60.json" }`.
- `MicroServices.sln` — `dotnet sln add GPAO/ImportP60/GpaoImportP60.csproj GPAO/ImportP60.Tests/GpaoImportP60.Tests.csproj --solution-folder GPAO`.
- `GPAO/ImportP60.Tests/GpaoImportP60.Tests.csproj` + tests — **nouveaux**, modèle
  `OrdresFabricationSync.Tests.csproj` (net10.0, EF InMemory, xunit, `IsTestProject`).
- `_bmad-output/planning-artifacts/epics.md` — Stories 3.0 / 3.4 : `Kape22ImportP60`
  / `Kape22/ImportP60` → `GpaoImportP60` / `GPAO/ImportP60`.

## Tasks & Acceptance

**Execution (MicroServices.sln, SVN, PAS de commit) :**
- [x] `GPAO/ImportP60.Tests/` — csproj + `InMemoryFileSource` + `ClientConfigurationTests`
  (`ReadConfig` guard + liaison `Import`) + `RunTickCoreTests` (tick nominal, inbox
  vide, source qui lève → propagée, FR‑8 wiring compatible, FR‑8 descripteur
  incompatible → lève). 7 tests. Rouge d'abord (le projet ne compilait pas).
- [x] `GPAO/ImportP60/` — `GpaoImportP60.csproj` (+ `Microsoft.Extensions.Hosting`
  10.0.9 requis par `Program.cs`, non listé au plan), `Globals.cs`, `Program.cs`,
  `WorkerService.cs`, `GpaoImportP60.json`.
- [x] `GPAO/ImportP60/Client.cs` — `Client` + `ReadConfig`/`WorkerConfiguration`
  (nommé ainsi, pas `WorkerConfig`) + `RunTickCore` + `CreateAsync` + `Actions` + helpers.
- [x] `Launcher/Launcher.csproj` + `Launcher/WorkerRegistry.cs` + `Launcher/workers.json`
  — `GpaoImportP60` enregistré.
- [x] `MicroServices.sln` — `dotnet sln add` puis `dotnet sln remove` des 3 projets
  transitifs cross‑dépôt aspirés par `add` (ne reste que `GpaoImportP60` + `.Tests`
  dans le dossier `GPAO` ; les deps se construisent via `ProjectReference`).

**Execution (TextToXml.sln, git — commit à part, doc only) :**
- [x] `_bmad-output/planning-artifacts/epics.md` — `Kape22ImportP60` → `GpaoImportP60`
  (Story 3.0 + Story 3.4, y compris le bloc « Tests xUnit »).

**Acceptance Criteria:**
- Given `MicroServices.sln`, when `dotnet build MicroServices.sln` puis `dotnet test`,
  then verts — 79 tests existants intacts + `GpaoImportP60.Tests`.
- Given le `Launcher`, when `WorkerRegistry.CreateAsync` lit `workers.json`, then
  `GpaoImportP60` est un `IManagedWorker` connu (figure dans `GET /workers` avec
  `IsRunning`/`IsActive`/`LastStartedAt`/`LastError`) — `AC-FR14-5`.
- Given un `Client` construit avec `ConnectionStrings:AscoLSI` vide, when la
  construction, then `InvalidOperationException` au message explicite.
- Given `RunTickCore` avec un `InMemoryFileSource` de N Fichiers et un
  `IFichierProcessor` fake qui réussit, when il s'exécute, then les N Fichiers
  sont archivés (le pipeline réel est déjà couvert en TextToXml.sln).
- Given `StartupCompatibilityCheck.Verify` sur un `AscoLsiDbContext` InMemory dont
  le modèle ne correspond pas au descripteur, when `CreateAsync` (ou un test
  direct), then `StartupCompatibilityException` et la boucle ne démarre pas.
- Given `grep -rnE "Server\s*=|Data Source\s*=|password" GPAO/ImportP60/*.cs`,
  when exécuté, then aucun résultat (CC‑7).

## Verification

**Commands:**
- `cd C:\Users\Administrateur\Documents\MicroServices && dotnet build MicroServices.sln` — expected: `Build succeeded`.
- `cd C:\Users\Administrateur\Documents\MicroServices && dotnet test MicroServices.sln` — expected: 0 failed (79 + nouveaux).
- `grep -rnE "Server\s*=|Data Source\s*=" C:/Users/Administrateur/Documents/MicroServices/GPAO/ImportP60` — expected: aucun `.cs`.
- `cd C:\Users\Administrateur\Documents\TextToXml && dotnet build TextToXml.sln -warnaserror` — expected: 0 warning (inchangé — seul epics.md bouge).

## Design Notes

- **Seams `internal static`** : `Client.Actions` / `CreateAsync` sont des coquilles
  non testables (broker, timer, sink SQL). La logique testable est extraite en
  méthodes statiques prenant leurs dépendances en paramètres — comme
  `OrdresFabricationSync.Client.SyncCoreAsync` : `RunTickCore` (scan + purge isolés,
  jeton d'annulation, `onError`), `IsDescriptorCompatible` (décision de la porte
  FR‑8), `ReadConfig` (gardes de config). Le pipeline lui‑même (`InboxScanner` /
  `Kape22FichierProcessor`) est déjà couvert dans TextToXml.sln.
- **Pont Serilog** : `InboxScanner` / `Kape22FichierProcessor` prennent `ILogger<T>` ;
  `AbstractService.Logger` est un `Serilog.Core.Logger`.
  `new SerilogLoggerFactory(Logger).CreateLogger<T>()` (`Serilog.Extensions.Logging`)
  route le journal vers `MQTTnetServices.Logs` sans câbler de sink (revisite 3.3).
- **Arrêt coopératif** (renégocié 2026‑09‑09) : `Client.Stop()` annule un
  `CancellationTokenSource` ; `InboxScanner.RunTick` vérifie le jeton entre deux
  Fichiers (jamais mi‑Fichier, jamais passé à `processor.Process`) et `RunTickCore`
  saute la purge de rétention si le jeton est annulé. `Stop()` attend ensuite le
  tick en vol (`ShutdownBudget` = 4 s) avant `base.Stop()`, qui dispose le `_timer`.
  L'atomicité par Fichier reste garantie par la transaction de `Kape22Persister` :
  jamais de demi‑insertion, même tué. Reste différé (`deferred-work.md`) : rendre
  `PurgeRetention` elle‑même interruptible ; la ré‑entrance du timer de `Publisher`.
- **Cross‑repo** : `GpaoImportP60` → `Kape22Importer.csproj` par chemin relatif ;
  EF Core 10 arrive transitivement (la lib est net10/EF10). Aucun conflit avec
  `OrdresFabricationSync` (EF 9) — assemblies séparées.

## Suggested Review Order

**Le worker — construction et cycle de vie**

- Point d'entrée : la config est lue et validée hors du ctor pour rester testable sans le sink SQL d'`AbstractService`.
  [`Client.cs:47`](../../../MicroServices/GPAO/ImportP60/Client.cs#L47)
- `CreateAsync` : connexion broker → gate FR‑8 (`StartupCompatibilityCheck.Verify`) → `Start()` ; `StartupCompatibilityException` ⇒ `LogError` + `return` sans armer la boucle.
  [`Client.cs:65`](../../../MicroServices/GPAO/ImportP60/Client.cs#L65)
- `Actions` : ne lève jamais (le callback du timer de `Publisher` tuerait le worker sinon) ; construit le pipeline lib et délègue à `RunTickCore`.
  [`Client.cs:98`](../../../MicroServices/GPAO/ImportP60/Client.cs#L98)
- `RunTickCore` : le seam statique testable (`InboxScanner.RunTick()` + `PurgeRetention()`), calqué sur `OrdresFabricationSync.SyncCoreAsync`.
  [`Client.cs:134`](../../../MicroServices/GPAO/ImportP60/Client.cs#L134)
- Pont journalisation : `new SerilogLoggerFactory(Logger)` route l'`ILogger<T>` de la lib vers `MQTTnetServices.Logs` sans câbler de sink (revisite 3.3).
  [`Client.cs:111`](../../../MicroServices/GPAO/ImportP60/Client.cs#L111)

**Enregistrement Launcher**

- Une ligne dans `Factories` — le worker est un `WorkerAdapter<GpaoImportP60.Client>`, rien de plus.
  [`WorkerRegistry.cs:19`](../../../MicroServices/Launcher/WorkerRegistry.cs#L19)
- Entrée `workers.json` + `ProjectReference` dans `Launcher.csproj` + les 2 projets dans `MicroServices.sln` (dossier `GPAO`).

**Tests (MicroServices.sln, modèle `OrdresFabricationSync.Tests`)**

- `RunTickCore` : tick nominal (archive tout), inbox vide (no‑op), source qui lève (propagée pour qu'`Actions` catch), FR‑8 wiring compatible, FR‑8 descripteur incompatible ⇒ lève.
  [`RunTickCoreTests.cs:52`](../../../MicroServices/GPAO/ImportP60.Tests/RunTickCoreTests.cs#L52)
- `ReadConfig` : guard `ConnectionStrings:AscoLSI` vide + liaison de la section `Import`.
  [`ClientConfigurationTests.cs:17`](../../../MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs#L17)

**Doc (TextToXml.sln, git)**

- `epics.md` Stories 3.0/3.4 : `Kape22ImportP60` → `GpaoImportP60`. `deferred-work.md` : arrêt fin `InboxScanner.RunTick(CancellationToken)` + ré‑entrance du timer de `Publisher` (pré‑existant).

## Review Findings

Adversarial code review 2026-09-09 (`/bmad-code-review`, 4-layer fan-out: blind-hunter,
edge-case-hunter, verification-gap, acceptance-auditor). Scope: SVN r530+r531
(`GPAO/ImportP60/**`, `Launcher/**`) + git `8eb735c` (`InboxScanner.cs` cooperative token).
Verdict: **review refused, then all findings applied** (user chose "apply every patch",
2026-09-09). CC-2/CC-3/CC-4/CC-6/CC-7 were clean from the start; the blocking gaps
(AC↔test traceability, frozen-spec breach, `Actions` can throw) are fixed.
`MicroServices.sln` build 0 warning, `dotnet test` green (MicroService 33 · GpaoImportP60 9 ·
CopyDataToDb 13 · OrdresFabricationSync 33 · Launcher.Tests 2). Commits pending: SVN by the
user; TextToXml git doc-only.

### Decision-needed — resolved 2026-09-09 (all → patch, applied)

- [x] [Review][Patch] **(D1 — ratify)** Frozen `<frozen-after-approval>` block breached by the
  delivered cooperative cancellation. **Applied:** frozen block amended (Approach, `Always`
  lib-reuse clause, `Never` clause, Design Notes "Arrêt coopératif") with a dated renegotiation
  note; matches `epics.md` l.1408-1411 and PRD `AC-FR14-6`.
- [x] [Review][Patch] **(D2 — add test)** `AC-FR14-5` had no `[Trait("AC","FR14-5")]` test.
  **Applied:** new `Launcher.Tests` project (added to `MicroServices.sln`), `WorkerRegistry.Factories`
  made `internal` + `InternalsVisibleTo`. `WorkerRegistryTests` asserts `GpaoImportP60` resolves to
  `WorkerAdapter<GpaoImportP60.Client>` exposing `IsRunning`/`IsActive`/`LastStartedAt`/`LastError`,
  and that `workers.json` lists it. 2 tests, `[Trait("AC","FR14-5")]`.
- [x] [Review][Patch] **(D3 — enforce budget)** NFR-9 "< 5 s" not enforced. **Applied:** `Actions`
  stores its running tick as `_tick` (`Task.Run` over the synchronous body); `Client.override Stop()`
  cancels then `await Task.WhenAny(_tick, Task.Delay(ShutdownBudget))` (4 s) before `base.Stop()`.
  `RunTickCore_WithACancelledToken_ProcessesNothingAndSkipsPurge_AcFr14_6` covers the seam.

### Patch — applied 2026-09-09

- [x] [Review][Patch] `Actions`: `PayLoad` / `await Publish()` moved so the tick body is a guarded
  task; the guarded body never throws (`RunTickCore` wraps scan and purge each in try/catch →
  `onError`). *Always: Actions ne lève jamais* holds. [`GPAO/ImportP60/Client.cs`]
- [x] [Review][Patch] `RunTickCore`: `if (cancellationToken.IsCancellationRequested) return;`
  between `RunTick` and `PurgeRetention` — a cancelled Stop skips the purge sweep.
- [x] [Review][Patch] `RunTickCore`: scan and purge each isolated in their own try/catch → a scan
  that throws no longer skips that tick's purge; neither escapes to the Publisher timer callback.
- [x] [Review][Patch] `Client` ctor `Frequency`: `>= TimeSpan.FromSeconds(1) ? (int)…TotalSeconds : 30`
  — a sub-second interval falls back to 30 instead of arming the timer at a zero period; comment fixed.
- [x] [Review][Patch] `ReadConfig`: throws `InvalidOperationException` with a clear message when
  `Import:InboxPath` is empty, next to the `AscoLSI` guard. New test
  `ReadConfig_WhenInboxPathIsMissing_ThrowsWithAClearMessage`.
- [x] [Review][Patch] Testable seams extracted from `Client`: `IsDescriptorCompatible` (FR-8 gate
  decision → `AC-FR8-1` / `AC-FR8-4` tests exercise the Client's branch, not just the library
  function); `RunTickCore` gains `onError` so the "never throws" contract is tested
  (`RunTickCore_WhenTheFileSourceThrows_ReportsToOnErrorAndDoesNotEscape`).
- [x] [Review][Patch] `RunTickCore_ArchivesEveryInboxFichier` → renamed
  `RunTickCore_ArchivesEveryStableInboxFichier`, `[Trait("AC","FR13-1")]` dropped (it is a wiring
  smoke test), comment corrected.
- [x] [Review][Patch] CC-5: worker test names now cite their AC (`…_AcFr14_6`, `…_AcFr8_1`,
  `…_AcFr8_4`, `…_AcFr14_5`); the config-guard tests carry a header comment naming the FR-11-8 /
  FR-12-8 contracts they re-verify and no longer wrongly tag those other-story ACs.

### Deferred (pre-existing or design-acknowledged)

- [x] [Review][Defer] FR-8 tests build the model with `UseInMemoryDatabase` while production
  `CreateAsync` uses `UseSqlServer`; the `IModel` differs (column types, nullability) — exactly
  what FR-8 checks. Exhaustive `AC-FR8-1..3` are in `Kape22Importer.Tests` (AR-12 harness) by
  spec. — deferred, spec-sanctioned split.
- [x] [Review][Defer] Non-`StartupCompatibilityException` faults from `NewAscoLsiContext()` /
  `Verify` in `CreateAsync` (e.g. malformed connection string) escape the catch; caught by
  `WorkerAdapter.StartAsync` (LastError + rethrow), not a clean `LogError + return`. — deferred.
- [x] [Review][Defer] `Dispose()` does not `Cancel()`; disposal not via `Stop()` tears down
  mid-tick without signalling. `WorkerAdapter.StopAsync` always calls `Stop()` first in practice.
  — deferred, low risk.
- [x] [Review][Defer] `WorkerService` (standalone `dotnet run` host): `RetryHelper` rebuilds a
  `Client` per attempt without disposing the previous; `StopAsync` never `Dispose()`s. Impact
  limited to `dotnet run`; inherited from the `OrdresFabricationSync` model. — deferred.
- [x] [Review][Defer] `IsRunning` (=`IsConnected`) stays true after the FR-8-incompatible /
  broker-never-connected branch, with `WorkerAdapter.LastError` null: dashboard green, worker
  silently idle. The I/O matrix accepts "IsConnected reste vrai"; the null `LastError` is an
  observability follow-up. — deferred.
