---
title: 'Story 6.6 — GPAO worker config validation and share outage'
type: 'feature'
created: '2026-10-01'
status: 'done'
baseline_commit: '260dc42dcf8ddc9ec45b2546d971775fe417621f'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Both GPAO workers start on configurations that make every tick fail or loop: no database in `ConnectionStrings:AscoLSI`, a negative or huge stability quiet period, and on P60 an `Import:Commande` over 50 chars or an out-of-range `Import:RetentionDays`. And a P60 inbox share fault is a Warning that still publishes the heartbeat. The P60 inbox existence check also runs outside the tick task, so the 4 s stop budget does not cover it.

**Approach:** Extend each worker's `Client.ReadConfig` with the AC-FR25-9 rules, the same way in both workers (D32). `InboxScanner.RunTick` returns `false` when a reception folder could not be listed, and `RunTickCore` turns that into a failed tick. The P60 `Directory.Exists` check moves inside the `Task.Run` (AC-FR25-10).

## Boundaries & Constraints

**Always:** CC-1 (red before green; the move inside `Task.Run` is structural, shown red by a mutation then reverted), CC-2, CC-4, CC-7. Tests named `..._AcFr25_9` / `..._AcFr25_10` with `[Trait("AC", "FR25-9")]` / `"FR25-10"`. Every refusal is an `InvalidOperationException` naming the full key. Bounds are inclusive: quiet period `00:00:00`..`01:00:00`, `RetentionDays` 1..3650, `Commande` length ≤ 50. The log levels stay as they are today. A missing inbox or source is an `Error` through `OnTickError` (Story 6.4). A listing fault is the `InboxScanner` `Warning` (AC-FR15-2). Neither one publishes a heartbeat.

**Ask First:** a new config key; any change to `P89FolderConverter` or to `DirectoryFileSource`; changing a log level; making a failed move into `processing/` a failed tick.

**Never:** a "folder must exist" startup rule (user decision, AC-FR25-9); retry cap (Story 6.7); Launcher / `MicroService.Publisher` changes; creating worker READMEs (none exist, no key is added).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| No database | `Server=x;Trusted_Connection=True;` (both) | refused, names `ConnectionStrings:AscoLSI` | startup |
| `Database=` synonym | `Server=x;Database=Y;` (both) | accepted | N/A |
| Quiet period out of range | `-00:00:01` or `01:00:01` (both) | refused, names `Import:` / `P89:StabilityQuietPeriod` | startup |
| Quiet period at the limits | `00:00:00`, `01:00:00`, absent (10 s default) | accepted | N/A |
| Commande too long | 51 chars (P60) | refused, names `Import:Commande` | startup |
| Commande at the limit or absent | 50 chars, or absent (`P60` default) | accepted | N/A |
| RetentionDays out of range | absent, `0`, `3651` (P60) | refused, names `Import:RetentionDays` | startup |
| Inbox listing fault | `IFileSource.List` throws `IOException` (P60) | one `Warning` (unchanged); `RunTick` → `false`; `RunTickCore` → `false`, `onError` not called; no heartbeat | next tick retries |
| Missing inbox | `Import:InboxPath` absent on disk (P60) | `Error` "Import tick failed" (unchanged); no heartbeat; check runs inside the tick task | next tick |
| Missing / unreachable source | `P89:SourcePath` absent (P89) | `Error` (unchanged), `RunTickCore` → `false`, no heartbeat; already inside `Task.Run` | next tick |

</frozen-after-approval>

## Code Map

MicroServices (SVN, user commits), root `C:\Users\Administrateur\Documents\MicroServices`:

