---
title: 'Story 6.11 — Production FK order guarded by tests'
type: 'chore'
created: '2026-10-06'
status: 'done'
baseline_commit: '832572ca21545c7e41b0457f02650e7552432bfc'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The schema mirror carries no foreign key, so an OF creation or a D34 replace can be green in tests and fail in production on one of the 20 production FKs touching the dispatch: the INSERT order rests on EF Core's command sort (AD-7, no relation) and nothing guards `DeleteOf`'s children-first order (6.10 D-2, 6.10 first-pass F-1).

**Approach:** One Integration test (AC-FR21-7) fills `L_P_PROFIL_PRODUIT` / `L_P_TEXT_OPERATIONS` (copied from production, SELECT only, when configured; otherwise the codes read on 2026-10-06 as literals), adds the 20 FKs to `AscoLSI_Test`, creates an OF on a new (hot) Coulée, then replaces it (D34) with `L_D_OF_SUIVI` / `L_D_REBUT` rows present, and drops the FKs in a `finally`. No production code change expected.

## Boundaries & Constraints

**Always:** the 20 FKs exactly as read in production on 2026-10-06 (names, `NO_ACTION`, `WITH CHECK`); FKs dropped in a `finally`, and the fixture's `DropExistingUserTables` drops every FK first so a killed run never turns the whole Integration suite into a silent skip; `ResetData`'s `TRUNCATE` list unchanged; the 2 `L_P_*` mirrors key-only, no FK in `scripts/schema/`; Skip only when the test instance is absent (no production → the 2026-10-06 literal codes, so the guard also runs in CI); production read with `ApplicationIntent=ReadOnly`, SELECT only (CC-7); strict TDD (CC-1); English comments (CC-2); alphabetical ordering (CC-4); Integration runs `-m:1 --blame-hang-timeout 2m`.

**Ask First:** the test revealing a real FK violation (any production code change — `Kape22Persister`, `AscoLsiDbContext`); the reference Fichier's `ProfilProduit` or a `CodeOperation` missing from production's `L_P_*`; any change outside `tests/Kape22Importer.Tests/` + `scripts/schema/`.

**Never:** FKs in `scripts/schema/01-ascolsi-tables.sql`; any write to production; EF navigation properties or declared relations (AD-7); Docker; checking `ProfilProduit` / `CodeOperation` in the persister (cluster (e), accepted).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| CREATE | 20 FKs on, `L_P_*` filled, Coulée absent; reference Fichier made hot (`CodeConsignePits` `3 148 00 740`, Coulée `065718`) | Accepted; `L_D_COULEE` + OF + sections inserted with no FK violation | N/A |
| REPLACE | then `L_D_OF_SUIVI` and 2 `L_D_REBUT` rows seeded for that OF; same Fichier re-sent as NumeroFichier `999` | Accepted, `AlreadyImported` false; OF replaced, its `L_D_OF_SUIVI` / `L_D_REBUT` rows gone | N/A |
| MUTATION (manual, once) | `DeleteOf` moved to delete `L_D_ORDRE_FABRICATION` first | REPLACE fails (PersistenceError, FK conflict); result recorded in the Spec Change Log | revert the mutation |
| CLEANUP | test fails or throws mid-way | the 20 FKs are gone after the test; next `ResetData` truncates normally | `finally` |

</frozen-after-approval>

## Code Map

