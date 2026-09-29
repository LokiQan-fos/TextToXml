---
title: 'Story 6.3 — P60 XML export to a dedicated folder'
type: 'feature'
created: '2026-09-29'
status: 'done'
baseline_commit: '90cda6191a8e69e9b607b074212b7db998d666dd'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A P60 Fichier's normalized XML only lives next to it in `archive/` / `error/`, which `Import:RetentionDays` purges; operators cannot hand it to third parties as they already can for P89 (`P89:XmlPath`, D33).

**Approach:** `InboxScanner` writes `<nom>_<yyyyMMddHHmmss>.xml` into `Import:XmlExportPath` (absolute, never purged) for every Fichier whose outcome is final and that has a normalized XML, before filing the outcome; `GpaoImportP60` refuses to start on a bad key (FR-26, AC-FR16-5).

## Boundaries & Constraints

**Always:** CC-1, CC-2, CC-4 (new `ImportOptions.XmlExportPath` between `RetentionDays` and `StabilityQuietPeriod`), CC-7. Suffix from `timeProvider.GetUtcNow()` converted to `ParisTime.Zone` (same instant/zone as the `archive/<yyyy>/<MM>` dating). Written with `FileMode.CreateNew` (never overwrite); a collision or any `IOException`/`UnauthorizedAccessException` goes through the existing outcome-filing catch: Fichier stays in `processing/`, `Warning`. If the export was created and archiving/rejecting then fails, the export file is deleted best-effort so a retry leaves one XML, like `P89FolderConverter.Accept`. Library-level: empty `XmlExportPath` = export off (the worker enforces it); `archive/` / `error/` sidecars unchanged. `PurgeRetention` unchanged.

**Ask First:** Exporting on `PersistenceError` (DB unreachable / journal pending) results — planned: no export until the outcome is final; any change to `IFileSource`; any change to `src/P89Converter/` or `src/TextToXml/`.

**Never:** Route the export through `IFileSource` (it is rooted at the inbox; the export is an absolute path outside it). Worker hardening (6.4). Purging the export folder.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Imported | valid Fichier `F`, now 2026-09-08T10:00Z | `F_20260908120000.xml` in export + usual archive | N/A |
| Rejected after conversion | mapper / business / SQL rejection | export written, Fichier in `error/` | N/A |
| D22 guard | already imported | export written, archived | N/A |
| Conversion failure | Step 1 errors, no XML | nothing in export | N/A |
| Unexpected throw | processor throws | nothing in export, `error/` | N/A |
| Persistence failure | `PersistenceError` | nothing exported, stays in `processing/` | existing Warning |
| Same name twice | two ticks at different seconds | two export files | N/A |
| Export unwritable | folder unreachable / name exists | Fichier stays in `processing/`, no archive/error move | Warning, retried next tick |
| Filing fails after export | archive move throws | export deleted, stays in `processing/` | Warning |
| Retention purge | aged export file | untouched | N/A |
| Worker config | key absent, relative, malformed, equal to inbox/processing, equal to or under archive/error | `ReadConfig` throws naming `Import:XmlExportPath` | startup refused |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/ImportOptions.cs` -- add `string XmlExportPath` (default `""`) alphabetically; update the class comment.
- `src/Kape22Importer/InboxScanner.cs:231-255` -- in `ProcessFromProcessing`'s filing `try`, call a new `Export(fichierName, normalizedXml)` before `Archive`/`Reject` when `NormalizedXml` is non-null and the option non-empty; on catch delete the created export. `IsPersistenceFailure` return (`:224`) stays before it; `UnexpectedFailure` has no XML.
- `src/P89Converter/P89FolderConverter.cs:57-59,136-137` -- read-only reference for suffix + `CreateNew`.
- `tests/Kape22Importer.Tests/InboxScannerTests.cs` + `InboxScannerTestSupport.cs` -- `InMemoryFileSource`/`FakeFichierProcessor` stay; export goes to a real temp folder.
- `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs:22-39` -- add `[26] = 6`; `FR26-5` to `KnownExceptions` (worker-level, MicroServices).
- `tests/P89Converter.Tests/P89FolderConverterTests.cs:72-107` -- add one AC-FR16-5 test (converted Fichier leaves `<name>_<ts>.xml` in `XmlPath`, distinct folder); no production change.
- MicroServices (SVN, user commits): `GPAO/ImportP60/Client.cs:64-103` `ReadConfig` validation after the `InboxPath` check; `GPAO/ImportP60/GpaoImportP60.json` add `"XmlExportPath"`; `GPAO/ImportP60.Tests/ClientConfigurationTests.cs` -- existing tests' config dictionaries gain a valid key; new AC-FR26-5 tests.
- `README.md:8` and P89 config paragraph -- "Export XML" section (both keys, never purged).

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/InboxScannerTests.cs` -- AC-FR26-1/2/3/4/6 tests per the matrix; run red.
- [x] `src/Kape22Importer/ImportOptions.cs`, `InboxScanner.cs` -- option + `Export` -- green.
- [x] `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs` -- register FR-26.
- [x] `tests/P89Converter.Tests/P89FolderConverterTests.cs` -- AC-FR16-5 conformance test (green on arrival; P89 already complies).
- [x] `MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs` -- AC-FR26-5 tests; run red; then `Client.cs` validation + `GpaoImportP60.json` -- green.
- [x] `README.md` -- Export XML section.

