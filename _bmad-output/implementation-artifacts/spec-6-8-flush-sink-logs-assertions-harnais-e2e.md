---
title: 'Story 6.8 — Flush the Logs sink and assert worker rows and exports in the E2E harness'
type: 'chore'
created: '2026-10-01'
status: 'done'
baseline_commit: '6c074df7d0a56f21cb67b3b0f7d3ab2321aadce6'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `scripts/e2e-worker-import.ps1` kills the Launcher with `Stop-Process -Force` while the shared batched Logs sink (`SharedLogger`, ~5 s period) still holds the worker's rows, so its Logs check only proves Broker/CopyDataToDb rows (`deferred-work.md` 6.4-bis D-1, F-2); it lists the FR-26 export folder without asserting anything (D-2); and `GpaoClientIntegrationTests` never proves its rows stay out of other databases (6.5 F-1). A worker that stops journaling or exporting still passes.

**Approach:** Stop the worker gracefully through the Launcher API (`POST /workers/GpaoImportP60/stop`), whose last act is the worker-logged `Worker stopped.` row; poll until that row lands — the sink is FIFO, so every earlier worker row is then flushed — and only then kill the Launcher. Assert `GpaoImportP60` rows, one `processed` row per seeded Fichier, and exactly one export per seeded Fichier. Add one MicroServices routing test. No production code changes.

## Boundaries & Constraints

**Always:** worker config restored in `finally`; the Logs target stays a `*_Test` database; every new assertion throws (non-zero exit) on failure; English comments (CC-2); alphabetical ordering where applicable (CC-4); no secret (CC-7); Integration runs with `--blame-hang-timeout 2m`.

**Ask First:** any change to the Launcher, `SharedLogger`, `AbstractService` or any other MicroServices/PortalSharedLibrary production code.

**Never:** production code changes in either repo; Docker/Testcontainers; queries against a production database.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| HAPPY_PATH | 2 seeded Fichiers, worker logs and exports normally | Worker stopped via API; `Worker stopped.` row lands; ≥1 `[GpaoImportP60 ` row and 1 `processed` row per Fichier after the baseline; exactly 1 `<Fichier>_<yyyyMMddHHmmss>.xml` per Fichier; exit 0 | N/A |
| WORKER_SILENT | Worker rows never reach `Logs` (e.g. sink not flushed) | Poll for `Worker stopped.` times out | Throws naming the Logs database; exit 1 |
| FICHIER_UNLOGGED | A seeded Fichier has no `processed` row | Script fails | Throws naming the Fichier |
| EXPORT_MISSING_OR_EXTRA | 0 or ≥2 exports for a Fichier, or a foreign file in the export folder | Script fails | Throws naming the Fichier and the count |
| STOP_REFUSED | `POST /workers/GpaoImportP60/stop` fails | Script fails | `Invoke-RestMethod` error propagates; `finally` still tears down |

</frozen-after-approval>

## Code Map

- `scripts/e2e-worker-import.ps1:143-147` -- Logs baseline `$logsIdBefore` (reuse); `$logsSqlcmd`, `ConvertFrom-SqlScalar` (lines 87-95) reuse for every new query.
- `scripts/e2e-worker-import.ps1:185-186` -- export listing with `-ErrorAction SilentlyContinue`, asserts nothing (D-2); replace by a per-Fichier count assertion.
- `scripts/e2e-worker-import.ps1:218-230` -- current Logs poll: any row `Id > baseline` (F-2); replace by stop + flush wait + source-filtered checks.
- `scripts/e2e-worker-import.ps1:233-236` -- `Stop-Process -Force` teardown; stays as is (worker rows already flushed).
- `MicroServices/Launcher/Program.cs:108-120` (read-only) -- `POST /workers/{name}/stop` → `WorkerAdapter.StopAsync`.
- `MicroServices/Launcher/Adapters/WorkerAdapter.cs:49-57` (read-only) -- `Stop()`, `Disconnect()`, then `_client.LogInformation(WorkerLifecycle, "Worker stopped.")` on the shared logger: the last worker row.
- `MicroServices/MicroService/Service/AbstractService.cs:215-219` (read-only) -- message format `"[" + Name,-20 + "][" + id,-20 + "] : " + message`; `Name` = `GpaoImportP60` (13 chars), so worker rows start with `[GpaoImportP60 ` (padding). Broker/CopyDataToDb rows never do.
- `MicroServices/MicroService/Logging/SharedLogger.cs` (read-only) -- one `Logger` per (connection string, table), never disposed; `GetDefaultConnectionString()` is the public routing resolver (env var `MICROSERVICE_LOG_CONNECTION_STRING` over `microservice.settings.json`).
- `src/Kape22Importer/InboxScanner.cs:351` (read-only) -- `"Fichier {Fichier} processed: {Outcome}."` via `SerilogLoggerFactory(Logger)`: rendered `Message` carries the Fichier name (no `[GpaoImportP60` prefix).
- `src/Kape22Importer/InboxScanner.cs:387-396` (read-only) -- export name `<name>_<yyyyMMddHHmmss>.xml`.
- `MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs` -- add the routing test; reuse `NewClient`, `NewWorkerFolders`, `WriteStableFichier`, `CountLogsAsync` (polls until landed = flush wait); `Probe.Value.Settings` gives `AscoLSI` + `MQTTnetServices` test strings. Test bin has no `microservice.settings.json` (only `.example`).
- `tests/Kape22Importer.Tests/GpaoImportP60WorkerEndToEndTests.cs` (unchanged) -- runs the script, asserts exit 0.
- Config keys: none added, renamed or removed.

