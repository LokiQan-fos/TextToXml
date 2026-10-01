---
title: 'Story 6.5 — Integration test of a real GPAO Client'
type: 'chore'
created: '2026-10-01'
status: 'done'
baseline_commit: '74b3a4989f8bd2c2b5d793f899d8c9d6a2242fb2'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** No test builds a real `GpaoImportP60.Client` or `GpaoConvertP89.Client` (public `IConfiguration` constructor, real `AbstractService` SQL log sink). Their instance wiring — `Frequency`, error containment in `Actions`, `Stop()` cancelling the tick, the `Warning` of a deferred Fichier — can regress silently (deferred-work F-1 of Story 5.2, plus the P60 `Client.Actions` test owed since Epic 3).

**Approach:** One shared integration harness in `MicroServices.sln` that, for each worker, builds the real `Client` from an in-memory configuration with its Logs sink routed to a `*_Test` database, drives the real `Execute` delegate, and asserts on the file system and on the `Logs` rows the sink writes (AC-FR25-7). Test-only: no production code changes.

## Boundaries & Constraints

**Always:** CC-1 (characterization tests: red shown by a temporary mutation, reverted), CC-2, CC-4, CC-7 (connection strings in a test settings file, never in code). Tests named `..._AcFr25_7`, `[Trait("AC", "FR25-7")]`, `[Trait("Category", "Integration")]`. Skip cleanly (`Xunit.SkippableFact` 1.5.85, as in TextToXml) when the test settings are missing, the Logs database name does not end with `_Test`, or SQL Server is unreachable. `MICROSERVICE_LOG_CONNECTION_STRING` is set before any `Client` is built and restored afterwards. `IsConnected` is set to `true` directly so no broker is reached.

**Ask First:** any production code change (MicroServices or this repo); any change to existing test projects; writing anywhere but the Logs table of `MQTTnetServices_Test` and a temp folder.

