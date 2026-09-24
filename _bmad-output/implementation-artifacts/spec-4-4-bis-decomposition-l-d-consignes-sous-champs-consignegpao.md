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
- Decoupe (XP1): the size-18 block (`TypeConsigne` 24, 25–29, `SizeCodeConsigne=18`) is emitted only when its own raw code (`GetConsignes(..., 24)`) is non-empty, independently of the size-12 block (13, 16, 17, `SizeCodeConsigne=12`). **Renegotiated 2026-09-23 (human-approved, step-04 review):** the real P60 wire format (`Templates/P60.xml`) declares no second, independent source field for this block — verified directly, only one `CodeConsigneDecoupe` Champ (`Size="12"`) exists, contiguous with the next Champ. The implementation reuses that same field, gated on it carrying ≥13 characters (satisfies every size-18 offset), as the least-inventive reading still faithful to "own raw code, independently of the size-12 block." This is unreachable via any real Fichier (confirmed: the field is always ≤12 characters once padded) and is tracked as `assumed, unverified` in `deferred-work.md`. **Renegotiated again 2026-09-23 (human-approved, code review D-1 — supersedes the note above):** the independent size-18 source does exist — `LibelleConsigneDecoupe` (`Templates/P60.xml` Position 287, Size 18). Legacy turns the second consigne it adds to the Decoupe section while loading a KAPE22 into `TypeConsigne=24`/`SizeCodeConsigne=18` (`OrdreFabrication.cs:627-634`), and every Decoupe record in the real `P60/` fixtures (324 records across 350 files) carries a structured code there (`.LLLLL BC X.XM`), never free text like the other sections' Libelle Champs. With it, OF `2039771` yields 30 `ConsigneGPAO=1` rows, matching production. The ≥13-character gate is dropped: each block is emitted iff its own code is non-empty, independently of the other.
- Blank raw code (6 decoded sections): an applicable section whose own raw code is blank produces no row at all. **Renegotiated 2026-09-23 (human-approved, code review D-2):** the Intent and the I/O matrix only covered the non-empty case; this follows legacy's own `if (!string.IsNullOrEmpty(_global))` guard around the full code and every sub-field (`OrdreDeFabricationManager.cs:1457/1493/1524/1563/1618/1646`).
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
| Decoupe (XP1), only size-12 code present | `CodeConsigneDecoupe` set, `LibelleConsigneDecoupe` empty (renegotiated 2026-09-23, D-1) | rows 13/16/17 only, `SizeCodeConsigne=12` | N/A |
| Decoupe (XP1), both codes present | `CodeConsigneDecoupe` and `LibelleConsigneDecoupe` set (renegotiated 2026-09-23, D-1) | rows 13/16/17 (size 12) + 24/25-29 (size 18) | N/A |
| SVT | applicable | unchanged single row, `ConsigneGPAO=1`, `TypeConsigne` unset (à_clarifier) | N/A |
| Section not applicable | section entity null | no rows for that section (unchanged) | N/A |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/ConsignesMapper.cs:21-107` (pre-change: `:15-89`, `Map`/`Build`) — rewrite to emit the rows below instead of one row per section.
- `Desktop/kape22/OrdreDeFabricationManager.cs` — read-only legacy reference for every offset/constant below (never call/reference at runtime, AD-3):

| Section | Full code (size) | Sub-fields: TypeConsigne = Substring(start,len) | Legacy lines |
|---|---|---|---|
| Chutage (XC1) | 13 (12) | 0=Sub(0,2), 1=Sub(3,1), 2=Sub(5,1).Trim, 3=Sub(7,2).Trim, 4=Sub(10,1).Trim | 1520-1547 |
| Lingot (LA1) | 13 (12) | 15=Sub(0,3), 7=Sub(4,1), 8=Sub(6,3), 9=Sub(10,2) | 1488-1514 |
| Pits (PC1) | 13 (12) | 12=Sub(0,1), 10=Sub(2,3), 11=Sub(2,3), 5=Sub(6,2).Trim, 6=Sub(9,3).Trim | 1452-1482 |
| Decoupe/size12 (XP1a) | 13 (12) | 16=Sub(0,5).Trim, 17=Sub(6,5).Trim | 1557-1573 |
| Decoupe/size18 (XP1b) | 24 (18), from `LibelleConsigneDecoupe`, only if non-empty | 25=Sub(0,1).Trim, 26=Sub(1,5).Trim, 27=Sub(7,2).Trim, 28=Sub(9,4).Trim, 29=Sub(11,1).Trim | 1577-1599 |
| PoidsMetrique (XP9) | 13 (12) | 18=Sub(0,4).Trim, 19=Sub(5,2).Trim, 20=Sub(8,2).Trim | 1612-1636 |
| Refroidissoir (XA1) | 13 (12) | 21=Sub(0,2).Trim, 22=Sub(3,1).Trim, 23=Sub(5,3).Trim | 1642-1664 |
| SVT | unchanged (current mapper output) | none — legacy has no decode (dead comment only, lines 1668-1669) | n/a |

- `src/Kape22Importer/Persistence/L_D_CONSIGNES.cs` — target entity, 7 columns, key `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)`.
- `tests/Kape22Importer.Tests/ConsignesMapperTests.cs` (pre-change `:141-155,190-222`) — existing `Assert.Single`-per-section tests and the CLR-default test must be restructured for multi-row output.
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

### Review Findings

- [x] [Review][Patch] (from Decision D-1, resolved 2026-09-23: option 1, source found) Source the XP1 size-18 block from `LibelleConsigneDecoupe` (Position 287, Size 18), gated on it being non-empty, independently of the size-12 code; drop the >=13-char gate; E2E count 30; annex + deferred-work resolution entry; renegotiation note in Boundaries [src/Kape22Importer/ConsignesMapper.cs:145]
- [x] [Review][Decision] (resolved -> Patch above) XP1 size-18 block: 6 production rows still missing (24 produced vs 30 ConsigneGPAO=1 in production) — the proposal's own arithmetic (60 = 30 x 2) only balances with XP1 types 24-29 included, so production does carry a size-18 code for OF 2039771; the >=13-char gate on CodeConsigneDecoupe (ConsignesMapper.cs:158-172) is unreachable and not the real source. Frozen I/O matrix rows (spec:41-42) and row-13 SizeCodeConsigne=12 on >12-char input also hinge on this.
- [x] [Review][Patch] (from Decision D-2, resolved 2026-09-23: option 1) Add a dated renegotiation note to the frozen block covering the blank-code skip, citing the legacy guard; no code change [spec-4-4-bis:19]
- [x] [Review][Decision] (resolved -> Patch above) Blank raw code now yields zero rows for the 6 decoded sections (was one full-code row) with no dated renegotiation note in the frozen block — legacy guard `if (!string.IsNullOrEmpty(_global))` supports the behaviour (OrdreDeFabricationManager.cs:1457/1493/1524/1563/1618/1646).
- [x] [Review][Decision] (resolved 2026-09-23: accepted as exception, to be recorded in the /commit-review commit body — "CC-1 exception: blank-skip and PadForSlicing tests added post-implementation during review") CC-1 deviation self-admitted — blank-skip, PadForSlicing and Decoupe 12/13 threshold tests added after the code ("code-review patch", ConsignesMapperTests.cs:43,173,288,313).
- [x] [Review][Patch] (from Decision D-4, resolved 2026-09-23: option 1) Split SVT into its own `à_clarifier` rows for TypeConsigne/SizeCodeConsigne, move the per-section Code Map table into the annex, cite `LibelleConsigneDecoupe` as the XP1 size-18 source [_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md:210]
- [x] [Review][Decision] (resolved -> Patch above) Annex marks TypeConsigne/SizeCodeConsigne `règle` while SVT stays `à_clarifier` inside the cell text — the status column hides a partial à_clarifier from the annex/deferred-work cross-check (annexe-mapping-dispatch-epic4.md:210-211).
- [x] [Review][Patch] Parity test is one-directional and can pass vacuously — add reverse check (production ConsigneGPAO=1 rows of mapped CodeOperations with no mapped counterpart) and assert testRows.Count == consignes.Count [tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:307]
- [x] [Review][Patch] Restore the "assumed, unverified" + deferred-work.md marker on LibelleConsigne and on the SVT row (AC-FR19-4 pattern) [src/Kape22Importer/ConsignesMapper.cs:188]
- [x] [Review][Patch] E2E test only counts rows — assert ConsigneGPAO=true and the (CodeOperation, TypeConsigne, SizeCodeConsigne) set on persisted rows [tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs:231]
- [x] [Review][Patch] Unit tests derive expected values via padded.Substring (self-agreeing with the mapper) — use literal expected values; deferred-work claim "pinned to human-reviewed values" is currently false [tests/Kape22Importer.Tests/ConsignesMapperTests.cs:35]
- [x] [Review][Patch] CC-3 preservation — restore the original comments above the unchanged PoidsMetrique/SVT not-applicable tests [tests/Kape22Importer.Tests/ConsignesMapperTests.cs:134]
- [x] [Review][Patch] Persister A-5 comment cites removed ConsignesMapper.Build, and has a broken wrap [src/Kape22Importer/Persistence/Kape22Persister.cs:125]
- [x] [Review][Patch] Class header states the blank-code rule for every section; SVT still emits a blank-code row (spec: unchanged) — scope the sentence to the 6 decoded sections [src/Kape22Importer/ConsignesMapper.cs:15]
- [x] [Review][Patch] AC-FR19-4 test comment says only "one column" stays à_clarifier (Decoupe size-18 is not a column; LibelleConsigne and SVT TypeConsigne/SizeCodeConsigne are) [tests/Kape22Importer.Tests/ConsignesMapperTests.cs:392]
- [x] [Review][Patch] deferred-work SVT collision entry points at "another undecoded section" — real overlap is Chutage's TypeConsigne=0 sub-field row; append a correcting entry (append-only) [_bmad-output/implementation-artifacts/deferred-work.md:1132]
- [x] [Review][Patch] epic-4-context says "each of the 7 sections decodes" and describes an independent optional Decoupe code — align with the spec (6 sections, renegotiated XP1) [_bmad-output/implementation-artifacts/epic-4-context.md:74]
- [x] [Review][Patch] Suggested Review Order / Code Map line numbers are stale (ConsignesMapperTests.cs:410/507/581 do not exist) [spec-4-4-bis:98]
- [x] [Review][Defer] Rows imported by the pre-4.4-bis mapper (ConsigneGPAO=false, TypeConsigne=0) indistinguishable from OF-initial rows, no remediation note — deferred, pre-existing
- [x] [Review][Defer] L_D_COULEE column-by-column parity gap has no tracker entry [_bmad-output/implementation-artifacts/epic-4-context.md:91] — deferred, pre-existing

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
  [`ConsignesMapper.cs:115`](../../src/Kape22Importer/ConsignesMapper.cs#L115)

- Decoupe's two independent blocks: size-12 from `CodeConsigneDecoupe`, size-18 from `LibelleConsigneDecoupe`.
  [`ConsignesMapper.cs:144`](../../src/Kape22Importer/ConsignesMapper.cs#L144)

- SVT left unchanged (no legacy decode rule exists) except `ConsigneGPAO`.
  [`ConsignesMapper.cs:96`](../../src/Kape22Importer/ConsignesMapper.cs#L96)

- Right-pad before slicing, compensating for XML normalization trimming trailing spaces.
  [`ConsignesMapper.cs:174`](../../src/Kape22Importer/ConsignesMapper.cs#L174)

- Every row now carries `ConsigneGPAO=true` and the section's real `TypeConsigne`/`SizeCodeConsigne`.
  [`ConsignesMapper.cs:176`](../../src/Kape22Importer/ConsignesMapper.cs#L176)

**Ripple effects on existing invariants**

- A-5 collision-check comment updated: `TypeConsigne`/`ConsigneGPAO` no longer constant, collision still reachable via each section's own full-code row.
  [`Kape22Persister.cs:118`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L118)

**Documentation of the decoded rule**

- `TypeConsigne` reclassified à_clarifier → règle, per-section offsets cited.
  [`annexe-mapping-dispatch-epic4.md:211`](../../_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md#L211)

- `ConsigneGPAO` reworded from legacy "miroir" framing to the confirmed initial-vs-adjusted semantics.
  [`annexe-mapping-dispatch-epic4.md:207`](../../_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md#L207)

**Tests — decomposition coverage**

- Comprehensive test pinning the exact row set across all 6 decodable sections at once.
  [`ConsignesMapperTests.cs:338`](../../tests/Kape22Importer.Tests/ConsignesMapperTests.cs#L338)

- Decoupe's size-12/size-18 blocks: reference-Fichier literals, each block alone, full-width size-18 offsets.
  [`ConsignesMapperTests.cs:238`](../../tests/Kape22Importer.Tests/ConsignesMapperTests.cs#L238)

- Blank-code-but-applicable-section case, locking in the new skip behavior.
  [`ConsignesMapperTests.cs:175`](../../tests/Kape22Importer.Tests/ConsignesMapperTests.cs#L175)

**Tests — production-parity closure (AC-4)**

- New end-to-end column-by-column comparison against real production `L_D_CONSIGNES` rows.
  [`Kape22ProductionDataParityTests.cs:269`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L269)

- Multi-row-per-OF round-trip helper, scoped to just-inserted `CodeOperation`s.
  [`Kape22ProductionDataParityTests.cs:363`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L363)

**Peripherals**

- Pre-existing E2E row-count assertion updated for the new per-section row counts.
  [`Kape22FichierProcessorTests.cs:225`](../../tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs#L225)

- Ledger entries closing the prior collision-risk note and tracking the Decoupe size-18 assumption.
  [`deferred-work.md:1129`](../../_bmad-output/implementation-artifacts/deferred-work.md#L1129)
