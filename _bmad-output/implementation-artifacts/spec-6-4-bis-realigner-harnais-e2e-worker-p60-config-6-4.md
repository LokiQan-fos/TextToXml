---
title: 'Story 6.4-bis — Realign the P60 worker E2E harness with the 6.4 config'
type: 'bugfix'
created: '2026-09-29'
status: 'done'
baseline_commit: 'a8db07599cceba416d4ef9d958eeb3f690f3f128'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Story 6.4 removed `ConnectionStrings:MQTTnetServices` from `GpaoImportP60.json` (AC-FR25-4), but `scripts/e2e-worker-import.ps1` still assigns it on the patched worker config (line 106). PowerShell throws `Exception setting "MQTTnetServices"`, so `GpaoImportP60WorkerEndToEndTests.WorkerEndToEnd_ImportsArchivesAndMatchesProduction` fails and `Category=Integration` is no longer a trustworthy baseline for Story 6.5.

**Approach:** Stop writing the removed key into the worker config. Keep routing the worker's Logs sink to the test database through the `MICROSERVICE_LOG_CONNECTION_STRING` override (read by `SharedLogger`, which wins over `Logging:ConnectionString` of `microservice.settings.json`, a production `AFV004-LSI` string), sourced from the test settings' `ConnectionStrings:MQTTnetServices` — and refuse to start when that value is empty, since an empty/absent override silently falls back to production.

## Boundaries & Constraints

**Always:** the worker config is still restored in `finally`; the Logs sink of the Launcher run targets `MQTTnetServices_Test`, never production; English comments (CC-2); no secret added (CC-7).

**Ask First:** any change outside `scripts/e2e-worker-import.ps1` (the test settings files keep their `MQTTnetServices` key: `DoubleJournalIntegrationTests` reads it).

**Never:** production code changes; any edit in the `MicroServices` checkout; re-adding `MQTTnetServices` to the worker JSON; Docker/Testcontainers.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| HAPPY_PATH | Worker JSON without `MQTTnetServices`; test settings with `MQTTnetServices` = `MQTTnetServices_Test` | Script patches `AscoLSI`, `Import.InboxPath`, `Import.XmlExportPath` only; Launcher logs to `MQTTnetServices_Test`; exit 0 | N/A |
| LOG_TARGET_MISSING | Test settings `ConnectionStrings:MQTTnetServices` empty or absent | Script throws before touching the worker config or starting the Launcher | Message names `ConnectionStrings:MQTTnetServices` and the settings path |

</frozen-after-approval>

## Code Map

