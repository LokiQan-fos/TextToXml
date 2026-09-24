---
title: '4.13 — ConsigneGPAO=0 working-copy rows + composite labels (port of legacy BuildLibelleConsigne)'
type: 'feature'
created: '2026-09-24'
status: 'done'
review_loop_iteration: 0
baseline_commit: '78921a902015c56e778629a69fd5fd7d8dd1ccbf'
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** For every consigne, production `L_D_CONSIGNES` holds two rows: `ConsigneGPAO=1` (the value received from the GPAO, never edited) and `ConsigneGPAO=0` (a working copy the import creates with the same codes, later edited by operators through MCC). The worker writes only the `1` rows. For OF `2039771` that is 30 rows where production has 60. The `0` rows of type 13, and type 24 for XP1, carry a composite label built by the legacy `BuildLibelleConsigne`.

**Approach:** `ConsignesMapper.Map` also emits one `ConsigneGPAO=0` row per `1` row, with the same key and codes and the same `GetLibelle` label. The only exception is the composite rows: their label comes from a pure, exact port of `BuildLibelleConsigne`. The mapper also gets corrected `ConsigneGPAO` wording and extended parity and E2E checks.

## Boundaries & Constraints

**Always:**
- Port `Desktop/kape22/OrdreFabrication.cs:700-1222` faithfully, with no normalization. Per section, concatenate in the legacy `__tabCodes*` order `label + "\t\t"`, including after the last element:
  - PC1: 12, then 10 and 11, each with `"°C"` before the separator; 14 is a no-op. Then append the type 5 label with no separator, then `"\n" + label6` when label 6 is non-empty.
  - LA1/LA9: 15, 7, 8, 9.
  - XC1: 0, 1, 2, 3, 4.
  - XP1: 16, 17 go on the type 13 row; 24, 25, 26, 27, 28 go on the type 24 row. Type 29 is excluded.
  - XP9: 18, 19, 20.
  - XA1/XA2: 21, 22, 23.
- A type the section did not produce contributes nothing, not even its separator. This is the legacy `Find` null → caught NRE.
- Sub-labels are the `GetLibelle` values that were resolved before any composite was written. `?` is copied as is. For example, the XP1 type 24 composite starts with `?\t\t`.
- The `ConsigneGPAO=1` rows stay exactly as they are today, including `?` on types 13 and 24.
- `ConsignesMapper` stays pure (AD-2), with no DB access. No schema change and no `Kape22Persister` change.
- CC-1 to CC-5. `deferred-work.md` is append-only.

**Ask First:**
- Before running any `dotnet test` (Unit included) or the E2E script: runs wipe `AscoLSI_Test`.
- Any production composite label on an unedited section that the port does not reproduce.

**Never:**
- A `ConsigneGPAO=0` row for SVT. Production has no `L_D_CONSIGNES` rows at all for the SVT CodeOperations (`DS2`, `NLT`, `VD1`, `VD2`, `VD9`…), and `CompleteConsignes2` never touches SVT.
- Restoring the trailing spaces on `CodeConsigne` (AC-4, accepted deviation).
- Referencing the legacy assembly (AD-3), backfilling rows already imported, or writing to production.

## I/O & Edge-Case Matrix

Expected values are production OF `2039771`. `⇥` = `"\t\t"`.

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| PC1 composite | 12 `Lingot Froid `, 10 `1250`, 11 `1200`, 5 `Pas de maintien exigé `, 6 `?` | `Lingot Froid ⇥1250°C⇥1200°C⇥Pas de maintien exigé \n?` | N/A |
| LA1 composite | 15 `1070`, 7 `Pas de scarfing`, 8 `?`, 9 `Descente serrée - vitesse lente` | `1070⇥Pas de scarfing⇥?⇥Descente serrée - vitesse lente⇥` | N/A |
| XA1 composite | 21/22/23 | `Refroidissement à l'air⇥0⇥Pas de consigne⇥` | N/A |
| XP1 both blocks | 16/17 `11,400`; 24 `?`, 25 `.`, 26 `11,400`, 27 `BC`, 28 `SAN` | 13: `11,400⇥11,400⇥`; 24: `?⇥.⇥11,400⇥BC⇥SAN⇥` | N/A |
| XP1 one block only | only the size-12 code, or only the size-18 code | a composite only on the row that exists (13 or 24) | N/A |
| Blank section code | a decoded section with a blank raw code | no rows, neither `1` nor `0` (unchanged) | N/A |
| SVT | SVT section applicable | single `1` row unchanged, no `0` row | N/A |

</frozen-after-approval>

## Code Map

