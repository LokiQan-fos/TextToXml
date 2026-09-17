---
title: 'Story 4.7 — Extend the E2E suite to the 10 downstream tables'
type: 'feature'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'a7fb70de27eedeca3b244d4f2ecd014f12b9c508'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Story 3.6 E2E suite (`EndToEndImportIntegrationTests`, SM-2) only asserts against `L_D_KAPE22` + `L_D_LOG_COMMANDE`. It does not prove the AD-1 single-transaction atomicity Story 4.6 introduced across the 9 downstream tables, on either the success or the rollback path, at the real pipeline level (only `TransactionalPersistenceTests` proves it at the Persister-unit level).

**Approach:** Extend the existing SM-2 E2E test to assert coherent rows in all 10 tables for the 10 reference `P60/` files (AC-FR21-4), and add dedicated faulty E2E fixtures — one per rejection cause (cold Coulée missing, inconsistent ingot/furnace distribution, simulated SQL failure) — each proving zero rows land in any of the 10 tables and the cause is readable via the existing double-journal circuit (AC-FR21-5). No production code changes; this is a test-only story.

## Boundaries & Constraints

**Always:**
- Reuse `TestSupport.InsertableReferenceFichier()` / `InsertableFichier(name)` (the `ZeroOutOfScaleDimensions` workaround) for the 10 reference files, consistent with every other Epic 4 integration suite since Story 4.6 — do not touch the mappers or attempt a real fix (Story 4.3-bis owns that).
- Reuse existing assertion patterns rather than inventing new plumbing: the "list every `*Rows.AsNoTracking()`, assert empty across all 10 tables" pattern from `TransactionalPersistenceTests.cs` (lines 127-141, 291-330) for rollback proof; the `DoubleJournalIntegrationTests`/`ErrorsReportReadabilityTests` readback patterns for AC-FR21-5.
- Reuse existing fixture-mutation helpers (`MapMutatedBundle`, `SetChamp`) and existing failure-injection precedents (oversized `commande` string for a forced `DbUpdateException`) rather than building a new failure injector.
- Bump `AcCoverageCompletenessTests.AcCountByFr[21]` from `3` to `5` (AC-FR21-4, AC-FR21-5) — pre-reserved by Story 4.6's own comment at `AcCoverageCompletenessTests.cs:29`.
- `[Trait("Category", TestCategory.Integration)]` + `[Collection(SqlServerIntegrationCollection.Name)]`, same AR-12 conventions as `EndToEndImportIntegrationTests`.

**Ask First:** None — the one open decision (decimal-scale workaround vs. blocking on Story 4.3-bis) was resolved by the human before this spec was written: reuse the workaround.

**Never:**
- No change inside any mapper (`OrdreFabricationMapper`, `SectionCharge*Mapper`, `Kape22Persister`, `Kape22FichierProcessor`) — this story is test-only.
- Do not mutate the real, untouched `P60/` reference files or `Kape22ProductionDataParityTests` fixtures.
- Do not attempt to un-skip or fix `GpaoImportP60WorkerEndToEndTests` — out of scope, tracked separately under the Story 4.3-bis deferred-work entry.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Valid files, full dispatch | 10 reference `P60/` files via `InsertableReferenceFichier()`/`InsertableFichier(name)` | Each file produces coherent rows in all 10 tables (`L_D_KAPE22` + 9 downstream), consistent with the file's own data | N/A |
| Cold Coulée missing | `MapMutatedBundle` fixture with a Coulée not present in `L_D_COULEE` and not new-to-file | Zero rows in all 10 tables; REJETÉ `L_D_LOG_COMMANDE` row; cause readable in the double-journal circuit | AC-FR20-5 business rejection, no exception |
| Inconsistent ingot/furnace distribution | `MapMutatedBundle` fixture with a distribution Champ contradicting the OF's half-product count | Zero rows in all 10 tables; REJETÉ `L_D_LOG_COMMANDE` row; cause readable in the double-journal circuit | Business rejection, no exception |
| Simulated SQL failure | Fixture forcing `DbUpdateException` on `SaveChanges` (oversized column value, precedent from `TransactionalPersistenceTests.cs:116-141`) | Zero rows in all 10 tables including `L_D_KAPE22`; failure cause readable via `MQTTnetServices.Logs` / `error/*.errors.json` | `PersistenceError`, EF transaction rollback |

</frozen-after-approval>

## Code Map

