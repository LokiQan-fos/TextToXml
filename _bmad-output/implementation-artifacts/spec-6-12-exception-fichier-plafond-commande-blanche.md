---
title: 'Story 6.12 — Any per-Fichier exception counts toward the cap; blank Import:Commande refused'
type: 'bugfix'
created: '2026-10-07'
status: 'done'
baseline_commit: '90144ddb797d7d0a8f403c0774d267a009073b7b'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A non-I/O exception raised while reading, exporting or filing a P60 Fichier (`InboxScanner.ProcessFromProcessing`) or converting/writing a P89 one (`P89FolderConverter.Process`) escapes the tick before the Fichier is counted: the AC-FR25-8 cap never freezes it and every later Fichier is starved on every tick. Separately, a present but blank `Import:Commande` passes `GpaoImportP60` startup and `Kape22Persister` (`?? "P60"`) would write a blank `Commande` to `L_D_LOG_COMMANDE`.

**Approach:** Contain any exception at those per-Fichier sites as an ordinary retained attempt (P60: left in `processing/`, cause returned to the counter; P89: `Deferred` outcome), so the tick moves on and the existing cap freezes the Fichier with its one `Error`. `ReadConfig` refuses a non-null `Import:Commande` that is `string.IsNullOrWhiteSpace`. Same fix in both formats (D32).

## Boundaries & Constraints

**Always:** `IOException` / `UnauthorizedAccessException` keep today's behavior and cause text (transient retry, one Warning per attempt); a non-I/O cause names the exception type and message, so the cap's single `Error` names the Fichier and the exception; a filing failure of any type deletes the export this attempt created (AC-FR26-6); the Fichier is never moved to `error/` by this path (AC-FR15-3); `processor.Process` throws keep their `UnexpectedFailure` quarantine (AC-FR13-4); absent `Import:Commande` keeps the `P60` fallback; strict TDD, red first (CC-1); English comments (CC-2); alphabetical ordering (CC-4); no secrets (CC-7).

**Ask First:** any change outside the files in the Code Map; any change to `GpaoConvertP89` (MicroServices) — its `LogOutcome` already logs `Deferred` as Warning and `Frozen` as Error.

