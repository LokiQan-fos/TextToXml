---
title: 'Story 6.10 — Re-sent existing OF: replacement or explicit refusal'
type: 'feature'
created: '2026-10-05'
status: 'review'
baseline_commit: 'f21953c5003602fcaec4f8285d4d222ceb0fc241'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** A Fichier whose OF already sits in `L_D_ORDRE_FABRICATION` is not specified: `Kape22Persister` inserts blindly and fails on a raw `PK_L_D_CONSIGNES` / `PK_L_D_ORDRE_FABRICATION`, where the legacy replaced the OF (state GPAO) or refused it ("nombre d'OF sauvés : 0"). Real cases: `P60/error/P60_847_682_446/447/449`, and `448` accepted by the legacy.

**Approach:** D34 in `Kape22Persister`, after the D22 guard and the cold-Coulée check, before A-5/B-5/C-4: an existing OF is refused (BusinessRuleViolation, no write) when its `Etat` is 1/2/3/5/8 or it appears in `L_D_FOURS.OFEnCours`, `L_D_PLANS_FOURS.[OF]` or `L_D_PSO.[OF]`; otherwise its rows in the 9 tables are deleted and the new ones inserted in the same single `SaveChanges()`. A generic theory replays every `P60/error/` Fichier against the state rebuilt from the production legacy trace.

## Boundaries & Constraints

**Always:** OF compared zero-padded (`DownstreamOf.Pad`); refusal shape = the cold-Coulée one (`Block.File`, `BusinessRuleViolation`, one REJETÉ journal entry); message `OF '<of>' : l'OF existe déjà et ne peut pas être remplacé (<reasons>).` with reasons joined by ` ; `, each `état <NAME> (<n>)` or `présent dans <table>`; `L_D_COULEE` and earlier `L_D_KAPE22` rows untouched; the 3 precondition tables read-only (entity with only key + OF columns, never added to a `DbSet` write path); strict TDD (CC-1); English comments (CC-2); alphabetical ordering (CC-4); no secret, production read only by SELECT with `ApplicationIntent=ReadOnly` (CC-7); Integration runs `-m:1 --blame-hang-timeout 2m`.

**Ask First:** EF refusing a Deleted + Added pair with the same key in one `SaveChanges()` (do not switch to `ExecuteDelete` or a second transaction silently); any change outside `Kape22Importer` + its tests + `scripts/schema/`; any `P60/error/` case whose obtained cause differs from the legacy one.

