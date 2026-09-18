---
title: 'Story 4.9 — Hardening Épic 4 (A-2, A-3, A-4, A-5)'
type: 'refactor'
created: '2026-09-18'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '5280dbd71923f236b8b5164bc71a17db1d3b729f'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The Épic 4 retrospective (2026-09-17, `epic-4-retro-2026-09-17.md`) left 4 robustness gaps open on the `FichierProcessor → BundleMapper → Persister` path: `Category=Unit` success tests never assert the 9 downstream `DbSet`s (A-2); `Kape22Mapper.Map` never trims `entity.OF`/`entity.Coulee`, so 9 mappers + `Kape22Persister` each trim/don't-trim independently, with a proven `IdCoulee` desync risk (A-3); the hot/cold Coulee marker `"1"` is duplicated as a named const in `Kape22Persister` and an inline literal in `Kape22ImportBundleMapper` (A-4); a same-bundle `L_D_CONSIGNES` natural-key collision `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` would throw an uncaught `InvalidOperationException` at `AddRange` time instead of a `ConversionError` (A-5).

**Approach:** Fix each gap at its one shared root: extend the 2 existing `Category=Unit` success tests to assert all 10 `DbSet`s; trim `entity.OF`/`entity.Coulee` once inside `Kape22Mapper.Map`'s reflective loop; extract the `"1"` marker onto `Kape22ImportBundle` as a shared `public const`; add a natural-key pre-check in `Kape22Persister.PersistMapped` before `ConsignesRows.AddRange`, routing any collision through the existing `ConversionError` + REJETÉ `L_D_LOG_COMMANDE` circuit (AD-4) — never widening the `catch (... when (exception is DbUpdateException or DbException))` filter.

## Boundaries & Constraints

**Always:**
- Trim happens once, in/immediately after `Kape22Mapper.Map`'s reflective loop (`Kape22Mapper.cs:78-107`) — every downstream reader (9 mappers + `Kape22Persister`) stops trimming/not-trimming at its own site.
- The shared cold-Coulee marker lives on `Kape22ImportBundle` (a collaborator both `Kape22Persister` and `Kape22ImportBundleMapper` already reference) — extract the existing value, do not invent a new one.
- A-5's pre-check runs in-memory over `bundle.Consignes` before `context.ConsignesRows.AddRange(...)` (`Kape22Persister.cs:126`); a collision produces one `ConversionError` (`Block.File`, `ErrorCode.BusinessRuleViolation`, same shape as the existing missing-Coulee check at `Kape22Persister.cs:94`) and a REJETÉ `L_D_LOG_COMMANDE` row, and `AddRange` is never called on the colliding set.
- `Kape22Persister`'s `catch (... when (exception is DbUpdateException or DbException))` filter is untouched — it must not widen to `InvalidOperationException` (AD-4 boundary, Story 3.5's `ErrorCode.UnexpectedFailure` contract).

**Ask First:** None — all 4 fixes are fully specified by the epics.md ACs (A-2..A-5); no open design choice.

