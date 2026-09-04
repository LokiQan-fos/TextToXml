---
title: 'Story 2.4 - Kape22Mapper: DTO Kape22File to L_D_KAPE22 entity mapping'
type: 'feature'
created: '2026-09-04'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'bb3f85f931310908025bc05ad5704dc92c0252c3'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Kape22Importer` can deserialize a normalized XML into `Kape22File` (Story 2.3) but nothing yet turns that DTO into an insertable `L_D_KAPE22` entity, so Step 2 has no mapping stage.

**Approach:** Add `Kape22Mapper.Map(normalizedXml, sourceFileName)` returning `MapResult<L_D_KAPE22>`: it calls `P60Deserializer.Deserialize`, then copies `Kape22File.Message` (the Detail block) onto `L_D_KAPE22` by case-insensitive property name, applying Annexe B's one naming exception and skipping Annexe B's ignored properties. A reflection-based test enforces that every `Kape22FileMessage` property is either mapped or explicitly ignored, so an unmapped new field breaks the test build rather than silently dropping data.

## Boundaries & Constraints

**Always:** Map by case-insensitive property name between `Kape22FileMessage` and `L_D_KAPE22`; apply the single Annexe B exception `OFOriginInterne` (DTO) -> `OForiginInterne` (entity); skip Annexe B's ignored DTO properties (`File`, `Date`, `NumeroFichier`, `Segment`, `Element`, `KAP`, `Reserve`, any `Id` starting with `Reserve`, `ReserveSVT`) with zero errors; copy a nullable DTO value into a non-nullable entity property (`Indice`) without throwing when absent; every AC-named test carries `[Trait("AC","FR7-x")]` (CC-5) and lives in `TestCategory.Unit` — no database needed.

**Ask First:** none identified.

**Never:** touch `Kape22File.Header` or `Kape22File.Footer` in this story — `Header.NumeroFichier` (roulette) and `DateReception` are Story 2.6's derived fields, `Footer.Records` is Story 2.7's warning; never raise a `RequiredFieldMissing`-style error for a blank `Indice` here — that validation belongs to Story 2.6/FR-9; never re-implement schema validation — reuse `P60Deserializer.Deserialize` as-is for AC-FR7-1 (already green from Story 2.3).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| AC-FR7-2 happy path | Valid normalized XML (fixture `P60_847_682_001`) | `MapResult.Value` is an `L_D_KAPE22` whose `OF`, `Client`, `Coulee`, `Nuance`, `Type`, `Indice`, and every other homonymous Detail property equal the DTO's typed value | N/A |
| AC-FR7-3 completeness | Reflect over `Kape22FileMessage` properties | Every property is present in a hand-written map or the ignored list | Test/build fails if a property is in neither |
| AC-FR7-4 naming exception | DTO `Message.OFOriginInterne = "X"` | Entity `OForiginInterne == "X"` | N/A |
| AC-FR7-5 ignored properties | DTO `Filler`, `Reserve*`, `Element`, `KAP`, `Segment`, `Date` (Detail) | Not copied anywhere on the entity, `MapResult.Errors` empty | N/A |
| AC-FR7-6 Annexe B entries | Each naming-exception table row | Source property exists on `Kape22FileMessage`, target exists on `L_D_KAPE22` (reflection) | Theory fails naming the missing member |
| Indice blank (edge case) | DTO `Message.Indice == null` | `Map` does not throw; entity `Indice` left at its default (0) — no error raised here | N/A |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Kape22File.cs` -- DTO being mapped from; `Kape22FileMessage` is the Detail block, alphabetical CC-4 does not apply (generated).
- `src/Kape22Importer/Persistence/L_D_KAPE22.cs` -- target entity, 92 alphabetically-sorted properties (CC-4), NOT NULL set documented in its header comment.
- `src/Kape22Importer/P60Deserializer.cs` -- `Deserialize(string)` returns `P60DeserializeResult`; `Kape22Mapper.Map` calls this first and forwards its `Errors` (AC-FR7-1, already tested in Story 2.3).
- `_bmad-output/planning-artifacts/PRD.md:1048-1075` -- Annexe B: the authoritative naming-exception and ignored-property lists.
- `tests/Kape22Importer.Tests/P60XsdTests.cs` -- naming/style/[Trait] convention to mirror (`TestCategory.Unit`, `AcFr7_x` method suffix, `RepoLayout`/`EmbeddedResource` fixture helpers already available via `TextToXml.Tests` reference).
- `tests/Kape22Importer.Tests/AcTraitCoverageTests.cs` -- the AC->[Trait] gate every new AC-named test method must satisfy.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/Kape22MapperTests.cs` -- write AC-FR7-2, AC-FR7-4, AC-FR7-5, AC-FR7-6, and the blank-Indice edge-case tests, red first -- CC-1
- [x] `tests/Kape22Importer.Tests/Kape22MapperCompletenessTests.cs` -- write the AC-FR7-3 reflection test (compilation-barrier style, exempt from red/green per CC-1's own exemption clause) -- CC-1
- [x] `src/Kape22Importer/Kape22Mapper.cs` -- add `MapResult<T>` record (alphabetical CC-4: `Errors`, `Success`, `Value`) and `Kape22Mapper` with the naming-exception/ignored-list maps plus `Map(string, string)` -- makes the above tests pass

**Acceptance Criteria:**
- Given a valid normalized XML, when `Map` runs, then `MapResult.Value` carries every Detail property copied by name with the one Annexe B exception applied (AC-FR7-2, AC-FR7-4)
- Given the DTO's ignored properties, when `Map` runs, then none are copied and no error is raised (AC-FR7-5)
- Given `Kape22FileMessage`'s property set, when the completeness test runs, then every property is mapped or ignored, else the test fails (AC-FR7-3)
- Given each Annexe B naming-exception entry, when the parameterized test runs, then both the source and target members exist by reflection (AC-FR7-6)

## Spec Change Log

## Design Notes

`Kape22Mapper` builds its exception/ignore sets once as static readonly collections (mirrors `P60Deserializer`'s `Lazy<>` pattern for cost-once setup). The completeness test enumerates `typeof(Kape22FileMessage).GetProperties()` and asserts each `Name` is a key in the exception map, equals (case-insensitively) an `L_D_KAPE22` property name, or is in the ignored set — this is the AC-FR7-3 build gate. `sourceFileName` is accepted now (per the PRD API signature) but unused until Story 2.7's file-name coherence check; do not implement that check here.

## Verification

**Commands:**
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: all new `AC-FR7-2/3/4/5/6` tests green, `AcTraitCoverageTests` still green

**Manual checks (if no CLI):**
- Confirm no new `PackageReference` was added and no SQL Server dependency was introduced (this story is Unit-only).

## Suggested Review Order

**Mapping contract (`Kape22Mapper.Map`)**

- Entry point: deserializes via Story 2.3's gate, forwards its errors on schema failure, else builds the entity by reflection.
  [`Kape22Mapper.cs:52`](../../src/Kape22Importer/Kape22Mapper.cs#L52)

- Annexe B's one naming exception and the ignored-property set, the only two deviations from the default by-name copy.
  [`Kape22Mapper.cs:17`](../../src/Kape22Importer/Kape22Mapper.cs#L17)

- `ResolveTargetName` is the single shared lookup — production and both test files all call this, not their own copy.
  [`Kape22Mapper.cs:47`](../../src/Kape22Importer/Kape22Mapper.cs#L47)

- Null-safe default for a blank DTO value landing on a non-nullable entity column (e.g. `Indice`).
  [`Kape22Mapper.cs:83`](../../src/Kape22Importer/Kape22Mapper.cs#L83)

- `MapResult<T>` mirrors `ConversionResult`/`P60DeserializeResult`'s success-by-empty-errors shape.
  [`Kape22Mapper.cs:92`](../../src/Kape22Importer/Kape22Mapper.cs#L92)

**Completeness gate (AC-FR7-3)**

- Reflects over every `Kape22FileMessage` property and fails the test run if one is neither mapped nor ignored — the build-gate PRD Annexe B requires.
  [`Kape22MapperCompletenessTests.cs:20`](../../tests/Kape22Importer.Tests/Kape22MapperCompletenessTests.cs#L20)

**Mapping behavior tests (AC-FR7-2/4/5/6 + edge cases)**

- Happy path: ground-truth spot-checks against the raw XML, then a reflective sweep of every mapped property.
  [`Kape22MapperTests.cs:29`](../../tests/Kape22Importer.Tests/Kape22MapperTests.cs#L29)

- Deserialization-failure passthrough: a schema-non-conformant XML yields `Success=false`, `Value=null`, and the deserializer's own `PersistenceError`.
  [`Kape22MapperTests.cs:155`](../../tests/Kape22Importer.Tests/Kape22MapperTests.cs#L155)

- Blank `Indice` does not throw and defaults to `0` — `RequiredFieldMissing` validation stays out of scope (Story 2.6).
  [`Kape22MapperTests.cs:138`](../../tests/Kape22Importer.Tests/Kape22MapperTests.cs#L138)

- Naming exception (`OFOriginInterne` -> `OForiginInterne`) and the Annexe B ignored-property theory.
  [`Kape22MapperTests.cs:69`](../../tests/Kape22Importer.Tests/Kape22MapperTests.cs#L69)

- Annexe B naming-exception entries are reflection-verified to exist on both sides.
  [`Kape22MapperTests.cs:122`](../../tests/Kape22Importer.Tests/Kape22MapperTests.cs#L122)