- `C:\Users\Administrateur\Desktop\kape22\OrdreFabrication.cs:700-1222`: the source of truth, read-only. `:60` `GetConsignes` defaults to `gpao=false`, so only the `0` row receives the composite.
- `Desktop/kape22/OrdreDeFabricationManager.cs:1360-1435` (`AddOrModifyConsigne`: same code on both rows) and `:1447-1664` (`CompleteConsignes2`: one `BuildLibelleConsigne` call per section, after every row exists; XP1 is called once for both blocks, at `:1601-1605`).
- `src/Kape22Importer/ConsignesMapper.cs:7-18` has the header comment with the inverted `ConsigneGPAO` semantics, which must be rewritten (AC-3). The sections are built at `:35-106` and labels are resolved at `:113-124`, which is where the `0` rows go (after resolution). `Row` is at `:196`, with `ConsigneGPAO = true` hard-coded. The composite rule depends on the section kind, not on the CodeOperation value: LA1 and LA9 are both Lingot, XA1 and XA2 are both Refroidissoirs.
- `tests/Kape22Importer.Tests/ConsignesMapperTests.cs:226,336-352,388-401,429` assert "every row `ConsigneGPAO=true`" and exact counts. Scope them to the `1` rows and double the counts.
- `tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs:228-238` asserts a persisted count of `6+9+5+6+4` with all rows true. It becomes ×2, split by `ConsigneGPAO`.
- `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:268-406` holds the consignes parity: the comment to fix is at `:272-274`, production is filtered to `1` at `:317`, and the reverse check is at `:334`. `RoundTripConsignes` at `:421-440` reads back `ConsigneGPAO` rows only.
- `tests/Kape22Importer.Tests/Kape22FichierProcessorIntegrationTests.cs` has the persisted-label assertions from Story 4.12; any count there is doubled.
- `scripts/e2e-worker-import.ps1:153-167` holds the label gate (Story 4.12).
- `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs:35` holds the FR-19 AC count, which goes from 5 to 6.
- `_bmad-output/planning-artifacts/epics.md:2471` is the 2026-09-23 section header that still carries the inverted semantics (AC-3). The annex is already corrected (commit `78921a9`).

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/LibelleConsigneComposerTests.cs`: write first (TDD). One test per matrix composite row, using the production values above. Also cover a missing type contributing nothing, and PC1 with an empty type 6 label (no `"\n"`).
- [x] `src/Kape22Importer/LibelleConsigneComposer.cs`: a pure static port, one entry per legacy `case`. It takes the section's resolved rows and returns the composite(s).
- [x] `tests/Kape22Importer.Tests/ConsignesMapperTests.cs`, then `src/Kape22Importer/ConsignesMapper.cs`:
  - New tests:
    - "the `0` rows mirror the `1` rows' key, codes, size and non-composite label";
    - "the `1` rows of types 13/24 stay `?`";
    - "SVT has no `0` row";
    - XP1 type 24 present and absent.
  - Adjust the existing true-only and count asserts to the `1` rows.
  - Emit the `0` rows after the `1` rows, and apply the composer.
  - Rewrite the header comment (AC-3).
- [x] `tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs`, `Kape22FichierProcessorIntegrationTests.cs`: double the counts and split them by `ConsigneGPAO`.
- [x] `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs`: rewrite the comment (AC-3), round-trip both flags, and add the `0`-row parity (AC-5, trait `FR19-6`).
- [x] `scripts/e2e-worker-import.ps1`: fail unless count(`0`) = count(`1`) > 0 and no type 13 `0` row has label `?` (AC-6).
- [x] `AcCoverageCompletenessTests.cs` (FR-19 → 6), `epics.md:2471` (AC-3), `deferred-work.md`: add the entry "the worker's SVT `1` row (type 0, size 0) has no production counterpart: production writes no `L_D_CONSIGNES` row for SVT".
- [ ] `sprint-status.yaml`: move 4-13 to `done` in step-05.

**Acceptance Criteria:**
- Given OF `2039771`, when it is mapped with the production snapshot, then 60 rows are produced (30 `1` + 30 `0`), and every `0` row equals production. The exceptions are the LA1 type 8/13 rows, which MCC edited (`394`→`999`), and the trailing spaces of `CodeConsigne` (AC-FR19-6).
- Given parity runs, when a `0` row is compared, then its natural key exists in production `0` and its codes equal production `1`. Its label is compared to production `0` only if no code in that (`OF`, `CodeOperation`) differs between production `0` and `1`. Otherwise it is reported as skipped, not failed. Every production `0` row of a mapped CodeOperation is produced.
- Given `pwsh scripts/e2e-worker-import.ps1 -Fichiers P60_847_682_001`, when it runs, then it passes with 60 rows persisted.

## Spec Change Log

- 2026-09-24 (review, acceptance auditor): the Always rule "a type the section did not produce contributes nothing" covers the legacy `Find` loops. It does not cover PC1 type 5, which the legacy reads through `GetConsignes` inside the same try that assigns the type 13 label (`OrdreFabrication.cs:764-781`). Without a type 5 row, the composite is not assigned and the row keeps its `GetLibelle` label. This follows the "port faithfully" rule. It is unreachable in practice, because a non-blank PC1 code always produces type 5. Behaviour kept; frozen block left as written.
- 2026-09-24 (review, acceptance auditor): the `ConsigneGPAO=0` parity also applies the Story 4.12 `DateMaj` guard. A non-composite `0` label equals its `1` label, which is already skipped under that guard, so failing it on the `0` side only would contradict 4.12. It applies to the row's own reference rows, or to those of the whole section for a composite. Surfaced to the human at step-04.

## Design Notes

At legacy build time, the two rows of a type carry the same `GetLibelle` label. That is why `Find` ignoring `ConsigneGPAO` is harmless, and why the composer can read either the `1` rows or the `0` rows before any composite is written. Section kind: record it when each section block of `Map` runs, for example as a `(CodeOperation, composer)` pair, so the composite is applied after resolution.

## Verification

The Ask First on test runs was resolved on 2026-09-24. The human authorized the build, `Category=Unit` and `Category=Integration` (production parity included, SELECT only), and the E2E script below. Run them freely.

**Commands:**
- `dotnet build TextToXml.sln -warnaserror`: expected 0 warnings or errors.
- `dotnet test TextToXml.sln --filter Category=Unit`, then `Category=Integration`: expected all green, and consignes parity green or skipped-reported.
- `pwsh scripts/e2e-worker-import.ps1 -Fichiers P60_847_682_001 -KeepArtifacts`: expected 60 `L_D_CONSIGNES` rows.

## Suggested Review Order

**Working-copy rows**

- Entry point: working copies are taken after label resolution, so both rows share `GetLibelle` values.
  [`ConsignesMapper.cs:124`](../../src/Kape22Importer/ConsignesMapper.cs#L124)

- SVT is added before the working rows and gets no copy (legacy never touches SVT).
  [`ConsignesMapper.cs:158`](../../src/Kape22Importer/ConsignesMapper.cs#L158)

- The composite is computed first, then written only on the working row of that type.
  [`ConsignesMapper.cs:181`](../../src/Kape22Importer/ConsignesMapper.cs#L181)

**Composite labels (port of BuildLibelleConsigne)**

- PC1: `°C` suffix, type 5 without separator, `\n` + type 6; null when type 5 is missing.
  [`LibelleConsigneComposer.cs:33`](../../src/Kape22Importer/LibelleConsigneComposer.cs#L33)

- XP1: two independent composites; type 29 excluded as in legacy.
  [`LibelleConsigneComposer.cs:23`](../../src/Kape22Importer/LibelleConsigneComposer.cs#L23)

- A missing type contributes nothing, not even its separator (legacy caught NRE).
  [`LibelleConsigneComposer.cs:64`](../../src/Kape22Importer/LibelleConsigneComposer.cs#L64)

**Production parity and E2E**

- ConsigneGPAO=0 parity: codes vs production 1, labels vs production 0 unless MCC-edited.
  [`Kape22ProductionDataParityTests.cs:423`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L423)

- MCC-edited section detection, including a 1 row with no 0 counterpart.
  [`Kape22ProductionDataParityTests.cs:436`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L436)

- DateMaj guard: row-scoped, section-scoped only for composites.
  [`Kape22ProductionDataParityTests.cs:480`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L480)

- E2E gate: per-key pairing (SVT excluded) and no `?` working composite.
  [`e2e-worker-import.ps1:171`](../../scripts/e2e-worker-import.ps1#L171)

**Tests and docs**

- Composer against production values for each section.
  [`LibelleConsigneComposerTests.cs:17`](../../tests/Kape22Importer.Tests/LibelleConsigneComposerTests.cs#L17)

- Mapper: mirrored working copies, composites on working rows only, no SVT copy.
  [`ConsignesMapperTests.cs:405`](../../tests/Kape22Importer.Tests/ConsignesMapperTests.cs#L405)

- Persisted working composite through the real processor and database.
  [`Kape22FichierProcessorIntegrationTests.cs:233`](../../tests/Kape22Importer.Tests/Kape22FichierProcessorIntegrationTests.cs#L233)

- epics.md section header: ConsigneGPAO meaning corrected (AC-3).
  [`epics.md:2471`](../planning-artifacts/epics.md#L2471)
