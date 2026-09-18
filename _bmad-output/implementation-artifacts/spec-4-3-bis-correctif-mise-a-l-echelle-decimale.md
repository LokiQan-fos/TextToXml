---
title: 'Story 4.3-bis — Decimal-scale correction across the 5 affected mappers'
type: 'bugfix'
created: '2026-09-17'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '13a61059f4031fc0f9898cdd2aef6f5179e39d0f'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `OrdreFabricationMapper`, `SectionChargeLingotMapper`, `SectionChargeChutageMapper`, `SectionChargeDecoupeMapper` and `SectionChargePitsMapper` write raw KAPE22 `int`/`int?` values straight into narrow `DECIMAL` EF columns with no scale conversion (e.g. a raw `18` lands in a `DECIMAL(2,1)` max-9.9 column), overflowing SQL on any real P60 File whose applicable `SectionCharge`/OF values sit outside `[-9,9]`-ish ranges. Story 4.2-bis already documented the exact per-column `Scale` in the mapping annex.

**Approach:** Add a small production-side `DecimalScale.Apply` helper (int/int? + literal scale → decimal/decimal?) and wrap each of the 21 affected columns across the 5 mappers with it, using the literal scale value from the annex/Design Notes table below. Remove the `TestSupport.ZeroOutOfScaleDimensions` workaround and un-skip `GpaoImportP60WorkerEndToEndTests`.

## Boundaries & Constraints

**Always:**
- Every one of the 21 columns below (grouped by mapper) is wrapped in `DecimalScale.Apply(rawValue, scale)` with that literal scale — no column left on direct/`?? 0` assignment.
- `DecimalScale` is production code (`src/Kape22Importer/DecimalScale.cs`), `internal static`, zero reflection, zero DB access (AD-2) — same shape as the existing `SectionChargeApplicability.cs` helper precedent.
- Exact conversion: `rawValue / 10^scale` as a decimal division (exact for these small scales, no rounding). `scale=0` is a real value (identity, not "no conversion").
- Update the 5 mapper test files' scale-column assertions to expect the scaled decimal instead of the raw widened int; remove `TestSupport.ZeroOutOfScaleDimensions`/`OutOfScaleDimensionFields` and the two dangling call sites in `TransactionalPersistenceTests.cs`; remove the `Skip.If(true, ...)` in `GpaoImportP60WorkerEndToEndTests.cs`.

**Ask First:** None — conversion values are fixed by the annex/DDL, no design choice beyond `DecimalScale`'s shape (dictated by AD-2 + existing precedent).