**Never:** deleting from tables outside the 9 (`L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PRODUITS_OUTIL`, `L_D_MAM_QUAL`, `L_D_PLANS_FOURS`, `L_D_PSO`, updating `L_D_FOURS`) — legacy `DeleteOF` does, D34 does not; any write to production; Docker; adding `P60/error/` to `Kape22ProductionDataParityTests`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| REFUSE_STATE | Coulée `063257` seeded, `443`/`444` imported, OF `2040310`/`2040311` set to `Etat` 2; then `error/446`, `error/447`, `error/449` | Each refused, message names the OF and `état EVC (2)`; 9 tables + `L_D_KAPE22` unchanged | REJETÉ entry |
| REFUSE_TABLE (×3) | reference Fichier imported (`Etat` 0), OF then put in one table each (`L_D_FOURS.OFEnCours`, `L_D_PLANS_FOURS`, `L_D_PSO`); re-sent with another NumeroFichier | Refused naming the table, no write | REJETÉ entry |
| REPLACE | `445` imported (`Etat` 0); then `448` | Accepted; 9 tables hold exactly `448`'s mapped rows for OF `2040312`; `445`'s `L_D_KAPE22` row and the Coulée unchanged, `448` adds its own `L_D_KAPE22` row | success entry |
| REPLACE_SQL_FAIL | reference imported; re-sent with another NumeroFichier and an over-long `LibelleConsigneChutage` | PersistenceError; previous OF rows intact in the 9 tables | REJETÉ entry |
| NEW_OF | OF absent | Unchanged insert path | N/A |
| LEGACY_THEORY | one case per `P60/error/` Fichier, state rebuilt (Design Notes) | Obtained cause = expected cause from the legacy reason; hot-Coulée-`0` reason → accepted, output "écart connu" | unknown reason / other cause → fail naming both; no trace or no production config → `Skip` |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Persistence/Kape22Persister.cs:85-104` -- insert the D34 branch after the cold check (line 104), before A-5 (106); staging at 175-193; stale business note Q-3 at 232-234 (an OF *can* now be re-sent) and header comment 14-42 to update.
- `src/Kape22Importer/Persistence/AscoLsiDbContext.cs:15-35,41-130` -- add 3 `DbSet`s + `HasKey` mapping, alphabetical.
- `src/Kape22Importer/Persistence/L_D_ORDRE_FABRICATION.cs:44,68` -- `Etat` int, key `OF` (padded NCHAR(12)); 8 other tables keyed `(OF, CodeOperation[, TypeConsigne, ConsigneGPAO])`.
- `src/Kape22Importer/DownstreamOf.cs` -- `Pad` (reuse).
- New entities (database-first, legacy `DALLevel3.edmx`): `L_D_FOURS` (PK `Id` nvarchar(3), `OFEnCours` nchar(12) null), `L_D_PLANS_FOURS` (PK `FourId` nvarchar(3) + `Position` smallint, `OF` nchar(12) null), `L_D_PSO` (PK `Coulee` nchar(6) + `NumeroLingot` int, `OF` nchar(12) null). Verify against `AFV004-LSI` `sys.columns` (SELECT) before freezing.
- `scripts/schema/01-ascolsi-tables.sql:151,294` -- add the 3 minimal mirrors (same `IF OBJECT_ID … IS NULL` idiom).
- `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs:92-122` -- `ResetData` truncates the 7 new tables (3 precondition + 4 legacy DeleteOF, review P-9).
- `tests/Kape22Importer.Tests/TestSupport.cs:83-122` -- `MapFichier`, `SeedCoulees`, `SeedCoulee` (reuse).
- `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:203` -- SQL-failure pattern (over-long `LibelleConsigneChutage`).
- `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:78,104-112` -- production config + Skip pattern to mirror.
- `tests/Kape22Importer.Tests/ZzLegacyRejectionReplayTests.cs` -- untracked draft: processor wiring and `CopyFromProduction` (sys.columns + SqlBulkCopy) to reuse, then delete the draft.
- Legacy (read-only): `Desktop/kape22/OrdreFabricationController.cs:1608` (`AddRange2`), `:1328` (`GetOFById` pads), `Lsi.Net/DALLevel3/EtatOF.cs` (GPAO 0, ENC 1, EVC 2, ENFOURNE 3, LAMINAGE 5, LAMINE 8).
- Messages to classify: AC-FR20-5 `est introuvable dans L_D_COULEE` (`Kape22Persister.cs:101`), AC-FR20-2 `la répartition des lingots aux fours` and AC-FR20-4 `aucune consigne d'enfournement` (`Kape22ImportBundleMapper.cs:104,131`).

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/OfResendIntegrationTests.cs` -- new Integration class, `[Trait("AC","FR20-6")]` / `[Trait("AC","FR21-6")]`, one test per matrix row REFUSE_*/REPLACE*; red first -- AC-FR20-6, AC-FR21-6, AC-FR21-2.
- [x] `src/Kape22Importer/Persistence/L_D_FOURS.cs`, `L_D_PLANS_FOURS.cs`, `L_D_PSO.cs` + `AscoLsiDbContext.cs` + `scripts/schema/01-ascolsi-tables.sql` + `SqlServerIntegrationFixture.ResetData` -- read-only entities and mirrors.
- [x] `src/Kape22Importer/Persistence/Kape22Persister.cs` -- D34 branch; replace = `DeleteOf` (review P-9): `ExecuteDelete` over 13 tables, children first, `L_D_ORDRE_FABRICATION` last, `L_D_OF_SUIVI` re-ranked, inside an explicit transaction that the existing `Add` path's single `SaveChanges()` joins; update comments.
- [x] `tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs` -- `[SkippableTheory]` over `P60/error/*`, Integration, `[Trait("AC","FR20-6")]`, header comment "remove at the switchover to the new worker"; delete `ZzLegacyRejectionReplayTests.cs`.

**Acceptance Criteria:**
- Given the full suite, when Unit and Integration (`-m:1`) run, then 0 failures and `AcTraitCoverageTests` / `AcCoverageCompletenessTests` pass.
- Given the 6 `P60/error/` Fichiers and production configured, when the theory runs, then 407/408/430 → AC-FR20-5 and 446/447/449 → AC-FR20-6, none skipped.
- Given the D34 branch removed, when `OfResendIntegrationTests` runs, then REFUSE_* and REPLACE fail.

### Review Findings

- [x] [Review][Decision] D-1 Replace via EF UPDATE keeps unmapped production columns — resolved 2026-10-05 by the user: keep the legacy DeleteOF + insert, full scope (→ P-9)
- [x] [Review][Patch] P-9 Full legacy DeleteOF + insert: real DELETE (children, then L_D_ORDRE_FABRICATION) incl. L_D_OF_SUIVI (re-rank), L_D_MAM_QUAL, L_D_PRODUITS_OUTIL and L_D_REBUT, explicit transaction; renegotiate the frozen Never in the Spec Change Log [src/Kape22Importer/Persistence/Kape22Persister.cs:211]
- [x] [Review][Patch] P-1 Stale-row removal on replace untested (re-send with fewer sections/consignes) [tests/Kape22Importer.Tests/OfResendIntegrationTests.cs:113]
- [x] [Review][Patch] P-2 Multi-reason refusal message (` ; ` join, state then tables) untested [src/Kape22Importer/Persistence/Kape22Persister.cs:318]
- [x] [Review][Patch] P-3 Legacy theory matches the generic "nombre d'OF sauvés : 0" before specific reasons [tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs:45]
- [ ] [Review][Patch] P-4 CC-1: closure commit must attest AC-FR21-2 [git ae08655] — owed by /commit-review
- [x] [Review][Patch] P-5 Header comment says "deleted" while L_D_ORDRE_FABRICATION is UPDATE-merged [src/Kape22Importer/Persistence/Kape22Persister.cs:28]
- [x] [Review][Patch] P-6 L_D_PSO key order in comments is (NumeroLingot, Coulee) [src/Kape22Importer/Persistence/L_D_PSO.cs:4]
- [x] [Review][Patch] P-7 Mark 6.8 W-1 resolved by 6.10 in the ledger [_bmad-output/implementation-artifacts/deferred-work.md:1457]
- [x] [Review][Patch] P-8 Run and record the "D34 branch removed" mutation AC [spec Acceptance Criteria]
- [x] [Review][Defer] F-1 EF command order unaware of production FKs [src/Kape22Importer/Persistence/AscoLsiDbContext.cs] — deferred, pre-existing (Story 4.6, with D-2)
- [x] [Review][Defer] F-2 Legacy KAP22 reasons filtered by Id range only [tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs:198] — deferred, legacy is sequential

Second pass (2026-10-05, ae08655^ vs working tree):
- [x] [Review][Decision] D-1 Delete order of the replace is unguarded against production FKs — resolved 2026-10-05 by the user: option (c), deferred under D-2; the operator OF-delete script (children first, L_D_ORDRE_FABRICATION last) proves the order in production; P-9's mirror-FK clause dropped (would break the TRUNCATE reset)
- [x] [Review][Patch] P-1 CC-4: LegacyDeleteOfTables before PreconditionTables [tests/Kape22Importer.Tests/PersistenceSmokeTests.cs:38]
- [x] [Review][Patch] P-2 Architecture spine AD-1 D34 note predates P-9 [_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md:70]
- [x] [Review][Patch] P-3 Non-frozen spec sections still describe RemoveRange / EF UPDATE merge / 3 tables [spec:49,62,122,125,130,169]
- [x] [Review][Patch] P-4 OfResendIntegrationTests header still says "9 tables … same single SaveChanges" [tests/Kape22Importer.Tests/OfResendIntegrationTests.cs:21]
- [ ] [Review][Patch] P-5 CC-1: closure commit attests AC-FR21-2 and the P-1/P-2/P-9 tests [closure commit] — owed by /commit-review
- [x] [Review][Defer] F-1 Existing-OF check read outside the replace transaction [src/Kape22Importer/Persistence/Kape22Persister.cs:130] — deferred, same window as legacy AddRange2
- [x] [Review][Defer] F-2 Legacy theory OF history unbounded below, Etat rebuilt as GPAO/ENC only [tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs:243] — deferred, test code removed at switchover
- [x] [Review][Dismiss] F-3 NVARCHAR(12) OF in L_D_MAM_QUAL / L_D_PRODUITS_OUTIL — rejected 2026-10-05 by the user: these tables are fed after ENC, a replaceable OF never reached ENC, and orphan rows of a deleted OF are of no interest

## Spec Change Log

- 2026-10-05, step-04 (acceptance-auditor + edge-case-hunter, user-arbitrated, patch-level, no loopback): (1) existing test `RejectionAtomicityIntegrationTests.Import_SimulatedSqlFailure_…_AcFr21_5` provoked its SQL failure by pre-seeding the OF's `L_D_ORDRE_FABRICATION` row, which D34 now turns into a replace; its seed became an orphan `L_D_CONSIGNES` row (still a real PK violation, AC-FR21-2/21-5/24-3 coverage kept). (2) EF Core merges the Deleted + Added pair with the same key into an UPDATE, so the `L_D_ORDRE_FABRICATION` row is never deleted; verified 2026-10-05 that production has foreign keys to it from the 7 `L_D_SECTIONCHARGE_*`, `L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PLANS_FOURS`, `L_D_PSO` (absent from the mirror schema), so this behaviour is load-bearing — documented in the persister; mirror FKs deferred (D-2). Also: precondition tables verified zero-padded in production; `L_D_PSO` PK order is `(NumeroLingot, Coulee)`. KEEP: D34 branch placement, refusal message shape, the 6 legacy-theory cases green.
- 2026-10-05, code review (`reviews/story-6-10/aggregated-report.md`, D-1 → P-9, **user decision, renegotiates the frozen block**): the user chose to keep the legacy `DeleteOF` + insert, full scope. (1) Boundaries *Never* "deleting from tables outside the 9 (`L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PRODUITS_OUTIL`, `L_D_MAM_QUAL`, …)" is lifted for those 4 tables: the replace deletes the OF's rows there too, and moves every later `L_D_OF_SUIVI.Rang` up by one, as `OrdreFabricationController.DeleteOF:577-864` does. `L_D_PLANS_FOURS`, `L_D_PSO` (and its `L_D_SOUS_PRODUITS`) and `L_D_FOURS` stay untouched: an OF named there is refused before the replace. (2) *Ask First* "do not switch to `ExecuteDelete` or a second transaction silently" is approved by the user: `Kape22Persister.DeleteOf` runs `ExecuteDelete`/`ExecuteUpdate` statements, children first and `L_D_ORDRE_FABRICATION` last (production FKs), inside an explicit transaction that the single `SaveChanges()` joins, so a SQL failure still leaves the previous OF intact in all 13 tables (AC-FR21-2). (3) The step-04 KEEP "EF merged UPDATE, load-bearing" is revoked: no column of the old OF survives, mapped or not. New minimal entities and mirrors `L_D_MAM_QUAL`, `L_D_OF_SUIVI`, `L_D_PRODUITS_OUTIL`, `L_D_REBUT` (production `sys.columns`/`sys.indexes` read 2026-10-05, OF values zero-padded); production triggers `tu_l_d_of_suivi` / `tu_l_d_ordre_fabrication` are AFTER UPDATE, fired by the re-rank as with the legacy. (4) AC "Given the D34 branch removed" verified 2026-10-05 (P-8): with the existing-OF lookup forced to `null`, 14 of the 15 `OfResendIntegrationTests` cases fail (every REFUSE_*, REPLACE, ANNULEE, fewer-rows, several-reasons and legacy-DeleteOf case); REPLACE_SQL_FAIL stays green by design.

## Design Notes

State rebuild for the theory (from production `L_D_LOG_COMMANDE`, SELECT only). File names recycle since 2020, so only the **latest** run counts:
- start = max `Id` with `Commande='GPAO'` and `Message LIKE 'Traitement du fichier GPAO ''%\<name>''.'`; end = first later GPAO line containing `<name> s'est`; not "terminé avec une erreur" → `Skip`; T = start `Date`; legacy reasons = `KAP22` lines between.
- Coulée: copy the production `L_D_COULEE` row of the Fichier's Coulée when its `DateReception` < T.
- Existing OF: a GPAO `Création d'un OF` line for `Pad(OF)` with `Id` < start → insert the Fichier's own mapped `OrdreFabrication` row (`MapFichier`), `Etat` = 1 if an `ENC` line for that OF precedes start, else 0. ponytail: only GPAO/ENC are rebuilt; later states and the 3 tables are not, enough for the 6 reference cases.
- `L_P_CONSIGNES_*`: copied whole (current production values).

Legacy reason → expected cause: `coulée froide` → FR20-5; `nombre d'OF sauvés : 0` → FR20-6; `sans consignes d'enfournement` → FR20-4; `different de la somme des lingots` / `pas de consignes pour la répartition` → FR20-2; `ne commance pas par le caractère '0'` → accepted.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: 0 failures
- `dotnet test TextToXml.sln --filter Category=Integration -m:1 --blame-hang-timeout 2m` -- expected: 0 failures, the 6 theory cases not skipped locally

## Suggested Review Order

**D34 decision: refuse or replace an existing OF**

- Entry point: padded lookup of the existing OF, after D22 and cold Coulée, before A-5.
  [`Kape22Persister.cs:129`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L129)

- Protected states 1/2/3/5/8 named as the legacy EtatOF; any other value replaces.
  [`Kape22Persister.cs:63`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L63)

- Reasons accumulated: state first, then each precondition table holding the OF.
  [`Kape22Persister.cs:318`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L318)

**Replace: legacy DeleteOF + insert, one transaction**

- Explicit transaction opened only for a replace; the single SaveChanges joins it, all or nothing.
  [`Kape22Persister.cs:220`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L220)

- `DeleteOf`: 13 tables, children first, L_D_ORDRE_FABRICATION last, L_D_OF_SUIVI re-ranked.
  [`Kape22Persister.cs:353`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L353)

**Precondition and legacy DeleteOF tables**

- Seven DbSets, key + OF columns only (3 read-only, 4 only deleted from); PSO keyed in production order.
  [`AscoLsiDbContext.cs:157`](../../src/Kape22Importer/Persistence/AscoLsiDbContext.cs#L157)

- Minimal mirrors in the versioned test schema.
  [`01-ascolsi-tables.sql:451`](../../scripts/schema/01-ascolsi-tables.sql#L451)

**Legacy parity on real rejected Fichiers**

- One case per P60/error Fichier; legacy reason mapped to the expected cause.
  [`LegacyRejectionParityTests.cs:113`](../../tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs#L113)

- Latest legacy run only (names recycle since 2020), end line anchored.
  [`LegacyRejectionParityTests.cs:158`](../../tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs#L158)

- State at T: Coulée by DateReception, OF from creation line, Etat from ENC.
  [`LegacyRejectionParityTests.cs:217`](../../tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs#L217)

**Deterministic D34 tests**

- Real 446/447/449 refused on EVC, no write.
  [`OfResendIntegrationTests.cs:34`](../../tests/Kape22Importer.Tests/OfResendIntegrationTests.cs#L34)

- Each protected state refused; ANNULEE (4) replaced.
  [`OfResendIntegrationTests.cs:57`](../../tests/Kape22Importer.Tests/OfResendIntegrationTests.cs#L57)

- One refusal per precondition table.
  [`OfResendIntegrationTests.cs:95`](../../tests/Kape22Importer.Tests/OfResendIntegrationTests.cs#L95)

- 445 then 448: 9 tables equal 448's rows, 445's L_D_KAPE22 and Coulée kept.
  [`OfResendIntegrationTests.cs:113`](../../tests/Kape22Importer.Tests/OfResendIntegrationTests.cs#L113)

- SQL failure during a replace leaves the previous OF intact.
  [`OfResendIntegrationTests.cs:145`](../../tests/Kape22Importer.Tests/OfResendIntegrationTests.cs#L145)

**Peripherals**

- Existing AC-FR21-5 test: SQL failure now from an orphan L_D_CONSIGNES row.
  [`RejectionAtomicityIntegrationTests.cs:152`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L152)

- ResetData truncates the 7 new tables.
  [`SqlServerIntegrationFixture.cs:117`](../../tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs#L117)
