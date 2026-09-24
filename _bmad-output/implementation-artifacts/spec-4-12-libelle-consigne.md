---
title: '4.12 — Populate L_D_CONSIGNES.LibelleConsigne (port of legacy GetLibelle)'
type: 'bugfix'
created: '2026-09-24'
status: 'review'
review_loop_iteration: 0
baseline_commit: '4abad8c8318203e53fa62efc357dc39774998a3b'
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Test session #2 (OF `2039771`, `P60_847_682_001`) shows `L_D_CONSIGNES.LibelleConsigne` is always `NULL`, while production carries a value on all 30 `ConsigneGPAO=1` rows (for example `Pas de scarfing`, `1250`, `11,400`, `?`). The mapper never computes it: it was deferred as `à_clarifier` because the legacy source was missing. That source has now been found: `Documents/Lsi.Net/Ascometal.LSI.DAL/LibelleConsigneController.cs`, `GetLibelle`.

**Approach:** Port `GetLibelle` into a pure resolver. It fills `LibelleConsigne` on every row `ConsignesMapper` emits, from an in-memory snapshot of the `L_P_CONSIGNES_*` reference tables. `Kape22FichierProcessor` loads that snapshot once per Fichier (human decision 1a: the mapper stays pure, AD-2). Also:
- add the reference tables to the test schema;
- reload them from production on every E2E script run;
- extend the production parity test to `LibelleConsigne`.

## Boundaries & Constraints

**Always:**
- Port the legacy code faithfully, line for line, including its quirks. Only these legacy files are authoritative:
  - `LibelleConsigneController.GetLibelle` (branches by section/type, regexes, formats);
  - `OrdreDeFabricationManager.cs:1405-1415` (type 22 takes `ProfilProduit`, `DiametreProduit` and `H2Coulee`; a blank result becomes `"?"`).
- Reproduce these legacy behaviours exactly:
  - any exception inside the resolver gives `"?"`;
  - PC1 lookups append `" " + consignePlus`, so a trailing space is kept;
  - numeric formats use the `fr-FR` culture (`11,400`, `0,0`);
  - an unmatched section, type or code gives `"?"`.
- Match codes the way the legacy SQL `WHERE` clause did: ignore trailing spaces (the `nchar` columns are padded) and ignore case. `Consignes` is the column behind legacy `TypeConsigne.id`. When several rows match, take the first one in `(Section, Consignes, CodeConsigne)` order.
- Every reference read is a `SELECT` on the Fichier's own `AscoLsiDbContext`. Production (`AFV004-LSI`) is read-only (SELECT only).
- Follow CC-1 through CC-5. `deferred-work.md` is append-only.

**Ask First:** a legacy branch whose port would need data the P60 dispatch does not carry, beyond the `H2Coulee`/pits case in the matrix.

**Never:**
- Reference or call the legacy assembly (AD-3).
- Read the database from a mapper.
- Backfill rows that are already imported.
- Write to production.
- Hard-code reference values in C# (the tables are edited by operators).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Computed type | PC1 10 `205` / PC1 11 `205` / LA1 15 `107` / XC1 0 `00` / XP1 16 `11400` / XP1 27 `BC` | `1250` / `1200` / `1070` / `0,0` / `11,400` / `BC` | N/A |
| Lookup hit | LA1 7 `0`; XA1 21 `00`; XC1 4 `M` | `Pas de scarfing`; `Refroidissement à l'air`; `section: … - Pied: … - Tête: …` | N/A |
| `nchar` padding | XA1 23, code `""`, SMQ row `Code='   '` | `Pas de consigne` | N/A |
| No rule / no match | any type 13 or 24, or an XP1 lookup on the empty table | `?` | N/A |
| Degazage | XA1 22, code `1`, H2 = 1 (no bucket) | `0` | N/A |
| Degazage, pits section absent or `H2Coulee` null | XA1 22 | `?` (assumed, unverified: legacy would throw) | caught |
| Unparsable input | numeric branch on a non-numeric code | `?` | caught |

</frozen-after-approval>

## Code Map

