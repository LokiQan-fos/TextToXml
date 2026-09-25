---
title: 'Story 5.0 — Fichier journal: IFichierJournal + LSI implementation (L_D_LOG_COMMANDE)'
type: 'feature'
created: '2026-09-25'
status: 'done'
baseline_commit: '51a93b94d94ab5064cff9b23cfec3f4e6d4891e4'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Each Fichier → XML format must journal the result of its import, but the journal is an application concern: today the LSI journal (`L_D_LOG_COMMANDE`) lives inside the P60 library, so a new format would either depend on P60 or copy its rules, and another target application could not plug its own journal.

**Approach:** A dependency-free library `src/FichierJournal` defines `IFichierJournal.Record(FichierJournalEntry)`. A library `src/AscoLsiJournal` implements it for LSI: one `L_D_LOG_COMMANDE` row per entry, in its own write, outside any business transaction (D31, FR-23).

## Boundaries & Constraints

**Always:** Strict TDD (CC-1), English comments (CC-2/CC-3), alphabetical ordering (CC-4), glossary vocabulary (CC-5), no secret in code (CC-7). `FichierJournal` references nothing. `AscoLsiJournal` references only `FichierJournal` + `Microsoft.EntityFrameworkCore.SqlServer`. Entry = `Commande`, `FichierName`, `Instant` (UTC), `NumeroFichier?`, `OF?`, `Reasons` (empty = success). LSI row (D8): `Commande` from the entry, `Message` = `<NumeroFichier> — OK` or `<NumeroFichier> — REJETÉ : <reasons joined by " ; ">`, `OF` from the entry, `Date` = `Instant` in Paris time, `NumLingot = 0`, `Trace = true`, `User` = configured initiating server (machine name when blank; longer than the column ⇒ `ArgumentException` at construction). No row when `OF` is blank (D15). One fresh context and one `SaveChanges` per `Record`; any failure propagates to the caller, nothing is swallowed. Entity and EF mapping database-first, no migration (AD-5).

**Ask First:** Any change under `src/Kape22Importer/` or `src/TextToXml/`; any other NuGet package.

**Never:** Migrating P60 onto the interface (separate story, `deferred-work.md`). Format logic (P89) in these libraries. Retry or buffering inside the journal.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Success | `Reasons` empty, OF `2039841`, NumeroFichier `013` | one row `013 — OK`, OF `2039841`, Paris `Date`, `NumLingot 0`, `Trace 1`, configured `User` | N/A |
| Failure | two reasons, OF readable | one row `013 — REJETÉ : r1 ; r2` | N/A |
| OF unreadable | `OF` null or blank | no row | N/A |
| Blank user | initiating server `""` / `" "` | `User` = machine name | N/A |
| User too long | > 50 characters | constructor throws `ArgumentException` naming the setting | N/A |
| Database unreachable | `SaveChanges` throws | exception propagates, no row | caller decides |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Persistence/L_D_LOG_COMMANDE.cs`, `LogCommandeColumnLengths.cs` -- entity and column lengths to copy (read-only here).
- `src/Kape22Importer/Persistence/AscoLsiDbContext.cs:56-67` -- EF mapping of the table (`ToTable`, key, identity, `HasMaxLength`) to mirror.
- `src/Kape22Importer/ParisTime.cs` -- Paris zone resolution (IANA then Windows id) to copy.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:427-476` -- `BuildLogRow` / `ResolveUser`: the D8 row rules and user fallback to reproduce.
- `src/P89Converter/P89FolderConverter.cs` (`TryLog`, `ResolveUser`) -- the Story 5.1 draft of the same logic; it moves here, Story 5.1 then consumes the interface.
- `tests/Kape22Importer.Tests/SqlServerIntegration{Fixture,Collection}.cs` -- AR-12 harness, linked into the new test project; `P89Converter.Tests.csproj` shows the linking pattern.
- `scripts/schema/01-ascolsi-tables.sql` -- `L_D_LOG_COMMANDE` DDL (`Message` NVARCHAR(MAX)).

## Tasks & Acceptance