**Never:** a quarantine or `error/` move for these exceptions; catching around the inbox → `processing/` move or the folder listings (not a per-Fichier attempt in `processing/`; stays as today); a new log channel (AD-4); persisting the counter; trimming or defaulting a blank `Commande` in the library.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| P60 READ | `Read` throws `InvalidOperationException` for Fichier A in `processing/`, `Import:MaxAttempts` 3 | ticks 1-2: one Warning each, A stays; tick 3: one Error naming A and `InvalidOperationException` + message; tick 4: A not touched | contained per Fichier |
| P60 FILING | `Archive`/`Reject` move of A throws `InvalidOperationException`; Fichier B healthy, same tick | A stays in `processing/`, counted, export of A deleted; B archived on tick 1 | contained per Fichier |
| P60 I/O | `IOException` on read or filing | unchanged (existing tests stay green) | N/A |
| P89 CONVERT | conversion of A throws `InvalidOperationException`; B healthy, `P89:MaxAttempts` 3 | tick 1: A `Deferred` (reason names the type and message), B `Converted`; tick 3: A `Frozen`; A stays in the source folder | contained per Fichier |
| COMMANDE BLANK | `Import:Commande` = `""` or `"   "` | `ReadConfig` throws, message names `Import:Commande` | startup refused |
| COMMANDE ABSENT | key absent | accepted (library falls back to `P60`) | N/A |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/InboxScanner.cs:264-276` -- read catch (`IOException or UnauthorizedAccessException` filter): extend to any exception.
- `src/Kape22Importer/InboxScanner.cs:329-348` -- filing catch around `Export` / `Archive` / `Reject`: extend to any exception, keeping the `TryDelete(exportPath)` cleanup for every type.
- `src/Kape22Importer/InboxScanner.cs:153-177` -- `Attempt`: counts a non-null cause and logs the cap Error; unchanged, it already freezes whatever cause it gets.
- `src/P89Converter/P89FolderConverter.cs:204-224` -- `Process` catch: extend to any exception → `Deferred`. `Accept`'s bare `catch` (195) already deletes the XML for every type. `Counted` (113) turns the N-th `Deferred` into `Frozen`.
- `src/P89Converter/P89FolderConverter.cs:35,43-51,212-214` -- internal `XmlSchemaSet? schemas` seam: replace with an internal `Func<byte[], P89Conversion>` convert seam (public ctor passes `P89FichierConverter.Convert`), so a test can throw for one Fichier only. Callers: `tests/P89Converter.Tests/P89FolderConverterTests.cs:229,312` become `content => P89FichierConverter.Convert(content, P89FichierConverterTests.StricterSchema())`.
- `tests/Kape22Importer.Tests/InboxScannerTestSupport.cs:11-40` -- `InMemoryFileSource`: `ReadFault`, `MoveFault`, `MoveHook` (per-Fichier filing fault) are the triggers; add a per-Fichier read hook only if the READ row needs one.
- `tests/Kape22Importer.Tests/InboxScannerTests.cs:712-751` -- `RunTick_FichierLeftInProcessingForMaxAttemptsTicks_LogsOneErrorOnTheLast_AcFr25_8` theory: pattern to extend with non-I/O read and filing rows.
- `tests/P89Converter.Tests/P89FolderConverterTests.cs:434-457` -- `RunTick_DeferredForMaxAttemptsTicks_ReportsOneFrozenOutcome_AcFr25_8`, `CappedConverter`: pattern for the P89 test.
- `../MicroServices/GPAO/ImportP60/Client.cs:139-144` -- `ReadConfig` `Import:Commande` length check: add the blank check before it.
- `../MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs:293-318` -- `AssertThrowsNaming`, `ValidValues`; the absent case is already `ReadConfig_CommandeAtTheLimitOrAbsent_IsAccepted_AcFr25_9`.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:383` -- read-only: `configuration[CommandeKey] ?? DefaultCommande`.
- Config key grep (A-3; key kept, not renamed): `Import:Commande` in `GPAO/ImportP60/Client.cs:27`, `GpaoImportP60.json:7`, `ImportP60.Tests`, `Gpao.IntegrationTests:228`; TextToXml `src/Kape22Importer/appsettings.json:4`, `Kape22Persister.cs:59`, many tests (all `"P60"`). No JSON value is blank.
- PRD AC-FR25-8 / AC-FR25-9 already carry the extended text (sprint-change-proposal-2026-10-06); no AC reconciliation needed.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/InboxScannerTests.cs` -- red first: non-I/O READ and FILING rows (cap Error names the Fichier, `InvalidOperationException` and its message), and a healthy B processed on tick 1 while A's filing throws; `[Trait("AC","FR25-8")]` -- AC-FR25-8.
- [x] `src/Kape22Importer/InboxScanner.cs` -- widen the read and filing catches; a non-I/O cause is `"<Type>: <Message>"`, the Warning carries the exception for a non-I/O cause only (review P-1) -- AC-FR25-8.
- [x] `tests/P89Converter.Tests/P89FolderConverterTests.cs` -- red first: A throws `InvalidOperationException` from conversion, B converted on tick 1, A `Frozen` at the cap with a reason naming the type and message -- AC-FR25-8 (D32).
- [x] `src/P89Converter/P89FolderConverter.cs` -- convert seam; widen the `Process` catch; non-I/O reason `Fichier : <Type> : <Message>` -- AC-FR25-8.
- [x] `../MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs` + `../MicroServices/GPAO/ImportP60/Client.cs` -- red first: `""` and `"   "` throw naming `Import:Commande`; then the `ReadConfig` check -- AC-FR25-9. SVN commit by the user.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- mark 6.6 D-1 and the 6.7 implementation-review non-I/O entry RESOLVED by Story 6.12.

**Acceptance Criteria:**
- Given the full suites, when TextToXml Unit + Integration (`-m:1 --blame-hang-timeout 2m`) and `ImportP60.Tests` run, then 0 failures and `AcTraitCoverageTests` / `AcCoverageCompletenessTests` pass.

## Spec Change Log

- 2026-10-08, step-04 review (blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor; acceptance-auditor: no finding), no loopback, triage approved by the user. Patches P-1..P-7: (P-1) the read and filing Warnings carry the exception for a non-I/O cause only, so a transient I/O fault logs exactly as before (Always "I/O keeps today's behavior"); (P-2) `TryDelete` catches any exception, since it runs inside the widened filing catch; (P-3) I/O cause text pinned (P60 theory `DoesNotContain(nameof(IOException))`, P89 `RunTick_SourceUnreadable_DefersWithoutEntry` exact `Fichier : <message>`); (P-4) P89 test asserts the tick-1 Deferred reason, no journal entry / XML for the faulty Fichier, the healthy one in done; (P-5) the blank-`Commande` refusal names the `P60` default; (P-6) `ProcessFromProcessing` comments no longer imply an I/O-only read catch; (P-7) `deferred-work.md` RESOLVED lines reformatted; `ConvertP89.Tests` run (68 green). Deferred: D-1 P89 `Reject` double journal on a failed move (pre-existing, now any exception type), D-2 padded `Import:Commande`. P-1 and P-2 have no test (the recording logger drops the exception object; `File.Delete` has no seam).
- 2026-10-08, /run-review (`reviews/story-6-12/aggregated-report.md`), verdict REFUSÉ on D-1 (CC-1: P-1 and P-2 above shipped without a test), settled by the user as option 2. Patches: (P-1) P89 `Accept` catch comment says any exception; (P-2) P89 `TryDelete` catches any exception, as on P60 (D32) - no test, `File.Delete` has no seam on P89 either; (P-3) the blank-`Commande` test asserts the `'P60'` default (red shown by removing it from the message); (P-4) `processor.Process` catch comment disambiguated; (P-5) `ConvertP89.Tests` added to Verification; (P-6) `RecordingLogger` keeps the exception and the AC-FR25-8 theory asserts it on read and filing Warnings, null for I/O, the fault itself otherwise (red shown by inverting the ternary); (P-7) `InboxScanner.DeleteExport` internal seam and `RunTick_ExportDeleteThrowsAfterAFailedFiling_CountsTheFichierAndLogsTheExport_AcFr25_8` (red shown by narrowing `TryDelete` back to I/O). Deferred: F-1 P89 `Accept` XML cleanup after the write is unreached by any test. Unit 1265, Integration 1429 (20 skipped), `ImportP60.Tests` 72, `ConvertP89.Tests` 68 green.

## Design Notes

One widened catch per site rather than a second non-I/O clause: both clauses would do the same thing (leave the Fichier, clean the export, return a cause), only the cause text differs, so a small cause helper picks `Message` for I/O (unchanged) and `"<Type>: <Message>"` otherwise. The I/O filters' role — transient recovery — is preserved; the non-I/O case now also retries, but is bounded by the cap.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: 0 failures
- `dotnet test TextToXml.sln --filter Category=Integration -m:1 --blame-hang-timeout 2m` -- expected: 0 failures
- `dotnet test ../MicroServices/GPAO/ImportP60.Tests` -- expected: 0 failures
- `dotnet test ../MicroServices/GPAO/ConvertP89.Tests` -- expected: 0 failures (the `P89FolderConverter` internal constructor changed)

## Suggested Review Order

**P60: any read or filing fault is a counted attempt**

- Entry point: filing catch widened; export still deleted, Fichier stays in processing/.
  [`InboxScanner.cs:330`](../../src/Kape22Importer/InboxScanner.cs#L330)

- Read catch widened the same way; cause returned to the cap counter.
  [`InboxScanner.cs:268`](../../src/Kape22Importer/InboxScanner.cs#L268)

- Cause text: bare message for I/O (unchanged), type-prefixed otherwise.
  [`InboxScanner.cs:362`](../../src/Kape22Importer/InboxScanner.cs#L362)

- Cleanup can no longer escape the filing catch (P-2).
  [`InboxScanner.cs:441`](../../src/Kape22Importer/InboxScanner.cs#L441)

**P89: any conversion or write fault defers the Fichier (D32)**

- Process catch widened to Deferred; Counted freezes it at P89:MaxAttempts.
  [`P89FolderConverter.cs:219`](../../src/P89Converter/P89FolderConverter.cs#L219)

- Conversion seam replaces the schema seam so one Fichier can throw.
  [`P89FolderConverter.cs:46`](../../src/P89Converter/P89FolderConverter.cs#L46)

**Blank Import:Commande refused at startup**

- Non-null blank value refused naming the key; absent keeps the P60 fallback.
  [`Client.cs:145`](../../../MicroServices/GPAO/ImportP60/Client.cs#L145)

**Tests**

- Theory gains non-I/O read/filing rows; I/O rows pin the bare cause.
  [`InboxScannerTests.cs:722`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L722)

- Faulty Fichier counted, healthy one filed on tick 1.
  [`InboxScannerTests.cs:778`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L778)

- P89 faulty Fichier deferred then frozen, healthy one converted on tick 1.
  [`P89FolderConverterTests.cs:472`](../../tests/P89Converter.Tests/P89FolderConverterTests.cs#L472)

- I/O deferral reason pinned to `Fichier : <message>`.
  [`P89FolderConverterTests.cs:399`](../../tests/P89Converter.Tests/P89FolderConverterTests.cs#L399)

- Empty and whitespace Commande refused.
  [`ClientConfigurationTests.cs:310`](../../../MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs#L310)