## Tasks & Acceptance

**Execution:**
- [x] `scripts/e2e-worker-import.ps1` -- CC-1 red: first replace the step-5 export listing and Logs poll by the assertions only (export count per Fichier; `[GpaoImportP60 ` rows and per-Fichier `processed` rows after the baseline, bounded poll); run the E2E test and record the red (worker rows missing).
- [x] `scripts/e2e-worker-import.ps1` -- then add, before the Logs assertions, `POST http://127.0.0.1:5050/workers/GpaoImportP60/stop` and a bounded poll (30 s) for a `[GpaoImportP60 ` row containing `Worker stopped.` with `Id > $logsIdBefore`; rerun green. Update header/step comments.
- [x] `MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs` -- add `[SkippableTheory]` P60/P89 `Execute_FichierLocked_WritesLogsOnlyToTestDatabase_AcFr25_7` (`[Trait("AC","FR25-7")]`, `Category=Integration`): assert `SharedLogger.GetDefaultConnectionString()` equals the test `MQTTnetServices` string; run a locked-Fichier tick; wait for its GUID Warning in `MQTTnetServices_Test` (≥1); assert 0 rows carrying the GUID in `AscoLSI_Test.dbo.Logs` (0 when the table is absent). CC-1 red by mutation: route the env var to `AscoLSI_Test` temporarily, record the failure, revert.

**Acceptance Criteria:**
- Given the script at the end of a run, when it stops the Launcher, then the worker was first stopped through the API and its `Worker stopped.` row is in `MQTTnetServices_Test`, so the worker's batched rows are flushed before `Stop-Process`.
- Given step 5, when it checks `MQTTnetServices_Test`, then it requires ≥1 `GpaoImportP60` row after the baseline and exactly one exported XML per seeded Fichier.
- Given `GpaoClientIntegrationTests`, when the sink has flushed, then no `Logs` row of the test is written outside `MQTTnetServices_Test`.
- Given the full solution, when `dotnet test TextToXml.sln --filter Category=Integration -m:1` runs, then 0 failures.

### Review Findings

- [x] [Review][Decision] D-1 CC-1 red of the flush not shown by the planned step — resolved 2026-10-02, human option 1: the task-1 red (assertions without the worker stop) did **not** fail, since the 30 s bounded poll outlives the ~5 s sink batch period; the stop is a deterministic guarantee, not an observed fix. Red recorded instead by mutation runs of `GpaoImportP60WorkerEndToEndTests`, each failing with its named message then reverted: `POST /stop` line removed ("No 'GpaoImportP60 Worker stopped.' row ... within 30 s"), marker text, export regex ("Expected 1 exported XML ... found 0"), `processed` filter, stop URL (404), `archived` outcome. MicroServices test red by routing `MICROSERVICE_LOG_CONNECTION_STRING` to `AscoLSI_Test`.
- [x] [Review][Patch] P-1 "≥1 `[GpaoImportP60 ` row" was always met by the `Worker stopped.` marker; `WorkerLifecycle` rows now excluded [scripts/e2e-worker-import.ps1]
- [x] [Review][Patch] P-2 `processed` filter matched the retry-cap row, a `rejected` outcome and longer names; exact `Fichier "<name>" processed: "archived"` (quotes as `CHAR(34)`) [scripts/e2e-worker-import.ps1]
- [x] [Review][Patch] P-3 exactly one `processed` row per Fichier, per the matrix [scripts/e2e-worker-import.ps1]
- [x] [Review][Patch] P-4 `-TimeoutSec` on the stop call [scripts/e2e-worker-import.ps1]
- [x] [Review][Patch] P-5 extra export files named in the error [scripts/e2e-worker-import.ps1]
- [x] [Review][Patch] P-6 (found during patch verification, outside the triage) `MSBUILDDISABLENODEREUSE=1`: MSBuild nodes spawned by the script's builds inherited its redirected stdout and hung the E2E test until they idled out [scripts/e2e-worker-import.ps1]
- [x] [Review][Defer] W-1 standalone cleanup misses `L_D_CONSIGNES` and downstream tables — deferred, pre-existing