**Execution:**
- [x] `tests/AscoLsiJournal.Tests/` -- red first: Unit tests on EF InMemory for each matrix row, a structure test for the reference boundaries, `AcTraitCoverageTests`, one Integration test (AR-12) reading back a committed row with no ambient transaction.
- [x] `src/FichierJournal/` -- `IFichierJournal`, `FichierJournalEntry`.
- [x] `src/AscoLsiJournal/` -- `AscoLsiFichierJournal`, `AscoLsiJournalDbContext`, `L_D_LOG_COMMANDE`, `LogCommandeColumnLengths`, `ParisTime` (internal).
- [x] `TextToXml.sln`, `README.md` -- add the three projects; one table row per library.

**Acceptance Criteria:**
- AC-FR23-1: given the csproj files, then `FichierJournal` has no `ProjectReference`/`PackageReference` and `AscoLsiJournal` references exactly `FichierJournal` + EF SqlServer.
- AC-FR23-2: Success and user rows of the matrix.
- AC-FR23-3: Failure and OF-unreadable rows.
- AC-FR23-4: given a reachable SQL Server and no ambient transaction, when `Record` returns, then the row is committed; given `SaveChanges` failing, then the exception propagates and no row exists.

## Design Notes

The P60 copies of the entity, lengths, `ParisTime` and row rules stay in `Kape22Importer` until the P60 migration story deletes them; this duplication is temporary and recorded in `deferred-work.md`. `Record` is synchronous, like the tick that calls it.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- 0 warnings.
- `dotnet test tests/AscoLsiJournal.Tests` -- green, Integration included (test data may be reset).
- `git diff --stat 51a93b9 -- src/TextToXml` -- empty. (`src/Kape22Importer.csproj` still carries the Story 5.1 draft `InternalsVisibleTo`; Story 5.1 reverts it when `P89Converter` switches to the interface. It is not part of this story's commit.)

## Spec Change Log

- 2026-09-25, step-04 review (patches only, no loopback): ambient `TransactionScope` suppressed in `Record` (D31, proven red by a rolled-back caller transaction test); `OF` and `User` written as given (no `Trim`, matches the spec and P60); AC-FR23-4 unit test asserts no row; integration test checks every column and an accented REJETÉ row; DbContext comment corrected (EF does not enforce `HasMaxLength`); exception parameter name; CC-4 order in the test csproj; trait floor 9.

## Suggested Review Order

**The contract a format sees**

- One method, no dependency: the whole boundary between a format and its application journal.
  [`IFichierJournal.cs:7`](../../src/FichierJournal/IFichierJournal.cs#L7)

- Entry shape; no reason means success.
  [`FichierJournalEntry.cs:26`](../../src/FichierJournal/FichierJournalEntry.cs#L26)

**LSI implementation**

- D15 skip, then D8 wording.
  [`AscoLsiFichierJournal.cs:29`](../../src/AscoLsiJournal/AscoLsiFichierJournal.cs#L29)

- Suppressed ambient transaction: the journal survives a caller's rollback.
  [`AscoLsiFichierJournal.cs:41`](../../src/AscoLsiJournal/AscoLsiFichierJournal.cs#L41)

- User fallback and length check at construction.
  [`AscoLsiFichierJournal.cs:59`](../../src/AscoLsiJournal/AscoLsiFichierJournal.cs#L59)

- Journal-only context, same mapping as P60's.
  [`AscoLsiJournalDbContext.cs:12`](../../src/AscoLsiJournal/AscoLsiJournalDbContext.cs#L12)

**Tests**

- The D31 proof against the real SQL Server.
  [`AscoLsiFichierJournalIntegrationTests.cs:43`](../../tests/AscoLsiJournal.Tests/AscoLsiFichierJournalIntegrationTests.cs#L43)

- Matrix rows on EF InMemory.
  [`AscoLsiFichierJournalTests.cs:25`](../../tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs#L25)

- Reference boundaries (AC-FR23-1).
  [`JournalProjectStructureTests.cs:1`](../../tests/AscoLsiJournal.Tests/JournalProjectStructureTests.cs#L1)
