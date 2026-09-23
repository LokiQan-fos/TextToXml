---
title: '4.4-bis — Decompose L_D_CONSIGNES into per-section sub-fields + fix ConsigneGPAO'
type: 'bugfix'
created: '2026-09-23'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '547bf2acd673ff94e75244c6de6129c0a014b9d8'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `ConsignesMapper` emits only one "full code" row per applicable section (`TypeConsigne` left at CLR default `0`, `ConsigneGPAO=false`), while legacy (`OrdreDeFabricationManager.CompleteConsignes2`) also decodes each section's raw consigne code into several positional sub-fields, each its own row, all tagged `ConsigneGPAO=1` (confirmed by the business owner: `0`=OF-initial value, out of this mapper's scope; `1`=P60-dispatch value). Real data shows 5 rows produced vs. 60 in production for one OF — undetected because `Kape22ProductionDataParityTests` never covered `L_D_CONSIGNES`.

**Approach:** For each of the 6 sections with a known legacy decode rule (Chutage, Lingot, Pits, Decoupe, PoidsMetrique, Refroidissoir — see Code Map), emit one `TypeConsigne=13` full-code row plus one row per `Substring`-decoded sub-field at the exact legacy offset, `SizeCodeConsigne` set per rule, all `ConsigneGPAO=true`. SVT keeps its current single row (no legacy decode rule exists) with only `ConsigneGPAO` corrected to `true`. Update the mapping annex and extend production-parity coverage to `L_D_CONSIGNES`.

## Boundaries & Constraints