**Never:**
- No new `AD-*` invariant, no new `ErrorCode`, no new FR — this is hardening of already-declared Épic 4 behavior.
- No touching `SectionChargeRefroidissoirsMapper`/`PoidsMetriqueMapper`/`SvtMapper`'s decimal handling or the 4.3-bis scope — unrelated.
- No changing `L_D_CONSIGNES`'s `TypeConsigne`/`ConsigneGPAO` defaults — the pre-check works with today's constant `0`/`false` values.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Padded OF/Coulee Champ | `message.OF = " P1 "`, `message.Coulee = " 065718 "` | `entity.OF == "P1"`, `entity.Coulee == "065718"`; every downstream mapper output already trimmed, no per-mapper trim needed | N/A |
| Two sections share `CodeOperation` in one bundle | `CodeOpeChutage == CodeOpeDecoupe` on the mapped `L_D_KAPE22` | `PersistMapped` rejects before `AddRange`: one `ConversionError` (`BusinessRuleViolation`) naming the OF/CodeOperation, one REJETÉ log row, zero rows in any of the 10 downstream tables | Pre-check, no exception ever raised |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Kape22Mapper.cs:78-107` -- reflective per-Champ loop; add `entity.OF = entity.OF.Trim(); entity.Coulee = entity.Coulee.Trim();` immediately after it, before the existing "Legacy blank-Champ defaults" block (line 109) (A-3).
- `src/Kape22Importer/Persistence/Kape22Persister.cs:36,89,91,126` -- remove `private const string ColdConsignePits`, reference `Kape22ImportBundle.ColdConsignePits` instead (A-4); simplify `entity.Coulee.Trim()` to `entity.Coulee` (A-3, now pre-trimmed); add the natural-key pre-check right before the `ConsignesRows.AddRange` call at line 126 (A-5), modeled on the missing-Coulee rejection block at lines 91-109 (same `ConversionError`/REJETÉ-log shape).
- `src/Kape22Importer/Kape22ImportBundleMapper.cs:113` -- replace the inline `"1"` in `kape22.CodeConsignePits != "1"` with `Kape22ImportBundle.ColdConsignePits` (A-4).
- `src/Kape22Importer/Kape22ImportBundle.cs:14` -- add `public const string ColdConsignePits = "1";` on the record, the shared source both collaborators above reference (A-4).
- `src/Kape22Importer/CouleeMapper.cs:41`, `OrdreFabricationMapper.cs:38,50`, `SectionChargeChutageMapper.cs:26`, `SectionChargeDecoupeMapper.cs:24`, `SectionChargeLingotMapper.cs:24`, `SectionChargePitsMapper.cs:26`, `SectionChargePoidsMetriqueMapper.cs:23`, `SectionChargeRefroidissoirsMapper.cs:31`, `SectionChargeSvtMapper.cs:23`, `ConsignesMapper.cs:29-59` -- read-only: no code change, each already reads `source.OF`/`source.Coulee` verbatim and now receives the pre-trimmed value structurally (A-3).
- `src/Kape22Importer/Persistence/AscoLsiDbContext.cs:15-37` -- the 10 `DbSet` names (`Kape22Rows` + 9 downstream) the extended A-2 assertions enumerate.
- `tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs:91-107,190-201` -- extend `Import_CleanFichier_RunsThroughToTheInsert_AcFr13_1` and `Import_Success_ResultShape_AcFr13_5` to assert all 9 downstream `DbSet`s hold exactly one row each (A-2).
- `tests/Kape22Importer.Tests/Kape22MapperTests.cs` -- new test: a padded `OF`/`Coulee` Champ maps to a trimmed `entity.OF`/`entity.Coulee` (A-3).
- `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:285-330,433-443` -- model for A-5's new `[SkippableFact]`: mutate the reference Fichier so `CodeOpeChutage == CodeOpeDecoupe` (`TestSupport.SetChamp`/`MapMutatedBundle`), then assert the rejection shape exactly like `Persist_ColdCouleeMissingFromLDCoulee_RejectsWithNoInsertsAndBusinessRuleViolation_AcFr20_5`; also add a small non-regression test that `Kape22Persister` and `Kape22ImportBundleMapper` both read `Kape22ImportBundle.ColdConsignePits` (A-4).

## Tasks & Acceptance

**Execution:**
- [x] `Kape22Mapper.cs` -- trim `entity.OF`/`entity.Coulee` once after the reflective loop -- root-cause fix for A-3.
- [x] `Kape22ImportBundle.cs` -- add shared `ColdConsignePits` const -- single source for A-4.
- [x] `Kape22Persister.cs` -- reference the shared const, drop the redundant local `.Trim()`, add the pre-`AddRange` natural-key check -- A-3/A-4/A-5.
- [x] `Kape22ImportBundleMapper.cs` -- reference the shared const instead of the inline `"1"` -- A-4.
- [x] `Kape22FichierProcessorTests.cs` -- extend the 2 named tests' assertions to the 9 downstream `DbSet`s -- A-2, TDD red→green.
- [x] `Kape22MapperTests.cs` -- new padded-Champ trim test -- A-3, TDD red→green.
- [x] `TransactionalPersistenceTests.cs` -- new `CodeOperation`-collision integration test + A-4 non-regression test -- A-5/A-4, TDD red→green.

**Acceptance Criteria:** the 4 Given/When/Then ACs verbatim in `epics.md` § "Story 4.9 : Hardening Épic 4 (A-2, A-3, A-4, A-5)".

## Design Notes

A-5's pre-check groups `bundle.Consignes` by `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` (today effectively just `CodeOperation`, since `OF` is bundle-constant and `TypeConsigne`/`ConsigneGPAO` are always `0`/`false` per `ConsignesMapper.cs:82-88`'s own documented comment) and rejects on any group with more than one row — same one-`ConversionError`-then-REJETÉ-log shape as the existing cold-Coulee-missing block it sits beside.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: clean build.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: A-2/A-3 tests green, full suite green.
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: A-5's new `[SkippableFact]` green (or clean skip without local SQL Server, AR-12); no existing Integration test regresses.

## Suggested Review Order

**A-3: trim OF/Coulee once, at the source**

- The one trim site every downstream reader (9 mappers + Kape22Persister) now relies on structurally.
  [`Kape22Mapper.cs:112`](../../src/Kape22Importer/Kape22Mapper.cs#L112)

- The redundant second trim this fix removes -- reads the already-trimmed `entity.OF` instead of re-trimming the raw DTO field.
  [`Kape22Mapper.cs:146`](../../src/Kape22Importer/Kape22Mapper.cs#L146)

- Persister drops its own local `.Trim()`, now trusting the upstream trim structurally -- closes the `IdCoulee` desync risk.
  [`Kape22Persister.cs:88`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L88)

**A-5: same-bundle Consignes natural-key collision**

- The pre-check that catches a collision before `AddRange`, routed through the existing ConversionError/REJETÉ-log circuit.
  [`Kape22Persister.cs:110`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L110)

- The natural-key grouping itself -- `OF` rides along per the literal AC wording though it's bundle-constant today.
  [`Kape22Persister.cs:121`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L121)

- The rejection-path test: a forced `CodeOpeChutage`/`CodeOpeDecoupe` collision, asserting zero exception and zero rows in all 10 downstream tables.
  [`TransactionalPersistenceTests.cs:427`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L427)

- The nested-catch test (review finding): the collision's own REJETÉ-log `SaveChanges` failing too, asserting both errors survive.
  [`TransactionalPersistenceTests.cs:471`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L471)

**A-4: one shared ColdConsignePits constant**

- The single shared source both collaborators now reference, replacing the duplicated `"1"` literal.
  [`Kape22ImportBundle.cs:19`](../../src/Kape22Importer/Kape22ImportBundle.cs#L19)

- `Kape22ImportBundleMapper`'s inline literal replaced with the shared constant.
  [`Kape22ImportBundleMapper.cs:113`](../../src/Kape22Importer/Kape22ImportBundleMapper.cs#L113)

- The non-regression test, widened during review to also catch a future `public` reintroduction of the duplicate.
  [`TransactionalPersistenceTests.cs:508`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L508)

**A-2: extend the two Category=Unit success tests to the 10 downstream DbSets**

- The shared assertion helper -- 7 tables single, 2 legitimately empty on this fixture (verified against the real fixture, not assumed).
  [`Kape22FichierProcessorTests.cs:216`](../../tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs#L216)

- First of the two extended tests.
  [`Kape22FichierProcessorTests.cs:92`](../../tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs#L92)

- Second of the two extended tests.
  [`Kape22FichierProcessorTests.cs:193`](../../tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs#L193)

**Peripherals**

- The A-3 padded-Champ trim test.
  [`Kape22MapperTests.cs:129`](../../tests/Kape22Importer.Tests/Kape22MapperTests.cs#L129)

- The shared collision-bundle fixture builder both A-5 tests reuse.
  [`TransactionalPersistenceTests.cs:561`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L561)