- `scripts/e2e-worker-import.ps1:63-68` -- reads test settings; `$mqttLogTest` (line 65) is the Logs-sink target; add the empty-value guard next to the existing `AscoLSI` one.
- `scripts/e2e-worker-import.ps1:106` -- the failing assignment on the patched worker config; delete.
- `scripts/e2e-worker-import.ps1:117-119` -- sets `MICROSERVICE_LOG_CONNECTION_STRING`; comment to state it is the only Logs-sink routing (worker JSON has no such key since 6.4).
- `MicroServices/MicroService/Logging/SharedLogger.cs:40-41` (read-only) -- `EnvironmentOverride.GetOrDefault("MICROSERVICE_LOG_CONNECTION_STRING", Logging:ConnectionString)`: env var null ⇒ production fallback; in pwsh assigning `''` to `$env:` removes the variable, hence the guard.
- `MicroServices/Launcher/microservice.settings.json` (read-only) -- `Logging:ConnectionString` = `AFV004-LSI` production.
- `MicroServices/GPAO/ImportP60/GpaoImportP60.json` (read-only) -- `ConnectionStrings` has `AscoLSI` only.
- `tests/Kape22Importer.Tests/GpaoImportP60WorkerEndToEndTests.cs` -- the red test; unchanged, runs the script and asserts exit code 0.
- `scripts/` -- no other harness references `MQTTnetServices` (grep done: only `schema/02-mqtt-tables.sql` comments); no P89 E2E script exists.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/GpaoImportP60WorkerEndToEndTests.cs` -- run it before the fix and record the red (`Exception setting "MQTTnetServices"`) -- CC-1 red step.
- [x] `scripts/e2e-worker-import.ps1` -- delete line 106; add a guard throwing when `$mqttLogTest` is blank, placed before step 0 (no side effect yet); update the step-3 comment -- AC-FR25-4 alignment + production-log safety.

**Acceptance Criteria:**
- Given worker JSONs without `ConnectionStrings:MQTTnetServices`, when the script patches the config, then it neither reads nor assigns that key on the worker config and does not fail (AC-FR25-4).
- Given the `AbstractService` Logs sink, when the worker runs under the script, then its logs go to `MQTTnetServices_Test` (verified by a `Logs` row of this run in `MQTTnetServices_Test`), never production.
- Given the full solution, when `dotnet test TextToXml.sln --filter Category=Integration -m:1` runs, then 0 failures.

## Design Notes

The only reachable Logs-sink selector for a Launcher-hosted worker is `SharedLogger`'s env-var-over-settings resolution; the worker JSON never carried it (6.4 finding). So the fix is a deletion plus a fail-fast guard, not a new config key.

## Verification

**Commands:**
- `dotnet test tests/Kape22Importer.Tests --filter "FullyQualifiedName~GpaoImportP60WorkerEndToEndTests" --blame-hang-timeout 2m` -- expected: red before, green after.
- `sqlcmd -S localhost -d MQTTnetServices_Test -C -Q "SELECT COUNT(*) FROM Logs WHERE TimeStamp >= '<run start>'"` -- expected: > 0.
- `dotnet test TextToXml.sln --filter Category=Integration -m:1 --blame-hang-timeout 2m` -- expected: 0 failed.
- `dotnet build TextToXml.sln -warnaserror` + `dotnet test TextToXml.sln --filter Category=Unit` -- expected: green (gates unaffected).

**Manual checks (if no CLI):**
- LOG_TARGET_MISSING (human decision at step-04 triage, 2026-09-29, option [b]: kept manual, no automated test; the E2E fixture skips when the key is blank). Blank `ConnectionStrings:MQTTnetServices` in `tests/Kape22Importer.Tests/appsettings.Test.json`, run `pwsh -File scripts/e2e-worker-import.ps1 -SkipProductionCompare`, restore the file. Expected: exit 1, message naming the key, no scratch inbox created, `GpaoImportP60.json` unchanged. Run 2026-09-29: as expected.

## Suggested Review Order

**Removed worker-config key**

- The failing assignment is gone; only `AscoLSI`, inbox and export path are patched.
  [`e2e-worker-import.ps1:111`](../../scripts/e2e-worker-import.ps1#L111)

**Logs sink stays on the test database**

- Fail fast: a blank override would silently fall back to the production Logs server.
  [`e2e-worker-import.ps1:70`](../../scripts/e2e-worker-import.ps1#L70)

- Baseline `Logs.Id` before the Launcher starts (P-1); `set_ConnectionString` avoids the dictionary-adapter trap.
  [`e2e-worker-import.ps1:122`](../../scripts/e2e-worker-import.ps1#L122)

- Polls for new `Logs` rows, since the sink flushes in batches; throws on none (P-1).
  [`e2e-worker-import.ps1:201`](../../scripts/e2e-worker-import.ps1#L201)

**Peripherals**

- Header prerequisites and comments realigned (P-2).
  [`e2e-worker-import.ps1:20`](../../scripts/e2e-worker-import.ps1#L20)

- `LAUNCHER_API_KEY` comment now states the variable is removed (P-2).
  [`e2e-worker-import.ps1:134`](../../scripts/e2e-worker-import.ps1#L134)
