---
title: 'Story 6.4 — Shared hardening of the GPAO workers'
type: 'bugfix'
created: '2026-09-29'
status: 'done'
baseline_commit: '02391c6b1dbe165b2c80659a43b8e275b8230da8'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Publisher` stacks ticks; `GpaoImportP60.Actions` lets a broker-connect exception escape (the timer then stops the worker); an orphaned tick past the 4 s budget logs and publishes through a disposed `Client`; a failed tick still publishes "alive"; a malformed `ConnectionStrings:AscoLSI` passes startup and a refusal runs after `AbstractService` has built its SQL sink; the standalone `WorkerService` retries configuration errors 10 times and never disposes the `Client`.

**Approach:** Fix re-entrancy once in `MicroService.Publisher`, and each `Client` defect identically in `GpaoImportP60` and `GpaoConvertP89` (FR-25, D32, AC-FR25-1..6). Human decision 2026-09-29: `ConnectionStrings:MQTTnetServices` is dead (nothing reads it; the Logs sink uses `Logging:ConnectionString` / `MICROSERVICE_LOG_CONNECTION_STRING` via `SharedLogger`), so it is removed from both worker JSONs and AC-FR25-4 is reconciled to name `AscoLSI` only.

## Boundaries & Constraints

**Always:** CC-1, CC-2, CC-4, CC-7. Tests `_AcFr25_n` + `[Trait("AC", "FR25-n")]`. A "failed tick" = one where `onError` fired (scan, purge, pipeline construction); a rejected/deferred Fichier is not a failure. Configuration is validated before the `AbstractService` base constructor runs (`: this(ReadConfig(configuration))`). A skipped tick is dropped, not queued. Laminoir, Video, Zumbach build and their tests pass unchanged.

**Ask First:** Any change to `src/` of this git repo (`Kape22Importer`, `P89Converter`, `TextToXml`); any change to `AbstractService`, `SharedLogger`, `WorkerAdapter` or other workers' `Client`s; stopping a Publisher worker on a skipped tick.

**Never:** The five deferred items marked "PLANNED Story 6.4" outside FR-25 (retry cap, stability-gate trace, export `.tmp`, export-folder existence, `Import:Commande` length) — decision 2026-09-29, reclassified unplanned. Validating the Logs sink connection string. Blocking `Dispose` on the in-flight tick.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Overlapping tick | timer fires while `Execute` still runs | second callback returns without invoking `Execute` | N/A |
| Tick ends | previous `Execute` completed or threw | next callback runs `Execute` | existing catch → `Stop()` |
| Connect throws | `Connect()` throws in `Actions` | `Actions` returns, error logged | `OnTickError` |
| Failed tick | inbox / source folder missing | error logged, `Publish` not called | `OnTickError` |
| Clean tick | empty folder | `Publish` called once | N/A |
| After `Dispose` | tick completes / `Actions` runs after `Dispose()` | no `Log*`, no `Publish`, no `Connect` | silent |
| AscoLSI absent / blank / malformed | e.g. `"garbage"`, `"Foo=1"` | `ReadConfig` throws naming `ConnectionStrings:AscoLSI`; base ctor never runs | startup refused |
| Standalone config error | `Client` factory throws `InvalidOperationException` | one attempt, message, `StopApplication()` | no retry |
| Standalone transient error | factory throws anything else | existing 10 × 3 s retry; a failed attempt's `Client` is disposed | unchanged |
| Standalone stop | `StopAsync` with a client | `Stop`, `Disconnect`, `Dispose` | N/A |

</frozen-after-approval>

## Code Map

MicroServices (SVN, user commits), root `C:\Users\Administrateur\Documents\MicroServices`:

- `MicroService/Publish/Publisher.cs:85-97` -- timer callback: extract to `internal async Task RunTickAsync()` guarded by `Interlocked.Exchange` on an `int` flag, reset in `finally`.
- `MicroService/MicroService.csproj:11` -- add `InternalsVisibleTo` `GpaoImportP60`, `GpaoConvertP89` so each `Client` can chain to `Publisher`'s internal `(IMqttClient, MqttClientOptions, Logger)` ctor (`Publisher.cs:34`).
- `MicroService.Tests/PublisherLifecycleTests.cs` -- AC-FR25-1 tests; reuse `CreatePublisher` / `Fakes/FakeMqttClient`.
- `GPAO/ImportP60/Client.cs:42-60, 67-74, 228-289, 331` -- public ctor `: this(ReadConfig(configuration))`; internal test ctor taking the three fakes; `SqlConnectionStringBuilder` check on AscoLSI; `Connect` inside the `try` (as P89 `:158-165`); `volatile bool _disposed` set in `Dispose`, checked at `Actions` entry, before `Publish`, and in `OnTickError`; `RunTickCore` returns `bool` (false when `onError` fired), `Publish` only on true.
- `GPAO/ConvertP89/Client.cs:33-42, 56-63, 156-247` -- same changes; `LogOutcome` instance forwarder `:244` also checks `_disposed`.
- `GPAO/*/WorkerService.cs` -- `internal` factory seam (`Func<Client>`, default `new Client(configuration)`); `InvalidOperationException` from it → message + `lifetime.StopApplication()`, no retry; dispose a failed attempt's client; `StopAsync` adds `Dispose()`.
- `GPAO/*/Gpao*.json` -- drop `"MQTTnetServices"`. `ImportP60.json` already has an uncommitted SVN change (Story 6.3).
- `GPAO/ImportP60.Tests`, `GPAO/ConvertP89.Tests` -- `ClientConfigurationTests.cs` (AC-FR25-4), existing `RunTickCoreTests.cs` (adapt to `bool`), new `ClientRobustnessTests.cs` (spy `Client` subclass overriding virtual `Connect`/`Publish`/`Log*`, real `Actions`, `IsConnected` set directly), new `WorkerServiceTests.cs`.

This repo:
- `_bmad-output/planning-artifacts/PRD.md:1141-1143`, `epics.md` (Epic 6 AC list if restated) -- AC-FR25-4 text: `ConnectionStrings:AscoLSI` absent or malformed; decision note.
- `_bmad-output/implementation-artifacts/deferred-work.md:1252,1304,1308,1316,1320,1324,1328` -- mark RESOLVED by 6.4; `:1334,1338,1354,1360,1364,1368` -- "UNPLANNED 2026-09-29 (Story 6.4 scope decision)".
- `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs` -- read-only: FR-25 is not registered (all MicroServices), no change.

## Tasks & Acceptance

**Execution:**
- [x] `MicroService.Tests/PublisherLifecycleTests.cs` -- AC-FR25-1 red; then `Publisher.cs` guard -- green.
- [x] `GPAO/ImportP60.Tests` -- AC-FR25-2/3/4/5/6 tests red; then `MicroService.csproj`, `Client.cs`, `WorkerService.cs`, JSON -- green.
- [x] `GPAO/ConvertP89.Tests` -- same tests red; then `Client.cs`, `WorkerService.cs`, JSON -- green.
- [x] `PRD.md`, `epics.md`, `deferred-work.md` -- reconciliation per Code Map.

**Acceptance Criteria:**
- Given `MicroServices.sln`, when built with `-warnaserror` and all its test projects run, then 0 warnings and all green (Laminoir, Zumbach included).
- Given `TextToXml.sln` Unit suite, when run, then green (no source change there).

## Spec Change Log

- 2026-09-29 — Code review D-1 (option 1, human-approved): the frozen matrix row "Standalone config error — `Client` factory throws `InvalidOperationException` → one attempt, no retry" and the matching Code Map line are superseded. Amended intent: `WorkerService` validates the configuration once through `Client.ReadConfig` before the retry loop, and any exception there stops the host without retry. Inside the loop every factory exception is retried, `InvalidOperationException` included, because the `AbstractService` SQL log sink can raise it transiently at cold start. The frozen text is left verbatim; this note supersedes it. KEEP: the `ReadConfig` pre-check with its `catch (Exception)`.
- 2026-09-29 — Code review D-2 (option 1, human-approved): the frozen matrix row "Standalone transient error … unchanged" is amended. Once every attempt has failed, `WorkerService` stops the host rather than leaving it idle with no worker. The frozen text is left verbatim; this note supersedes it. KEEP: the `StopApplication()` after the last failed attempt.

## Design Notes

AC-FR25-3 scope: "access to the `Client`" = its own members (`Log*`, `Publish`, `PayLoad`, `Connect`). The library `ILogger` of an orphaned P60 tick writes to the shared `SharedLogger` instance, which `AbstractService.Dispose` deliberately never disposes; stopping those would need blocking `Dispose` (Never).

AC-FR25-4 "no SQL sink": evaluating `ReadConfig` in the `this(...)` argument runs before `AbstractService()`, so `SharedLogger.GetDefault()` is never reached on refusal.

## Verification

**Commands:**
- `dotnet build <MicroServices>/MicroServices.sln -warnaserror` -- expected: 0 warnings, 0 errors.
- `dotnet test <MicroServices>/MicroService.Tests`, `GPAO/ImportP60.Tests`, `GPAO/ConvertP89.Tests`, `Laminoir/OrdresFabricationSync.Tests`, `Zumbach/CopyDataToDb.Tests` -- expected: all green.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all green.

## Suggested Review Order

**Tick re-entrancy (once, in Publisher)**

- Entry point: every timer tick goes through one Interlocked guard; overlaps are dropped.
  [`Publisher.cs:98`](../../../MicroServices/MicroService/Publish/Publisher.cs#L98)

- A throwing `Stop()` in the catch no longer escapes the async void callback (review P-2).
  [`Publisher.cs:117`](../../../MicroServices/MicroService/Publish/Publisher.cs#L117)

**Tick containment and heartbeat (identical in both workers)**

- Connect, token read and tick all inside the try; publish only after a clean tick.
  [`ImportP60/Client.cs:276`](../../../MicroServices/GPAO/ImportP60/Client.cs#L276)

- Missing inbox is a failed tick for P60, as for P89 (review D-1).
  [`ImportP60/Client.cs:303`](../../../MicroServices/GPAO/ImportP60/Client.cs#L303)

- `RunTickCore` reports whether `onError` fired; drives the heartbeat decision.
  [`ImportP60/Client.cs:371`](../../../MicroServices/GPAO/ImportP60/Client.cs#L371)

- P89 twin of the same body.
  [`ConvertP89/Client.cs:206`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L206)

**Disposal (AC-FR25-3)**

- Dispose flags, cancels, then frees; residual check-then-act window documented.
  [`ImportP60/Client.cs:357`](../../../MicroServices/GPAO/ImportP60/Client.cs#L357)

- Outcome forwarder stays silent once disposed.
  [`ConvertP89/Client.cs:316`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L316)

**Startup configuration (AC-FR25-4, AC-FR25-6)**

- `ReadConfig` runs before `AbstractService()`, so a refusal builds no SQL sink.
  [`ImportP60/Client.cs:48`](../../../MicroServices/GPAO/ImportP60/Client.cs#L48)

- AscoLSI absent, blank or unreadable is refused naming the key.
  [`ImportP60/Client.cs:127`](../../../MicroServices/GPAO/ImportP60/Client.cs#L127)

- Standalone host: config error stops without retry; transient errors keep retrying.
  [`ImportP60/WorkerService.cs:46`](../../../MicroServices/GPAO/ImportP60/WorkerService.cs#L46)

- Stop always disposes the Client, even when Stop/Disconnect throws.
  [`ImportP60/WorkerService.cs:85`](../../../MicroServices/GPAO/ImportP60/WorkerService.cs#L85)

**Tests and peripherals**

- AC-FR25-1 through the real timer, not only `RunTickAsync` (review P-11).
  [`PublisherLifecycleTests.cs:114`](../../../MicroServices/MicroService.Tests/PublisherLifecycleTests.cs#L114)

- Spy Client drives the real `Actions`: connect, failed tick, dispose mid-tick.
  [`ImportP60.Tests/ClientRobustnessTests.cs:29`](../../../MicroServices/GPAO/ImportP60.Tests/ClientRobustnessTests.cs#L29)

- Scan-only failure returns false, also under a cancelled token (review P-10).
  [`ImportP60.Tests/RunTickCoreTests.cs:184`](../../../MicroServices/GPAO/ImportP60.Tests/RunTickCoreTests.cs#L184)

- No-sink proof compares the SharedLogger cache count (review P-8).
  [`ImportP60.Tests/ClientConfigurationTests.cs:211`](../../../MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs#L211)

- Standalone host lifecycle tests.
  [`ImportP60.Tests/WorkerServiceTests.cs:25`](../../../MicroServices/GPAO/ImportP60.Tests/WorkerServiceTests.cs#L25)

- AC-FR25-4 reconciled: AscoLSI only, dead MQTTnetServices key removed.
  [`PRD.md:1141`](../planning-artifacts/PRD.md#L1141)

- Review defer: RetentionDays range check.
  [`deferred-work.md:1372`](deferred-work.md#L1372)
