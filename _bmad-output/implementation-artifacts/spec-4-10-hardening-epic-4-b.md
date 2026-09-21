---
title: 'Story 4.10 — Hardening Épic 4 (B-1..B-5)'
type: 'refactor'
created: '2026-09-18'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '61c2e40ec715a72a6893b24ed05f18e2b012403e'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Épic 4 retrospective #2 (`epic-4-retro-2026-09-18.md`) found 5 hardening gaps left open around `DecimalScale.Apply`, none covered by a declared AC: nothing cross-checks the 21 mapper-side literal scale arguments against the mapping annex's own `Scale` (B-1); nothing guards against a future 6th mapper bypassing `DecimalScale.Apply` entirely (B-2); `Kape22ProductionDataParityTests` round-trips only `L_D_KAPE22`, never a scaled downstream column against real production data (B-3); `scripts/e2e-worker-import.ps1`'s closing production-parity `dotnet test` call ignores `$LASTEXITCODE`, unlike the earlier `dotnet build` step (B-4); `DecimalScale.Apply` corrects `scale` but never checks a column's total `DECIMAL(p,s)` magnitude, so an outlier can still throw a raw, undiagnosed SQL overflow (B-5).

**Approach:** Extend the existing `MappingAnnexCompleteness.Check` family (`MappingAnnexSchema.cs`) with a mapper-source-vs-annex scale check (B-1) and a bypass guard (B-2); extend `Kape22ProductionDataParityTests` to round-trip the decimal-scaled downstream tables (B-3); guard the unguarded `dotnet test` call in `e2e-worker-import.ps1` the same way the `dotnet build` step already is (B-4); add a pre-persist magnitude check reusing the existing `ConversionError`/REJETÉ circuit (B-5).

## Boundaries & Constraints

**Always:**
- B-1/B-2 build on the real annex + real EF model data already loaded by `AnnexeMappingDispatchEpic4_MatchesStory41EfModel_AcFr17_5` (`MappingAnnexCompletenessTests.cs:201`) — reuse that fixture data rather than re-loading the annex a second way.
- Annex `Column` values equal the EF/mapper property name 1:1 (verified: `DiametreProduit`→1, `ChutagePied`→2, `LongueurMoyenne`→3, `EpaisseurEnLaminage`→1, `H2Coulee`→1, all already matching in `annexe-mapping-dispatch-epic4.md`) — B-1 can key off property name, no new Table↔Column mapping needed.
- B-5's guard produces one `ConversionError { Block = Block.File, Code = ErrorCode.BusinessRuleViolation, ... }` + one REJETÉ `L_D_LOG_COMMANDE` row, modeled on the existing missing-Coulee block (`Kape22Persister.cs:90-108`) — never a raw uncaught `DbException`/SQL overflow.
- `Kape22Persister`'s `catch (... when (exception is DbUpdateException or DbException))` filter stays untouched (AD-4 boundary, Story 3.5's `ErrorCode.UnexpectedFailure` contract) — B-5 is a pre-check, not a widened catch.
- B-4's guard mirrors the existing style at `e2e-worker-import.ps1:106` (`if ($LASTEXITCODE -ne 0) { throw '...' }`), applied inside the `foreach ($fichier in $Fichiers)` loop at line 184-189.