- `C:\Users\Administrateur\Documents\Lsi.Net\Ascometal.LSI.DAL\LibelleConsigneController.cs:14-281` -- read-only source of truth to port.
- `Desktop/kape22/OrdreDeFabricationManager.cs:1405-1415` -- call-site semantics: type 22 arguments and the `"?"` fallback.
- `src/Kape22Importer/ConsignesMapper.cs:19-107,176-188` -- `Map` and `Row`. `Row` currently leaves `LibelleConsigne` null (comment at `:185`). The snapshot is passed as an optional last parameter, defaulting to empty, so the ~20 existing callers keep compiling.
- `src/Kape22Importer/Kape22ImportBundleMapper.cs:17,44` -- forwards the snapshot. `OrdreFabricationMapper` output (`DiametreProduit` decimal, `ProfilProduit`) and pits `H2Coulee` (decimal?) supply the type 22 inputs.
- `src/Kape22Importer/Kape22FichierProcessor.cs:71,81` -- the context is created after mapping. Move context creation before `Map` and load the snapshot from it.
- `src/Kape22Importer/Persistence/AscoLsiDbContext.cs` -- the snapshot uses `Database.SqlQuery<T>` into keyless records. No new `DbSet`, so `SchemaModelParityTests` is unaffected.
- Production reference tables (13): `L_P_CONSIGNES_{PITS, PRECHAUFFAGE_PARTICULIER, LINGOT, CHUTAGE, CODEOUTIL_COUPE, MARQUAGE, DECOUPE, POIDSMETRIQUE, REFROIDISSEMENT, SMQ, REFROIDISSOIRS, DEGAZAGE_GLOBAL, DEGAZAGE_DETAIL}`, about 250 rows. Take their shapes from AFV004-LSI `sys.columns`, never from memory (R-3). `REFROIDISSEMENT.Code` is `nchar(2)` and `SMQ.Code` is `nchar(3)`. The `DEGAZAGE_*` tables have no `DateMaj`.
- `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs:55,190` -- `DropExistingUserTables` wipes every table on each run, so the new DDL must be applied here.
- `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:266-367` -- consignes parity test. `LibelleConsigne` is currently excluded (`:275`), and `ReadProductionRows` (`:188`) gives the production `DateReception`.
- `scripts/e2e-worker-import.ps1:85-100` -- the sync hooks in before the Launcher starts. `bcp` and `sqlcmd` are available (ODBC 170 tools).
- `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs:20-40` -- the FR-19 AC count must grow by 1.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/LibelleConsigneResolverTests.cs` -- write first (TDD): cover every matrix row plus one test per legacy branch, including the PC1 type 6 "particulier" path and the XP9 18/19/20 types.
- [x] `src/Kape22Importer/ConsigneReferenceData.cs` -- immutable snapshot (`Empty`, and `Load(AscoLsiDbContext)` via `SqlQuery`) carrying each row's `DateMaj`.
- [x] `src/Kape22Importer/LibelleConsigneResolver.cs` -- pure port. `Resolve(...)` returns the libellé plus the latest `DateMaj` among the reference rows it consulted (null for computed types).
- [x] `src/Kape22Importer/ConsignesMapper.cs`, `Kape22ImportBundleMapper.cs`, `Kape22FichierProcessor.cs` -- thread the snapshot through and set `LibelleConsigne` on every row. Replace the `assumed, unverified` comment.
- [x] `scripts/schema/03-ascolsi-reference-consignes.sql` + `SqlServerIntegrationFixture.cs` -- idempotent DDL for the 13 tables, applied by the fixture.
- [x] `scripts/sync-reference-consignes.ps1` + `scripts/e2e-worker-import.ps1` -- on every run, `TRUNCATE` the 13 test tables, then reload them from production with `bcp queryout` (`ApplicationIntent=ReadOnly`, SELECT only) and `bcp in`.
- [x] `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs` -- load the snapshot from production and compare `LibelleConsigne`. Skip a row (reported, not failed) when its consulted `DateMaj` is later than the production `DateReception` of that Fichier.
- [x] `tests/Kape22Importer.Tests/Kape22FichierProcessorIntegrationTests.cs` -- seed a few reference rows, import `P60_847_682_001`, and assert the persisted libellés.
- [x] `_bmad-output/planning-artifacts/epics.md`, `AcCoverageCompletenessTests.cs`, `annexe-mapping-dispatch-epic4.md`, `deferred-work.md` -- add Story 4.12 and AC-FR19-5; set the annex row `LibelleConsigne` from à_clarifier to règle; add a resolution note plus a deferred entry "no backfill of NULL libellés".
- [x] `sprint-status.yaml` + `PROJECT-CLOSED.md` -- add `4-12-libelle-consigne`; set the project status to `open` (test/correction session 2).

**Acceptance Criteria:**
- Given OF `2039771`, when it is mapped with the production snapshot, then all 30 `ConsigneGPAO=1` `LibelleConsigne` values equal production (AC-FR19-5).
- Given a reference row whose `DateMaj` is later than the production `DateReception` of the Fichier, when parity runs, then that row's libellé is reported as skipped, not failed.
- Given `pwsh scripts/e2e-worker-import.ps1 -Fichiers P60_847_682_001`, when it runs, then the 13 test tables are first truncated and reloaded from production, and the persisted libellés are non-null.
- Given an empty snapshot, when mapping runs, then lookup-based libellés are `"?"` and computed ones are still correct.

## Spec Change Log

## Design Notes

`SqlQuery` into keyless records was chosen over 13 `DbSet` entities. The tables are only read, only into the snapshot, and never written by the dispatch, so full EF entities would add model and parity-test surface for no benefit. The `DateMaj` guard compares the production import date against the current reference rows. A parameter edited after dispatch explains a legitimate delta (same principle as "P60 is a forecast").

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings/errors
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all green
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: all green, including consignes parity with `LibelleConsigne`
- `pwsh scripts/e2e-worker-import.ps1 -Fichiers P60_847_682_001 -KeepArtifacts` -- expected: 30 rows with production-equal libellés
