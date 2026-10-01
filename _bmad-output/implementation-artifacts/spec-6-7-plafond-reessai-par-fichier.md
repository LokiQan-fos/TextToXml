---
title: 'Story 6.7 — Per-Fichier retry cap'
type: 'feature'
created: '2026-10-01'
status: 'done'
baseline_commit: '3e851b996bad50ce9098965c951d4c611d29a126'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A Fichier that fails the same way every tick (journal truncation, unreachable export folder, `AscoLSI` outage) stays in P60 `processing/` or comes back P89 `Deferred` forever, and writes new `Logs` / `L_D_LOG_COMMANDE` rows each tick (`deferred-work.md:1334` remainder, `:1338`, `:1360`).

**Approach:** Count consecutive retained attempts per Fichier in worker memory. On the `MaxAttempts`-th, log one `Error` with the Fichier name and the last cause, then skip that Fichier until restart. A Fichier no longer in its folder (P60 `processing/`, P89 source) has its count dropped. P60: `InboxScanner` gets a caller-owned counter, held by the `Client` across ticks. P89: the long-lived `P89FolderConverter` keeps the counter itself and emits a new `Frozen` outcome once.

## Boundaries & Constraints

**Always:** CC-1, CC-2, CC-4, CC-7. Tests are named `..._AcFr25_8` and carry `[Trait("AC", "FR25-8")]`. Keys are `Import:MaxAttempts` / `P89:MaxAttempts`, default 10. Each worker's `ReadConfig` refuses a value < 1 with an `InvalidOperationException` naming the full key (same shape as AC-FR25-9). The library treats ≤ 0 as "cap off". A frozen Fichier gets no processing, no journal call and no log. It is never moved to `error/` (AC-FR15-3 unchanged). Name matching is `OrdinalIgnoreCase`.

**Ask First:** a new public type, beyond the `Frozen` enum member and the two `MaxAttempts` properties; persisting the counter; changing an existing log level or message.

**Never:** quarantine to `error/`; a retry cap on a failed move from inbox into `processing/` (the Fichier is not in `processing/`); Launcher / `MicroService.Publisher` changes; worker READMEs.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Cap reached (P60) | Fichier left in `processing/` (persistence failure, read fault, filing failure) for N ticks | ticks 1..N-1: existing Warning only; tick N: Warning + one `Error` naming Fichier + last cause | none |
| Frozen (P60) | tick N+1.., Fichier still in `processing/` | processor not called, no log | none |
| Success before cap (P60) | fails N-1 times, then archived / rejected | no `Error`; count dropped | N/A |
| Left `processing/` externally (P60) | frozen Fichier removed by operator, same name later dropped in inbox | count dropped at next `processing/` listing; processed normally | N/A |
| Cap reached (P89) | `Deferred` N ticks | ticks 1..N-1 `Deferred` (Warning); tick N one `Frozen` outcome → `Error` with reasons | none |
| Frozen (P89) | tick N+1.., Fichier still in source | no outcome, no journal call | none |
| Name reused (P89) | frozen Fichier leaves source, index rotation brings the name back | count dropped when the name is absent from a listing | N/A |
| No cap | library `MaxAttempts` ≤ 0, or no counter passed (P60) | today's behavior | N/A |
| Config | `MaxAttempts` absent / `1` / `0` / `-1` | 10 / accepted / refused / refused, naming the key | startup |

</frozen-after-approval>

## Code Map

This repo:

- `src/Kape22Importer/InboxScanner.cs:22-28` -- primary ctor gains a trailing optional `Dictionary<string, int>? attempts = null`. Existing call sites (~51) still compile. `:63-76` after listing `processing/`: drop the keys absent from the listing, then skip frozen entries (count ≥ `options.MaxAttempts` > 0). `:188-283` `ProcessFromProcessing` → returns `string?` retained cause (read fault message, PersistenceError message(s), filing failure message), `null` when the Fichier left `processing/`. The retain/Error logic sits in one private helper, called from both loops. An inbox Fichier moved in and retained counts 1.
- `src/Kape22Importer/ImportOptions.cs` -- `MaxAttempts` (int, default 10), alphabetical slot.
- `src/P89Converter/P89Options.cs` -- `MaxAttempts` (default 10).
- `src/P89Converter/P89FolderConverter.cs:49-86` -- private `Dictionary<string,int>` field. Prune keys absent from `files`. Skip frozen before `Process`. Count a `Deferred` result, and turn the N-th into `Status = Frozen` (same reasons). Any other status drops the key. `:239-249` add `Frozen` (alphabetical: Converted, Deferred, Frozen, Rejected).
- `tests/Kape22Importer.Tests/InboxScannerTests.cs` + `InboxScannerTestSupport.cs` (fakes `InMemoryFileSource`, `FakeFichierProcessor`, `RecordingLogger`) -- P60 rows.
- `tests/P89Converter.Tests/P89FolderConverterTests.cs` + `TestSupport.cs` -- P89 rows (failing journal fake).
- `deferred-work.md:1334, :1338, :1360` -- mark RESOLVED by Story 6.7.

MicroServices (SVN, user commits):

