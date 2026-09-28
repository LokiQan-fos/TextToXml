---
title: 'Story 5.1 — P89Converter: raw P89 folder → timestamped normalized XML'
type: 'feature'
created: '2026-09-25'
status: 'done'
baseline_commit: 'ec4043bc6234098492ddd79e64980018d7571dcc'
review_loop_iteration: 1
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

> **Rescoped 2026-09-25 (Epic 5 re-plan 5.0 / 5.1 / 5.2).** The journal (`IFichierJournal`,
> `AscoLsiJournal`) shipped in Story 5.0; the worker `GpaoConvertP89` is Story 5.2. Story 5.1 is the
> `P89Converter` library only, journaling through `IFichierJournal`, with no P89 ↔ P60 link.

## Intent

**Problem:** P89 Fichiers (UTF-8, `LP89_682_617_<nnn>`, index wraps 999 → 001) have no tested conversion: `<name>.xml` would be overwritten on rotation, converted Fichiers stay in the source folder, nothing is journaled.

**Approach:** `src/P89Converter` runs a tick over the source folder: strict UTF-8 → Windows-1252 transcode, `Converter.Convert` with the embedded P89 Descripteur, validation against the embedded `P89.xsd`, write `<name>_<yyyyMMddHHmmss>.xml`, one `IFichierJournal.Record` per processed Fichier, then move the source to done (success) or error (failure) under the same suffix.

## Boundaries & Constraints

**Always:** CC-1 strict TDD, CC-2/CC-3 English comments, CC-4 alphabetical ordering, CC-5 glossary vocabulary, CC-7 no literal folder. `P89Converter` references only `TextToXml` + `FichierJournal`; Descripteur + XSD embedded. Timestamp from an injected `TimeProvider` (local time), one reading per tick, also the entry `Instant`. Nothing ever overwrites an existing file. One Fichier's failure never stops the others. Cancellation checked between Fichiers only (NFR-9). The format never decides whether a row is written (D15 lives in `AscoLsiJournal`): every processed Fichier, success or failure, gets one entry (`Commande="P89"`, raw zero-padded `NumeroFichier`/`OF`, `null` when unreadable). `Record` throwing (any exception) defers the Fichier. Unit tests only: temp folders + a recording fake journal, no database.

**Ask First:** any change under `src/TextToXml/`, or under `src/Kape22Importer/` beyond reverting its `InternalsVisibleTo P89Converter`; any new NuGet package; committing the real Fichiers of `P89/raw`.

**Never:** the worker or `MicroServices.sln` (Story 5.2). P60 changes. Mapping or persistence of P89 business data (D30). Replacement characters. Anti-duplicate guard on `NumeroFichier`. Any database in `P89Converter.Tests`. Docker. Touching `P60/tmp/`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid Fichier | real `LP89_*` with accents | `xml/<name>_<ts>.xml` XSD-valid; entry without reason, `NumeroFichier="013"`; source → `done/<name>_<ts>` | N/A |
| Invalid UTF-8 / char outside 1252 | `0xFF` byte / `ő` | no XML; entry with an `Encodage` reason, no OF; → `error/<name>_<ts>` | outcome carries the reason |
| Truncated Ligne | message Ligne shortened | no XML; entry with conversion Errors, no OF; → `error/` | idem |
| XSD-invalid XML | conversion ok, schema error | no XML; entry with `XSD` reason and OF; → `error/` | idem |
| Mixed folder | 1 bad + 2 good | 2 → `done/`, 1 → `error/` | tick continues |
| Rotation | same name at two instants | distinct XML + distinct done files | N/A |
| Target XML exists | `<name>_<ts>.xml` already there | nothing written, Fichier left in source | Deferred |
| Journal throws | `Record` throws | XML just written deleted; source stays, retried next tick | Deferred, reason in outcome |
| File-system fault | source unreadable | no entry, Fichier left in source | Deferred |

</frozen-after-approval>

## Code Map

