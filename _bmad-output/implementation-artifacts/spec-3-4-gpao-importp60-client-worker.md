---
title: 'Story 3.4 — worker GpaoImportP60 (Client Launcher)'
type: 'feature'
created: '2026-09-09'
status: 'done'
review_loop_iteration: 0
baseline_commit: 'eeb84f96f9dbfe329d156e06983e7653400958e0'
context:
  - _bmad-output/implementation-artifacts/epic-3-context.md
  - _bmad-output/implementation-artifacts/spec-3-0-repositionnement-structurel-lib.md
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** La lib `Kape22Importer` (TextToXml.sln, `eeb84f9`) est prête mais rien
ne l'exécute. Cette story fusionne l'objectif B de la Story 3.0 (créer le `Client`),
la Story 3.4 réécrite (boucle `Actions` + FR‑8 + arrêt) et le seam de journalisation
de la Story 3.3 (`ILogger` → `SharedLogger`).

**Approach:** Ajouter à `MicroServices.sln` un worker mince
**`class Client : Publisher, IPublisher, IService`** (projet `GpaoImportP60`,
dossier de solution `GPAO`, modèle `Laminoir/OrdresFabricationSync`) et l'enregistrer
dans le `Launcher` (`WorkerRegistry` + `workers.json`). L'interruption fine
entre Fichiers (`InboxScanner.RunTick(CancellationToken)`) est un suivi séparé
(`deferred-work.md`) : ici `Stop()` dispose le timer (aucun nouveau tick) et le
tick en cours se termine, comme `OrdresFabricationSync.Client`.

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
  Aucune modif de la lib.
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
- Ne pas modifier `InboxScanner` (l'interruption fine est différée).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Démarrage nominal | config valide, broker joignable, descripteur ⟺ table compatibles | `CreateAsync` connecte, FR‑8 passe, `Start()` arme le timer (`Frequency` = `Import:PollingInterval` en secondes) | — |
| FR‑8 incompatible | modèle `AscoLsiDbContext` / descripteur incompatibles | `LogError` (event dédié), la boucle **ne démarre pas**, `IsConnected` reste vrai | `StartupCompatibilityException` capturée dans `CreateAsync` |
| Tick nominal | 3 Fichiers dans l'inbox | `RunTickCore` : `InboxScanner.RunTick()` + `PurgeRetention()` ; puis `Publish()` d'un message de statut | exception par Fichier déjà contenue dans `InboxScanner.ProcessFromProcessing` |
| Exception inattendue dans `Actions` | `DirectoryFileSource` lève (dossier injoignable) | capturée dans `Actions`, `LogError`, le worker **continue** (timer non tué) | `try/catch` autour du corps de `Actions` |
| `Stop()` du Launcher | `WorkerAdapter.StopAsync` → `Client.Stop()` | `base.Stop()` dispose le timer → aucun nouveau tick ; un tick en vol se termine ; pas de demi‑insertion (garanti par la transaction de `Kape22Persister`) | — |
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

- **`RunTickCore` `internal static`** : `Client.Actions` est une coquille non
  testable (broker, timer). Toute la logique testable est extraite dans une
  méthode statique prenant ses dépendances en paramètres — comme
  `OrdresFabricationSync.Client.SyncCoreAsync`. Le pipeline lui‑même
  (`InboxScanner` / `Kape22FichierProcessor`) est déjà couvert dans TextToXml.sln.
- **Pont Serilog** : `InboxScanner` / `Kape22FichierProcessor` prennent `ILogger<T>` ;
  `AbstractService.Logger` est un `Serilog.Core.Logger`.
  `new SerilogLoggerFactory(Logger).CreateLogger<T>()` (`Serilog.Extensions.Logging`)
  route le journal vers `MQTTnetServices.Logs` sans câbler de sink (revisite 3.3).
- **Arrêt (grossier, assumé)** : `Publisher.Stop()` dispose le `_timer` → aucun
  nouveau tick. Un `Execute` en cours court jusqu'au bout (pas d'annulation
  coopérative — comme `OrdresFabricationSync`). L'atomicité par Fichier est
  garantie par la transaction de `Kape22Persister` : jamais de demi‑insertion,
  même tué. L'interruption fine entre Fichiers est différée (`deferred-work.md`,
  `spec-3-4-...`).
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