- `GPAO/ImportP60/Client.cs:100-163` `ReadConfig`: refuse `Import:MaxAttempts` < 1. Add a field `_attempts = new(StringComparer.OrdinalIgnoreCase)`, pass it through `RunTickCore` (`:417-428`, new parameter) into `InboxScanner`. Ticks never overlap (Story 6.4), so no lock.
- `GPAO/ConvertP89/Client.cs:83-139` `ReadConfig`: refuse `P89:MaxAttempts` < 1. `:306-328` `LogOutcome`: `Frozen` → `logError(ErrorExtractingFile, new InvalidDataException(reasons), "<name> left in the source folder after <n> attempts; no longer processed until the worker restarts.")`. `n` = the configured value, passed via the outcome reasons or a parameter (implementer's choice, no new public type).
- `GPAO/ImportP60.Tests/{ClientConfigurationTests,RunTickCoreTests}.cs`, `GPAO/ConvertP89.Tests/{ClientConfigurationTests,LogOutcomeTests}.cs` -- config rows, `RunTickCore` carries the counter across two calls, `Frozen` → one Error.
- `GPAO/ImportP60/GpaoImportP60.json`, `GPAO/ConvertP89/GpaoConvertP89.json` -- add `"MaxAttempts": 10` (documents the key).
- Key grep (A-3): `MaxAttempts` exists only as `WorkerService` private consts (startup retry, unrelated). The keys are new and nothing is renamed.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/InboxScannerTests.cs` + `src/Kape22Importer/{ImportOptions,InboxScanner}.cs` -- red then green on every P60 matrix row, `MaxAttempts` = 3 in tests.
- [x] `tests/P89Converter.Tests/P89FolderConverterTests.cs` + `src/P89Converter/{P89Options,P89FolderConverter}.cs` -- same for the P89 rows.
- [x] `GPAO/ImportP60.Tests` + `GPAO/ImportP60/Client.cs` -- config rows; `RunTickCore` keeps the counter across ticks (frozen at tick N+1).
- [x] `GPAO/ConvertP89.Tests` + `GPAO/ConvertP89/Client.cs` -- config rows; `LogOutcome(Frozen)` → one Error naming Fichier and reasons.
- [x] Both worker JSON files -- add `MaxAttempts`.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- reconcile per Code Map.

**Acceptance Criteria:**
- Given each matrix row, when its test runs, then it passes with the `FR25-8` trait.
- Given `TextToXml.sln`, when built `-warnaserror` and `Category=Unit` runs, then 0 warnings, all green, AC gates included.
- Given `MicroServices.sln`, when built `-warnaserror` and tested with `--blame-hang-timeout 2m`, then 0 warnings, 0 failed.

## Design Notes

**Why a caller-owned dictionary for P60:** `RunTickCore` rebuilds the scanner every tick, so the state must outlive it. A plain `Dictionary<string,int>` owned by the `Client` is the smallest seam: no new type, and today's callers keep the uncapped behavior. P89 needs no seam, because `P89FolderConverter` is built once in `Client.Configure` and already lives as long as the worker.

**"Consecutive":** a count goes up only on an attempt that leaves the Fichier in place. A tick that is cancelled before reaching it does not reset the count. Leaving the folder resets it. A successful attempt also resets it, since the Fichier has then left the folder.

**AC-FR12-6 / AC-FR15-4:** a stranded Fichier is still resumed after a restart, because the counter is in memory and starts empty.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: green.
- `dotnet build MicroServices.sln -warnaserror` (from `C:\Users\Administrateur\Documents\MicroServices`) -- expected: 0 warnings.
- `dotnet test MicroServices.sln --blame-hang-timeout 2m` -- expected: 0 failed.

## Suggested Review Order

**Retry cap, P60 library**

- Entry point: caller-owned counter; the scanner is rebuilt per tick, the worker keeps state.
  [`InboxScanner.cs:32`](../../src/Kape22Importer/InboxScanner.cs#L32)

- Counts dropped for names no longer in processing/ (filed or removed).
  [`InboxScanner.cs:77`](../../src/Kape22Importer/InboxScanner.cs#L77)

- A newly moved-in Fichier starts afresh even when it overwrote a frozen one (review P-1).
  [`InboxScanner.cs:130`](../../src/Kape22Importer/InboxScanner.cs#L130)

- Count, freeze and the single Error with the last cause.
  [`InboxScanner.cs:138`](../../src/Kape22Importer/InboxScanner.cs#L138)
  [`InboxScanner.cs:147`](../../src/Kape22Importer/InboxScanner.cs#L147)

- Each retain path now returns its cause; null means filed.
  [`InboxScanner.cs:251`](../../src/Kape22Importer/InboxScanner.cs#L251)

**Retry cap, P89 library**

- Prune, then skip frozen before Process, keyed on the listed name.
  [`P89FolderConverter.cs:74`](../../src/P89Converter/P89FolderConverter.cs#L74)
  [`P89FolderConverter.cs:97`](../../src/P89Converter/P89FolderConverter.cs#L97)

- N-th Deferred becomes Frozen once, same reasons.
  [`P89FolderConverter.cs:113`](../../src/P89Converter/P89FolderConverter.cs#L113)

**Workers (MicroServices, SVN)**

- P60 counter lives in the Client; required RunTickCore parameter enforces the wiring (P-4).
  [`ImportP60/Client.cs:54`](../../../MicroServices/GPAO/ImportP60/Client.cs#L54)
  [`ImportP60/Client.cs:441`](../../../MicroServices/GPAO/ImportP60/Client.cs#L441)

- Startup refusal below 1, both workers.
  [`ImportP60/Client.cs:133`](../../../MicroServices/GPAO/ImportP60/Client.cs#L133)
  [`ConvertP89/Client.cs:100`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L100)

- Frozen outcome logged as one Error, reasons in the exception.
  [`ConvertP89/Client.cs:332`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L332)

**Tests and peripherals**

- P60 matrix rows, including overwrite and MaxAttempts = 1.
  [`InboxScannerTests.cs:719`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L719)
  [`InboxScannerTests.cs:839`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L839)

- P89 rows and boundary.
  [`P89FolderConverterTests.cs:528`](../../tests/P89Converter.Tests/P89FolderConverterTests.cs#L528)

- Worker counter across ticks, Frozen log, config rows.
  [`RunTickCoreTests.cs:208`](../../../MicroServices/GPAO/ImportP60.Tests/RunTickCoreTests.cs#L208)
  [`LogOutcomeTests.cs:58`](../../../MicroServices/GPAO/ConvertP89.Tests/LogOutcomeTests.cs#L58)
  [`ImportP60.Tests/ClientConfigurationTests.cs:360`](../../../MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs#L360)

- Options defaults (10, ≤ 0 = cap off at library level).
  [`ImportOptions.cs:36`](../../src/Kape22Importer/ImportOptions.cs#L36)
  [`P89Options.cs:21`](../../src/P89Converter/P89Options.cs#L21)
