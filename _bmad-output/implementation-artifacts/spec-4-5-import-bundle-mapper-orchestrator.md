---
title: 'Kape22ImportBundleMapper — orchestrator + pure business controls (Story 4.5)'
type: 'feature'
created: '2026-09-16'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: '278ce45bbe2e0fd8ae7d09298dfb8cef76f5a6ac'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** After Stories 4.3/4.4, the 4.1-4.4 mappers exist but nothing composes them into one bundle, and the three DB-free FR-20 business controls (ingot/furnace distribution, hot-Coulee format, missing enfournement instruction) aren't enforced anywhere yet.

**Approach:** A new `Kape22ImportBundleMapper.Map` calls `Kape22Mapper.Map` first (short-circuits on its failure), then composes `OrdreFabricationMapper`/`CouleeMapper`/`ConsignesMapper`/the 7 `SectionCharge*Mapper`s into a new `Kape22ImportBundle` record, running the 3 pure controls afterward and appending any failures to the bundle's `Errors`.

## Boundaries & Constraints

**Always:**
- `Kape22ImportBundleMapper.Map(string normalizedXml, string sourceFileName)` is pure: no DB read, no I/O, no reflection (AD-2), mirrors the 4.3/4.4 static-mapper style but as an instance method (needs a `TimeProvider?` ctor param to forward to `Kape22Mapper`/`OrdreFabricationMapper`/`CouleeMapper`, same optional-param pattern already used by those three).
- If `Kape22Mapper.Map`'s result has `Errors.Count > 0`, short-circuit: return a bundle carrying only its `Errors`/`Warnings`/`NumeroFichier`/`OF`, all entities `null` — never invoke the 4.3/4.4 mappers on a value that failed Steps 1/2.
- Otherwise build every entity (`OrdreFabrication`, `Coulee`, each applicable `SectionCharge*`, `Consignes`), then run the 3 pure controls, accumulating failures into one local mutable `List<ConversionError>` before constructing the final immutable `Kape22ImportBundle` — mirrors `Kape22Mapper.Map`/`CoherenceChecker`'s existing accumulate-then-freeze pattern. `Success` stays derived (`Errors.Count == 0`), never set directly.
- Add one new generic `ErrorCode.BusinessRuleViolation` member to `TextToXml.Contract`'s enum (new group, after "Blocking errors (Step 2)"), reused by all 3 controls — keeps `TextToXml` domain-neutral (CC-6: no P60 vocabulary in the shared enum), each failure still a distinct `ConversionError{Block:File, Code}` instance per AD-4, distinguished by `Message`.
- Business controls read straight from already-mapped entities: AC-FR20-2 compares `SectionChargeRefroidissoirs.NombreLingotsFour1 + NombreLingotsFour2` against `OrdreFabrication.NombreDemiProduit`; AC-FR20-3 reads `L_D_KAPE22.CodeConsignePits` (hot when `!= "1"`) and checks `L_D_KAPE22.Coulee` starts with `'0'`; AC-FR20-4 fires when `SectionChargePits` is `null`.
- `Kape22ImportBundle` is a new `sealed record` in `Kape22Importer`, properties alphabetical (CC-4), carrying `L_D_KAPE22?`, the 9 downstream entities/lists (nullable where a mapper can return null), plus `Errors`/`NumeroFichier`/`OF`/`Success`/`Warnings` mirroring `MapResult<T>`'s shape.

**Ask First:** none identified — no DB access, no signature changes to existing mappers.

