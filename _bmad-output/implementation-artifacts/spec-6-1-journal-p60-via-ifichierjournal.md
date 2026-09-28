---
title: 'Story 6.1 — P60 journal via IFichierJournal'
type: 'refactor'
created: '2026-09-28'
status: 'done'
baseline_commit: '89a75fb64c0e2db879a64e024a394fce0a131605'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Kape22Persister` writes its own `L_D_LOG_COMMANDE` rows inside the AD-1 `SaveChanges()`, with private copies of the LSI entity, column lengths and row rules; an SQL failure leaves no journal row, and the D22 guard reads the "— OK" log row.

**Approach:** The persister calls `IFichierJournal` after the business transaction for every outcome with a readable OF (success, rejection, SQL failure). The D22 guard moves onto `L_D_KAPE22` (`NumeroFichier` + `OF`). `GpaoImportP60` composes `AscoLsiFichierJournal` the same way `GpaoConvertP89` does (FR-24, D31, D22 revised).

## Boundaries & Constraints

**Always:** CC-1 (the AC-FR24-6 test is a characterization test: green on the baseline, then kept green), CC-2, CC-4, CC-5, CC-7; AD-1, AD-4, AD-5. `Kape22Importer` references `FichierJournal`, never `AscoLsiJournal`. `Message` wording is unchanged: each P60 message tail becomes one `Reasons` entry (`Summarize(errors)`, the Coulee message, the business-rule message). Config keys `Import:Commande` / `Import:InitiatingServer` are unchanged. `IFichierJournal` gains `bool HasSuccess(FichierJournalEntry entry)`; LSI answers from its own "— OK" message rule, so nothing is duplicated. Existing tests that read `L_D_LOG_COMMANDE` are adapted, never deleted: Unit tests use a recording fake journal, Integration tests use the real `AscoLsiFichierJournal` (the test project may reference `AscoLsiJournal`).

**Ask First:** Any change to `src/TextToXml/`, to `AscoLsiFichierJournal.Record`'s D8/D15 rules, to the schema scripts, or to `InboxScanner`'s outcome routing.

**Never:** Stability gate, XML export, worker hardening (Stories 6.2–6.4). Retry or buffering in the journal. Deleting `Kape22Importer.ParisTime`: it still serves D4, `DateReception` and the archive folder, and only the journal's use of it goes away.

**Renegotiated 2026-09-28 (step-04 triage, human decisions D1/D2):** (D1) a rejection with a readable OF but a blank `NumeroFichier` now starts its `Message` with the Fichier name (the `AscoLsiFichierJournal` F-1 rule) instead of an empty head; the reference-Fichier messages are unchanged (AC-FR24-6). (D2) Unit tests may use the real `AscoLsiFichierJournal` over EF InMemory in place of a recording fake; fakes remain for journal failures and entry shape.

## I/O & Edge-Case Matrix

| Scenario | State | Expected | Error handling |
|---|---|---|---|
| Success | no prior `L_D_KAPE22` row | one `SaveChanges` without a log row, then `Record` success; `InsertedId` set | N/A |
| Guard hit, journal has success | `L_D_KAPE22` row exists | no insert, no `Record`; `AlreadyImported` | N/A |
| Guard hit, no success entry | row exists (crash after commit, or legacy row) | no insert, `Record` success, `AlreadyImported` | journal throws ⇒ as below |
| Rejection | bundle Errors / Coulee / business rule | no business row, `Record` failure with unchanged tail | journal throws ⇒ PersistenceError appended, Fichier stays in `processing/` |
| SQL failure | `SaveChanges` throws | rollback, `Record` failure whose reason carries SQL Server's message (table/column/cause) | journal throws ⇒ still PersistenceError |
| Journal fails after commit | `Record` throws after success | `InsertedId` set + File-level PersistenceError "journal" ⇒ stays in `processing/`, Logs `Warning`; next tick: guard hit completes it | no duplicate business row |
| OF unreadable | Step 2 failure, no OF | no `Record` call (D15) | N/A |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Persistence/Kape22Persister.cs` -- `OkLogRowExists` :256 (guard), `LogCommandeRows.Add` :102/:188/:241/:272, `BuildLogRow` :427, `ResolveUser` :469, `PersistenceFailure` :448 (cause = inner exception message, already names table/column).
- `src/Kape22Importer/Persistence/{L_D_LOG_COMMANDE,LogCommandeColumnLengths}.cs`, `AscoLsiDbContext.cs:21,56-67` -- the P60 copies to delete.
- `src/Kape22Importer/Kape22FichierProcessor.cs:105` -- builds the persister; `Journal` :135 (Logs line; the committed-but-unjournaled case becomes a `Warning`).
- `src/Kape22Importer/ImportResult.cs`, `ImportOptions.cs:57` -- comments to update (`InsertedId` + Errors meaning; `InitiatingServer` now read by the host).
- `src/FichierJournal/IFichierJournal.cs`, `src/AscoLsiJournal/AscoLsiFichierJournal.cs:72` -- add `HasSuccess` (query `Commande` + `OF` + OK message).
- Fakes to extend: `tests/P89Converter.Tests/TestSupport.cs`, `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs`, `MicroServices/GPAO/ConvertP89.Tests/RunTickCoreTests.cs`.
- `src/Kape22Importer/InboxScanner.cs:314-321` -- File-level PersistenceError ⇒ left in `processing/` (reused as-is).
- `MicroServices/GPAO/ImportP60/Client.cs:56,150`, `GpaoImportP60.csproj`; pattern: `GPAO/ConvertP89/Client.cs:93-106`.
- Tests touching the log table (18 files): `TransactionalPersistenceTests`, `RejectionAtomicityIntegrationTests`, `DoubleJournal*`, `Kape22FichierProcessor*Tests`, `DecimalMagnitudeGuardTests`, `StringLengthGuardTests`, `PersisterConfigurationTests`, `SchemaModelParityTests`, `AscoLsiDbContextModelTests`, `EndToEndImportIntegrationTests`, `Kape22ProductionDataParityTests`, `MapResultFileMetadataTests`, `PersistenceSmokeTests`, `ConsignesCaseInsensitiveCollisionTests`, `SqlServerIntegrationFixture`.
- Reference Fichiers: `P60/P60_847_682_*`.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/JournalMessageParityIntegrationTests.cs` -- first, on the baseline: import the reference Fichiers and assert their exact `Message` values -- AC-FR24-6 golden values.
- [x] `src/FichierJournal`, `src/AscoLsiJournal` + their tests -- `HasSuccess`, red first.
- [x] `tests/Kape22Importer.Tests` -- red: recording fake journal; matrix rows; AC-FR24-1 structure test (no `L_D_LOG_COMMANDE`/`LogCommandeColumnLengths` type, reference to `FichierJournal` only); adapt the listed tests.
- [x] `src/Kape22Importer` -- persister takes `IFichierJournal` and `fichierName`; guard on `Kape22Rows`; copies and `ResolveUser` deleted; processor takes and forwards the journal; csproj references `FichierJournal`.
- [x] `MicroServices/GPAO/ImportP60` + tests -- compose `AscoLsiFichierJournal` in `ReadConfig` from `Import:InitiatingServer` (invalid value ⇒ `InvalidOperationException` naming the key); SVN commit by the user.
- [x] `PRD.md` (AC-FR11-3/6, AC-FR14-7 already revised), `README.md`, `deferred-work.md:1259` -- mark resolved.

**Acceptance Criteria:**
- AC-FR24-1..5: matrix rows plus the structure test; AC-FR24-3 integration test forces an SQL failure and reads back a REJETÉ row naming the SQL cause.
- AC-FR24-6: given the reference Fichiers, when imported after the migration, then every `Message` equals its baseline value.
- AC-FR11-3, AC-FR11-6, AC-FR14-7 (revised): the existing tests carrying these traits assert the new behavior.

## Design Notes

Production check (read-only, 2026-09-28): `L_D_KAPE22` has 17 946 rows and 17 790 distinct `NumeroFichier`+`OF` pairs. Legacy re-imported 144 pairs (up to 10×), and no `P60 … — OK` row exists yet. During a parallel run, a Fichier the legacy chain already imported is archived as "already imported", which is the intended D22 behavior. A re-sent Fichier with the same pair is now always skipped; this matches the Q-3 business note (a failed OF is reissued under a new number). SQL comparison ignores the trailing spaces on the legacy `OF`.

A permanent SQL failure keeps the Fichier in `processing/` (AC-FR15-3), so it writes one REJETÉ row per tick. The ceiling is accepted: the C-4/B-5 pre-checks make it rare, and an unreachable database writes nothing.

Implementation notes (step-03, 2026-09-28):
- `fichierName` reaches the persister through its constructor, not `Persist`: `AC-FR21-3` pins `Persist(Kape22ImportBundle)` as the single overload.
- Unit tests use the real `AscoLsiFichierJournal` over its own EF InMemory database (`InMemoryContextFactory.Journal` / `LogRows()`), which keeps every `Message` assertion. A `FailingJournal` covers journal failures, and a local `RecordingJournal` covers the entry shape (`FichierJournalMigrationTests`).
- The SQL-failure integration test (`RejectionAtomicityIntegrationTests`) now provokes a real primary-key violation on `L_D_ORDRE_FABRICATION`. `LibelleConsigneChutage` cannot overflow from the Fichier bytes (18 = 18). The old over-long `Commande` trick now fails the journal after the commit, so it tests AC-FR24-5.
- Found by the relocated schema-parity test: `AscoLsiJournalDbContext` (Story 5.0) mapped `L_D_LOG_COMMANDE.Date` as `datetime2`. It is now pinned to `datetime`, as the P60 copy did.
- `AC-FR16-1` (only `TextToXml` + `PortalSharedLibrary`) contradicted `AC-FR24-1`. It is reconciled in `PRD.md`, `FormatIsolationTests` and `SolutionStructureTests` (+ `FichierJournal`).
- CC-1: HasSuccess and the new Kape22Importer tests were red first. Both were compile failures, the second shown with `git stash` of `src/` on the baseline. AC-FR24-6 is a characterization test, green on the baseline. The variant that ran there had no journal argument: `new Kape22FichierProcessor(fixture.NewAscoLsiContext, configuration, options, WinterClock(), logger)`, 1 passed. The committed version only adds `fixture.NewJournal()` to that call.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- 0 warnings.
- `dotnet test TextToXml.sln --filter Category=Unit` -- green, gates included.
- `dotnet test TextToXml.sln --filter Category=Integration -m:1` -- green.
- `dotnet build` + `dotnet test` on `MicroServices/GPAO/ImportP60.Tests` and `ConvertP89.Tests` -- green.
- `git diff --stat 89a75fb -- src/TextToXml` -- empty.

## Spec Change Log

- 2026-09-28, step-04 review (patches only, no loopback; human decisions D1/D2 recorded in the frozen block): a D22 guard hit whose journal read or write fails now logs the `JournalPending` Warning instead of `ImportRejected` (new processor-level test); the journal error reads "Échec du journal"; PRD `AC-FR24-1` (`ParisTime` kept for D4) and `AC-FR11-7` (guard on `L_D_KAPE22`) reworded; the SQL-failure integration test asserts every dispatch table again; stale comments fixed (`InboxScanner`, `RunWithSerilog`, persister header); the dead `priorErrors` parameter was removed; "Épic" became "Epic" in new comments; `LogCommandeColumnLengths.User + 1` replaces the magic 51; `HasSuccess` has a blank-`NumeroFichier` test. Deferred: `Import:Commande` length validation at startup (Story 6.4). KEEP: journal after the AD-1 commit, guard on `L_D_KAPE22`, one reason per P60 message tail.

## Suggested Review Order

**The journal after the business transaction**

- Entry point: the persister now takes the journal and the Fichier name, one per Fichier.
  [`Kape22Persister.cs:43`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L43)

- The D22 guard reads `L_D_KAPE22`, the row the AD-1 commit actually leaves behind.
  [`Kape22Persister.cs:80`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L80)

- SQL failure: the rollback happens first, then a failure entry carries SQL Server's message.
  [`Kape22Persister.cs:203`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L203)

- The success entry comes after the commit; if the journal fails, InsertedId is kept.
  [`Kape22Persister.cs:211`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L211)

- A guard hit completes a lost success entry and never re-inserts.
  [`Kape22Persister.cs:233`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L233)

- Each P60 message tail becomes one reason, so the message wording stays the same (AC-FR24-6).
  [`Kape22Persister.cs:267`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L267)

- A journal failure becomes a File-level PersistenceError, which sends the Fichier through the existing retry path.
  [`Kape22Persister.cs:296`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L296)

**Journal contract and LSI implementation**

- The new read on the shared contract, used by the P60 guard.
  [`IFichierJournal.cs:12`](../../src/FichierJournal/IFichierJournal.cs#L12)

- `HasSuccess` reuses the same "— OK" message rule as `Record`.
  [`AscoLsiFichierJournal.cs:29`](../../src/AscoLsiJournal/AscoLsiFichierJournal.cs#L29)

- `Date` is pinned to `datetime`, as the deleted P60 mapping did.
  [`AscoLsiJournalDbContext.cs:23`](../../src/AscoLsiJournal/AscoLsiJournalDbContext.cs#L23)

**Orchestration and routing**

- The processor forwards the injected journal and the Fichier name.
  [`Kape22FichierProcessor.cs:107`](../../src/Kape22Importer/Kape22FichierProcessor.cs#L107)

- A committed import still waiting for its journal entry is logged as a Warning, not a rejection.
  [`Kape22FichierProcessor.cs:147`](../../src/Kape22Importer/Kape22FichierProcessor.cs#L147)

- The retry signal is unchanged; its comment now names the journal path.
  [`InboxScanner.cs:316`](../../src/Kape22Importer/InboxScanner.cs#L316)

**Boundaries and host composition**

- The importer references the contract only (AC-FR24-1, AC-FR16-1 reconciled).
  [`Kape22Importer.csproj:25`](../../src/Kape22Importer/Kape22Importer.csproj#L25)

- `GpaoImportP60` composes the LSI journal the way `GpaoConvertP89` does (SVN, user commit).
  [`Client.cs:90`](../../../MicroServices/GPAO/ImportP60/Client.cs#L90)

- AC-FR16-1 now also allows `FichierJournal`.
  [`PRD.md:846`](../planning-artifacts/PRD.md#L846)

**Tests**

- Baseline golden messages: rejections and the ten successes.
  [`JournalMessageParityIntegrationTests.cs:53`](../../tests/Kape22Importer.Tests/JournalMessageParityIntegrationTests.cs#L53)

- One unit test per matrix row, plus the importer boundary.
  [`FichierJournalMigrationTests.cs:30`](../../tests/Kape22Importer.Tests/FichierJournalMigrationTests.cs#L30)

- Journal fails after the commit: the retry completes it with no duplicate row.
  [`TransactionalPersistenceTests.cs:141`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L141)

- Guard hit with the journal down: a Warning, not a rejection (review patch).
  [`DoubleJournalTests.cs:277`](../../tests/Kape22Importer.Tests/DoubleJournalTests.cs#L277)

- A real primary-key violation, with its cause read back from the journal row.
  [`RejectionAtomicityIntegrationTests.cs:144`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L144)