**Never:** the internal test constructor or a spy subclass (that is Story 6.4's `ClientRobustnessTests`); a real broker; Docker/Testcontainers; production databases; the Launcher.

## I/O & Edge-Case Matrix

Each row runs for both workers (P60: `Import:InboxPath`; P89: `P89:SourcePath`).

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Frequency | `PollingInterval` = `00:00:07` | `Frequency == 7`, `Execute` set | N/A |
| Contained failure | inbox / source folder missing | `Execute()` does not throw; one `Error` row in `Logs` whose exception names the folder | contained by `Actions` |
| Deferred Fichier | one stable Fichier (mtime − 1 min) held open with `FileShare.None` | Fichier left in place; one `Warning` row in `Logs` naming it | retried next tick |
| Stop cancels | one stable, unlocked Fichier; `Stop()` awaited, then `Execute()` | `Stop()` returns < 5 s; Fichier left in place, no processing/done/error/xml output | N/A |

</frozen-after-approval>

## Code Map

MicroServices (SVN, user commits), root `C:\Users\Administrateur\Documents\MicroServices`:

- `GPAO/ImportP60/Client.cs:48-80` -- public ctor `: this(configuration, ReadConfig(configuration))`, `Configure` sets `Frequency`/`Execute = Actions`. `:276-340` `Actions`: missing inbox → `DirectoryNotFoundException` → `OnTickError` (`LogError`). `:346` `Stop` cancels `_cancellation`. Required keys: `ConnectionStrings:AscoLSI`, `Import:InboxPath`, `Import:XmlExportPath` (absolute, outside inbox), `Import:InitiatingServer`, `Import:Commande`.
- `GPAO/ConvertP89/Client.cs:41-65, 206-239, 243, 316` -- same shape; `LogOutcome` maps `Deferred` → `LogWarning`. Required keys: `ConnectionStrings:AscoLSI`, `AscoLsiJournal:InitiatingServer`, `P89:DonePath|ErrorPath|SourcePath|XmlPath` (distinct, absolute).
- `MicroService/Service/AbstractService.cs:62` -- the parameterless ctor calls `SharedLogger.GetDefault()`; `Logging/SharedLogger.cs:40` reads `MICROSERVICE_LOG_CONNECTION_STRING` at that moment (empty fallback in the test bin: no `microservice.settings.json`).
- `MicroService/Publish/Publisher.cs:25, 47-80, 132` -- `Frequency`, `Execute` public; `Publish` on a disconnected MQTT client is caught; `Stop` disposes the timer.
- `TextToXml/src/Kape22Importer/InboxScanner.cs:80-100` -- a locked Fichier fails `Move` → `Warning` "could not be moved"; token checked before each Fichier. `ImportOptions.cs:45` quiet period 10 s.
- `TextToXml/src/P89Converter/P89FolderConverter.cs:49-81, 164-183` -- token checked before each Fichier; locked Fichier → `IOException` → `Deferred`.
- `MQTTnetServices_Test.dbo.Logs` (local) -- columns `Id, Message, MessageTemplate, Level, TimeStamp, Exception, Properties`; the sink batches (~5 s), so assertions poll (bounded, 30 s).
- `TextToXml/tests/Kape22Importer.Tests/appsettings.Test.json` -- pattern for the test settings file (local `AscoLSI_Test`, `MQTTnetServices_Test`, trusted connection; no production entry).

This repo:
- `_bmad-output/implementation-artifacts/deferred-work.md:1312` -- mark RESOLVED by Story 6.5 (also closes the P60 `Client.Actions` debt it cites).

## Tasks & Acceptance

**Execution:**
- [x] `GPAO/Gpao.IntegrationTests/Gpao.IntegrationTests.csproj` -- new xUnit project (same package versions as `GpaoConvertP89.Tests` + `Xunit.SkippableFact`), references both worker projects, copies `appsettings.Test.json`; add to `MicroServices.sln` under the `GPAO` folder.
- [x] `GPAO/Gpao.IntegrationTests/appsettings.Test.json` -- `ConnectionStrings:AscoLSI` (`AscoLSI_Test`) and `ConnectionStrings:MQTTnetServices` (`MQTTnetServices_Test`).
- [x] `GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs` -- one class, one `[SkippableTheory]` per matrix row with `[InlineData("P60")]`/`[InlineData("P89")]`; a per-worker table builds the config (temp root per test, GUID-named Fichiers) and the real `Client` as a `Publisher`; a helper polls `Logs` by level and GUID. Run each test with its wiring mutated (e.g. `Execute = null`, `Frequency` constant, `Stop` not cancelling, `LogOutcome` dropping `Deferred`) to record the red, then revert.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- reconciliation per Code Map.

**Acceptance Criteria:**
- Given a reachable local SQL Server with `MQTTnetServices_Test`, when `dotnet test GPAO/Gpao.IntegrationTests` runs, then 8 tests pass and no `Logs` row lands outside `MQTTnetServices_Test`.
- Given SQL Server unreachable or the settings file absent, when the project runs, then all 8 are reported skipped with the reason, not failed.
- Given `MicroServices.sln`, when built with `-warnaserror` and all its test projects run, then 0 warnings and all green.

## Design Notes

The `Logs` sink of `MQTTnetServices_Test` is used rather than `AscoLSI_Test` (story note): `Logs` already lives there (`scripts/schema/02-mqtt-tables.sql`) and is the 6.4-bis E2E target, while `AutoCreateSqlTable` would otherwise add a foreign table to the database the TextToXml fixtures reset. The `Client`'s own `ConnectionStrings:AscoLSI` does target `AscoLSI_Test`; none of the four scenarios writes to it (a locked Fichier is deferred before any journal or business write).

`Execute` (= `Actions`) is invoked directly rather than through `Start()`, so the test controls the tick; this is the same delegate the Publisher timer calls.

## Verification

**Commands:**
- `dotnet build MicroServices.sln -warnaserror` -- expected: 0 warnings, 0 errors.
- `dotnet test GPAO/Gpao.IntegrationTests --blame-hang-timeout 2m` -- expected: 8 passed.
- `dotnet test MicroServices.sln --blame-hang-timeout 2m` -- expected: 0 failed.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: green (no change there).

**CC-1 red evidence (2026-10-01):** each mutation was applied to both `Client.cs` files, the suite run, then the originals restored (`svn diff` empty). Each failed exactly the two worker rows (P60, P89) of its scenario:
- `Frequency` hard-coded to 30 -> Frequency.
- `OnTickError` rethrows -> Contained failure.
- P60 `SerilogLoggerFactory` replaced by `NullLoggerFactory`; P89 `LogOutcome` `Deferred` case disabled -> Deferred Fichier.
- `_cancellation.Cancel()` removed from `Stop()` -> Stop cancels.

## Suggested Review Order

**Real Client construction**

- Entry point: the real public constructor, returned as `Publisher`; `IsConnected` forced so no broker is reached.
  [`GpaoClientIntegrationTests.cs:175`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L175)

- Logs sink routed to `MQTTnetServices_Test` before any Client exists, restored in `Dispose`.
  [`GpaoClientIntegrationTests.cs:31`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L31)

- Skip gate: settings, `_Test` names, both databases reachable; any failure becomes a skip (P-2).
  [`GpaoClientIntegrationTests.cs:219`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L219)

**The four AC-FR25-7 behaviors (P60 and P89 rows)**

- Locked Fichier deferred: stays in place, exactly one Warning row.
  [`GpaoClientIntegrationTests.cs:62`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L62)

- Missing folder contained in `Actions`, one Error row matched on the root GUID (P-3).
  [`GpaoClientIntegrationTests.cs:83`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L83)

- `Frequency` and `Execute` wired from `PollingInterval`.
  [`GpaoClientIntegrationTests.cs:102`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L102)

- `Stop()` cancels the token: the next tick processes nothing, within the budget.
  [`GpaoClientIntegrationTests.cs:118`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L118)

**Peripherals**

- Batched sink: poll, then wait one more batch so "exactly one" holds (P-1).
  [`GpaoClientIntegrationTests.cs:139`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L139)

- New test project, `Xunit.SkippableFact` aligned with TextToXml.
  [`Gpao.IntegrationTests.csproj:19`](../../../MicroServices/GPAO/Gpao.IntegrationTests/Gpao.IntegrationTests.csproj#L19)

- Ledger: F-1 of Story 5.2 resolved (P-5), D-1 deferred.
  [`deferred-work.md:1312`](deferred-work.md#L1312)
