---
title: 'Consignes and SectionCharge mappers (Story 4.4)'
type: 'feature'
created: '2026-09-15'
status: 'done'
review_loop_iteration: 0
context: []
route: 'one-shot'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** After Story 4.3 (OF + Coulee), Epic 4's dispatch still has no mapper for `L_D_CONSIGNES` or the 7 `L_D_SECTIONCHARGE_*` tables, so a P60 file cannot yet produce its full downstream bundle (FR-19).

**Approach:** One pure, reflection-free mapper per `L_D_SECTIONCHARGE_*` table (`SectionCharge<Name>Mapper.Map(L_D_KAPE22)`, returning `null` when the Story 4.2 annex's per-OF applicability rule excludes that section), a shared `SectionChargeApplicability` helper for that rule, and `ConsignesMapper.Map` composing the 7 already-mapped section entities into the `L_D_CONSIGNES` rows they own via an explicit `CodeOperation` FK (AD-7).

</frozen-after-approval>

## Suggested Review Order

**Per-OF applicability rule**

- The one shared rule every SectionCharge mapper calls to decide null-vs-row (AC-FR19-2).
  [`SectionChargeApplicability.cs:10`](../../src/Kape22Importer/SectionChargeApplicability.cs#L10)

**SectionCharge mappers — à_clarifier / annex-drift cases**

- 8 `PriseDeFer*`/`Programme*` columns left at CLR default: a DB-lookup computation out of a pure mapper's reach (AD-2).
  [`SectionChargeLingotMapper.cs:34`](../../src/Kape22Importer/SectionChargeLingotMapper.cs#L34)

- `OFInterne` annex-drift finding (no unambiguous `L_D_KAPE22` field) — same family as the Story 4.3 `SuiviDeZoneZone` fix.
  [`SectionChargeRefroidissoirsMapper.cs:43`](../../src/Kape22Importer/SectionChargeRefroidissoirsMapper.cs#L43)

- `DateEnfournementFour1/2` deliberately never sourced, distinct from the `à_clarifier` `DateDefournementFour1/2`.
  [`SectionChargePitsMapper.cs:29`](../../src/Kape22Importer/SectionChargePitsMapper.cs#L29)

**ConsignesMapper — FK composition and the key-collision risk**

- `ConsignesMapper` class entry point: composes the 7 mapped sections into `L_D_CONSIGNES` rows by explicit FK.
  [`ConsignesMapper.cs:13`](../../src/Kape22Importer/ConsignesMapper.cs#L13)

- Documents the natural-key collision risk from leaving `TypeConsigne`/`ConsigneGPAO` constant across sections.
  [`ConsignesMapper.cs:85`](../../src/Kape22Importer/ConsignesMapper.cs#L85)

**Annex correction and deferred-work trail**

- `OFInterne` reclassified `à_clarifier` with the annex-drift rationale.
  [`annexe-mapping-dispatch-epic4.md:298`](../../_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md#L298)

- New Story 4.4 deferred-work section: `OFInterne` drift + the `L_D_CONSIGNES` key-collision risk.
  [`deferred-work.md:766`](../../_bmad-output/implementation-artifacts/deferred-work.md#L766)

**Peripherals**

- `FR19` registered in the AC completeness gate (`AcCoverageCompletenessTests`).
  [`AcCoverageCompletenessTests.cs:34`](../../tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs#L34)

- The other 6 SectionCharge mappers (`Chutage`, `Decoupe`, `PoidsMetrique`, `Svt` — fully annex-sourced) and their tests.
  [`SectionChargeChutageMapper.cs`](../../src/Kape22Importer/SectionChargeChutageMapper.cs)

- `ConsignesMapperTests.cs` and the 7 `SectionCharge*MapperTests.cs` files (AR-12, unit-only).
  [`ConsignesMapperTests.cs`](../../tests/Kape22Importer.Tests/ConsignesMapperTests.cs)

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all pass (460 in Kape22Importer.Tests, verified by running the suite; includes 4 tests added at code review)
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: all pass or skip cleanly (no local SQL Server needed for this story, no DB access in these mappers)
