---
title: 'Story 6.2 — Stability gate for P60 + P89 Fichiers'
type: 'bugfix'
created: '2026-09-28'
status: 'done'
baseline_commit: '56ee83ad2ff7bafd2deebabc39e520a84f5ebca1'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `InboxScanner` treats a Fichier as stable when two back-to-back `List` probes report the same size, which is a near no-op on a real share; `P89FolderConverter` has no gate at all. A Fichier still being copied is read truncated and lands in `error/` for good.

**Approach:** One rule in both formats: a Fichier whose last write is more recent than a quiet period (`Import:StabilityQuietPeriod` / `P89:StabilityQuietPeriod`, default 10 s) is skipped this tick, silently, and retried next tick. The clock is the injected `TimeProvider` (AC-FR12-5 revised, AC-FR22-9).

## Boundaries & Constraints

**Always:** CC-1 (new/rewritten AC tests seen red first), CC-2, CC-4 (new option properties in alphabetical position), CC-5. Age = `timeProvider.GetUtcNow() - LastWriteTimeUtc`; skip when `age < quietPeriod` — a last write in the future (clock skew) is therefore skipped too. The default lives in the property initializer so the workers' existing `GetSection(...).Get<T>()` binding picks it up with no worker code change. The two-probe logic and `InMemoryFileSource.MarkUnstableOnce` are deleted, not kept alongside. Stranded `processing/` Fichiers are not gated (they are already out of the inbox). Existing tests that seed inbox/source Fichiers with a last write younger than 10 s relative to their clock are backdated, never given a zero quiet period to hide the gate.

**Ask First:** Any worker code change in MicroServices (`GpaoImportP60`, `GpaoConvertP89`) beyond test-data backdating; any new `P89FichierStatus` member or logged outcome for a skipped Fichier; any change to `src/TextToXml/`.

**Never:** Journal, XML export, worker hardening (6.1, 6.3, 6.4). A delay/sleep between probes. A new `IFileSource` member. Startup validation of the quiet period.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| P60 young Fichier | inbox, last write 5 s ago, quiet 10 s | stays in inbox, not processed, nothing logged ≥ Warning | N/A |
| P60 settled | same Fichier, next tick 10 s+ later | moved to `processing/` and processed | N/A |
| P60 mixed inbox | one young, one old | old one processed, young one left | N/A |
| P89 young Fichier | source, last write 5 s ago | left in source, no XML, no journal entry, no outcome returned | N/A |
| P89 settled | next tick after quiet period | converted as today | N/A |
| Future timestamp | last write after `now` | skipped (age < quiet) | N/A |
| Default | option not configured | 10 s | N/A |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/InboxScanner.cs:124-140` -- `TryStableInboxFichiers`: replace the double `TryList` + size comparison with one `TryList(InboxFolder, …)` filtered by age; keep its AC-FR15-2 contract (unlisted inbox ⇒ false). `timeProvider` already injected (`:23`).
- `src/Kape22Importer/ImportOptions.cs` -- add `TimeSpan StabilityQuietPeriod { get; set; } = TimeSpan.FromSeconds(10);` between `RetentionDays` and end (alphabetical: after `RetentionDays`).
- `src/P89Converter/P89FolderConverter.cs:61-73` -- in `RunTick`, reuse the single `now` reading; skip `path` when `now - File.GetLastWriteTimeUtc(path) < options.StabilityQuietPeriod` before `Process`.
- `src/P89Converter/P89Options.cs` -- add `StabilityQuietPeriod` (default 10 s) between `SourcePath` and `XmlPath`.
- `tests/Kape22Importer.Tests/InboxScannerTests.cs:192-226` -- rewrite the two AC-FR12-5 tests on timestamps (clock `FixedClock(Now)`, `Now` = 2026-09-08T10:00Z; second tick = a second scanner with `FixedClock(Now + 10 s)` over the same source).
- `tests/Kape22Importer.Tests/InboxScannerTestSupport.cs:15-17,48-49,64-67` -- delete `unstableOnce` / `MarkUnstableOnce`.
- Backdate young seeds: `EndToEndPerformanceTests.cs:118` (`Now.AddSeconds(-i)`) and `:134` (`Now`), `ErrorsReportReadabilityTests.cs:91,114,139` (`Now`).
- `tests/P89Converter.Tests/TestSupport.cs:95-96` -- `Drop` writes with the real clock while tests use `FixedClock(Instant)` (2026-02-10): set `File.SetLastWriteTimeUtc` to `Instant - 1 min` so existing tests stay green.
- `tests/P89Converter.Tests/P89FolderConverterTests.cs` -- add AC-FR22-9 tests (seed a Fichier then `SetLastWriteTimeUtc(Instant - 5 s)`).
- MicroServices (SVN, user commits): `GPAO/ConvertP89.Tests/RunTickCoreTests.cs:104` writes a real file under `TimeProvider.System` ⇒ backdate it with `File.SetLastWriteTimeUtc`. `GPAO/ImportP60.Tests` seeds are already ≥ 1 min old or cancelled — no change.
- `README.md:8` (P60 row) and `:85-90` (P89 config paragraph) -- document both keys, default 10 s.
- `deferred-work.md:338` (InboxScanner two-probe) and `:1248` (P89 no gate) -- mark RESOLVED by Story 6.2 at closure.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/InboxScannerTests.cs` -- rewrite the AC-FR12-5 tests (young skipped + nothing ≥ Warning; settled next tick; mixed inbox) and add a default-10 s test; run red -- AC-FR12-5.
- [x] `src/Kape22Importer/ImportOptions.cs`, `InboxScanner.cs`, `InboxScannerTestSupport.cs` -- option + age gate, delete two-probe and `MarkUnstableOnce` -- green.
- [x] `tests/P89Converter.Tests/TestSupport.cs`, `P89FolderConverterTests.cs` -- backdate `Drop`; add AC-FR22-9 tests (young left in source with no XML/entry/outcome; converted once settled; default 10 s); run red -- AC-FR22-9.
- [x] `src/P89Converter/P89Options.cs`, `P89FolderConverter.cs` -- option + gate -- green.
- [x] `tests/Kape22Importer.Tests/EndToEndPerformanceTests.cs`, `ErrorsReportReadabilityTests.cs` -- backdate young seeds.
- [x] `MicroServices/GPAO/ConvertP89.Tests/RunTickCoreTests.cs` -- backdate the real file; run that test project.
- [x] `README.md` -- document `Import:StabilityQuietPeriod` and `P89:StabilityQuietPeriod`.