**Never:**
- No change to `SectionChargeRefroidissoirsMapper`/`PoidsMetriqueMapper`/`SvtMapper` (confirmed no `decimal` column, out of scope).
- No trimming `Kape22Mapper.Map`'s `OF`/`Coulee` — that is Story 4.9/A-3, sequenced strictly after this story.
- No reusing `MappingAnnexEntry`/`MappingAnnex.Parse` from production code — they are `internal` in the test assembly (`tests/Kape22Importer.Tests/MappingAnnexSchema.cs`), wrong assembly for `src/Kape22Importer`. Scale is hardcoded per call site instead.
- No `Math.Round`/floating-point rounding — division by an exact power of ten is exact for `decimal`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Non-nullable target, in-range raw int | `source.ToleranceMaxSection = 18`, scale 1 | `entity.ToleranceMaxSection == 1.8m` | N/A |
| Scale-0 column | `source.ToleranceMaxLongueur = 5`, scale 0 | `entity.ToleranceMaxLongueur == 5m` (not treated as absent) | N/A |
| Nullable target, null source | `source.PoidsDemiProduitUnitaire = null`, scale 3 | `entity.PoidsDemiProduitUnitaire == null` | N/A |
| Non-nullable target, null source | `source.DiametreProduit = null`, scale 1 | `?? 0` applied before scaling → `entity.DiametreProduit == 0m` | N/A |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/SectionChargeApplicability.cs` -- existing `internal static class` precedent for a small cross-mapper helper (used by the 4 `SectionCharge*` mappers today) — model `DecimalScale` on this shape.
- `src/Kape22Importer/OrdreFabricationMapper.cs:37-55` -- 11 offending assignments (`DiametreProduit`, `Epaisseur`, `LongueurCD`, `PoidsDemiProduitUnitaire`, `PoidsPrevuDemiProduit`, 6× `Tolerance*`); non-nullable targets keep `?? 0` then wrap in `DecimalScale.Apply`.
- `src/Kape22Importer/SectionChargeLingotMapper.cs:23,28-32` -- 6 offending assignments (`EpaisseurEnLaminage`, `SectionLaminage`, 4× `Tolerance*1`-sourced); all nullable `decimal?` targets, no `?? 0`.
- `src/Kape22Importer/SectionChargeChutageMapper.cs:22-23` -- `ChutagePied`, `ChutageTete`, nullable targets.
- `src/Kape22Importer/SectionChargeDecoupeMapper.cs:23` -- `LongueurMoyenne`, nullable target.
- `src/Kape22Importer/SectionChargePitsMapper.cs:23` -- `H2Coulee`, nullable target.
- `tests/Kape22Importer.Tests/OrdreFabricationMapperTests.cs:23-50` -- `SourcedColumns()` theory currently asserts verbatim-widened equality for all 25 sourced columns via `Widen()` (line 221); remove the 11 scale columns from this list, add scale-specific assertions (raw `ReferenceKape22()` int → expected scaled decimal) for them instead.
- `tests/Kape22Importer.Tests/SectionChargeLingotMapperTests.cs:43,48-52` -- 6 `Assert.Equal((decimal?)source.X, entity.Y)` lines to change to scaled expectations.
- `tests/Kape22Importer.Tests/SectionChargeChutageMapperTests.cs:29-30` -- 2 lines, same treatment.
- `tests/Kape22Importer.Tests/SectionChargeDecoupeMapperTests.cs:30` -- 1 line, same treatment.
- `tests/Kape22Importer.Tests/SectionChargePitsMapperTests.cs:31` -- 1 line, same treatment.
- `tests/Kape22Importer.Tests/TestSupport.cs:85-120,153-160,162-181` -- delete `ZeroOutOfScaleDimensions` (both overloads), `OutOfScaleDimensionFields`, and the explanatory comment; `InsertableFichier`/`InsertableReferenceFichier` keep their signatures but stop calling it (return the fixture bytes as-is / only Coulee-corrected).
- `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:181,349` -- remove the two now-dangling `ZeroOutOfScaleDimensions(d);` lines inside `MapMutatedBundle` lambdas.
- `tests/Kape22Importer.Tests/GpaoImportP60WorkerEndToEndTests.cs:44-49` -- remove the unconditional `Skip.If(true, ...)` block (test stays gated by its other, pre-existing `Skippable`/connection checks).
- `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md` -- read-only reference: the annex's `Scale` column is this story's source of truth (already populated by Story 4.2-bis).
- `tests/Kape22Importer.Tests/DecimalScaleTests.cs` (new, added during implementation) -- the 5 mapper tests assert `entity.X == DecimalScale.Apply(source.X, scale)`, which is tautological for `DecimalScale.Apply`'s own arithmetic; this file covers the I/O matrix's 4 rows against literal expected values instead.

## Tasks & Acceptance

**Execution:**
- [x] `src/Kape22Importer/DecimalScale.cs` -- new `internal static class` with `Apply(int, int)` and `Apply(int?, int)` -- single, reflection-free scale-conversion point (AD-2).
- [x] `src/Kape22Importer/OrdreFabricationMapper.cs` -- wrap the 11 columns -- root-cause fix.
- [x] `src/Kape22Importer/SectionChargeLingotMapper.cs` -- wrap the 6 columns.
- [x] `src/Kape22Importer/SectionChargeChutageMapper.cs` -- wrap the 2 columns.
- [x] `src/Kape22Importer/SectionChargeDecoupeMapper.cs` -- wrap the 1 column.
- [x] `src/Kape22Importer/SectionChargePitsMapper.cs` -- wrap the 1 column.
- [x] 5 mapper test files listed above -- TDD red→green (CC-1): assert scaled decimal, not raw-widened int, for the 21 columns.
- [x] `tests/Kape22Importer.Tests/TestSupport.cs` -- remove the workaround.
- [x] `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs` -- remove the 2 dangling calls.
- [x] `tests/Kape22Importer.Tests/GpaoImportP60WorkerEndToEndTests.cs` -- remove the `Skip.If(true, ...)`.
- [x] `tests/Kape22Importer.Tests/DecimalScaleTests.cs` (added post-implementation, matrix audit) -- 4 facts against literal values -- the mapper-level assertions compare against `DecimalScale.Apply(...)` itself (tautological for the helper's own arithmetic); this file covers the I/O matrix directly.

**Acceptance Criteria:**
- Given the annex's per-column `Scale` (Story 4.2-bis), when each of the 5 mappers is corrected, then the decimal value written respects that scale (e.g. `DECIMAL(2,1)` max 9.9 no longer overflows for KAPE22 ints in the real fixture range).
- Given `SectionChargeRefroidissoirsMapper`/`PoidsMetriqueMapper`/`SvtMapper`, when this story's scope is checked, then none of them is touched.
- Given `Kape22FichierProcessorIntegrationTests`, `DoubleJournalIntegrationTests`, `EndToEndImportIntegrationTests`, `WorkerLoopRobustnessIntegrationTests`, when the fix lands, then they run against unmutated `P60/` fixtures (no `ZeroOutOfScaleDimensions`) and stay green.
- Given `GpaoImportP60WorkerEndToEndTests`, when the fix lands, then its unconditional skip is removed and it passes on the `P60_847_682_081/082` fixtures.

## Design Notes

Scale per column (mapper-grouped, from Story 4.2-bis's Design Notes / `annexe-mapping-dispatch-epic4.md`, itself read from `scripts/schema/01-ascolsi-tables.sql`):

| Mapper | Column | Scale |
|---|---|---|
| OrdreFabricationMapper | DiametreProduit, Epaisseur, ToleranceMaxEpaisseur, ToleranceMinEpaisseur, ToleranceMaxSection, ToleranceMinSection | 1 |
| OrdreFabricationMapper | ToleranceMaxLongueur, ToleranceMinLongueur | 0 |
| OrdreFabricationMapper | LongueurCD, PoidsDemiProduitUnitaire, PoidsPrevuDemiProduit | 3 |
| SectionChargeLingotMapper | EpaisseurEnLaminage, SectionLaminage, ToleranceMaxEpaisseur, ToleranceMinEpaisseur, ToleranceMaxSection, ToleranceMinSection | 1 |
| SectionChargeChutageMapper | ChutageTete, ChutagePied | 2 |
| SectionChargeDecoupeMapper | LongueurMoyenne | 3 |
| SectionChargePitsMapper | H2Coulee | 1 |

`DecimalScale.Apply`: `rawValue / Pow10[scale]` where `Pow10 = [1m, 10m, 100m, 1000m]` — exact decimal division, no `Math.Round`. Non-nullable mapper targets keep their existing `?? 0` on the raw `int?` before calling the `int` overload; nullable targets call the `int?` overload directly (null propagates).

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: clean build.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all mapper tests green with scaled assertions.
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: the 4 previously-patched integration suites green on unmutated fixtures; `GpaoImportP60WorkerEndToEndTests` no longer skipped (requires local SQL Server + sibling `MicroServices.sln` checkout per AR-12 — report skip reason if unavailable, do not treat as failure).

## Suggested Review Order

**The conversion helper (entry point)**

- The single scale-conversion point every mapper below calls into — division by an exact power of ten, no `Math.Round`.
  [`DecimalScale.cs:12`](../../src/Kape22Importer/DecimalScale.cs#L12)

- Bounds guard added during review: an out-of-range scale now fails descriptively instead of a bare `IndexOutOfRangeException`.
  [`DecimalScale.cs:17`](../../src/Kape22Importer/DecimalScale.cs#L17)

- Nullable overload: null KAPE22 source propagates to a null target instead of scaling `0`.
  [`DecimalScale.cs:21`](../../src/Kape22Importer/DecimalScale.cs#L21)

**The 5 mapper fixes (root cause)**

- 11 columns, the largest offender — non-nullable targets keep their existing `?? 0` before scaling.
  [`OrdreFabricationMapper.cs:41`](../../src/Kape22Importer/OrdreFabricationMapper.cs#L41)

- 6 columns, all nullable targets — no `?? 0`, null propagates through `DecimalScale.Apply`.
  [`SectionChargeLingotMapper.cs:23`](../../src/Kape22Importer/SectionChargeLingotMapper.cs#L23)

- 2 columns, scale 2 — the smallest annex scale value in this story.
  [`SectionChargeChutageMapper.cs:22`](../../src/Kape22Importer/SectionChargeChutageMapper.cs#L22)

- 1 column, scale 3.
  [`SectionChargeDecoupeMapper.cs:23`](../../src/Kape22Importer/SectionChargeDecoupeMapper.cs#L23)

- 1 column, scale 1.
  [`SectionChargePitsMapper.cs:23`](../../src/Kape22Importer/SectionChargePitsMapper.cs#L23)

**Removing the workaround**

- `InsertableFichier`/`InsertableReferenceFichier` return fixture bytes unmutated now that the mappers scale correctly.
  [`TestSupport.cs:112`](../../tests/Kape22Importer.Tests/TestSupport.cs#L112)

- The E2E test that used to trip the defect through the real Launcher now runs unconditionally, gated only by its pre-existing SQL Server/checkout checks.
  [`GpaoImportP60WorkerEndToEndTests.cs:41`](../../tests/Kape22Importer.Tests/GpaoImportP60WorkerEndToEndTests.cs#L41)

- 2 dangling `ZeroOutOfScaleDimensions(d)` calls removed from `MapMutatedBundle` lambdas.
  [`TransactionalPersistenceTests.cs:180`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L180)

### Review Findings

- [x] [Review][Patch] Broken anchor: the new `ref:` in sprint-status.yaml doesn't match deferred-work.md's actual heading slug (`story-4.3-bis` slugifies to `story-43-bis`, not `story-4-3-bis`) [_bmad-output/implementation-artifacts/sprint-status.yaml:96]
- [x] [Review][Patch] This spec's own "Suggested Review Order" section says the sprint-status flag "flipped to `in-progress`", but the diff actually sets it to `review` [_bmad-output/implementation-artifacts/spec-4-3-bis-correctif-mise-a-l-echelle-decimale.md:166]
- [x] [Review][Patch] The 4 new deferred-work.md entries this story's review produced have no sprint-status.yaml `action_items`, unlike the established retro/review-finding convention (e.g. `epic-4-retro-item-2..5`) [_bmad-output/implementation-artifacts/deferred-work.md:74-117]
- [x] [Review][Patch] "Resolved by: story-4.3-bis" entry doesn't follow deferred-work.md's own documented `source_spec:`/`summary:`/`evidence:` template — freeform prose instead [_bmad-output/implementation-artifacts/deferred-work.md:96-106]
- [x] [Review][Patch] The now-superseded "Deferred from: story-4.6" / "code review of story-4.6" decimal-scale entries below the new "Resolved by" note aren't marked resolved — a top-to-bottom reader could still treat them as open [_bmad-output/implementation-artifacts/deferred-work.md:55,151]
- [x] [Review][Patch] `DecimalScaleTests.MappedFichier` dereferences `Map(...).Value!` without asserting `Success` first — an unsuccessful map yields an opaque `NullReferenceException` instead of a clear assertion failure [tests/Kape22Importer.Tests/DecimalScaleTests.cs:75]
- [x] [Review][Patch] No test exercises `DecimalScale.Pow10ForScale`'s out-of-range throw path (scale outside 0-3) [src/Kape22Importer/DecimalScale.cs:17-19]
- [x] [Review][Defer] `DecimalScale.Apply` fixes decimal-point placement but never validates the scaled result against each column's total `DECIMAL(p,s)` precision — an outlier raw magnitude would still overflow at persist time [src/Kape22Importer/DecimalScale.cs:12] — deferred, pre-existing (out of this story's stated boundary: "no design choice beyond DecimalScale's shape")

**Tests (TDD proof, CC-1)**

- Literal-value arithmetic coverage for `DecimalScale.Apply` itself — closes the tautology the mapper-level assertions alone would leave (matrix audit finding).
  [`DecimalScaleTests.cs:18`](../../tests/Kape22Importer.Tests/DecimalScaleTests.cs#L18)

- Scale 2/3 non-null cases, sourced from real `P60/` fixture raw values per epics.md's own AC wording — added during review.
  [`DecimalScaleTests.cs:49`](../../tests/Kape22Importer.Tests/DecimalScaleTests.cs#L49)

- The 11 scaled `OrdreFabricationMapper` columns, converted to a `[Theory]` during review for per-column failure isolation.
  [`OrdreFabricationMapperTests.cs:64`](../../tests/Kape22Importer.Tests/OrdreFabricationMapperTests.cs#L64)

**Peripherals**

- Story 4.6/4.3-bis defect entries closed out; 4 new defer entries from the review layers (scale-literal triplication, no anti-bypass guard, production-parity scope gap, script exit-code gap).
  [`deferred-work.md:96`](deferred-work.md#L96)

- `story-4-6-decimal-scale-defect-story-4-3-bis` action item flipped to `done`; `4-3-bis-correctif-mise-a-l-echelle-decimale` flipped to `review`.
  [`sprint-status.yaml:80`](sprint-status.yaml#L80)