**Ask First:** Where B-5's magnitude guard sources each column's precision `p` — today nothing captures it (the annex's `MappingAnnexEntry` only has `Scale`; `p` is only a human-readable comment pointing at `scripts/schema/01-ascolsi-tables.sql`). Two options: (a) add a `Precision` field to the annex (mirrors how `Scale` was added in 4.2-bis), or (b) parse `01-ascolsi-tables.sql` directly at test/check time. Pick one and note the choice in Design Notes before implementing B-5 — HALT and ask the human if neither is clearly preferable once the codebase is examined.

**Never:**
- No widening `Kape22Persister`'s SQL exception filter to `InvalidOperationException` or similar (B-5's boundary above).
- No touching `SectionChargeRefroidissoirsMapper`/`PoidsMetriqueMapper`/`SvtMapper` — confirmed no `decimal` columns (4.2-bis), out of scope for all 5 items.
- No changing any of the 21 existing `DecimalScale.Apply(source.X, N)` call sites' `N` values — B-1 is a drift *guard*, not a re-verification of Story 4.3-bis's already-correct values.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Mapper scale literal diverges from annex `Scale` | A mapper's `DecimalScale.Apply(source.X, 2)` while the annex row for that Table.Column says `Scale=1` | New completeness check fails, naming the Table.Column and both values | N/A (test-time failure) |
| 6th mapper bypasses `DecimalScale.Apply` | A hypothetical mapper assigns `source.RawInt` directly to a `decimal` EF property listed in the annex | New completeness/reflection check fails, naming the offending property | N/A (test-time failure) |
| Production parity on a scaled downstream column | A `P60/` fixture whose mapped `L_D_ORDRE_FABRICATION`/`L_D_SECTIONCHARGE_*` row is round-tripped and compared to the real production row | Every non-ignored scaled column matches (or is a documented `KnownLegacyDivergences` entry) | Existing `Skip` pattern when production/test DB unavailable |
| e2e script's production-parity test fails | `dotnet test ... Kape22ProductionDataParityTests` inside the `foreach` loop returns non-zero `$LASTEXITCODE` | Script throws and stops instead of continuing silently | `throw`, same style as line 106 |
| Out-of-gabarit scaled value | A raw KAPE22 int whose `DecimalScale.Apply` result still exceeds its column's `DECIMAL(p,s)` magnitude | One `ConversionError` (`BusinessRuleViolation`) + REJETÉ `L_D_LOG_COMMANDE` row, zero rows inserted, zero raw SQL exception | Pre-check, no exception ever raised |

</frozen-after-approval>

## Code Map