Post-commit review 2026-10-02 (`reviews/story-6-8/aggregated-report.md`, verdict REFUSÉ, D=0 P=2 F=0):

- [x] [Review][Patch] P-1 the tick-row check was met by the startup `Connecting`/`Starting` rows; now matches `Executing import tick.` — mutation red (tick log removed from `Client.cs`: "No GpaoImportP60 tick row ... after the flush marker"), reverted [scripts/e2e-worker-import.ps1:262]
- [x] [Review][Patch] P-2 every `Wait-LogsRow` call restarted a 30 s deadline, so a failure path could outlive `--blame-hang-timeout 2m`; only the `Worker stopped.` marker is polled, the later checks read once (`-Once`) — `processed` mutation red in 19 s with its named message [scripts/e2e-worker-import.ps1:104]

## Spec Change Log

## Design Notes

Why a worker stop, not a Launcher graceful stop: `SharedLogger` never disposes its `Logger`, so even a clean Launcher exit (`ApplicationStopping` → `StopFleetAsync`) drops the pending batch; a real flush would need a Launcher/`SharedLogger` change (Ask First, SVN). The worker stop yields an ordered marker instead: the batched sink emits events in order, so once `Worker stopped.` is in the table, every earlier worker row is too. The `Worker stopped.` row is written by the adapter on the worker's own logger with the worker's `Name`, so it alone would not prove the worker journals its work — hence the per-Fichier `processed` check.

## Verification

**Commands:**
- `dotnet test tests/Kape22Importer.Tests --filter "FullyQualifiedName~GpaoImportP60WorkerEndToEndTests" --blame-hang-timeout 2m` -- expected: red after task 1, green after task 2.
- `dotnet test C:/Users/Administrateur/Documents/MicroServices/GPAO/Gpao.IntegrationTests --blame-hang-timeout 2m` -- expected: all green (red under the mutation).
- `dotnet test TextToXml.sln --filter Category=Integration -m:1 --blame-hang-timeout 2m` -- expected: 0 failed.
- `dotnet build TextToXml.sln -warnaserror` + `dotnet test TextToXml.sln --filter Category=Unit` -- expected: green.

## Suggested Review Order

**Flush before teardown**

- Entry point: graceful worker stop through the Launcher API, bounded so a hung endpoint fails fast.
  [`e2e-worker-import.ps1:258`](../../scripts/e2e-worker-import.ps1#L258)

- Waits for the ordered `Worker stopped.` marker: once landed, every earlier worker row is flushed.
  [`e2e-worker-import.ps1:259`](../../scripts/e2e-worker-import.ps1#L259)

- Shared bounded poll over the test Logs table, reusing the 6.4-bis baseline and scalar parser.
  [`e2e-worker-import.ps1:104`](../../scripts/e2e-worker-import.ps1#L104)

**Worker journaling assertions**

- Tick rows only: `WorkerLifecycle` rows excluded, so the marker cannot satisfy it (P-1).
  [`e2e-worker-import.ps1:260`](../../scripts/e2e-worker-import.ps1#L260)

- Exact `processed: "archived"` text per Fichier, exactly once; quotes as `CHAR(34)` for sqlcmd (P-2, P-3).
  [`e2e-worker-import.ps1:267`](../../scripts/e2e-worker-import.ps1#L267)

**Export assertion**

- One `<Fichier>_<14 digits>.xml` per seeded Fichier, and no foreign file (P-5 names extras).
  [`e2e-worker-import.ps1:211`](../../scripts/e2e-worker-import.ps1#L211)

**Logs routing (MicroServices)**

- Resolver points at the test string, row lands there, none in `AscoLSI_Test`.
  [`GpaoClientIntegrationTests.cs:87`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L87)

- Second-database count, 0 when the sink never created the table.
  [`GpaoClientIntegrationTests.cs:167`](../../../MicroServices/GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs#L167)

**Peripherals**

- MSBuild node reuse disabled: spawned nodes held the test's stdout pipe and hung it (P-6).
  [`e2e-worker-import.ps1:44`](../../scripts/e2e-worker-import.ps1#L44)

- Teardown comment: on the happy path the worker is already stopped and flushed.
  [`e2e-worker-import.ps1:272`](../../scripts/e2e-worker-import.ps1#L272)
