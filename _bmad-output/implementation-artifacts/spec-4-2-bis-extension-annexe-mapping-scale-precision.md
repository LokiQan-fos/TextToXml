---
title: 'Story 4.2-bis — Extend the mapping annex with a decimal scale field'
type: 'feature'
created: '2026-09-17'
status: 'done'
review_loop_iteration: 1
context: []
baseline_commit: '98c0c85a633771ceb3b72ce8bed64376ff5c58d2'
---

<!-- Renégocié 2026-09-17 (implémentation, CC-1) : la règle de complétude a mécaniquement détecté un
     21e cas réel du même défaut — L_D_ORDRE_FABRICATION.Epaisseur (DECIMAL(4,1) NOT NULL, sourcée de
     KAPE22.Epaisseur int?). La liste énumérée ci-dessous est étendue de 20 à 21 pour l'inclure.
     AC-FR17-5 d'epics.md anticipait explicitement ce cas ("sans distinction entre une colonne déjà
     identifiée par la rétro et une colonne nouvellement détectée par le test"). -->

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Retrospective root-cause across Stories 4.3/4.4: 5 mappers (`OrdreFabricationMapper`, `SectionChargeLingotMapper`, `SectionChargeChutageMapper`, `SectionChargeDecoupeMapper`, `SectionChargePitsMapper`) write KAPE22 `int`/`int?` values straight into narrow `DECIMAL` columns with no scale conversion — and nothing in the mapping annex or its completeness test would catch a 6th mapper repeating the mistake.

**Approach:** Add a `Scale` field to the annex's per-column entry format and its parser/model, extend `MappingAnnexCompleteness.Check` with a rule that fails whenever a `sourcée` row targets a `decimal`/`decimal?` EF column whose KAPE22 source field is `int`/`int?` and has no `Scale`, then populate `Scale` for the 20 already-known offending columns across the 5 tables. Test-only and doc-only: no mapper code changes (Story 4.3-bis owns that).

## Boundaries & Constraints

**Always:**
- Add `Scale` (nullable `int`, decimal-place count, e.g. `1` for `DECIMAL(2,1)`) as a 4th pipe-delimited column, applied uniformly across all 10 tables' markdown blocks in `annexe-mapping-dispatch-epic4.md` — one row shape annex-wide; non-applicable rows get `-`.
- Populate real `Scale` values for the 21 known offending columns: `L_D_ORDRE_FABRICATION` (`DiametreProduit`, `Epaisseur`, `LongueurCD`, `PoidsDemiProduitUnitaire`, `PoidsPrevuDemiProduit`, `ToleranceMaxEpaisseur`, `ToleranceMaxLongueur`, `ToleranceMaxSection`, `ToleranceMinEpaisseur`, `ToleranceMinLongueur`, `ToleranceMinSection`), `L_D_SECTIONCHARGE_LINGOT` (`SectionLaminage`, `EpaisseurEnLaminage`, `ToleranceMaxEpaisseur`, `ToleranceMaxSection`, `ToleranceMinEpaisseur`, `ToleranceMinSection`), `L_D_SECTIONCHARGE_CHUTAGE` (`ChutageTete`, `ChutagePied`), `L_D_SECTIONCHARGE_DECOUPE` (`LongueurMoyenne`), `L_D_SECTIONCHARGE_PITS` (`H2Coulee`) — derive each value from the target column's real `DECIMAL(p,s)` scale on the EF entity/`sys.columns`, never guessed. (epics.md's "12 colonnes" counts list entries, not physical columns — the `6×`/`4×` Tolerance groups fold to 20 physical columns from the retro's known list, plus `Epaisseur` mechanically detected during implementation as a genuine 21st occurrence, per the renegotiation note above.)
- Extend `MappingAnnexEntry`, `MappingAnnex.Parse`'s row regex, and `MappingAnnexCompleteness.Check`'s signature to thread target-CLR-type and KAPE22-source-CLR-type through (reflect `L_D_KAPE22` for source field types); update every existing call site.
- New rule fires only when: target CLR type is `decimal`/`decimal?` AND `Statut == sourcée` AND the referenced `KAPE22.<Field>` is `int`/`int?`. A `decimal` column sourced from an already-`decimal` KAPE22 field is exempt.
- Add one explicit line per table noting `L_D_SECTIONCHARGE_REFROIDISSOIRS`/`POIDSMETRIQUE`/`SVT` carry no `decimal` column and are out of scope.