**Acceptance Criteria:**
- Given the full Unit suite (gates `AcTraitCoverageTests` included), when run, then it is green and every new test carries `[Trait("AC", "FR12-5")]` or `[Trait("AC", "FR22-9")]` with the matching `_AcFr12_5` / `_AcFr22_9` suffix.
- Given `InboxScanner.cs`, when inspected, then the inbox is listed exactly once per tick.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all green.
- `dotnet test TextToXml.sln --filter Category=Integration -m:1` -- expected: all green (or skipped when SQL Server is unreachable).
- `dotnet test <MicroServices>/GPAO/ConvertP89.Tests` and `ImportP60.Tests` -- expected: all green.

## Suggested Review Order

**The gate rule**

- P60 entry point: one inbox listing, filtered by age against the injected clock.
  [`InboxScanner.cs:137`](../../src/Kape22Importer/InboxScanner.cs#L137)

- P89 twin of the same rule, reusing the tick's single clock reading; skipped Fichiers yield no outcome.
  [`P89FolderConverter.cs:75`](../../src/P89Converter/P89FolderConverter.cs#L75)

**Configuration**

- Default in the initializer so the workers' existing `Get<T>()` binding needs no change.
  [`ImportOptions.cs:42`](../../src/Kape22Importer/ImportOptions.cs#L42)

- Same for P89, placed alphabetically (CC-4).
  [`P89Options.cs:26`](../../src/P89Converter/P89Options.cs#L26)

- Operator doc: `hh:mm:ss` format, a bare `10` binds as 10 days.
  [`README.md:88`](../../README.md#L88)

**Tests**

- AC-FR12-5 suite: young, elapsed boundary, mixed, future, configured, default.
  [`InboxScannerTests.cs:197`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L197)

- AC-FR22-9 suite, same shape over real temp folders.
  [`P89FolderConverterTests.cs:307`](../../tests/P89Converter.Tests/P89FolderConverterTests.cs#L307)

- `Drop` backdates by default: existing P89 tests run at a fixed 2026-02-10 clock.
  [`TestSupport.cs:97`](../../tests/P89Converter.Tests/TestSupport.cs#L97)
