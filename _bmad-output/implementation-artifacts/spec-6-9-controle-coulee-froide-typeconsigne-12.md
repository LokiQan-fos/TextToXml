---
title: 'Story 6.9 — Cold-Coulée check on TypeConsigne 12'
type: 'bugfix'
created: '2026-10-02'
status: 'done'
baseline_commit: 'e1bb2d2e72a2122d4e5201b768c268976357ab57'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-6-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Kape22Persister` compares the whole 12-character `CodeConsignePits` (e.g. `"1 207 00 000"`) with `"1"`, so the AC-FR20-5 cold-Coulée check never fires on a real Fichier: `P60_847_682_407` is accepted on a Coulée absent from `L_D_COULEE`, and `408`/`430` then fail on a raw `PK_L_D_CONSIGNES` instead of the business cause. Existing tests seed a synthetic `"1"`, so AC-FR20-5 is green while dead on real data.

**Approach:** A Coulée is cold when the **first character** of `CodeConsignePits` is `1` (TypeConsigne 12, the slice `ConsignesMapper.cs:73` already decodes, as the legacy `GetConsignes("ConsignesEnfournementPits", 12)` reads it). One-line fix in the persister, covered by real-Fichier regression tests from `P60/error/` with the production state seeded in the test (no production dependency).

## Boundaries & Constraints

**Always:** AC-FR20-5 rejection keeps its current shape (`Block.File`, `BusinessRuleViolation`, message `OF '…' : la coulée '…' est introuvable dans L_D_COULEE.`, one REJETÉ journal entry, no business row); existing `CodeConsignePits = "1"` tests stay unchanged and green; null/empty `CodeConsignePits` is hot (no exception); strict TDD (red before green, CC-1); English comments (CC-2); alphabetical ordering (CC-4); no secret, no production connection in committed tests (CC-7); Integration runs with `-m:1 --blame-hang-timeout 2m`.

**Ask First:** any change outside `Kape22Persister`'s cold predicate (mapper, bundle shape, processor, schema); any test needing `L_P_CONSIGNES_*` rows copied from production for 412/428 to be accepted.

**Never:** the re-sent-OF branch (Story 6.10); the generic `P60/error/` legacy-reason theory (Story 6.10); committing or deleting `ZzLegacyRejectionReplayTests.cs` (kept, untracked, until 6.10); adding `P60/error/` to `Kape22ProductionDataParityTests`; Docker/Testcontainers.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| REAL_COLD_MISSING | `error/P60_847_682_407`, no `L_D_COULEE` `063241` | Refused, `BusinessRuleViolation`, message names `063241`; zero rows in `L_D_KAPE22` + 9 downstream tables | REJETÉ journal entry |
| REAL_COLD_MISSING_AFTER | then `error/P60_847_682_408`, same base | Same refusal, same cause, never `PersistenceError` | idem |
| REAL_COLD_EXISTING_OF | `P60_847_682_428` imported with Coulée `063196` seeded, Coulée row then deleted; `error/P60_847_682_430` | Refused, same cause naming `063196`; only 428's `L_D_KAPE22` row remains | idem |
| REAL_COLD_PRESENT | Coulée `063241` seeded; `P60_847_682_412` | Accepted | N/A |
| SYNTH_COLD_FULL_CODE | Reference bundle, `CodeConsignePits = "1 207 00 000"`, Coulée absent | Refused, AC-FR20-5 shape | idem |
| SYNTH_HOT_FULL_CODE | Reference bundle, `CodeConsignePits = "3 148 00 740"`, Coulée absent | Accepted, Coulée row inserted | N/A |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Persistence/Kape22Persister.cs:85-102` -- cold check; line 97 compares the whole code to `ColdConsignePits`. The comment at 85 (`CodeConsignePits == "1"`) must be updated to the first-character rule.
- `src/Kape22Importer/Kape22ImportBundle.cs:16-19` -- `ColdConsignePits = "1"`; its comment wrongly says `Kape22ImportBundleMapper` also references it (only the persister does) — correct it.
- `src/Kape22Importer/ConsignesMapper.cs:73` -- `(12, raw => raw.Substring(0, 1))`: the TypeConsigne 12 slice to match (read-only).
- `src/Kape22Importer/Kape22FichierProcessor.cs` -- real entry point for the real-Fichier tests (`Import(name, bytes)`); loads `ConsigneReferenceData` (labels only, `Empty` supported) so empty `L_P_CONSIGNES_*` after `ResetData` should not reject.
- `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:358-440` -- existing AC-FR20-5 tests (synthetic `"1"`) and their `Persist`/`MapMutatedBundle`/`SetChamp` pattern; 10-table emptiness assertions to reuse.
- `tests/Kape22Importer.Tests/ZzLegacyRejectionReplayTests.cs` -- untracked throwaway; shows the processor wiring (`ImportOptions`, in-memory config `Import:Commande`/`Import:InitiatingServer`, `fixture.NewJournal()`) and the 407/408, 412, 428→430 scenarios. Reference only: do not copy its production `CopyFromProduction`.
- `tests/Kape22Importer.Tests/TestSupport.cs` -- `ReadValidFixture`, `MapMutatedBundle`, `WinterClock`; `RepoLayout.ProjectFile("P60/...")` for repo-root Fichiers.
- `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs:92` -- `ResetData` truncates all 25 tables incl. `L_P_CONSIGNES_*`.
- `P60/error/P60_847_682_{407,408,430}` -- fixtures (untracked; committed with this story). `P60/P60_847_682_{412,428}` -- staged, not in HEAD: the tests depend on them, so they join this story's commit (other staged P60/P89 files stay out).

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs` -- add `[SkippableTheory]` AC-FR20-5 cases `"1 207 00 000"` (rejected, AC-FR20-5 shape, Coulée absent) and `"3 148 00 740"` (accepted); red on the cold case before the fix -- synthetic coverage of the real code shape.
- [x] `tests/Kape22Importer.Tests/ColdCouleeRealFichierTests.cs` -- new Integration class (`[Collection(SqlServerIntegrationCollection.Name)]`, `[Trait("AC","FR20-5")]`), one test per I/O row REAL_*: import through `Kape22FichierProcessor`; seed a Coulée by mapping the same Fichier's bundle and adding `bundle.Coulee` (as `Persist_ColdCouleeAlreadyOnFile_…`), delete it via EF for the 430 case; assert code, message containing `la coulée '<id>' est introuvable dans L_D_COULEE`, and 10-table row counts. 407/408/430 red before the fix -- real-data regression.
- [x] `src/Kape22Importer/Persistence/Kape22Persister.cs` -- cold predicate = `CodeConsignePits` starts with `ColdConsignePits` (ordinal, null-safe); update the comment -- the fix.
- [x] `src/Kape22Importer/Kape22ImportBundle.cs` -- comment: first-character marker of TypeConsigne 12, referenced by the persister only -- stale comment.
- [x] Tests importing a reference Fichier (`tests/Kape22Importer.Tests/*`, `TestSupport.cs`, `SqlServerIntegrationFixture.cs`, `scripts/e2e-worker-import.ps1`, MicroServices `Gpao.IntegrationTests` if affected) -- seed the Fichier's own Coulée in `L_D_COULEE` before the import, as centrally as possible (shared helper / fixture method, not per-test copies); fix the stale "Stays hot" comment in `TestSupport.cs:66` and the A-4 test comment naming `Kape22ImportBundleMapper` -- the reference Fichiers 001..010 are cold under the real rule (user decision 2026-10-02, option A). Prefer a design that leaves the existing `CodeConsignePits = "1"` tests unchanged; if one must change (e.g. a "Coulée absent" test whose Coulée is now seeded centrally), change only its setup, never its assertions, and list it in the report. Do not mutate `CodeConsignePits` to make references hot (option B rejected). Report every MicroServices file touched (SVN, committed by the user).

**Acceptance Criteria:**
- Given the full suite, when `dotnet test TextToXml.sln --filter Category=Unit` and `--filter Category=Integration -m:1` run, then 0 failures and the AC-trait gates (`AcTraitCoverageTests`, `AcCoverageCompletenessTests`) pass.
- Given the fix reverted to whole-code equality, when the new tests run, then the cold synthetic case and 407/408/430 fail.

## Spec Change Log

- 2026-10-02, step-03 (implementation finding, user-arbitrated): the planning assumption that the reference Fichiers are hot was false — `P60_847_682_001` carries `CodeConsignePits = "1 205 00 999"` (all 001..010 start with `1`), so the fix breaks 48 existing tests (22 Unit, 26 Integration incl. the E2E harness) that import a reference without its Coulée. Amended: Tasks gain the "seed the reference Coulée" task; the user approved option A (seed, centrally) over B (mutate references hot) and put the E2E harness and MicroServices tests in scope of 6.9. Avoids a green-by-mutation suite that hides the real cold behaviour. KEEP: the one-line predicate fix and the new ColdCouleeRealFichierTests / theory as delivered (red before, green after).

- 2026-10-02, step-04 (acceptance-auditor + verification-gap, user-arbitrated): the implementation changed `L_D_COULEE` assertions (Empty → Single) in 6 existing rollback/rejection tests — `TransactionalPersistenceTests.Persist_SqlFailure_…_AcFr11_5`, both `Persist_ConsignesNaturalKeyCollision_…_A5`, `RejectionAtomicityIntegrationTests.Import_SimulatedSqlFailure_…_AcFr21_5` and `…ConsignesNaturalKeyCollision_…_A5`, `DecimalMagnitudeGuardTests.Persist_OutOfGabaritScaledColumn_…_AcB5` — against the "never its assertions" task rule. User accepted them, strengthened: each asserts the remaining row is exactly the seeded one (IdCoulee, fields unchanged), and a new hot-code / Coulée-absent SQL-failure test restores the `Assert.Empty` rollback check of a staged Coulée insert. Avoids losing AC-FR11-5/21-5 coverage of the Coulée insert. KEEP: central seeding helpers; 6 `P60/error/` Fichiers committed (user decision step-01).

## Design Notes

Predicate golden example:

```csharp
bool cold = entity.CodeConsignePits?.StartsWith(Kape22ImportBundle.ColdConsignePits, StringComparison.Ordinal) == true;
if (cold && !couleeAlreadyExists) { /* unchanged rejection */ }
```

`StartsWith` equals `Substring(0, 1) == "1"` on non-empty input and is null/empty-safe; `"1"` (existing tests) still matches.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: 0 failures
- `dotnet test TextToXml.sln --filter Category=Integration -m:1 --blame-hang-timeout 2m` -- expected: 0 failures (new tests not skipped on the local instance)

## Suggested Review Order

**The fix: cold = first character of CodeConsignePits**

- Entry point: TypeConsigne 12 slice, ordinal and null-safe; rejection shape unchanged.
  [`Kape22Persister.cs:98`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L98)

- Marker constant comment now states the first-character rule, persister-only use.
  [`Kape22ImportBundle.cs:19`](../../src/Kape22Importer/Kape22ImportBundle.cs#L19)

**Real-Fichier regression (P60/error/)**

- 428 then 430: re-sent OF still rejected for the missing Coulée, never PersistenceError.
  [`ColdCouleeRealFichierTests.cs:32`](../../tests/Kape22Importer.Tests/ColdCouleeRealFichierTests.cs#L32)

- 407 then 408 rejected with the exact AC-FR20-5 message, 11 tables empty.
  [`ColdCouleeRealFichierTests.cs:52`](../../tests/Kape22Importer.Tests/ColdCouleeRealFichierTests.cs#L52)

- 412 accepted once its Coulée is on file.
  [`ColdCouleeRealFichierTests.cs:66`](../../tests/Kape22Importer.Tests/ColdCouleeRealFichierTests.cs#L66)

**Synthetic coverage and rollback**

- Full-code theory: "1 207 00 000" rejected, "3 148 00 740" accepted.
  [`TransactionalPersistenceTests.cs:416`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L416)

- Restores the staged-Coulée rollback check lost by central seeding.
  [`TransactionalPersistenceTests.cs:466`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L466)

**Reference Fichiers are cold: central Coulée seeding**

- Shared map/seed helpers; fail clearly on conversion or mapping errors.
  [`TestSupport.cs:83`](../../tests/Kape22Importer.Tests/TestSupport.cs#L83)

- Strengthened assertion replacing Empty: only the seeded row, fields unchanged.
  [`TestSupport.cs:107`](../../tests/Kape22Importer.Tests/TestSupport.cs#L107)

- Unit tier: in-memory factory seeds the reference Coulée at construction.
  [`TestSupport.cs:243`](../../tests/Kape22Importer.Tests/TestSupport.cs#L243)

- SQL tier: `Ready()` seeds by default; the theory opts out.
  [`TransactionalPersistenceTests.cs:840`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L840)

- Dispatch-emptiness helper now tolerates exactly the seeded reference Coulée.
  [`RejectionAtomicityIntegrationTests.cs:290`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L290)

- Journal parity: Coulée-missing Fichiers first, then seed, then the rest.
  [`JournalMessageParityIntegrationTests.cs:79`](../../tests/Kape22Importer.Tests/JournalMessageParityIntegrationTests.cs#L79)

**Peripherals**

- E2E harness seeds cold Coulées inside the try, removed in finally.
  [`e2e-worker-import.ps1:161`](../../scripts/e2e-worker-import.ps1#L161)

- NFR-1/2: scanners and seeding built before the stopwatch.
  [`EndToEndPerformanceTests.cs:100`](../../tests/Kape22Importer.Tests/EndToEndPerformanceTests.cs#L100)

- Failing-save context seeds through a normal context on the same store.
  [`FichierJournalMigrationTests.cs:256`](../../tests/Kape22Importer.Tests/FichierJournalMigrationTests.cs#L256)

- MicroServices (SVN, user commit): `GPAO/ImportP60.Tests/EndToEndSmokeTests.cs` seeds the reference Coulée before the tick.