**Never:** no DB reads (that's Story 4.6's cold-Coulee check); no changes inside `OrdreFabricationMapper`/`CouleeMapper`/`ConsignesMapper`/`SectionCharge*Mapper`; no new logging channel (AD-4 — logging to `L_D_LOG_COMMANDE` is Story 4.6's job); no EF navigation properties (AD-7).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Happy path (AC-FR20-1) | Valid normalized XML, all 3 controls pass | Bundle `Success=true`, carries `L_D_KAPE22` + `L_D_ORDRE_FABRICATION` + `L_D_COULEE` + each applicable `SectionCharge*` (or `null`) + `Consignes`, plus `NumeroFichier`/`OF`/`Warnings` from `Kape22Mapper` | N/A |
| Upstream failure | `Kape22Mapper.Map` returns `Errors.Count > 0` | Bundle `Success=false`, same `Errors`/`Warnings`/`NumeroFichier`/`OF`, all entities `null` | Short-circuit, no 4.3/4.4 mapper calls |
| Ingot/furnace mismatch (AC-FR20-2) | `NombreLingotsFour1 + NombreLingotsFour2 != NombreDemiProduit` | `Success=false`; dedicated `ConversionError{Block:File, Code:BusinessRuleViolation}` citing OF + both values | Entities still populated on the bundle; `Success` flag signals the (Story 4.6) persister to add nothing |
| Hot Coulee malformed (AC-FR20-3) | `CodeConsignePits != "1"` and `Coulee` doesn't start with `'0'` | `Success=false`; dedicated error citing OF + Coulee number | Same as above |
| Missing Pits (AC-FR20-4) | `SectionChargePitsMapper.Map` returned `null` | `Success=false`; dedicated error citing missing enfournement instruction | Same as above |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Kape22Mapper.cs:65` -- `Kape22Mapper.Map(string, string) -> MapResult<L_D_KAPE22>`, the upstream call to compose; `MapResult<T>` shape at line 171 to mirror for `Kape22ImportBundle`.
- `src/Kape22Importer/OrdreFabricationMapper.cs:23`, `src/Kape22Importer/CouleeMapper.cs:18` -- Story 4.3 static `Map(L_D_KAPE22, TimeProvider?)` mappers to compose as-is.
- `src/Kape22Importer/SectionChargeRefroidissoirsMapper.cs:13`, `SectionChargePitsMapper.cs:13`, and the other 5 `SectionCharge*Mapper.cs` -- Story 4.4 static `Map(L_D_KAPE22) -> L_D_SECTIONCHARGE_*?` mappers.
- `src/Kape22Importer/ConsignesMapper.cs:15` -- `Map(source, 7 nullable SectionCharge entities) -> List<L_D_CONSIGNES>`.
- `src/Kape22Importer/Persistence/L_D_ORDRE_FABRICATION.cs:52` -- `NombreDemiProduit` (`int`), the AC-FR20-2 comparison target.
- `src/Kape22Importer/Persistence/L_D_KAPE22.cs:28,52` -- `CodeConsignePits` (hot/cold source) and `Coulee` (number to format-check).
- `src/Kape22Importer/RequiredFieldCheck.cs:29`, `src/Kape22Importer/CoherenceChecker.cs:27` -- existing "accumulate errors, freeze once" pattern to mirror for the 3 new controls.
- `src/TextToXml/Contract.cs:19` -- `ErrorCode` enum to extend with `BusinessRuleViolation`.
- `tests/Kape22Importer.Tests/OrdreFabricationMapperTests.cs`, `ConsignesMapperTests.cs` -- test conventions to mirror (`[Trait("Category", TestCategory.Unit)]`, `TestSupport.ReferenceKape22()`/`WinterClock()`, `[Trait("AC","FRxx-y")]`, compile-barrier no-reflection/no-DB test).
- `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs:34` -- FR-19's registration to mirror for FR-20.

## Tasks & Acceptance

**Execution:**
- [x] `src/TextToXml/Contract.cs` -- add `BusinessRuleViolation` to `ErrorCode`, new comment group after "Blocking errors (Step 2)" -- generic, domain-neutral code reused by all 3 FR-20 controls (CC-6).
- [x] `src/Kape22Importer/Kape22ImportBundle.cs` -- new sealed record: `L_D_KAPE22?`, `OrdreFabrication`, `Coulee`, the 7 nullable `SectionCharge*`, `Consignes` (`List<L_D_CONSIGNES>`), `Errors`, `NumeroFichier`, `OF`, `Success` (derived), `Warnings`; CC-4 alphabetical order -- AD-6 carrier for Story 4.6.
- [x] `src/Kape22Importer/Kape22ImportBundleMapper.cs` -- new class: ctor `(TimeProvider? timeProvider = null)`, `Map(string normalizedXml, string sourceFileName) -> Kape22ImportBundle` composing the upstream mapper + short-circuit, then the 4.3/4.4 mappers, then the 3 pure controls -- orchestrator entry point Story 4.6 will call.
- [x] `tests/Kape22Importer.Tests/Kape22ImportBundleMapperTests.cs` -- AC-FR20-1..4 tests (happy path + 3 dedicated faulty fixtures/variants) plus a compile-barrier test asserting no `System.Reflection`/`DbContext` source references -- TDD, written first (CC-1).
- [x] `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs` -- register `FR20` in the completeness gate, mirroring the existing `FR19` entry.

**Acceptance Criteria:**
- Given a valid normalized XML, when `Kape22ImportBundleMapper.Map` runs, then the bundle carries `L_D_KAPE22`, `L_D_ORDRE_FABRICATION`, `L_D_COULEE`, each applicable `SectionCharge*` (or `null`), `Consignes`, and the `Success`/`Errors`/`Warnings`/`NumeroFichier`/`OF` metadata inherited from `Kape22Mapper` (AC-FR20-1).
- Given a mapped `SectionChargeRefroidissoirs` whose `NombreLingotsFour1 + NombreLingotsFour2 != NombreDemiProduit`, when the bundle is built, then `Success=false` and a dedicated error cites the OF and both diverging values (AC-FR20-2).
- Given a hot Coulee (`CodeConsignePits != "1"`) whose `Coulee` doesn't start with `'0'`, when the bundle is built, then `Success=false` and a dedicated error cites the OF and Coulee number (AC-FR20-3).
- Given an OF with no applicable `L_D_SECTIONCHARGE_PITS`, when the bundle is built, then `Success=false` and a dedicated error signals the missing enfournement instruction (AC-FR20-4).

## Design Notes

The legacy hot/cold call passes a `12` size/type filter (`GetConsignes("ConsignesEnfournementPits", 12)`, annex line 106) that isn't reproduced here — this mapper reads `L_D_KAPE22.CodeConsignePits` directly rather than filtering `L_D_CONSIGNES` rows, so the `12` argument doesn't apply; the annex already flags this link as `à_clarifier`, no new `deferred-work.md` entry needed since AC-FR20-3 doesn't depend on it.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: 0 warnings, 0 errors.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all pass, including the new `Kape22ImportBundleMapperTests` and the updated completeness gate.
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: all pass or skip cleanly (no DB access in this story).

## Suggested Review Order

**Orchestration entry point**

- Start here: composes the upstream mapper, short-circuits on its failure, then wires in every 4.3/4.4 mapper.
  [`Kape22ImportBundleMapper.cs:17`](../../src/Kape22Importer/Kape22ImportBundleMapper.cs#L17)

- Short-circuit shape: a failed `Kape22Mapper.Map` returns a bundle with metadata only, no downstream mappers invoked.
  [`Kape22ImportBundleMapper.cs:20`](../../src/Kape22Importer/Kape22ImportBundleMapper.cs#L20)

**The 3 FR-20 business controls**

- AC-FR20-2: ingot/furnace sum vs `NombreDemiProduit`; blank Champs are zero-filled upstream, not null here.
  [`Kape22ImportBundleMapper.cs:78`](../../src/Kape22Importer/Kape22ImportBundleMapper.cs#L78)

- AC-FR20-3: hot-Coulee format check, reads `CodeConsignePits` straight off `L_D_KAPE22` (see Design Notes).
  [`Kape22ImportBundleMapper.cs:111`](../../src/Kape22Importer/Kape22ImportBundleMapper.cs#L111)

- AC-FR20-4: a null `SectionChargePits` from Story 4.4's applicability rule is itself the violation.
  [`Kape22ImportBundleMapper.cs:130`](../../src/Kape22Importer/Kape22ImportBundleMapper.cs#L130)

**New carrier type and error code**

- `Kape22ImportBundle`: alphabetical (CC-4), mirrors `MapResult<T>`'s metadata shape plus the 9 downstream entities.
  [`Kape22ImportBundle.cs:14`](../../src/Kape22Importer/Kape22ImportBundle.cs#L14)

- One new domain-neutral `ErrorCode` shared by all 3 controls, keeping `TextToXml` free of P60 vocabulary (CC-6).
  [`Contract.cs:42`](../../src/TextToXml/Contract.cs#L42)

**Tests and completeness gate**

- Happy path: asserts every entity the bundle should carry, including the two naturally-inapplicable sections.
  [`Kape22ImportBundleMapperTests.cs:27`](../../tests/Kape22Importer.Tests/Kape22ImportBundleMapperTests.cs#L27)

- Upstream short-circuit: all 9 downstream entities stay null.
  [`Kape22ImportBundleMapperTests.cs:62`](../../tests/Kape22Importer.Tests/Kape22ImportBundleMapperTests.cs#L62)

- AC-FR20-2 mismatch, plus the zero-fill and multi-violation variants added at code review.
  [`Kape22ImportBundleMapperTests.cs:92`](../../tests/Kape22Importer.Tests/Kape22ImportBundleMapperTests.cs#L92)

- AC-FR20-3, twice: a deliberate mutation and the untouched reference fixture (already hot and malformed).
  [`Kape22ImportBundleMapperTests.cs:167`](../../tests/Kape22Importer.Tests/Kape22ImportBundleMapperTests.cs#L167)

- AC-FR20-4 missing-Pits case.
  [`Kape22ImportBundleMapperTests.cs:206`](../../tests/Kape22Importer.Tests/Kape22ImportBundleMapperTests.cs#L206)

- FR-20 registered in the AC completeness gate.
  [`AcCoverageCompletenessTests.cs:36`](../../tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs#L36)