- `tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs` -- extend `Tick_TenSampleFichiers_InsertTenCoherentRowsAndArchiveEveryFichier_Sm2`: swap the 10 fixtures to `InsertableReferenceFichier()`/`InsertableFichier(name)` if not already, add assertions on the 9 downstream `*Rows` per file (matched by OF), alongside the existing `L_D_KAPE22`/`L_D_LOG_COMMANDE` checks.
- `tests/Kape22Importer.Tests/TestSupport.cs` -- reuse `InsertableReferenceFichier`/`InsertableFichier` (line ~150), `MapMutatedBundle`, `SetChamp`; add no new helpers unless a genuinely new mutation shape is needed.
- New or extended test file for the 3 dedicated faulty E2E fixtures (cold Coulée, inconsistent distribution, SQL failure) — place alongside `EndToEndImportIntegrationTests.cs` in `tests/Kape22Importer.Tests/`, same `[Collection(SqlServerIntegrationCollection.Name)]` + `Category.Integration`. Reuse the "list every `*Rows.AsNoTracking()`, assert empty" pattern from `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:127-141` and `:291-330`.
- `tests/Kape22Importer.Tests/DoubleJournalIntegrationTests.cs` -- read as the template for the double-journal readback (`RunWithSerilog`, `ReadMqttLogs`, `LogCommandeRows` assertions) to compose into the new rejection-cause tests for AC-FR21-5.
- `tests/Kape22Importer.Tests/ErrorsReportReadabilityTests.cs` -- read as the template for the `error/*.errors.json` readback if a rejection cause is asserted via the error report instead of/in addition to the log circuit.
- `src/Kape22Importer/Persistence/AscoLsiDbContext.cs` -- `DbSet` names to assert on: `ConsignesRows`, `CouleeRows`, `Kape22Rows`, `LogCommandeRows`, `OrdreFabricationRows`, `SectionChargeChutageRows`, `SectionChargeDecoupeRows`, `SectionChargeLingotRows`, `SectionChargePitsRows`, `SectionChargePoidsMetriqueRows`, `SectionChargeRefroidissoirsRows`, `SectionChargeSvtRows`.
- `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs:22-37` -- bump `AcCountByFr[21]` from `3` to `5`.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- top entry ("Deferred from: story-4.6 implementation") documents the decimal-scale workaround precedent this story follows; no new entry needed unless a new gap surfaces during implementation.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs` -- extend SM-2 to assert coherent rows across all 10 tables for the 10 reference files (via the zeroed-dimension fixtures) -- proves AD-1 atomicity on the success path at the real pipeline level
- [x] `tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs` (new sibling file) -- add a dedicated faulty fixture + test for cold Coulée missing, asserting zero rows across all 10 tables and a readable cause -- AC-FR21-5
- [x] same file -- add a dedicated faulty fixture + test for inconsistent ingot/furnace distribution, asserting zero rows across all 10 tables and a readable cause -- AC-FR21-5
- [x] same file -- add a dedicated faulty fixture + test for a simulated SQL failure, asserting zero rows across all 10 tables (including `L_D_KAPE22`) and a readable cause -- AC-FR21-5
- [x] `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs` -- bump `AcCountByFr[21]` from `3` to `5` -- keeps the AC-coverage gate accurate

**Acceptance Criteria:**
- Given the 10 reference `P60/` files (zeroed-dimension variant), when the E2E suite replays them through one `InboxScanner.RunTick()`, then each file produces coherent rows in all 10 tables consistent with the file's own data (AC-FR21-4)
- Given a dedicated faulty fixture per cause (cold Coulée missing, inconsistent ingot/furnace distribution, simulated SQL failure), when the E2E suite replays it, then none of the 10 tables receives any row — not even `L_D_KAPE22` — and the cause is readable in `L_D_LOG_COMMANDE` + `MQTTnetServices.Logs` / `*.errors.json` (AC-FR21-5)

## Design Notes

The decimal-scale defect (5 mappers writing un-rescaled ints into narrow `DECIMAL` columns, deferred to Story 4.3-bis, still open) means the real, untouched reference files overflow a full 10-table transaction (confirmed 0/10 at Story 4.6). Per human decision (2026-09-16, this story's planning), Story 4.7 reuses the same `ZeroOutOfScaleDimensions`-backed helpers Story 4.6 already applied to every other Epic 4 integration suite, rather than blocking on Story 4.3-bis. AC-FR21-4 is proven for structural/cross-table coherence, not for real out-of-scale dimension values — that gap stays tracked under the existing Story 4.3-bis deferred-work entry; no new entry needed.

**Downstream-table count correction (2026-09-17, code review):** the frozen Intent paragraph above says "the 9 downstream tables" while the frozen Approach/I-O-Matrix and everywhere else in this spec, the Code Map, and the shipped code say "10 tables" / "all 10 tables (L_D_KAPE22 + 9 downstream)". The real count, confirmed against `AscoLsiDbContext.cs` (12 `DbSet`s minus `Kape22Rows`/`LogCommandeRows`) and `epics.md:1630` ("les 10 nouvelles entités"), is 10 downstream tables (11 total including `L_D_KAPE22`). The "9" is a pre-existing off-by-one inherited from AD-1's wording in `project-profile.md` ("L_D_KAPE22 + les 9 tables aval", itself enumerating 10 tables), predating this story. The shipped tests correctly assert on 10 downstream / 11 total (see `RejectionAtomicityIntegrationTests.AssertAllElevenTablesEmpty`); only this spec's frozen prose carried the stale "9" forward. No code change; documented here so the frozen block's internal inconsistency isn't silently re-litigated. The AD-1 wording fix itself is tracked separately in `deferred-work.md` (pre-existing, out of this test-only story's scope).

**AC-FR21-5 channel substitution (2026-09-17, code review):** epics.md's literal AC-FR21-5 text names `L_D_LOG_COMMANDE` + `*.errors.json` as the readable-cause channels. This spec's I/O matrix instead used `L_D_LOG_COMMANDE` + `MQTTnetServices.Logs` (the Story 3.3 double-journal pattern), and the implementation follows that: the 3 rejection tests call `Kape22FichierProcessor.Import` directly (same shortcut `DoubleJournalIntegrationTests` already takes), never through `InboxScanner`, so no `*.errors.json` sidecar is produced or asserted. Per human decision, this substitution is accepted as-is: `*.errors.json`'s shape is already proven at Unit level by the pre-existing SM-3 (`ErrorsReportReadabilityTests`), and the DB-backed double-journal assertion is at least as strong a real-pipeline proof of "readable cause" for this story's purpose. No code change; documented here so the deviation from the literal epics.md wording isn't silently re-litigated.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: no warnings/errors
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: new and extended E2E tests pass against the local SQL Server harness (or skip cleanly if unreachable, AR-12)
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: `AcCoverageCompletenessTests` passes with `AcCountByFr[21] == 5`

## Suggested Review Order

**Success-path atomicity (AC-FR21-4)**

- Entry point: SM-2 now runs the full bundle mapper and checks the 10 downstream tables, not just `L_D_KAPE22`.
  [`EndToEndImportIntegrationTests.cs:91`](../../tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs#L91)

- Per-file downstream assertions, matched by OF, mirroring the Persister-unit-level pattern one layer up.
  [`EndToEndImportIntegrationTests.cs:130`](../../tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs#L130)

- `expected.OF`/`expected.NumeroFichier` now come from the bundle root, both trimmed to match the trimmed DB row.
  [`EndToEndImportIntegrationTests.cs:134`](../../tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs#L134)

- Replaces the old mapper-only `Expected` helper with one that runs the same composition the real pipeline uses.
  [`EndToEndImportIntegrationTests.cs:182`](../../tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs#L182)

**Rollback atomicity (AC-FR21-5) — three dedicated causes**

- New file, one class housing all three rejection-path E2E fixtures plus shared readback helpers.
  [`RejectionAtomicityIntegrationTests.cs:35`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L35)

- Cold Coulée missing: a business rejection before anything is staged, zero rows everywhere.
  [`RejectionAtomicityIntegrationTests.cs:73`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L73)

- Inconsistent ingot/furnace distribution: same rejection shape, different business rule.
  [`RejectionAtomicityIntegrationTests.cs:100`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L100)

- Simulated SQL failure: the one scenario where `L_D_LOG_COMMANDE` itself also stays empty (mid-transaction rollback).
  [`RejectionAtomicityIntegrationTests.cs:129`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L129)

- Shared "all 11 tables empty" assertion reused by all three scenarios; renamed post-review to match its real count.
  [`RejectionAtomicityIntegrationTests.cs:160`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L160)

**Peripherals**

- AC-coverage gate bumped to reflect the two new AC traits this story adds.
  [`AcCoverageCompletenessTests.cs:39`](../../tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs#L39)

- Two review-surfaced gaps (generic rejection-cause wording, untested `ErrorCode` categories) recorded for later.
  [`deferred-work.md`](deferred-work.md)

- Story marked `in-progress` → `review` in the sprint tracker.
  [`sprint-status.yaml`](sprint-status.yaml)

### Review Findings

- [x] [Review][Patch] Les 3 noms de test de rejet sous-comptent les tables vérifiées ("AllTenTables" vs `AssertAllElevenTablesEmpty`) [tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs:73]
- [x] [Review][Patch] Le bloc frozen contient une incohérence interne sur le nombre de tables avales (9 vs 10), sans note de renégociation datée [_bmad-output/implementation-artifacts/spec-4-7-e2e-suite-downstream-tables.md:15]
- [x] [Review][Defer] Coquille préexistante « 9 tables aval » dans AD-1 (project-profile.md / epics.md) [_bmad-output/project-profile.md] — deferred, pre-existing
- [x] [Review][Defer] Les tests de rejet contournent InboxScanner ; aucune preuve de déplacement vers error/ [tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs] — deferred, pre-existing (raccourci déjà accepté par décision humaine)
- [x] [Review][Defer] Les assertions sur les 9 tables avales (SM-2) ne vérifient que la présence/le compte par OF, pas les valeurs de champs [tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs:130] — deferred, pre-existing (patron imposé par la spec elle-même)