**Ask First:** None — format is fixed by the AC (integer `Scale`, no unit/multiplier text).

**Never:**
- No mapper code changes (`OrdreFabricationMapper`, etc.) — Story 4.3-bis.
- No renumbering/merging of the existing 3 `Statut` values.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| `decimal`-target, `int`-sourced, `Scale` present | annex row with `Scale=1` | `Check` passes | N/A |
| `decimal`-target, `int`-sourced, `Scale` missing | annex row with `Scale=-`/empty | `Check` fails, naming table+column | assertion message names the failing entry |
| `decimal`-target, already-`decimal`-sourced | `Scale=-`, KAPE22 source field is `decimal` | `Check` passes (rule doesn't apply) | N/A |
| Real annex end-to-end | full `annexe-mapping-dispatch-epic4.md` + 10 EF entities | 0 failures once the 21 columns are populated | N/A |

</frozen-after-approval>

## Code Map

- `tests/Kape22Importer.Tests/MappingAnnexSchema.cs:12` -- `MappingAnnexEntry` record (4 positional props today, alphabetical per CC-4) -- add `Scale`.
- `tests/Kape22Importer.Tests/MappingAnnexSchema.cs:35-37,84` -- `Row` regex hard-coded to exactly 3 pipe cells; construction call -- widen to 4 cells.
- `tests/Kape22Importer.Tests/MappingAnnexSchema.cs:94-134` -- `MappingAnnexCompleteness.Check`, 2 existing loops (orphan/invalid-status/`à_clarifier` citation; missing-annex-entry) -- add a 3rd rule branch; needs target-type + KAPE22-source-type info, not just column names.
- `tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs:140-170` -- `ModelColumnsByTable()` returns column names only today -- extend to carry CLR type.
- `tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs:25-138` -- 7 existing `[Fact]`s, each an inline synthetic fixture calling `Check` (no shared helper) -- update all 7 call sites for the new signature; add 2 new facts (`Scale` present/missing) in the same inline pattern; the real end-to-end fact at 123-138 is the actual gate (currently 0 `Scale` values in the real file, so it will fail until the annex is populated).
- `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md` -- 10 `### TableName` blocks (lines 35,87,178,198,211,223,256,273,283,311), 3-cell rows today -- widen every row to 4 cells; populate `Scale` for the 21 columns above, `-` elsewhere; add the no-decimal notes.
- `src/Kape22Importer/Persistence/L_D_KAPE22.cs` -- source KAPE22 field CLR types (all `int?` for the 21 offending fields) -- reflect for the new rule, do not modify.
- `src/Kape22Importer/Persistence/L_D_ORDRE_FABRICATION.cs`, `L_D_SECTIONCHARGE_LINGOT.cs`, `_CHUTAGE.cs`, `_DECOUPE.cs`, `_PITS.cs` -- read-only reference for each column's real `DECIMAL(p,s)` scale.

## Tasks & Acceptance

**Execution:**
- [x] `MappingAnnexSchema.cs` -- widen `Row` regex to 4 cells, add `Scale` to `MappingAnnexEntry`, update parser construction -- annex rows can carry a scale value.
- [x] `MappingAnnexSchema.cs` -- extend `Check` with the decimal-from-int-without-scale rule, threading CLR type info through its signature -- mechanizes the AC-FR17-5 extension.
- [x] `MappingAnnexCompletenessTests.cs` -- update the 7 existing `Check` call sites for the new signature; add `DecimalFromIntWithoutScale_FailsCompleteness_AcFr17_5` + a passing counterpart -- TDD red→green (CC-1).
- [x] `annexe-mapping-dispatch-epic4.md` -- add the 4th column to all 10 table blocks; populate `Scale` for the 21 known columns; `-` elsewhere; add the no-decimal table notes -- makes the real end-to-end fact pass.

**Acceptance Criteria:**
- Given a `decimal`-target column sourced from KAPE22 `int`/`int?` with no `Scale`, when `Check` runs, then it fails naming that table and column (AC-FR17-1 extended).
- Given the real annex plus the 10-table EF model, when `AnnexeMappingDispatchEpic4_MatchesStory41EfModel_AcFr17_5` runs, then it passes with 0 failures (AC-FR17-5 extended).
- Given a `decimal`-target column not sourced from `int`/`int?`, when `Check` runs, then the new rule does not apply to it.

## Spec Change Log

- **2026-09-17, review loop 1** — Acceptance-auditor + blind-hunter findings: the frozen "Always" list and Design Notes table enumerated 20 offending columns, but the implementation's mechanized completeness rule correctly detected a 21st real occurrence (`L_D_ORDRE_FABRICATION.Epaisseur`, `DECIMAL(4,1) NOT NULL` sourced from `KAPE22.Epaisseur` `int?`), per AC-FR17-5's own "no distinction between a known and a newly-detected column" requirement. Amended: the frozen enumeration (20→21, `Epaisseur` added) and the Design Notes table (added the `Epaisseur` row). Known-bad state avoided: leaving the frozen list at 20 while the real annex and test comment already said 21 would have left the spec self-contradictory and Story 4.3-bis's checklist incomplete. KEEP: the implementation's `Scale=1` value for `Epaisseur` (already correctly derived from `scripts/schema/01-ascolsi-tables.sql:164`, never guessed) — no code change needed for this item, only the spec's own bookkeeping.

## Design Notes

Scale values below are the `s` in each column's real `DECIMAL(p,s)`, read directly from `scripts/schema/01-ascolsi-tables.sql` (the frozen database-first DDL, AR-8/AR-12 — no EF `HasPrecision` config exists in code, so the DDL is the only source of truth). This table is the fixed checklist for editing `annexe-mapping-dispatch-epic4.md`: one row here = one `Scale` value to write in the annex.

| Table | Colonne | DECIMAL(p,s) | Scale |
|---|---|---|---|
| L_D_ORDRE_FABRICATION | DiametreProduit | DECIMAL(4,1) | 1 |
| L_D_ORDRE_FABRICATION | Epaisseur | DECIMAL(4,1) | 1 |
| L_D_ORDRE_FABRICATION | ToleranceMaxSection | DECIMAL(2,1) | 1 |
| L_D_ORDRE_FABRICATION | ToleranceMinSection | DECIMAL(2,1) | 1 |
| L_D_ORDRE_FABRICATION | ToleranceMaxEpaisseur | DECIMAL(2,1) | 1 |
| L_D_ORDRE_FABRICATION | ToleranceMinEpaisseur | DECIMAL(2,1) | 1 |
| L_D_ORDRE_FABRICATION | LongueurCD | DECIMAL(5,3) | 3 |
| L_D_ORDRE_FABRICATION | ToleranceMaxLongueur | DECIMAL(4,0) | 0 |
| L_D_ORDRE_FABRICATION | ToleranceMinLongueur | DECIMAL(4,0) | 0 |
| L_D_ORDRE_FABRICATION | PoidsDemiProduitUnitaire | DECIMAL(7,3) | 3 |
| L_D_ORDRE_FABRICATION | PoidsPrevuDemiProduit | DECIMAL(6,3) | 3 |
| L_D_SECTIONCHARGE_LINGOT | SectionLaminage | DECIMAL(4,1) | 1 |
| L_D_SECTIONCHARGE_LINGOT | ToleranceMaxSection | DECIMAL(2,1) | 1 |
| L_D_SECTIONCHARGE_LINGOT | ToleranceMinSection | DECIMAL(2,1) | 1 |
| L_D_SECTIONCHARGE_LINGOT | EpaisseurEnLaminage | DECIMAL(4,1) | 1 |
| L_D_SECTIONCHARGE_LINGOT | ToleranceMaxEpaisseur | DECIMAL(2,1) | 1 |
| L_D_SECTIONCHARGE_LINGOT | ToleranceMinEpaisseur | DECIMAL(2,1) | 1 |
| L_D_SECTIONCHARGE_CHUTAGE | ChutageTete | DECIMAL(3,2) | 2 |
| L_D_SECTIONCHARGE_CHUTAGE | ChutagePied | DECIMAL(3,2) | 2 |
| L_D_SECTIONCHARGE_DECOUPE | LongueurMoyenne | DECIMAL(5,3) | 3 |
| L_D_SECTIONCHARGE_PITS | H2Coulee | DECIMAL(2,1) | 1 |

Note: `ToleranceMaxLongueur`/`ToleranceMinLongueur` have scale `0` (`DECIMAL(4,0)`) — a legitimate value, not a missing one; the completeness rule must treat `Scale=0` as present, not as absent.

## Verification

**Commands:**
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all `MappingAnnexCompletenessTests` green, including the 2 new facts and the real end-to-end fact.

## Suggested Review Order

**Completeness rule (the mechanism this story adds)**

- Entry point: the new `Scale` field and its decimal-place semantics.
  [`MappingAnnexSchema.cs:14`](../../tests/Kape22Importer.Tests/MappingAnnexSchema.cs#L14)

- The new rule: fires per `sourcée` decimal column, now fails loudly instead of skipping an unresolvable KAPE22 citation.
  [`MappingAnnexSchema.cs:170`](../../tests/Kape22Importer.Tests/MappingAnnexSchema.cs#L170)

- `SourceKape22Type`: resolves the cited `KAPE22.<Field>` type the rule above depends on.
  [`MappingAnnexSchema.cs:200`](../../tests/Kape22Importer.Tests/MappingAnnexSchema.cs#L200)

- Widened row shape (3→4 cells) and `ParseScale`: `0` is a real value, `-`/empty is absent, malformed cells fail with table/column context.
  [`MappingAnnexSchema.cs:43`](../../tests/Kape22Importer.Tests/MappingAnnexSchema.cs#L43)
  [`MappingAnnexSchema.cs:104`](../../tests/Kape22Importer.Tests/MappingAnnexSchema.cs#L104)

**Annex data (what the rule now enforces against)**

- `Scale` column semantics documented once, annex-wide.
  [`annexe-mapping-dispatch-epic4.md:13`](annexe-mapping-dispatch-epic4.md#L13)

- The 21st occurrence found mechanically during implementation, renegotiated into the frozen spec.
  [`annexe-mapping-dispatch-epic4.md:59`](annexe-mapping-dispatch-epic4.md#L59)

**Tests (TDD proof, CC-1)**

- The rule's failing branch, pinned against a synthetic fixture.
  [`MappingAnnexCompletenessTests.cs:128`](../../tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs#L128)

- The real end-to-end gate: the actual annex against the actual Story 4.1 EF model.
  [`MappingAnnexCompletenessTests.cs:201`](../../tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs#L201)

- `ModelColumnsByTable`: restored to read the real EF model (`IEntityType`) rather than plain CLR reflection.
  [`MappingAnnexCompletenessTests.cs:220`](../../tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs#L220)

- New fact exercising the widened parser directly against raw markdown (`Scale=0`, `-`, malformed).
  [`MappingAnnexCompletenessTests.cs:167`](../../tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs#L167)

**Peripherals**

- Spec's own renegotiation note (20→21 columns) and change log entry.
  [`spec-4-2-bis-extension-annexe-mapping-scale-precision.md:11`](spec-4-2-bis-extension-annexe-mapping-scale-precision.md#L11)

- Planning-doc consistency: epic context recompiled, hot/cold Coulée direction clarified.
  [`epic-4-context.md:48`](epic-4-context.md#L48)

- Speculative-hardening deferral (`IsInt` narrow type match) logged for later, not fixed now (YAGNI).
  [`deferred-work.md:922`](deferred-work.md#L922)

- Sprint tracking flip to `in-progress`.
  [`sprint-status.yaml:79`](sprint-status.yaml#L79)