- `scripts/schema/01-ascolsi-tables.sql:448-540` -- append key-only mirrors (same `IF OBJECT_ID … IS NULL` idiom): `L_P_PROFIL_PRODUIT ([ID] NCHAR(3) NOT NULL, PK_L_P_PROFIL_PRODUIT)`, `L_P_TEXT_OPERATIONS ([CodeOperation] NCHAR(3) NOT NULL, PK_L_P_TEXT_OPERATIONS)`. Production `sys.columns` read 2026-10-06: `nchar` max_length 6 bytes = 3 chars, plus a `nvarchar(max) NOT NULL` label each (not mirrored); 4 and 19 rows. Parent keys already exist in the mirror: `PK_L_D_ORDRE_FABRICATION([OF] NCHAR(12))`, `PK_L_D_COULEE([IdCoulee] NCHAR(6))`; child columns match (`Coulee NCHAR(6)`, `ProfilProduit NCHAR(3)`, `CodeOperation NCHAR(3)`, `OF NCHAR(12)` in the 11 child tables).
- `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs:192-207` -- `DropExistingUserTables`: drop every `sys.foreign_keys` constraint before the tables. `ResetData` (92-122) unchanged; the 2 `L_P_*` tables need no truncate (only this test fills them, and it empties them in its `finally`).
- `tests/Kape22Importer.Tests/PersistenceSmokeTests.cs:22-80` -- `SchemaApplies_CreatesExactlyTheHarnessTables` enumerates every mirror table: add an alphabetical `ForeignKeyParentTables` list with the 2 new tables.
- `tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs:67-73,148-157,269-305` -- `ProductionConnectionString`, `OpenProduction`, `CopyFromProduction` (SELECT + per-row INSERT): move to `TestSupport.cs` (taking the test connection string) and reuse from both classes; the legacy class is deleted at switchover, the new test is not.
- `tests/Kape22Importer.Tests/OfResendIntegrationTests.cs` -- reuse `Persist`, `Ready(seedReferenceCoulee: false)`, `AssertAccepted`, `MapMutatedBundle` / `SetChamp`; `SeedLegacyDeleteOfRows` cannot be reused (it seeds rows for OF `…001`/`…003` absent from `L_D_ORDRE_FABRICATION`, an FK violation).
- `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:408-430` -- hot-Coulée pattern (`SetChamp(d, "message", "CodeConsignePits", "3 148 00 740")`, Coulée not seeded).
- `src/Kape22Importer/Persistence/Kape22Persister.cs:220-232,353-376` -- read-only: replace transaction, `DeleteOf` (target of the manual mutation), insert path including `L_D_COULEE` when new.
- FK names (production `sys.foreign_keys`, 2026-10-06): `FK_CouleeOrdreFabrication`, `FK_OrdreFabricationProfilProduit`, `FK_SuiviDeOFOrdreFabrication`, `fk_l_d_rebut_of`, `FK_PlanFourOrdreFabrication`, `FK_OrdreFabricationPSO`, `FK_Consignes{Chutage,DecoupeLingot,Lingot,EnfournementPits,PoidsMetrique,Refroidissoir,SVT}OrdreFabrication`, `FK_SectionCharge{Chutage,Decoupe,Lingo,EnfournementPits,PoidsMetrique,Refroidissoir}SuiviConsigneLaminage`, `FK_SuiviConsigneLaminageConsignesSVT`. Production also has `L_D_PLANS_FOURS.IdCoulee`, `L_D_PSO.Coulee`, `L_D_REBUT.molding_id` → `L_D_COULEE`: out of scope (columns not mirrored, never written by the dispatch).

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs` -- new Integration class, `[Trait("AC","FR21-7")]`, one test `Persist_CreateThenReplaceUnderProductionForeignKeys_ViolatesNone_AcFr21_7` covering CREATE + REPLACE + CLEANUP; red first (fails on the missing `L_P_*` tables) -- AC-FR21-7.
- [x] `scripts/schema/01-ascolsi-tables.sql` + `PersistenceSmokeTests.cs` -- the 2 key-only mirrors and their smoke listing.
- [x] `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs` -- drop all FKs before dropping tables.
- [x] `tests/Kape22Importer.Tests/TestSupport.cs` + `LegacyRejectionParityTests.cs` -- move the production helpers, no behaviour change.
- [x] Manual MUTATION run, result appended to the Spec Change Log; then revert.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- mark 6.10 D-2 and 6.10 first-pass F-1 RESOLVED by Story 6.11.

**Acceptance Criteria:**
- Given the full suite, when Unit and Integration (`-m:1`) run, then 0 failures, the new test not skipped locally, `AcTraitCoverageTests` / `AcCoverageCompletenessTests` pass.
- Given the new test has run (pass or fail), when `sys.foreign_keys` of `AscoLSI_Test` is read, then it is empty.

## Spec Change Log

- 2026-10-06, implementation. **FK count:** production `sys.foreign_keys` re-read (SELECT only): the Code Map name list holds 20 keys, not 13 (2 on `L_D_ORDRE_FABRICATION`, 11 child `OF` keys, 7 `CodeOperation` keys); all 20 are added, `NO_ACTION`, `WITH CHECK`. The intent's "13" is a miscount; the names and columns are as read. Both `L_P_*` tables copied whole (4 and 19 rows); the reference Fichier's `ProfilProduit` and every `CodeOperation` are present (CREATE green). **Red:** before the schema change, the test failed on `Invalid object name 'dbo.L_P_PROFIL_PRODUIT'`. **MUTATION (manual, once):** `DeleteOf` given an extra `L_D_ORDRE_FABRICATION` delete as its first statement; the test failed on REPLACE with `PersistenceError` "The DELETE statement conflicted with the REFERENCE constraint \"FK_ConsignesChutageOrdreFabrication\"" (table `dbo.L_D_SECTIONCHARGE_CHUTAGE`, column `OF`); `sys.foreign_keys` of `AscoLSI_Test` was empty afterwards (CLEANUP `finally`). Mutation reverted, `src/` unchanged. No production code change.

- 2026-10-07, step-04 review (blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor), **user decisions, renegotiates the frozen block**: (D-1) the user ratified 20 FKs: the epic's "13" counted groups, its own list holds 20 constraints (2 on `L_D_ORDRE_FABRICATION`, 11 child `OF`, 7 `CodeOperation`); Intent, Always and the CREATE/CLEANUP rows now read 20, and epics.md AC-FR21-7 is corrected. (D-2) the user chose a literal fallback: CI never sets `ConnectionStrings:AscoLSI_Production`, so the guard was always skipped there; the test now copies the 2 `L_P_*` tables from production when configured and otherwise inserts the 4 `ProfilProduit` and 19 `CodeOperation` codes read on 2026-10-06, with no production-based Skip (Always amended). Patches P-1..P-6: empty `L_P_*` before the fill; assert the Fichier's `ProfilProduit`/`CodeOperation` are present before adding the FKs; `TestSupport.ProductionConnectionString` read on demand; `Kape22ProductionDataParityTests` reuses it; a fixture test for a leftover FK; Design Notes aligned. KEEP: the 20 FK names/columns, hot-Coulée CREATE, REPLACE with `L_D_OF_SUIVI`/`L_D_REBUT` rows, drop in `finally`, fixture drops every FK first.

## Design Notes

FKs added after `ResetData` + the `L_P_*` fill and before CREATE, so the INSERT order is guarded, not only the delete. The `finally` drops the 20 FKs (`IF OBJECT_ID(N'<fk>', N'F') IS NOT NULL ALTER TABLE … DROP CONSTRAINT …`) then empties the 2 `L_P_*` tables. The REPLACE seeds `L_D_OF_SUIVI` / `L_D_REBUT` for the created OF only, after CREATE (they reference it).

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: 0 failures
- `dotnet test TextToXml.sln --filter Category=Integration -m:1 --blame-hang-timeout 2m` -- expected: 0 failures, `ProductionForeignKeyOrderIntegrationTests` not skipped

## Suggested Review Order

**FK guard: create then replace under the production keys**

- Entry point: one test, keys added before CREATE so INSERT and DeleteOf orders are both guarded.
  [`ProductionForeignKeyOrderIntegrationTests.cs:71`](../../tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs#L71)

- The 20 production keys, names and columns as read 2026-10-06 (user-ratified 20, not 13).
  [`ProductionForeignKeyOrderIntegrationTests.cs:38`](../../tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs#L38)

- Hot Coulée absent, so L_D_COULEE is inserted before the OF too.
  [`ProductionForeignKeyOrderIntegrationTests.cs:128`](../../tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs#L128)

- Keys dropped in finally, even on failure; L_P_* emptied after.
  [`ProductionForeignKeyOrderIntegrationTests.cs:113`](../../tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs#L113)

**Reference data: production copy or 2026-10-06 literals (D-2)**

- Copy from production when configured, else literal codes, so CI runs the guard.
  [`ProductionForeignKeyOrderIntegrationTests.cs:179`](../../tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs#L179)

- A missing code fails as the Ask First data case, never as an FK-order bug.
  [`ProductionForeignKeyOrderIntegrationTests.cs:141`](../../tests/Kape22Importer.Tests/ProductionForeignKeyOrderIntegrationTests.cs#L141)

- Key-only mirrors of the 2 parent tables, no FK in the schema.
  [`01-ascolsi-tables.sql:543`](../../scripts/schema/01-ascolsi-tables.sql#L543)

**Killed-run safety**

- Fixture drops every FK before the tables, so a leftover key never silently skips the suite.
  [`SqlServerIntegrationFixture.cs:252`](../../tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs#L252)

- Leftover FK simulated; a new fixture stays Available with no FK left.
  [`PersistenceSmokeTests.cs:284`](../../tests/Kape22Importer.Tests/PersistenceSmokeTests.cs#L284)

**Peripherals**

- Production helpers shared, connection string read on demand.
  [`TestSupport.cs:215`](../../tests/Kape22Importer.Tests/TestSupport.cs#L215)

- Copy helper moved from LegacyRejectionParityTests, now takes the test connection string.
  [`TestSupport.cs:241`](../../tests/Kape22Importer.Tests/TestSupport.cs#L241)

- FR21 completeness count raised to 7.
  [`AcCoverageCompletenessTests.cs:44`](../../tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs#L44)

- Smoke test lists the 2 new mirror tables.
  [`PersistenceSmokeTests.cs:39`](../../tests/Kape22Importer.Tests/PersistenceSmokeTests.cs#L39)