- `GPAO/ImportP60/Client.cs:87-123` -- `ReadConfig`: add the quiet-period, `Commande`, and `RetentionDays` checks. `:127-155` `ValidAscoLsiConnectionString`: add `builder.InitialCatalog` blank → refuse (`Database=` maps to it). `:300-328`: move the `Directory.Exists` throw into the `Task.Run` lambda before `RunTickCore`. `:371-410` `RunTickCore`: `succeeded = scanner.RunTick(token)` without `onError` (Warning already logged); the purge still runs.
- `GPAO/ConvertP89/Client.cs:78-158` -- same `InitialCatalog` check and `P89:StabilityQuietPeriod` range. `:267-287` `RunTickCore` already returns `false` on a missing source: AC-FR25-10 is pinned by a test here, with no code change.
- `GPAO/ImportP60.Tests/ClientConfigurationTests.cs` -- the configs that must pass (`:37, :51, :77, :95, :127, :151, :170`) lack `Import:RetentionDays`: add `"30"`. `:121-132`: `RunTickCore_WhenTheFileSourceThrows_...` in `RunTickCoreTests.cs` must also assert `false`.
- `GPAO/ConvertP89.Tests/ClientConfigurationTests.cs:208` -- `ValidValues()` helper, reuse it.
- `GPAO/ImportP60/GpaoImportP60.json` already has `Database=` + `RetentionDays: 30`. `GPAO/ConvertP89/GpaoConvertP89.json` has empty values (filled per deployment). `Gpao.IntegrationTests` uses `Database=` + `RetentionDays: 30`. Nothing to change.

This repo:

- `src/Kape22Importer/InboxScanner.cs:58-104` -- `RunTick` `void` → `bool`: `false` when `TryList` fails (processing/ or inbox), `true` otherwise. A cancelled tick and a failed move into `processing/` stay `true`. Callers that ignore the result still compile (51 call sites).
- `src/Kape22Importer/Persistence/Kape22Persister.cs:52-54, 269` -- `Import:Commande` read with a `P60` default (read-only reference).
- `tests/Kape22Importer.Tests/InboxScannerTests.cs` -- add the `RunTick` result tests (`InboxScannerTestSupport.cs` fakes).
- `deferred-work.md:1334, :1352, :1374, :1382, :1390, :1394` -- mark the 6.6 parts RESOLVED. `:1334` keeps its 6.7 remainder.
- Key grep (A-3 rule): keys touched, none renamed or removed: `ConnectionStrings:AscoLSI`, `Import:Commande`, `Import:RetentionDays`, `Import:StabilityQuietPeriod`, `P89:StabilityQuietPeriod`. Every configuration that `ReadConfig` reads (the 2 JSON files, the test configs above, `scripts/e2e-worker-import.ps1` via `GpaoImportP60.json`) already names a database and `RetentionDays: 30`.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/InboxScannerTests.cs` + `src/Kape22Importer/InboxScanner.cs` -- red then green: `RunTick` returns `false` on a listing fault of the inbox or `processing/`, `true` on a clean tick, an empty inbox, a cancel, or a failed move.
- [x] `GPAO/ImportP60.Tests/ClientConfigurationTests.cs` + `GPAO/ImportP60/Client.cs` -- one test per refusal/acceptance row (P60), then the `ReadConfig` rules; add `RetentionDays` to the passing configs.
- [x] `GPAO/ConvertP89.Tests/ClientConfigurationTests.cs` + `GPAO/ConvertP89/Client.cs` -- same rules for the P89 rows.
- [x] `GPAO/ImportP60.Tests/RunTickCoreTests.cs` + `GPAO/ImportP60/Client.cs` -- listing fault → `RunTickCore` `false`, `onError` not called. Move the existence check into the task, and show it red by mutation.
- [x] `GPAO/ConvertP89.Tests/RunTickCoreTests.cs` -- `..._AcFr25_10` for a missing source folder: `false`, one `onError` (characterization, mutation red).
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- reconcile per Code Map.

**Acceptance Criteria:**
- Given each matrix row, when the matching test runs, then it passes and carries its AC trait.
- Given `TextToXml.sln`, when built with `-warnaserror` and `Category=Unit` runs, then 0 warnings and all green.
- Given `MicroServices.sln`, when built with `-warnaserror` and its test projects run (`--blame-hang-timeout 2m`), then 0 warnings and 0 failed, `Gpao.IntegrationTests` included.

## Design Notes

**Signal shape (decided at this checkpoint):** a `bool` from `InboxScanner.RunTick` is the smallest seam. It matches the existing `RunTickCore` `bool`, and Story 6.7 can replace it with a per-Fichier outcome. A failed move into `processing/` stays a clean tick, because a single locked Fichier (SAP still writing it) is not an unreachable folder.

**"Log Warning unchanged":** this is read as "no log level changes". The existing listing `Warning` stays a `Warning`. The `Error` that Story 6.4 gives a missing folder stays an `Error`. The 6.5 integration test ("Contained failure" → one `Error` row) is untouched.

**`RetentionDays` vs AC-FR12-9:** the library still treats ≤ 0 as "purge off". Only `GpaoImportP60` refuses it at startup, so the worker can no longer turn the purge off. That is the AC-FR25-9 product decision.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: green.
- `dotnet build MicroServices.sln -warnaserror` (from `C:\Users\Administrateur\Documents\MicroServices`) -- expected: 0 warnings.
- `dotnet test MicroServices.sln --blame-hang-timeout 2m` -- expected: 0 failed.

## Suggested Review Order

**Share outage = failed tick (AC-FR25-10)**

- Entry point: the new `bool` signal, false only when a reception folder cannot be listed.
  [`InboxScanner.cs:61`](../../src/Kape22Importer/InboxScanner.cs#L61)

- P60 tick core turns the scanner's `false` into a failed tick, without a second log.
  [`ImportP60/Client.cs:432`](../../../MicroServices/GPAO/ImportP60/Client.cs#L432)

- Inbox existence check moved inside `Task.Run`, so it is covered by the Stop() budget.
  [`ImportP60/Client.cs:352`](../../../MicroServices/GPAO/ImportP60/Client.cs#L352)

**Startup refusals (AC-FR25-9)**

- P60 rules: quiet period range, Commande ≤ 50, RetentionDays absent / out of 1..3650.
  [`ImportP60/Client.cs:118`](../../../MicroServices/GPAO/ImportP60/Client.cs#L118)

- Database required (`Initial Catalog` / `Database`), identical in both workers (D32).
  [`ImportP60/Client.cs:190`](../../../MicroServices/GPAO/ImportP60/Client.cs#L190)
  [`ConvertP89/Client.cs:166`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L166)

- P89 quiet period range, same bounds and message shape as P60.
  [`ConvertP89/Client.cs:89`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L89)

**Tests**

- Library signal: processing/ fault, inbox fault, and the cases that stay `true`.
  [`InboxScannerTests.cs:633`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L633)

- P60 tick core: listing fault returns false, onError untouched.
  [`RunTickCoreTests.cs:122`](../../../MicroServices/GPAO/ImportP60.Tests/RunTickCoreTests.cs#L122)

- Check-inside-task test (fragile lambda-name assertion, deferred D-2).
  [`ClientRobustnessTests.cs:75`](../../../MicroServices/GPAO/ImportP60.Tests/ClientRobustnessTests.cs#L75)

- P89 missing source: failed tick, one error (characterization).
  [`ConvertP89.Tests/RunTickCoreTests.cs:107`](../../../MicroServices/GPAO/ConvertP89.Tests/RunTickCoreTests.cs#L107)

- AC-FR25-9 matrices, P60 then P89.
  [`ImportP60.Tests/ClientConfigurationTests.cs:239`](../../../MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs#L239)
  [`ConvertP89.Tests/ClientConfigurationTests.cs:203`](../../../MicroServices/GPAO/ConvertP89.Tests/ClientConfigurationTests.cs#L203)

**Peripherals**

- Ledger: six 6.6 entries resolved, review D-1 / D-2 deferred.
  [`deferred-work.md:1334`](deferred-work.md#L1334)