**Acceptance Criteria:**
- Given the Unit suite (gates included), when run, then green and every new test carries `[Trait("AC", "FR26-n")]` / `"FR16-5"` with matching `_AcFr26_n` / `_AcFr16_5` suffix.
- Given `ImportP60.Tests` and `ConvertP89.Tests`, when run, then green.

## Spec Change Log

- 2026-09-29 — Renegotiation (human-approved, step-04 triage D-1): the frozen Always wording "`XmlExportPath` between `RetentionDays` and `StabilityQuietPeriod`" contradicts CC-4 (X sorts after S). Amended intent: `XmlExportPath` is declared after `StabilityQuietPeriod`. The frozen text is left verbatim; this note supersedes it. KEEP: the CC-4 placement in `ImportOptions.cs`.

## Design Notes

Export sits in the scanner, not the processor, because AC-FR26-6 ("Fichier stays in `processing/`") is the scanner's outcome-filing contract, and "outcome final" is known only there. Retry after an export failure re-runs the processor; the D22 guard prevents a second insert.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all green.
- `dotnet test TextToXml.sln --filter Category=Integration -m:1` -- expected: green or skipped.
- `dotnet test <MicroServices>/GPAO/ImportP60.Tests` and `ConvertP89.Tests` -- expected: all green.

## Suggested Review Order

**Export in the outcome path**

- Entry point: export after the persistence-failure return, before filing, same clock reading.
  [`InboxScanner.cs:243`](../../src/Kape22Importer/InboxScanner.cs#L243)

- `CreateNew` never overwrites; a partial write deletes its own file.
  [`InboxScanner.cs:318`](../../src/Kape22Importer/InboxScanner.cs#L318)

- Filing failure after export removes it, so a retry leaves one XML (AC-FR26-6).
  [`InboxScanner.cs:266`](../../src/Kape22Importer/InboxScanner.cs#L266)

- Failed cleanup is logged: the folder goes to third parties (review P-4).
  [`InboxScanner.cs:343`](../../src/Kape22Importer/InboxScanner.cs#L343)

**Configuration and startup gate**

- Empty = off at library level; placed after `StabilityQuietPeriod` (CC-4, D-1).
  [`ImportOptions.cs:50`](../../src/Kape22Importer/ImportOptions.cs#L50)

- Worker refuses absent/relative/malformed or working-folder paths, naming the key (AC-FR26-5).
  [`Client.cs:114`](../../../MicroServices/GPAO/ImportP60/Client.cs#L114)

- Malformed working folders now named too, not a raw path exception (review P-1).
  [`Client.cs:145`](../../../MicroServices/GPAO/ImportP60/Client.cs#L145)

**Tests and peripherals**

- AC-FR26-1 theory: imported, rejected, D22 guard all export.
  [`InboxScannerTests.cs:420`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L420)

- AC-FR26-6: unwritable export and filing failure after export.
  [`InboxScannerTests.cs:526`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L526)

- AC-FR26-4: purge never reaches the export folder.
  [`InboxScannerTests.cs:507`](../../tests/Kape22Importer.Tests/InboxScannerTests.cs#L507)

- AC-FR16-5: P89 conformance, no production change.
  [`P89FolderConverterTests.cs:59`](../../tests/P89Converter.Tests/P89FolderConverterTests.cs#L59)

- SM-1 gate registers FR-26; FR26-5 lives in MicroServices.
  [`AcCoverageCompletenessTests.cs:43`](../../tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs#L43)

- Worker e2e script supplies the now-mandatory key (review P-6).
  [`e2e-worker-import.ps1:49`](../../scripts/e2e-worker-import.ps1#L49)

- Operator doc for both formats' export folders.
  [`README.md:98`](../../README.md#L98)