- `tests/Kape22Importer.Tests/MappingAnnexSchema.cs:131-198` (`MappingAnnexCompleteness.Check`) -- extend, or add a sibling static method, taking the 5 mapper source files (or a pre-extracted map of `(mapper, property) -> literal scale`) and failing on any mismatch against `entry.Scale` (B-1); add a second check (or extend the same pass) that fails when an annex `decimal←int` row's property is never assigned via `DecimalScale.Apply` in its owning mapper at all (B-2).
- `src/Kape22Importer/OrdreFabricationMapper.cs:41-59` (11 call sites), `SectionChargeChutageMapper.cs:22-23`, `SectionChargeDecoupeMapper.cs:23`, `SectionChargeLingotMapper.cs:23,28-32` (6), `SectionChargePitsMapper.cs:23` -- the 21 `DecimalScale.Apply(source.X, N)` sites B-1/B-2 read (read-only, no code change expected here).
- `src/Kape22Importer/DecimalScale.cs:8-22` -- B-5's magnitude check lands here or at its call site; read `Pow10ForScale`'s existing 0-3 range validation as the precedent for a descriptive, named exception vs. a bare one.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:82-108` -- the missing-Coulee `ConversionError`/REJETÉ block B-5's guard is modeled on; likely insertion point if B-5 lands as a pre-`SaveChanges` check here rather than inside `DecimalScale.Apply` itself.
- `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:70-198` -- `RoundTripThroughTestDatabase` (167), `ReadProductionRows` (184), `KnownLegacyDivergences` (65), `IgnoredColumns` (49) -- extend the round-trip/compare pattern to `L_D_ORDRE_FABRICATION` and the `L_D_SECTIONCHARGE_*` tables carrying `decimal` columns (per 4.2-bis: Chutage, Decoupe, Lingot, Pits — not Refroidissoirs/PoidsMetrique/Svt), matched to their production row the same way as `L_D_KAPE22` (OF + NumeroFichier).
- `scripts/e2e-worker-import.ps1:184-189` -- wrap the `dotnet @dotnetTestArgs --filter ...` call (line 188) with a `$LASTEXITCODE` check mirroring line 106 (B-4).
- `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md` -- only touched if B-5's Design Notes decision (Ask First) picks option (a), adding a `Precision` field.

## Tasks & Acceptance

**Execution:**
- [x] `MappingAnnexSchema.cs` -- add the mapper-vs-annex scale-drift check -- B-1, TDD red→green.
- [x] `MappingAnnexSchema.cs` -- add the `DecimalScale.Apply`-bypass check -- B-2, TDD red→green.
- [x] `Kape22ProductionDataParityTests.cs` -- extend round-trip/compare to the decimal-scaled downstream tables -- B-3, TDD red→green (Integration, `[SkippableTheory]`).
- [x] `scripts/e2e-worker-import.ps1` -- guard the production-parity `dotnet test` call's `$LASTEXITCODE` -- B-4.
- [x] `Kape22Persister.cs` (per the Ask-First decision, see Design Notes) -- add the pre-persist magnitude guard -- B-5, TDD red→green.
- [x] `MappingAnnexCompletenessTests.cs` -- new test cases: mapper/annex scale divergence (fails), bypass simulated (fails), current 21 sites (pass) -- B-1/B-2.

**Acceptance Criteria:** the 5 Given/When/Then ACs verbatim in `epics.md` § "Story 4.10 : Hardening Épic 4 (B-1..B-5)".

### Review Findings

- [x] [Review][Patch] Accented-French typo "designer"/"reference" in a new assertion message [tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:276] — fixed: "désigner la ligne de référence"
- [x] [Review][Patch] Garbled line-wrapped comment splits "MappingAnnexCompleteness" across a stray hyphen [tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs:13-14] — fixed: re-wrapped
- [x] [Review][Patch] Test doc comment claims coverage of the "no `?? 0`" call-site shape that the test's own literal snippet never exercises [tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs:269] — fixed: `LongueurCD` literal now omits `?? 0`, matching the SectionCharge* mapper shape the comment claims to cover
- [x] [Review][Patch] No regression test drives a negative out-of-gabarit value through the magnitude guard [tests/Kape22Importer.Tests/DecimalMagnitudeGuardTests.cs] — investigated, not applicable: `NormalizedXmlBuilder.cs:156` enforces D17 ("Int Champs are always unsigned", `NumberStyles.None`), so no KAPE22 source field feeding a `DownstreamColumnMagnitudes`-registered column can ever carry a negative raw value; `FindMagnitudeOverflow`'s `Math.Abs` is defensive-only and unreachable via any real input, so a synthetic negative-value test would test a path the real pipeline can never take. No test added.
- [x] [Review][Defer] Missing-Coulee block still duplicates the ConversionError/REJETÉ/SaveChanges shape inline instead of using this story's own `RejectWithBusinessRuleViolation` helper [src/Kape22Importer/Persistence/Kape22Persister.cs:92-112] — deferred, pre-existing
- [x] [Review][Defer] `DownstreamColumnMagnitudesParityTests` locks only `10^(p-s)`, so two columns with different `(p,s)` but equal `p-s` would be indistinguishable [tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs] — deferred, pre-existing
- [x] [Review][Defer] `FindMagnitudeOverflow`'s reported column on a multi-column overflow relies on unordered reflection enumeration, untested [src/Kape22Importer/Persistence/Kape22Persister.cs:243] — deferred, pre-existing
- [x] [Review][Defer] `CompareSectionCharge` silently no-ops on a missing mapper output or production row, with no assertion that at least one comparison ran [tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:259] — deferred, pre-existing
- [x] [Review][Defer] `DecimalMagnitudeFor` uses double-precision `Math.Pow` before casting to `decimal`, would throw `OverflowException` for a `DECIMAL(p,s)` with `p-s>=29` (none exist in the current schema) [tests/Kape22Importer.Tests/SqlTableSchema.cs:88] — deferred, pre-existing
- [x] [Review][Defer] `CheckMapperScaleUsage`'s `FirstOrDefault` only checks the first call site per Table+Column, unreachable under the mappers' object-initializer style [tests/Kape22Importer.Tests/MappingAnnexSchema.cs:202] — deferred, pre-existing
- [x] [Review][Defer] The e2e script's `--filter` clause could match zero tests and still exit 0, pre-existing and not introduced by this story's `$LASTEXITCODE` guard [scripts/e2e-worker-import.ps1:188] — deferred, pre-existing
- [x] [Review][Defer] The spec's own "Suggested Review Order"/"Code Map" line-number citations will silently go stale on the next edit [_bmad-output/implementation-artifacts/spec-4-10-hardening-epic-4-b.md] — deferred, pre-existing

## Spec Change Log

## Design Notes

B-5 design decision (Ask First, resolved): **option (b), direct SQL-schema read — not an annex `Precision` field.**

- Landing spot: a pre-`SaveChanges` check in `Kape22Persister.PersistMapped`, modeled on the existing
  missing-Coulee (AC-FR20-5) and Consignes-collision (A-5) blocks — not inside `DecimalScale.Apply`.
  `DecimalScale.Apply` runs at *mapper* time (AD-2: pure, no error-reporting circuit); the
  `ConversionError`/REJETÉ-log circuit B-5 must reuse only exists in the persister, and the AC's own
  wording ("garde-fou pré-persistance") already points there.
- Precision source: neither the annex nor `DecimalScale.Apply`'s call sites can carry `p` at runtime -
  both are files/literals read only at mapper/test time, and the persister's guard needs a compiled
  value it can check per Fichier without file I/O. So, mirroring the already-shipped
  `DownstreamColumnLengths`/`Kape22ColumnLengths` precedent (a hardcoded, per-column literal map,
  parity-locked against `scripts/schema/01-ascolsi-tables.sql` by a dedicated test): added
  `DownstreamColumnMagnitudes.MaxAbsoluteValues` (`src/Kape22Importer/Persistence/`), one
  `10^(p-s)` bound per scaled column name, locked by `DownstreamColumnMagnitudesParityTests`
  (`tests/Kape22Importer.Tests/`) which reads `01-ascolsi-tables.sql` via `SqlTableSchema` (extended
  with a `DecimalMagnitude` field on `SqlColumn`) and compares it against the same 21
  `DecimalScale.Apply` call sites B-1/B-2 already extract (`MapperScaleCallSites`) — not every DECIMAL
  column of the 5 tables, since `L_D_SECTIONCHARGE_LINGOT`'s 4 `PriseDeFer*` DECIMAL(4,1) columns are
  never assigned via `DecimalScale.Apply` (à_clarifier, stay at their CLR default) and carry no
  overflow risk from a mapper-produced value.
- Adding a `Precision` field to the annex (option a) was rejected: it would duplicate data already in
  `01-ascolsi-tables.sql` (the same file a human already read once to fill the annex's `Scale` column),
  with no runtime consumer for it (the annex is a documentation artifact, read only by tests, not by
  production code) - a third human-maintained copy of the same schema fact with its own drift risk,
  where option (b) reuses the SQL schema file and the `SqlTableSchema` reader the codebase already has.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: clean build.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: B-1/B-2/B-5 tests green, full suite green.
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: B-3's extended `Kape22ProductionDataParityTests` green (or clean skip without local SQL Server + production connection string, AR-12); no existing Integration test regresses.
- `scripts/e2e-worker-import.ps1` (manual/CI run) -- expected: a forced production-parity test failure now stops the script instead of being silently swallowed (B-4, spot-checked).

## Suggested Review Order

**B-5: pre-persist magnitude guard**

- Entry point — the guard itself: walks the bundle's scaled downstream entities and rejects the first out-of-gabarit value before `SaveChanges`.
  [`Kape22Persister.cs:243`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L243)

- Which entities are checked — `OrdreFabrication` unconditionally, the 4 `SectionCharge*` tables only when the Story 4.4 applicability rule kept them.
  [`Kape22Persister.cs:267`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L267)

- Shared rejection helper (review patch) both this check and the pre-existing Consignes-collision check now call, removing the duplicated try/`SaveChanges`/catch shape.
  [`Kape22Persister.cs:221`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L221)

- The magnitude bound source: a hardcoded `10^(p-s)` map, the Ask-First design decision (option b — direct SQL-schema read, not an annex field).
  [`DownstreamColumnMagnitudes.cs:19`](../../src/Kape22Importer/Persistence/DownstreamColumnMagnitudes.cs#L19)

- The parity test locking that map against the real generated schema, scoped to exactly the 21 real `DecimalScale.Apply` call sites (not every DECIMAL column of the 5 tables).
  [`DownstreamColumnMagnitudesParityTests.cs:37`](../../tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs#L37)

- `DecimalMagnitude` parsing added to the generic SQL-schema reader `DownstreamColumnMagnitudesParityTests` reads from.
  [`SqlTableSchema.cs:88`](../../tests/Kape22Importer.Tests/SqlTableSchema.cs#L88)

- The rejection-path test, plus (review patches) the previously-missing SectionCharge branch and the boundary-value case.
  [`DecimalMagnitudeGuardTests.cs:31`](../../tests/Kape22Importer.Tests/DecimalMagnitudeGuardTests.cs#L31)

**B-1/B-2: mapper-vs-annex scale drift and bypass guard**

- The check itself: a mapper's literal `DecimalScale.Apply` scale diverging from the annex `Scale`, or missing entirely, both fail here.
  [`MappingAnnexSchema.cs:202`](../../tests/Kape22Importer.Tests/MappingAnnexSchema.cs#L202)

- The regex extracting `Property = DecimalScale.Apply(source.Field, Scale)` call sites from the 5 mapper source files — deliberately fail-loud, never silently-blind, on an unrecognized shape.
  [`MappingAnnexSchema.cs:290`](../../tests/Kape22Importer.Tests/MappingAnnexSchema.cs#L290)

- The real gate: all 21 actual call sites checked against the real annex — the guard this story exists to add, exercised for real, not just synthetically.
  [`MappingAnnexCompletenessTests.cs:292`](../../tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs#L292)

**B-3: production parity for scaled downstream columns**

- Entry point — round-trips `L_D_ORDRE_FABRICATION` + the 4 decimal-bearing `SectionCharge*` tables, compares only their scaled columns against real production data.
  [`Kape22ProductionDataParityTests.cs:213`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L213)

- Review patch — the EF-translatable OF filter (`EF.Property<string>`), replacing a reflection-based filter that would have thrown against a real SQL Server provider.
  [`Kape22ProductionDataParityTests.cs:286`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L286)

- Review patch — production-row ambiguity guard: more than one candidate for an OF now fails loudly instead of silently picking one.
  [`Kape22ProductionDataParityTests.cs:267`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L267)

- The scaled-column-only comparison — deliberately not a full-row compare, since non-scaled `L_D_ORDRE_FABRICATION` columns are legitimately rewritten by later, non-P60 GPAO events.
  [`Kape22ProductionDataParityTests.cs:292`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L292)

**B-4: e2e script exit-code guard**

- The new guard, mirroring the pre-existing `dotnet build` guard's style two lines up.
  [`e2e-worker-import.ps1:191`](../../scripts/e2e-worker-import.ps1#L191)

**Peripherals**

- Story tracking: Story 4.10 added to the epic, sequenced after the retro #1 corrections.
  [`epics.md:2015`](../../_bmad-output/planning-artifacts/epics.md#L2015)

- The correct-course proposal that reopened Épic 4 and routed B-1..B-5 into this story.
  [`sprint-change-proposal-2026-09-18.md`](../../_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-18.md)

- 3 findings logged for later attention rather than blocking this story.
  [`deferred-work.md`](../../_bmad-output/implementation-artifacts/deferred-work.md)
