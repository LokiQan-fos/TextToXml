---
title: 'Kape22Persister replaced — single-transaction bundle persist + cold-Coulee control (Story 4.6)'
type: 'feature'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 1
context: []
baseline_commit: '39642f2066ddc2fa480e10cd8d0105adc9ce4b4f'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Kape22Persister.Persist(MapResult<L_D_KAPE22>)` only writes `L_D_KAPE22`; Story 4.5's `Kape22ImportBundle` composes the other 9 downstream entities but nothing persists them yet, and the cold-Coulee existence check (the one DB-read business control) has no home.

**Approach:** Replace `Persist` with a single `Persist(Kape22ImportBundle bundle)` overload: run the existing anti-duplicate guard first (unchanged), then — only for a cold Coulee — check `L_D_COULEE` existence via the already-open context and reject if missing, otherwise stage `L_D_KAPE22` + all non-null downstream entities on the same `AscoLsiDbContext` and commit them with one `SaveChanges()`. Update `Kape22FichierProcessor.Import` to call `Kape22ImportBundleMapper.Map` then this new `Persist`.

## Boundaries & Constraints

**Always:**
- Guard order is fixed: anti-duplicate guard (`OkLogRowExists`, unchanged) first; on hit, add nothing, return `AlreadyImported = true` (AC-FR11-6/7, unchanged behavior).
- Cold-Coulee check only applies when `bundle.Kape22!.CodeConsignePits == "1"` (cold); read `context.CouleeRows` for `IdCoulee == bundle.Kape22.Coulee`; if absent, add nothing, write one REJETÉ `L_D_LOG_COMMANDE` row via the existing `BuildLogRow` mechanism citing the missing Coulee (new `ErrorCode.BusinessRuleViolation`, AC-FR20-5).
- On success path: add `L_D_KAPE22`, an OK `L_D_LOG_COMMANDE` row, `OrdreFabrication`, every non-null `SectionCharge*`, and `Consignes` to the same context, then exactly one `SaveChanges()` (AC-FR21-1, AD-1). **Renegotiated 2026-09-16 (code review, see Spec Change Log):** `Coulee` is added only when `IdCoulee` isn't already on file — several OF from the same cast legitimately share one Coulee, and unconditional insertion would throw a primary-key violation on the second OF, which the existing Story 3.6 SM-2 ten-fixture suite (three real same-Coulee pairs) would have caught immediately had it been left unpatched.
- `SaveChanges()` failure (`DbUpdateException`/`DbException`) is caught exactly like today's `PersistenceFailure`, producing `ConversionError{Code:PersistenceError}` citing the SQL cause — EF Core's own rollback means none of the up-to-11 rows persist (AC-FR21-2, extends AC-FR11-5).
- The old `Persist(MapResult<L_D_KAPE22>)` overload is deleted — one `Persist(Kape22ImportBundle)` remains (AC-FR21-3, AD-6).
- `Kape22FichierProcessor.Import` calls `new Kape22ImportBundleMapper(timeProvider).Map(...)` then `Persist(bundle)`; downstream reads (`Warnings`, `NumeroFichier`, `OF`) move from `map.*` to `bundle.*`.
- `IFichierProcessor.Process(string, byte[]) -> FichierProcessingResult` signature and `FichierProcessingResult` shape stay byte-for-byte unchanged — verify this explicitly before closing the story (scope note, `epic-3-retro-item-4`); if untrue, HALT (see Ask First).
- `AcCoverageCompletenessTests.AcCountByFr`: bump `[20]` from `4` to `5` (AC-FR20-5); add `[21] = 3` (AC-FR21-1..3 only — FR21-4/5 arrive with Story 4.7, do not add them here).

**Ask First:** if verifying `IFichierProcessor`/`FichierProcessingResult` reveals either must change shape to carry the new failure causes, STOP and ask — that triggers an out-of-scope `MicroServices.sln` (SVN) commit per the scope note.

**Never:** no `TransactionScope` (AD-1's atomicity is the single `SaveChanges()`); no EF navigation properties between bundle entities (AD-7); no change inside `Kape22ImportBundleMapper`/`Kape22Mapper`/the 4.3/4.4 mappers; no new logging channel (AD-4).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Already imported (AC-FR11-6/7) | Bundle whose OK log row already exists | Nothing added; `AlreadyImported=true` | N/A |
| Cold Coulee missing (AC-FR20-5) | `CodeConsignePits=="1"`, no matching `L_D_COULEE.IdCoulee` | Nothing added; REJETÉ log row citing missing Coulee; `BusinessRuleViolation` error | Guard passed, Coulee check rejects before any entity add |
| Full success (AC-FR21-1) | Guard + Coulee check pass (or hot Coulee, check skipped) | `L_D_KAPE22` + OK log + all non-null downstream entities committed in one `SaveChanges()` | N/A |
| SQL failure on commit (AC-FR21-2) | `SaveChanges()` throws `DbUpdateException`/`DbException` | Nothing committed (EF rollback); `PersistenceError` citing SQL cause | Same catch pattern as today's `PersistMapped` |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Persistence/Kape22Persister.cs:45-90` -- current `Persist(MapResult<L_D_KAPE22>)`/`PersistMapped`/`PersistRejected` to replace; `PersistMapped`'s guard check (`OkLogRowExists`, 121-125) and single-`SaveChanges` + catch (73-78) are the pattern to extend to the bundle; `BuildLogRow` (127-138) is reused as-is for the new REJETÉ row.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:145-159` -- `PersistenceFailure` static helper, reused unchanged for AC-FR21-2.
- `src/Kape22Importer/Kape22FichierProcessor.cs:68,78-79` -- the two call sites to change: `Kape22Mapper.Map` -> `Kape22ImportBundleMapper.Map`, and `Persist(map)` -> `Persist(bundle)`; lines 74/89 (`map.Warnings`, `map.NumeroFichier`, `map.OF`) become `bundle.*`.
- `src/Kape22Importer/IFichierProcessor.cs:6-9`, `src/Kape22Importer/FichierProcessingResult.cs:11-20` -- signature/shape to verify unchanged before closing (scope note).
- `src/Kape22Importer/Kape22ImportBundle.cs` -- bundle shape (Story 4.5, done): `Kape22`, `OrdreFabrication`, `Coulee`, 7 nullable `SectionCharge*`, `Consignes` (`List<L_D_CONSIGNES>`), `Errors`/`NumeroFichier`/`OF`/`Success`/`Warnings`.
- `src/Kape22Importer/Persistence/L_D_KAPE22.cs:28,52` -- `CodeConsignePits` (cold when `== "1"`) and `Coulee` (lookup value).
- `src/Kape22Importer/Persistence/L_D_COULEE.cs:93` -- `IdCoulee` (business key) to match against `L_D_KAPE22.Coulee`.
- `src/Kape22Importer/Persistence/AscoLsiDbContext.cs:13-...` -- `AscoLsiDbContext` DbSets, incl. `CouleeRows`, `OrdreFabricationRows`, `ConsignesRows`, the 7 `SectionCharge*Rows`.
- `src/TextToXml/Contract.cs:36,42` -- existing `ErrorCode.PersistenceError`/`BusinessRuleViolation` members to reuse (no new member needed).
- `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:19-21,261` -- commit+reset regime (never `TransactionScope`); private `Persist(MapResult<L_D_KAPE22> map, ...)` helper (line 261) to migrate to a bundle-based helper; ~9 existing tests calling it need updating to build/consume a `Kape22ImportBundle`.
- `tests/Kape22Importer.Tests/TestSupport.cs` -- `MapReferenceFichier()`/`WinterClock()`/`ReferenceFichierName` conventions; add a `MapReferenceBundle()` helper (via `Kape22ImportBundleMapper`) mirroring `MapReferenceFichier()`.
- `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs:22-37` -- `AcCountByFr` dictionary to update (`[20]=5`, add `[21]=3`).

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/TestSupport.cs` -- add `MapReferenceBundle()`/bundle-building helpers -- TDD support for the new tests (CC-1).
- [x] `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs` -- migrate the `Persist(map, ...)` helper and its ~9 callers to `Kape22ImportBundle`; add AC-FR20-5, AC-FR21-1, AC-FR21-2, AC-FR21-3 tests -- written first, red before green (CC-1).
- [x] `src/Kape22Importer/Persistence/Kape22Persister.cs` -- replace `Persist(MapResult<L_D_KAPE22>)` with `Persist(Kape22ImportBundle)`: guard, cold-Coulee check + REJETÉ row, stage all non-null entities, one `SaveChanges()`, existing exception handling -- delivers AC-FR20-5/AC-FR21-1..3.
- [x] `src/Kape22Importer/Kape22FichierProcessor.cs` -- switch to `Kape22ImportBundleMapper.Map` + new `Persist` call; update `Warnings`/`NumeroFichier`/`OF` reads -- wires the orchestrator to the new persister.
- [x] `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs` -- bump `[20]` to `5`, add `[21]=3` -- completeness gate for the new ACs.
- [x] Verify `IFichierProcessor`/`FichierProcessingResult` are byte-for-byte unchanged (diff review) -- closes the scope note before the story is marked done. Confirmed: `git diff --stat` on both files is empty.

**Acceptance Criteria:**
- Given a bundle whose OK log row already exists, when `Persist` runs, then nothing is added and `AlreadyImported=true` (AC-FR11-6/7, unchanged).
- Given a bundle that passes the guard with a cold Coulee (`CodeConsignePits=="1"`) absent from `L_D_COULEE`, when `Persist` runs, then nothing is added, one REJETÉ log row cites the missing Coulee (AC-FR20-5).
- Given a bundle that passes guard and Coulee check, when `Persist` runs, then `L_D_KAPE22` + OK log + `OrdreFabrication` + `Coulee` + every non-null `SectionCharge*` + `Consignes` commit in one `SaveChanges()` (AC-FR21-1).
- Given that same `SaveChanges()` throwing, when `Persist` intercepts it, then nothing is committed and the result carries a `PersistenceError` citing the SQL cause (AC-FR21-2).
- Given the codebase after this story, when `Kape22Persister` is inspected, then only `Persist(Kape22ImportBundle)` exists — the `MapResult<L_D_KAPE22>` overload is gone (AC-FR21-3).

## Spec Change Log

- **2026-09-16, code review (Acceptance Auditor):** Finding — the implemented `PersistMapped` skips re-inserting `Coulee` when its `IdCoulee` already exists (`couleeAlreadyExists` check), but the frozen "Always" bullet said `Coulee` is unconditionally added, with no renegotiation note. Amended: the "Always" bullet in `<frozen-after-approval>` now documents the reuse behavior as a dated renegotiation, in place instead of reverting the code — the unconditional reading is not just under-specified but actively wrong: several fixtures in the pre-existing, `done` Story 3.6 SM-2 ten-fixture suite (`P60_847_682_002/003/004` share Coulee `062597`, `_007/_008` share `062666`, `_009/_010` share `062814`) exercise exactly this multi-OF-same-Coulee case, and unconditional insertion would throw a `L_D_COULEE` primary-key violation on the second OF of each pair, failing that already-approved suite. KEEP: the guard order (anti-duplicate → cold-Coulee existence → stage-and-commit), the single `SaveChanges()`, and every other "Always" bullet are unaffected and must survive unchanged.

## Design Notes

`OkLogRowExists`/`BuildLogRow`/`PersistenceFailure` are reused verbatim — this story only changes what gets staged before `SaveChanges()` and adds one new guard (cold-Coulee existence) between the anti-duplicate guard and the staging step. No new `ErrorCode` member: `BusinessRuleViolation` (added in Story 4.5) already fits the cold-Coulee rejection.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all pass, including the updated completeness gate.
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: all pass or skip cleanly against the local SQL Server harness (AR-12), including the new AC-FR20-5/AC-FR21-1..3 tests in `TransactionalPersistenceTests`.

## Suggested Review Order

**The new persister: guard order and single transaction**

- Entry point: dispatches a bundle to the success or rejection path exactly like the old `MapResult` overload did.
  [`Kape22Persister.cs:55`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L55)

- New guard between anti-duplicate and staging: a cold Coulee with no matching `L_D_COULEE` row is rejected before anything is added.
  [`Kape22Persister.cs:90`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L90)

- Coulee is skipped when already on file (renegotiated post-review) — several OF share one cast's Coulee.
  [`Kape22Persister.cs:106`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L106)

- The single `SaveChanges()`: every non-null downstream entity commits with `L_D_KAPE22` or none does (AD-1).
  [`Kape22Persister.cs:122`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L122)

- `AddIfPresent` helper: only a Story 4.3/4.4 mapper's applicable `SectionCharge*` is staged.
  [`Kape22Persister.cs:173`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L173)

**Orchestrator wiring**

- `Kape22FichierProcessor` now maps through the bundle orchestrator instead of the bare `Kape22Mapper`.
  [`Kape22FichierProcessor.cs:71`](../../src/Kape22Importer/Kape22FichierProcessor.cs#L71)

- ...and persists the bundle through the new single `Persist` overload.
  [`Kape22FichierProcessor.cs:82`](../../src/Kape22Importer/Kape22FichierProcessor.cs#L82)

**Persister tests: the 4 new/restored acceptance cases**

- AC-FR20-5: cold Coulee missing from `L_D_COULEE` rejects with no inserts anywhere, including downstream tables.
  [`TransactionalPersistenceTests.cs:270`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L270)

- AC-FR21-1: full success commits `L_D_KAPE22` and every non-null downstream entity in one `SaveChanges()`.
  [`TransactionalPersistenceTests.cs:318`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L318)

- AC-FR21-3: reflection proof that only the bundle `Persist` overload remains (AD-6).
  [`TransactionalPersistenceTests.cs:365`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L365)

- AC-FR11-3 rollback case restored (dropped, then re-added at review) with the bundle-based signature.
  [`TransactionalPersistenceTests.cs:116`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L116)

**Test infrastructure: the decimal-scale workaround (deferred-work.md)**

- `ZeroOutOfScaleDimensions` on the post-Converter XDocument: blanks every Champ a Story 4.3/4.4 mapper writes unrescaled into a narrow `DECIMAL` column.
  [`TestSupport.cs:93`](../../tests/Kape22Importer.Tests/TestSupport.cs#L93)

- Byte-level counterpart for tests that mutate raw Fichier bytes instead of parsed XML.
  [`TestSupport.cs:169`](../../tests/Kape22Importer.Tests/TestSupport.cs#L169)

- `InsertableReferenceFichier`/`InsertableFichier`: the two byte-array entry points every rewritten integration test now uses.
  [`TestSupport.cs:150`](../../tests/Kape22Importer.Tests/TestSupport.cs#L150)

- `WithDetailChamp`: fixed-width Detail-block Champ patcher, position/size sourced from `Templates/P60.xml`.
  [`TestSupport.cs:182`](../../tests/Kape22Importer.Tests/TestSupport.cs#L182)

- `SqlServerIntegrationFixture.ResetData` now truncates all 9 downstream tables, not just the 2 from Story 2.8.
  [`SqlServerIntegrationFixture.cs:84`](../../tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs#L84)

**Peripherals**

- Completeness gate bump for the new ACs.
  [`AcCoverageCompletenessTests.cs:36`](../../tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs#L36)

- Pre-existing integration/E2E suites (`DoubleJournalIntegrationTests`, `DoubleJournalTests`, `Kape22FichierProcessorIntegrationTests`, `Kape22FichierProcessorTests`, `EndToEndImportIntegrationTests`, `WorkerLoopRobustnessIntegrationTests`) swapped to the insertable fixture helpers so the new single transaction doesn't trip the deferred decimal-scale defect.

- `GpaoImportP60WorkerEndToEndTests` left red on purpose, with an in-code note pointing at the deferred-work.md entry.
  [`GpaoImportP60WorkerEndToEndTests.cs:10`](../../tests/Kape22Importer.Tests/GpaoImportP60WorkerEndToEndTests.cs#L10)

- The decimal-scale defect, its scope, and the follow-up Story 4.3-bis action item.
  [`deferred-work.md:3`](deferred-work.md#L3)