- `src/P89Converter/P89FolderConverter.cs` -- replace `Func<AscoLsiDbContext>`, `TryLog`, `ResolveUser`, the `OF is null` skips and the `Kape22Importer` usings by one `TryRecord(FichierJournalEntry)`; keep order XML → journal → move, `FileMode.CreateNew`, single clock read, the internal schema seam, `P89FichierOutcome`/`P89FichierStatus`.
- `src/P89Converter/P89Options.cs` -- drop `InitiatingServer` (now `AscoLsiJournal`'s concern).
- `src/P89Converter/P89Converter.csproj` -- `Kape22Importer` ref → `FichierJournal`.
- `src/P89Converter/{P89FichierConverter,P89Templates}.cs` -- unchanged (transcode, XSD, raw `Raw()` padding).
- `src/Kape22Importer/Kape22Importer.csproj:6-7` -- revert the Story 5.1 `InternalsVisibleTo`.
- `src/AscoLsiJournal/AscoLsiFichierJournal.cs:34` -- `NumeroFichier ?? string.Empty`: F-1 of the 5.0 review, reachable in P89 (`NumeroFichier` is `minOccurs="0"` in `P89.xsd`, so a blank one yields an XSD rejection with a readable OF).
- `tests/P89Converter.Tests/` -- 35 tests; `P89FolderConverterTests` + `TestSupport` (`InMemoryDatabase`, `UnreachableDatabaseContext`) move to a recording fake; `P89LogCommandeIntegrationTests.cs` deleted (5.0's `AscoLsiFichierJournalIntegrationTests` covers the real row); csproj loses InMemory/SqlClient/Configuration/SkippableFact packages and the AR-12 links.
- `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs` -- home of the F-1 test.

## Tasks & Acceptance

**Execution:**
- [x] `tests/P89Converter.Tests/` -- red first: `RecordingJournal : IFichierJournal` (entries list, optional exception to throw) in `TestSupport`; folder tests assert entries instead of rows; unreadable-OF variants now expect one entry with `OF == null`; drop `RunTick_BlankInitiatingServer_LogsTheMachineName`; structure test becomes `P89Converter_ReferencesOnlyFichierJournalAndTextToXml_AcFr22_7`; delete the integration test and DB plumbing.
- [x] `src/P89Converter/` -- green: ctor `P89FolderConverter(P89Options, IFichierJournal, TimeProvider)`; csproj and options as in Code Map.
- [x] `src/Kape22Importer/Kape22Importer.csproj` -- revert.
- [x] `tests/AscoLsiJournal.Tests/` then `src/AscoLsiJournal/AscoLsiFichierJournal.cs` -- red first: null `NumeroFichier` + readable OF → message starts with the `FichierName` (`LP89_… — REJETÉ : …`).
- [x] `README.md` (P89 section: journal via `IFichierJournal`, `InitiatingServer` → `AscoLsiJournal:InitiatingServer`), `PRD.md` FR-23 (F-1 fallback), `project-profile.md` (P89Converter.Tests no longer shares the SQL fixture), `deferred-work.md` (F-1 resolved), `TextToXml.sln` (both P89 projects) -- CC-5 sync.

**Acceptance Criteria:**
- AC-FR22-1: given the 3 fixtures, when converted, then no Error and XML valid against `P89.xsd`; Descripteur contiguous, `LG24` ends at 3442.
- AC-FR22-2: `P89.xsd` matches `P89.xml` (types, order, typed optional); `gen.ps1 -Check` (P60) unchanged.
- AC-FR22-3: encoding rows of the matrix.
- AC-FR22-4: rotation and target-exists rows — no overwrite.
- AC-FR22-5: given a valid Fichier, when a tick runs, then it is in done under `<name>_<ts>` and one entry without reason carries raw `NumeroFichier`/`OF`.
- AC-FR22-6: failure, mixed-folder and journal-throws rows; the entry carries the reasons and the OF when readable.
- AC-FR22-7: `TextToXml` and `Kape22Importer` unchanged vs baseline; `P89Converter` references exactly `FichierJournal` + `TextToXml`.

## Spec Change Log

- 2026-09-25, iteration 1 (intent_gap, resolved with the user): finding "P89Converter references Kape22Importer" (user). Amended: frozen block renegotiated (journal interface + LSI implementation, outside transaction; P60 migration split out), Code Map, Tasks, ACs. Known-bad state avoided: a P89 → P60 dependency and a per-format copy of the LSI journal. KEEP: transcode/XSD/timestamp/move behavior, review patches of iteration 0 (raw zero-padded OF/NumeroFichier, catch-all on journal write, single clock read, cached compiled schema, CC-2 comment, format-aware `gen.ps1` header), worker registration, the 7 added tests.
- 2026-09-25, rescope after the Epic 5 re-plan: journal delivered by Story 5.0 (`ec4043b`), worker moved to Story 5.2 (AC-FR22-8 leaves this spec). Amended: frozen block (library only), Code Map, Tasks, ACs; F-1 of the 5.0 review settled here. Known-bad state avoided: re-implementing `AscoLsiJournal` inside 5.1, a database in `P89Converter.Tests`. KEEP: everything listed in the previous entry except worker registration (5.2).

## Design Notes

The converter catches any exception from `Record` (the contract says it throws on failure), deletes the XML on the success path, and defers. A move failure after a successful `Record` reconverts next tick with a second entry (accepted, as before). Until Story 5.2 rewires `GpaoConvertP89`, `MicroServices.sln` no longer compiles against the new constructor — expected, SVN and uncommitted.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- 0 warnings.
- `dotnet test tests/P89Converter.Tests tests/AscoLsiJournal.Tests tests/TextToXml.Tests --filter Category=Unit` -- green (Kape22Importer.Tests untouched; Integration not run).
- `pwsh scripts/gen.ps1 -Check` and `pwsh scripts/gen.ps1 -Check -Format P89` -- no drift.
- `git diff ec4043b -- src/TextToXml src/Kape22Importer` -- empty.

## Suggested Review Order

**Tick orchestration through the journal**

- Entry point: the converter now depends on `IFichierJournal`, not on a DbContext.
  [`P89FolderConverter.cs:31`](../../src/P89Converter/P89FolderConverter.cs#L31)

- One clock reading per tick feeds both the suffix and the entry `Instant`.
  [`P89FolderConverter.cs:56`](../../src/P89Converter/P89FolderConverter.cs#L56)

- Success path: CreateNew outside cleanup, XML deleted on journal, write or move failure.
  [`P89FolderConverter.cs:99`](../../src/P89Converter/P89FolderConverter.cs#L99)

- Failure path: every rejected Fichier gets an entry, D15 left to the implementation.
  [`P89FolderConverter.cs:153`](../../src/P89Converter/P89FolderConverter.cs#L153)

- Any exception from `Record` becomes a Deferred reason.
  [`P89FolderConverter.cs:170`](../../src/P89Converter/P89FolderConverter.cs#L170)

**Reference boundary**

- Kape22Importer replaced by FichierJournal; no P89 ↔ P60 link.
  [`P89Converter.csproj:16`](../../src/P89Converter/P89Converter.csproj#L16)

- Structure gate pins exactly FichierJournal + TextToXml.
  [`P89ProjectStructureTests.cs:17`](../../tests/P89Converter.Tests/P89ProjectStructureTests.cs#L17)

**F-1 settlement in the LSI journal**

- Unreadable NumeroFichier falls back on the Fichier name in the message.
  [`AscoLsiFichierJournal.cs:36`](../../src/AscoLsiJournal/AscoLsiFichierJournal.cs#L36)

- Both OK and REJETÉ fallbacks locked by tests.
  [`AscoLsiFichierJournalTests.cs:100`](../../tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs#L100)

**Conversion pipeline (unchanged behavior)**

- Strict UTF-8 → Windows-1252 transcode, then XSD validation.
  [`P89FichierConverter.cs:33`](../../src/P89Converter/P89FichierConverter.cs#L33)

- Schema compiled once, independent of static init order (CC-4 reorder).
  [`P89Templates.cs:19`](../../src/P89Converter/P89Templates.cs#L19)

**Tests and support**

- Recording fake journal replaces the in-memory database.
  [`TestSupport.cs:96`](../../tests/P89Converter.Tests/TestSupport.cs#L96)

- Clock with injectable local zone for the suffix test.
  [`TestSupport.cs:54`](../../tests/P89Converter.Tests/TestSupport.cs#L54)

- Move-failure cleanup and local wall-time suffix (review patches P1, P2).
  [`P89FolderConverterTests.cs:95`](../../tests/P89Converter.Tests/P89FolderConverterTests.cs#L95)

- Unreadable OF now yields an entry with `OF == null`.
  [`P89FolderConverterTests.cs:135`](../../tests/P89Converter.Tests/P89FolderConverterTests.cs#L135)

**Docs**

- P89 section rewritten around `IFichierJournal`; worker deferred to 5.2.
  [`README.md:56`](../../README.md#L56)