**Always:**
- Every row `ConsignesMapper` produces has `ConsigneGPAO = true` (was `false`).
- Offsets/`TypeConsigne` constants come only from the Code Map table below (sourced directly from `OrdreDeFabricationManager.cs`, read-only legacy reference — never referenced as an assembly or called at runtime, AD-3).
- Pits (PC1): both `TypeConsigne=10` and `TypeConsigne=11` rows are emitted from the identical `Substring(2,3)` slice (legacy's `exceptTypeConsigne` exclusion has no equivalent in P60 dispatch, so nothing is excluded).
- Decoupe (XP1): the size-18 block (`TypeConsigne` 24, 25–29, `SizeCodeConsigne=18`) is emitted only when its own raw code (`GetConsignes(..., 24)`) is non-empty, independently of the size-12 block (13, 16, 17, `SizeCodeConsigne=12`). **Renegotiated 2026-09-23 (human-approved, step-04 review):** the real P60 wire format (`Templates/P60.xml`) declares no second, independent source field for this block — verified directly, only one `CodeConsigneDecoupe` Champ (`Size="12"`) exists, contiguous with the next Champ. The implementation reuses that same field, gated on it carrying ≥13 characters (satisfies every size-18 offset), as the least-inventive reading still faithful to "own raw code, independently of the size-12 block." This is unreachable via any real Fichier (confirmed: the field is always ≤12 characters once padded) and is tracked as `assumed, unverified` in `deferred-work.md`.
- SVT: unchanged row shape (`TypeConsigne` stays at CLR default), only `ConsigneGPAO` fixed to `true` — do not invent a decomposition rule.
- `LibelleConsigne` stays untouched (`null`, `à_clarifier`) — out of scope.
- CC-1 (TDD, tests first), CC-2/CC-3 (English, non-trailing comments), CC-4 (alphabetical), CC-5 (glossary vocabulary).
- `deferred-work.md` is append-only: link the existing "story-4.4 mapper implementation" entry to this story's resolution; never edit its text.

**Ask First:** none — the one open business question (`ConsigneGPAO` semantics) was already resolved by the process owner on 2026-09-23 (see sprint-change-proposal).

**Never:** invent a decomposition rule for SVT; add a cross-mapper existence check for the out-of-scope `ConsigneGPAO=0` rows; touch `LibelleConsigne`; modify/reference the legacy assembly at runtime.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Standard section (Chutage/Lingot/PoidsMetrique/Refroidissoir) | section applicable, raw code non-empty | 1 full-code row (`TypeConsigne=13`) + N sub-field rows per Code Map offsets, all `ConsigneGPAO=1` | N/A |
| Pits (PC1) | applicable | as above + both `TypeConsigne=10` and `=11` rows (same slice) | N/A |
| Decoupe (XP1), only size-12 code present | code13 set, code24 empty | rows 13/16/17 only, `SizeCodeConsigne=12` | N/A |
| Decoupe (XP1), both codes present | code13 and code24 set | rows 13/16/17 (size 12) + 24/25-29 (size 18) | N/A |
| SVT | applicable | unchanged single row, `ConsigneGPAO=1`, `TypeConsigne` unset (à_clarifier) | N/A |
| Section not applicable | section entity null | no rows for that section (unchanged) | N/A |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/ConsignesMapper.cs:15-89` — `Map`/`Build`; rewrite to emit the rows below instead of one row per section.
- `Desktop/kape22/OrdreDeFabricationManager.cs` — read-only legacy reference for every offset/constant below (never call/reference at runtime, AD-3):

| Section | Full code (size) | Sub-fields: TypeConsigne = Substring(start,len) | Legacy lines |
|---|---|---|---|
| Chutage (XC1) | 13 (12) | 0=Sub(0,2), 1=Sub(3,1), 2=Sub(5,1).Trim, 3=Sub(7,2).Trim, 4=Sub(10,1).Trim | 1520-1547 |
| Lingot (LA1) | 13 (12) | 15=Sub(0,3), 7=Sub(4,1), 8=Sub(6,3), 9=Sub(10,2) | 1488-1514 |
| Pits (PC1) | 13 (12) | 12=Sub(0,1), 10=Sub(2,3), 11=Sub(2,3), 5=Sub(6,2).Trim, 6=Sub(9,3).Trim | 1452-1482 |
| Decoupe/size12 (XP1a) | 13 (12) | 16=Sub(0,5).Trim, 17=Sub(6,5).Trim | 1557-1573 |
| Decoupe/size18 (XP1b) | 24 (18), only if non-empty | 25=Sub(0,1).Trim, 26=Sub(1,5).Trim, 27=Sub(7,2).Trim, 28=Sub(9,4).Trim, 29=Sub(11,1).Trim | 1577-1599 |
| PoidsMetrique (XP9) | 13 (12) | 18=Sub(0,4).Trim, 19=Sub(5,2).Trim, 20=Sub(8,2).Trim | 1612-1636 |
| Refroidissoir (XA1) | 13 (12) | 21=Sub(0,2).Trim, 22=Sub(3,1).Trim, 23=Sub(5,3).Trim | 1642-1664 |
| SVT | unchanged (current mapper output) | none — legacy has no decode (dead comment only, lines 1668-1669) | n/a |

- `src/Kape22Importer/Persistence/L_D_CONSIGNES.cs` — target entity, 7 columns, key `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)`.
- `tests/Kape22Importer.Tests/ConsignesMapperTests.cs:141-155,190-222` — existing `Assert.Single`-per-section tests and the CLR-default test must be restructured for multi-row output.
- `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md:189-208` — `L_D_CONSIGNES` section to update (`TypeConsigne`/`SizeCodeConsigne`: à_clarifier→règle; `ConsigneGPAO`: drop "miroir" wording).
- `_bmad-output/implementation-artifacts/deferred-work.md:1019-1027` — entry to link (not edit) to this story.
- `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs` — `DownstreamOf.Pad` (src/Kape22Importer/DownstreamOf.cs:11), `CompareSectionCharge<TEntity>` (264-287) is the closest existing per-table comparison pattern to adapt for a multi-row-per-OF table.
- `tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs` — `AnnexeMappingDispatchEpic4_MatchesStory41EfModel_AcFr17_5` cross-checks annex status against `deferred-work.md`; must stay green after the annex wording change.

## Tasks & Acceptance

**Execution:**
- [x] `src/Kape22Importer/ConsignesMapper.cs` -- rewrite per-section row construction per the Code Map table, `ConsigneGPAO=true` everywhere, `SizeCodeConsigne` set (12/18) -- closes AC-FR19-3 parity gap
- [x] `tests/Kape22Importer.Tests/ConsignesMapperTests.cs` -- TDD first: restructure per-section tests to assert `TypeConsigne`-keyed row subsets; add Decoupe both-codes/one-code cases; replace the CLR-default test with one asserting `ConsigneGPAO=true`/`SizeCodeConsigne` set/`LibelleConsigne` still null
- [x] `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md` -- update `L_D_CONSIGNES` §: TypeConsigne/SizeCodeConsigne to `règle` citing per-section offsets; reword ConsigneGPAO row to initial-vs-adjusted semantics
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- append a linking note (append-only) pointing the story-4.4 TypeConsigne/collision entry at this story's resolution
- [x] `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs` -- add an `L_D_CONSIGNES` parity test (ConsigneGPAO=1 rows only) across the 100 `P60/` fixtures, by `DownstreamOf.Pad(OF)`
- [x] `tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs` -- adjust only if the annex wording change breaks the existing à_clarifier/deferred-work.md cross-check (no adjustment needed -- gate stayed green as-is)

**Acceptance Criteria:**
- Given a real OF with all 6 decodable sections applicable, when `ConsignesMapper.Map` runs, then it produces exactly the row set in the Code Map table, every row `ConsigneGPAO=true`
- Given the Decoupe section with only its size-12 code populated, when mapped, then no `TypeConsigne` 24/25-29 rows are produced
- Given `MappingAnnexCompletenessTests` and `AcCoverageCompletenessTests`, when run after this change, then both stay green
- Given OF `2039771` (`P60_847_682_001`), when imported, then the produced `ConsigneGPAO=1` rows match production column-by-column via the new parity test

## Design Notes

`AddOrModifyConsigne`'s `exceptTypeConsigne` parameter (legacy-only, gates PC1 types 10/11) has no caller-supplied value anywhere in the P60/`CompleteConsignes2` dispatch path — treated as always-null here, so both rows are always emitted; this is a direct reading of the legacy default, not an invented rule.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- 0 warnings/errors
- `dotnet test TextToXml.sln --filter Category=Unit` -- 100% green, including new/restructured `ConsignesMapperTests`
- `dotnet test TextToXml.sln --filter Category=Integration` -- 100% green, including the new `L_D_CONSIGNES` parity test

## Suggested Review Order

**Decomposition logic (the core change)**

- Entry point: one row per section produced today becomes full-code + N decoded sub-fields, per section.
  [`ConsignesMapper.cs:20`](../../src/Kape22Importer/ConsignesMapper.cs#L20)

- Shared shape for the 5 uniform sections: skip on blank code, else full-code row plus one row per offset.
  [`ConsignesMapper.cs:114`](../../src/Kape22Importer/ConsignesMapper.cs#L114)

- Decoupe's two-tier (size-12/size-18) block, including the reused-field fallback for the unreachable size-18 case.
  [`ConsignesMapper.cs:145`](../../src/Kape22Importer/ConsignesMapper.cs#L145)

- SVT left unchanged (no legacy decode rule exists) except `ConsigneGPAO`.
  [`ConsignesMapper.cs:95`](../../src/Kape22Importer/ConsignesMapper.cs#L95)

- Right-pad before slicing, compensating for XML normalization trimming trailing spaces.
  [`ConsignesMapper.cs:177`](../../src/Kape22Importer/ConsignesMapper.cs#L177)

- Every row now carries `ConsigneGPAO=true` and the section's real `TypeConsigne`/`SizeCodeConsigne`.
  [`ConsignesMapper.cs:179`](../../src/Kape22Importer/ConsignesMapper.cs#L179)

**Ripple effects on existing invariants**

- A-5 collision-check comment updated: `TypeConsigne`/`ConsigneGPAO` no longer constant, collision still reachable via each section's own full-code row.
  [`Kape22Persister.cs:125`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L125)

**Documentation of the decoded rule**

- `TypeConsigne` reclassified à_clarifier → règle, per-section offsets cited.
  [`annexe-mapping-dispatch-epic4.md:211`](../../_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md#L211)

- `ConsigneGPAO` reworded from legacy "miroir" framing to the confirmed initial-vs-adjusted semantics.
  [`annexe-mapping-dispatch-epic4.md:207`](../../_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md#L207)

**Tests — decomposition coverage**

- Comprehensive test pinning the exact row set across all 6 decodable sections at once.
  [`ConsignesMapperTests.cs:581`](../../tests/Kape22Importer.Tests/ConsignesMapperTests.cs#L581)

- Decoupe's size-12/size-18 boundary, including the exact 12/13-character threshold.
  [`ConsignesMapperTests.cs:507`](../../tests/Kape22Importer.Tests/ConsignesMapperTests.cs#L507)

- Blank-code-but-applicable-section case, locking in the new skip behavior.
  [`ConsignesMapperTests.cs:410`](../../tests/Kape22Importer.Tests/ConsignesMapperTests.cs#L410)

**Tests — production-parity closure (AC-4)**

- New end-to-end column-by-column comparison against real production `L_D_CONSIGNES` rows.
  [`Kape22ProductionDataParityTests.cs:269`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L269)

- Multi-row-per-OF round-trip helper, scoped to just-inserted `CodeOperation`s.
  [`Kape22ProductionDataParityTests.cs:346`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L346)

**Peripherals**

- Pre-existing E2E row-count assertion updated for the new per-section row counts.
  [`Kape22FichierProcessorTests.cs:225`](../../tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs#L225)

- Ledger entries closing the prior collision-risk note and tracking the Decoupe size-18 assumption.
  [`deferred-work.md:1129`](../../_bmad-output/implementation-artifacts/deferred-work.md#L1129)
