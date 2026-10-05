---
title: 'Story 6.10 — Re-sent existing OF: replacement or explicit refusal'
type: 'feature'
created: '2026-10-05'
status: 'done'
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
- `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs:92-122` -- `ResetData` truncates the 3 new tables.
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
- [x] `src/Kape22Importer/Persistence/Kape22Persister.cs` -- D34 branch; replace = load the OF's tracked rows in the 9 tables, `RemoveRange`, then the existing `Add` path, one `SaveChanges()`; update comments.
- [x] `tests/Kape22Importer.Tests/LegacyRejectionParityTests.cs` -- `[SkippableTheory]` over `P60/error/*`, Integration, `[Trait("AC","FR20-6")]`, header comment "remove at the switchover to the new worker"; delete `ZzLegacyRejectionReplayTests.cs`.

**Acceptance Criteria:**
- Given the full suite, when Unit and Integration (`-m:1`) run, then 0 failures and `AcTraitCoverageTests` / `AcCoverageCompletenessTests` pass.
- Given the 6 `P60/error/` Fichiers and production configured, when the theory runs, then 407/408/430 → AC-FR20-5 and 446/447/449 → AC-FR20-6, none skipped.
- Given the D34 branch removed, when `OfResendIntegrationTests` runs, then REFUSE_* and REPLACE fail.

## Spec Change Log

- 2026-10-05, step-04 (acceptance-auditor + edge-case-hunter, user-arbitrated, patch-level, no loopback): (1) existing test `RejectionAtomicityIntegrationTests.Import_SimulatedSqlFailure_…_AcFr21_5` provoked its SQL failure by pre-seeding the OF's `L_D_ORDRE_FABRICATION` row, which D34 now turns into a replace; its seed became an orphan `L_D_CONSIGNES` row (still a real PK violation, AC-FR21-2/21-5/24-3 coverage kept). (2) EF Core merges the Deleted + Added pair with the same key into an UPDATE, so the `L_D_ORDRE_FABRICATION` row is never deleted; verified 2026-10-05 that production has foreign keys to it from the 7 `L_D_SECTIONCHARGE_*`, `L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PLANS_FOURS`, `L_D_PSO` (absent from the mirror schema), so this behaviour is load-bearing — documented in the persister; mirror FKs deferred (D-2). Also: precondition tables verified zero-padded in production; `L_D_PSO` PK order is `(NumeroLingot, Coulee)`. KEEP: D34 branch placement, refusal message shape, the 6 legacy-theory cases green.

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

**Replace inside the single SaveChanges**

- Load-bearing: EF merges same-key Deleted+Added into UPDATE, keeping production FKs satisfied.
  [`Kape22Persister.cs:214`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L214)

- Tracked removal of the OF's rows in the 9 tables, before the existing Add path.
  [`Kape22Persister.cs:347`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L347)

**Read-only precondition tables**

- Three DbSets, key + OF columns only; PSO keyed in production order.
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

- ResetData truncates the 3 new tables.
  [`SqlServerIntegrationFixture.cs:115`](../../tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs#L115)
