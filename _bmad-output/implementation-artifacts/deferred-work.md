# Deferred Work

## Deferred from: Story 2.7 (2026-09-07)

- **AC-FR10-7's persistence + logging half is not exercised** — Story 2.7 delivers the mapper side:
  `Kape22Mapper.Map` now returns `MapResult.Warnings` and a Fichier whose only defects are coherence
  Warnings still yields an entity with an empty `Errors`. The "inserted into `L_D_KAPE22`,
  `L_D_LOG_COMMANDE` status OK, Warnings land in `MQTTnetServices.Logs`" assertion needs Story 2.8
  (transactional persistence) and Epic 3 Story 3.3 (the `MQTTnetServices.Logs` sink). Covered by a
  unit test on the mapper only until then (`CoherenceWarningsTests.Map_FichierWithOnlyCoherenceWarnings_StillProducesEntity_AcFr10_7`).
- **`MapResult.Warnings` is not yet folded into a `ConversionResult`** — the Story 2.8 orchestrator
  owns merging Step 1 `ConversionResult.Warnings` (SegmentMismatch) with the mapper's FR-10 Warnings
  and sorting the combined list by `LineNumber` (AC-FR6-4). `Kape22Mapper` returns its Warnings
  unsorted.
- **The existing `Kape22MapperTests` call sites still use `Map(xml, name)` with no clock** — the
  deferred item from Story 2.6 was to fold a fixed clock in "when Story 2.7 edits that file"; Story
  2.7 added a new file (`CoherenceWarningsTests`, fixed clock) instead of editing `Kape22MapperTests`,
  so those call sites still run on `TimeProvider.System`. Still not flaky (reference `Date` "200"
  valid every year); fold in next time that file is touched.
- **`CoherenceWarningsTests` copies the mapper test scaffolding verbatim** — `FixedTimeProvider`,
  `WinterClock`, `ConvertReferenceFichier`, `ReadValidFixture` and the `ReferenceFichierName` const are
  duplicated from `DerivedFieldsTests` (Story 2.6). Same recurring pattern already tracked for the
  embedded-resource helpers; the shared `TestSupport` in `TextToXml.Tests` (Epic 1 retro A-1) now
  exists, so the fold-in target is there. Do it in the next test hardening pass that touches these
  files.

## Deferred from: code review of story 2.6 (2026-09-07)

- **`ParisTimeZone` is resolved in a `static` field initializer** — on a globalization-invariant host
  or one without ICU / tz data, `TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")` would throw a
  `TypeInitializationException` on first touch of any `Kape22Mapper` static member (`IsIgnored`,
  `ResolveTargetName`, …), which `RequiredFieldCheck` and `StartupCompatibilityCheck` also use. Not a
  concern on the .NET 10 / Windows Server target (`InvariantGlobalization` is not set, ICU is bundled).
  Move to a `Lazy<TimeZoneInfo>` with a `"Romance Standard Time"` fallback if a leaner runtime is ever
  targeted.
- **The Paris clock is read twice per `Map` call** — once as `ParisNow(timeProvider)` for
  `DateReception`, once inside `TryConvertHeaderDate` for the year. With `TimeProvider.System` the two
  reads can straddle a New Year / DST boundary. Negligible for a file-import worker; the clean fix
  reworks the deliberate `TryConvertHeaderDate(string, TimeProvider, out DateTime)` seam to take a
  pre-resolved instant, so it waits until Story 2.8 wires the real clock.
- **`Kape22Mapper.DefaultForNonNullable`'s value-type branch (`int` -> `0`) is now unreachable for a
  persisted entity** — `Indice` was the only non-nullable value-type column fed by a Champ, and a
  blank `Indice` is now rejected by `RequiredFieldCheck` before any `Value` is produced. The branch is
  harmless (still correct if a future non-nullable derived column appears); simplify or delete it when
  the mapper is next touched. Its only direct assertion left with `Map_BlankIndice_DoesNotThrowAndDefaultsToZero`.
- **The Story 2.4 / 2.7 `Kape22MapperTests` call sites use `Map(xml, name)` with no clock** — they now
  run on `TimeProvider.System`. Not flaky in practice (the reference fixture's `Date` "200" is valid
  every year and no required Detail Champ is blank), but they should take a fixed clock for
  determinism (AR-12). Fold in when Story 2.7 edits that file anyway.

## Resolved by: Story 2.6 (2026-09-07)

- **How Story 2.6 recombines `DateEnfournementFour1/2` `_Date` / `_Heure`** (deferred from review 2.2)
  — it does not. AC-FR9-5 / D14: the four string slices stay in `Kape22Mapper.IgnoredProperties`, the
  `DateEnfournementFour1/2` `DateTime?` columns are left `NULL`, and `DerivedFieldsTests` covers both
  the reference Fichier and a Fichier where the four Champs carry values.
- **`RequiredFieldCheck.Check` has no production caller** (deferred from review 2.5) — `Kape22Mapper.Map`
  now calls it after the Annexe B copy, so a blank NOT NULL column (Detail Indice for AC-FR9-4, and the
  string columns `Client` / `Coulee` / `Nuance` / `OF` / `Type` from the 2.5 note) rejects the Fichier
  with `RequiredFieldMissing`. The Story 2.8 orchestrator still owns turning that `MapResult` into a
  file move / log line.
- **Blank NOT-NULL string columns mapped through as empty string with no error** (deferred from
  spec/review 2.4) — resolved by the same `RequiredFieldCheck.Check` wiring.

## Deferred from: Story 2.6 (2026-09-07)

- **AC-FR9-1 range tightened from the literal "1..366" to the length of the current Paris year** —
  `TryConvertHeaderDate` rejects `"366"` in a non-leap year so a converted date can never roll into
  the next year (the AC's "jour dans l'année courante" intent). If the source system is ever found to
  emit `"366"` in a 365-day year as a sentinel, revisit with the PM.
- **The converted day-of-year `Header.Date` value is validated but not persisted** — no `L_D_KAPE22`
  column consumes it in Epic 2. `Kape22Mapper.TryConvertHeaderDate` is public so the value is
  reachable, but `Map` only enforces validity (`InvalidDate`). Surface it on the result (or a
  successor type) when Story 2.8 needs "a file date".
- **`Kape22Mapper.Map` stayed a `static` method** — the PRD reference API (`public sealed class
  Kape22Mapper` with an instance `Map`) is still not matched; `TimeProvider` is threaded as an
  optional third parameter instead of constructor-injected. Revisit if Story 2.8's DI wiring makes an
  instance mapper cleaner.
## Resolved by: Story 2.5 (2026-09-07)

- **`Kape22Mapper.Map`'s reflection `SetValue` type-alignment assumption** — mitigated at worker
  startup by `StartupCompatibilityCheck.Verify` (AC-FR8-1): a Champ `datatype` that does not fit the
  `L_D_KAPE22` column CLR type aborts host startup. The `SetValue` call site itself is unchanged, so
  a type mismatch that reaches it at runtime (e.g. a Descripteur edited without redeploy) is still
  unguarded — see the residual item under the Story 2.5 review below.
- **Blank NOT-NULL columns mapped through as empty string with no error** — `RequiredFieldCheck.Check`
  (AC-FR8-5 / AC-FR8-6) returns one `RequiredFieldMissing` per empty NOT NULL Detail column, ordered
  by the Descripteur `<value>` Position. `Kape22Mapper.Map` itself is unchanged, so the Story 2.4
  blank-`Indice` test stays green.

## Deferred from: code review of story-2.5 (2026-09-07)

- **`RequiredFieldCheck.Check` has no production caller** — delivered with unit tests only; the
  orchestrator that runs it per Fichier and turns its `ConversionError`s into a rejection is Story
  2.8. AC-FR8-5 / AC-FR8-6 are accepted on unit behaviour alone until then.
- **`Kape22Mapper.Map`'s reflection `SetValue` is still unguarded at the call site** — the FR-8
  startup check catches an incompatible embedded Descripteur, but a runtime type mismatch that
  reaches `SetValue` (Descripteur changed without a redeploy, or a path the check does not cover)
  still throws a raw reflection exception. Add a guarded conversion in `Map` in a Story 2.4/2.8
  hardening pass.
- **`RequiredFieldCheck` / `StartupCompatibilityCheck` trust the `L_D_KAPE22` NRT annotations** —
  `IsRequired` reads `NullabilityInfoContext.WriteState`; if the entity is ever re-scaffolded with a
  `#nullable disable` region, `WriteState` is `Unknown`, every required string column silently drops
  out and the check becomes a no-op for strings. `SqlColumn.IsNullable` (already parsed for the
  parity test) is an authoritative fallback. Not imminent — `Directory.Build.props` sets
  `<Nullable>enable</Nullable>` and the entity is hand-maintained.
- **No `ILogger` records that the FR-8 startup check ran and passed** — in production there is no way
  to tell "check passed" from "check never wired". Not required by any AC-FR8; Epic 3 (Story 3.3 /
  3.4) owns worker logging and launcher integration — add a "FR-8 check passed: N mapped Champs
  verified" line there.
- **Embedded-descriptor reader still copied in test code** — `EmbeddedDescriptor` now backs the two
  production sites, and `StartupCompatibilityTests` / `RequiredFieldMissingTests` use it, but
  `Kape22MapperTests` / `P60XsdTests` / `P60DescriptorTests` still carry their own
  `GetManifestResourceStream` helpers. Fold into a shared `TestSupport` in the hygiene pass already
  tracked below.

## Deferred from: code review of story-2.4 (2026-09-04)

- **No test exercises a nullable DTO field actually blank landing as `null` on a nullable entity
  column** — only the non-nullable `Indice` blank-path (`Kape22MapperTests.cs:138`) is covered.
  Real coverage gap, not tied to a stated `AC-FRx-y`; worth a follow-up test.

- **Embedded-resource test helpers (`EmbeddedResource`, `EmbeddedP60Xml`, `ReadValidFixture`) are
  copy-pasted into a third test file (`Kape22MapperTests.cs`)** — repeats a pattern already present
  in `P60XsdTests.cs`/`P60DescriptorTests.cs`. Consolidate into a shared `TestSupport` helper in a
  future hardening pass.

## Deferred from: code review of story 2.4 (2026-09-04)

- **`P60Deserializer.Deserialize` throws `ArgumentNullException` on a `null` `normalizedXml`** —
  `Validate` wraps `XmlReader.Create(new StringReader(normalizedXml), ...)` in a `try/catch
  (XmlException)` only; `new StringReader(null)` throws before that catch runs, so a `null` input
  crashes instead of coming back as a `PersistenceError`. Pre-existing from Story 2.3, surfaced while
  reviewing `Kape22Mapper.Map` (Story 2.4), which simply forwards whatever `Deserialize` returns and
  inherits the same gap. Not currently reachable — every caller passes `Converter.Convert`'s own
  non-null `Xml` output — but worth a null-guard in `P60Deserializer.Validate` in a Story 2.3/2.4
  hardening pass.

## Deferred from: code review of story 2.2 (2026-09-04)

- **`DateEnfournementFour1/2` (`DateTime?` columns) are modelled as split `_Date` / `_Heure` `string`
  Champs** — the "derive `datatype` from the target column type" rule of Story 2.2 does not apply to
  them, by design (D6: P60 has no `datetime` Champ), but nothing records how Story 2.6 (`champs
  dérivés combinés`) recombines the two string slices into one `DateTime`, and no test covers the
  `datetime` columns at all. The strengthened `P60Xml_IntDatatypeMatchesTheL_D_KAPE22IntColumns`
  still only checks the `int` direction. Capture the recombination contract in Story 2.6 and add a
  test that the two `_Date` / `_Heure` pairs are the only `<message>` Ids with no direct `L_D_KAPE22`
  column.
- **The `int`-column list is re-derived from the built EF model, not consumed from a Story 2.1 API** —
  Story 2.1's epic AC promised "la liste des colonnes `int` … exposée pour la Story 2.2", but no such
  member exists on `AscoLsiDbContext`; `P60DescriptorTests.Kape22IntColumns()` rebuilds a `DbContext`
  and duplicates the model-only `DbContextOptions` helper already in `AscoLsiDbContextModelTests`.
  Extract one shared model-only context helper for `Kape22Importer.Tests` (rule of three: this is the
  second copy — extract on the third) or expose the int-column set from the persistence project.
- **Pre-existing `Description` typos in `Templates/P60.xml` left in place** — `Emet` → "Rmetteur"
  (Émetteur), "tolérence" ×4 (tolérance), despite every `<value>` line being rewritten in this story.
  Also reconcile the header comment's "Positions 526 to 636" with `epics.md` AC-FR4-5 phrasing
  ("637 > 526"); no test asserts the real Fichier's total record length (Story 2.5 territory).

## Deferred from: code review of story 2.1 (2026-09-04)

- **No CI job runs `Category=Integration`** — `ci.yml` now states outright that the runner providing a
  SQL Server test instance "is still an open infra decision". The AR-12 harness, the `scripts/schema`
  application and the EF round-trip therefore have no automated gate; only the `Category=Unit`
  model/parity tests run in CI. Decided during the review to accept this as a known deferral (D). Fold
  the runner choice (Linux service container vs Windows-native SQL) into the Epic 2 CI story; ties to
  risk R-1. When the job lands, also address the two items below.
- **The test schema is never reset between runs** — `scripts/schema/*.sql` guard every table with
  `IF OBJECT_ID(...) IS NULL` and `SqlServerIntegrationFixture` never drops. After any edit to
  `scripts/schema/`, an existing `AscoLSI_Test` / `MQTTnetServices_Test` keeps the stale tables and the
  integration tests pass against an outdated schema — exactly the R-3 failure mode, on the layer the
  file-vs-model parity test does not cover. Add a fixture drop/recreate of the four harness tables (or
  a documented reset step) when the integration CI job is built.
- **`MQTTnetServices.dbo.Logs` / `WorkerSettings` have no EF entity and no `SchemaModelParityTests`
  coverage** — R-3 drift protection currently exists only for `L_D_KAPE22` and `L_D_LOG_COMMANDE`.
  Extend parity (or add a lightweight column check) when Story 3.3 introduces the Serilog / launcher
  persistence.
- **Schema extraction is not reproducible** — the `.sql` headers describe the `sqlcmd` /
  `INFORMATION_SCHEMA.COLUMNS` + `sys.identity_columns` method but no extraction script or query is
  committed, so R-3's "regenerate from the same source if the production schema changes" cannot be
  followed mechanically. Commit the extraction query (or a small script) alongside `scripts/schema/`.
- **MQTTnetServices connection string is not wired into production configuration** — Story 2.1 AC
  bundles `AscoLSI` **and** `MQTTnetServices` as "read from `IConfiguration`", but only `AscoLSI` has a
  production reader (`AddAscoLsiPersistence`) and a unit test. Decided during the review to defer the
  MQTT wiring to Epic 3, where its entities land; the misleading `AscoLsiConnectionConfigTests` comment
  was corrected in this pass.
- **Second test-isolation regime (commit + reset via Respawn / TRUNCATE) is not built** — only
  `TransactionScope` + rollback exists. `SqlServerIntegrationFixture` documents that commit + reset is
  "the caller's job" but provides no helper. Decided during the review to defer until Story 2.8 (the
  anti-duplicate guard, D22) — the first test that needs committed state.
- **Importer host does not compose persistence** — `Program.cs` never calls `AddAscoLsiPersistence`
  and `appsettings.json` carries no `ConnectionStrings:AscoLSI` placeholder. The extension exists and
  is unit-tested in isolation. Decided during the review to defer host wiring to the story that first
  consumes the `DbContext` (2.4 / 2.8); revisit `AddDbContext` vs `AddDbContextFactory` there, since
  the only current consumer (`Worker`) is a singleton.
- **`SqlServerIntegrationFixture` GO-batch splitter has no unit test** — the `BatchSeparator` regex is
  private and only exercised by the integration path (which itself has no CI gate). Extract the
  splitter into a small internal helper and add a `Category=Unit` test over the real `scripts/schema/`
  files so the parsing half is guarded without a database.

## Resolved: Epic 1 retrospective hygiene pass (2026-09-04)

Closed by the "story 0" hygiene pass (retro action `epic-1-retro-item-1`), commit pending:

- **Test-fixture helpers duplicated** (`Windows1252` ×5, `Row` ×2, `ReadDescriptor` / `ReadInput` /
  `FileRoot` / `Root` / `Ascii` across 4+ files) — folded into
  `tests/TextToXml.Tests/TestSupport.cs`, imported per file with `using static`. The
  `Windows1252` helper now uses the real `Encoding.GetEncoding(1252)` (provider registered in a
  static ctor) instead of the ASCII byte cast, so the name is honest. Notes from reviews 1.4, 1.6,
  1.7, 1.8.
- **Bloc→section-name mapping duplicated three times** (`BlockAssigner.SegmentControlSections`,
  `LineLengthChecker.SectionByBlock`, `NormalizedXmlBuilder.SectionByBlock`) — replaced by a single
  `src/TextToXml/DescriptorSections.cs` (`For(Block)` + `All` + name constants), also used by
  `DescriptorValidator.SectionNames`. Note from review 1.6.
- **Misleading comment in `BlockAssigner`** (`"Members are ordered alphabetically (CC-4)"` above a
  structurally-ordered array) — the array moved to `DescriptorSections.All`, comment rewritten. Note
  from review 1.5.

Still open from those same notes: unifying `Position` / `Size` offset access (three parsing styles
across the pipeline) — deferred, no Epic 2 payoff yet.

Landed (retro action `epic-1-retro-item-2`): repo `.editorconfig` + `.github/workflows/ci.yml`
(`dotnet build -warnaserror` with `EnforceCodeStyleInBuild` + `dotnet test --filter Category=Unit`),
and `AcTraitCoverageTests` — the AC→`[Trait]` aggregator gate (Story 3.6 pulled forward): the build
fails if a test named after an `AC` / `CTR` / `NFR` lacks the matching `[Trait]`. Closes the story 1.1
notes on absent `.editorconfig` and absent CI.

Landed (retro action `epic-1-retro-item-3`, PRD reconciliation): `PRD.md` §3 glossary and the FR-1
descriptor grammar now list `decimalSeparator` and `convert` on `<value>`; the `decimal` / `datetime`
canonical forms and the "datetime requires a `convert` mask" rule are captured as §0bis **D28** plus
`AC-FR1-14` / `AC-FR5-15`. Closes the review 1.8 note on the glossary.

Decided (retro action `epic-1-retro-item-3`): the blank-typed-Champ question from the review 1.6 note
is resolved as §0bis **D27** — `P60.xsd` types `int` / `decimal` / `datetime` Champs strongly
(`minOccurs="0"`), so Étape 1 **omits** the element for a blank typed Champ instead of emitting
`<Id></Id>`; the DTO gets `int?` / `decimal?` / `DateTime?`. `string` Champs keep the empty element.

Landed (retro action `epic-1-retro-item-7`): `NormalizedXmlBuilder.Normalize` now returns a nullable
canonical value — `null` for a blank `int` / `decimal` / `datetime` Champ — and `Build` skips the
element when it is null. `AC-FR5-4` / `AC-FR5-6` tests updated (`Convert_IntChampBlank_OmitsElement`,
`Convert_TrailingIntChampAbsent_OmitsElement`, `Convert_Decimal/DatetimeChampBlank_OmitsElement`).
Closes the review 1.6 note "Blank int Champ emits `<Id></Id>` which will not deserialize into a
non-nullable `int` DTO member".

Landed (retro action `epic-1-retro-item-4`): `AC-FR6-5` — `DescriptorValidator.Validate` no longer
concatenates the English `XmlException.Message` for a not-well-formed Descripteur; it emits
`"Le descripteur XML n'est pas bien formé (ligne N, position M)."`, keeping only the language-neutral
location. `AssertCleanFrenchMessage` now also rejects English framework-text tokens. Closes the
review 1.7 note.

## Deferred from: code review of story 1.8 (2026-09-03)

- **`datetime` canonical output truncates sub-second precision and has no timezone strategy** — `NormalizedXmlBuilder.NormalizeDateTime` always emits `yyyy-MM-dd[THH:mm:ss]`; a `convert` mask carrying `f`/`F` (fractional seconds) or `z`/`K` (offset) would silently lose that component or risk a local-timezone shift. No descriptor in the repo uses such a mask (`P62.xml` masks are `ddMMyy` / `dd/MM/yy HH:mm` style). Revisit when a format needs sub-second or offset-bearing timestamps.
- **AC-FR1-9 genericity is only proven for the "no header, no footer, no Segment" shape** — `fixtures/generic/message-only.xml` follows Annexe A.4 literally. The AC also mentions "présence header/footer différents de P60"; a second synthetic fixture with a non-P60 header/footer and Segment control (non-P60 markers) would exercise that path directly. Header/footer handling is currently covered only indirectly via the KAPE22-like descriptors in `NormalizedXmlTests` / `DescriptorValidationTests`.
- **PRD glossary §3 does not list `decimalSeparator` (nor `convert` on a `<value>`)** — the glossary Champ definition still reads `<value Id Position Size datatype [convert] Description>` and states the attribute list is exhaustive, but Story 1.8 consumes `decimalSeparator` and CTR-1 names it. Update the PRD glossary and the FR-1 descriptor grammar so descriptor and spec agree.
- **Implied-scale decimal (`convert` on a `decimal` Champ) is out of scope for v1** — `NormalizeDecimal` is driven by `decimalSeparator` only; a fixed-width field like `SerrageBil` `Section` (`convert="{0:000.0}"`, raw `1234` meaning `123.4`) would normalize to `1234`. Deliberate per the Story 1.8 contract decision. Implement when a live format requires an implied decimal point.
- **AC-FR16-2 has only a proxy assertion** — `FormatIsolationTests.TextToXml_CarriesNoFormatArtifact_AcFr16_2` checks the library side (no `.xml`/`.xsd`/`EmbeddedResource` in `src/TextToXml`) but does not enumerate the format variation points or scan `Kape22Importer` types for P60 literals, because those artifacts do not exist yet. Strengthen the test in Epic 2 once `P60.xml`/`P60.xsd`/the DTO/the entity exist.
- **New test-fixture helpers duplicated** — `ReadDescriptor` / `ReadInput` / `Message` / `FileRoot` are copied between `ExtendedTypesTests` and `GenericFormatTests`, extending the existing story 1.7 note on `Windows1252` / `Row`. Fold all fixture-path plumbing onto `RepoLayout` (or a `GenericFixtures` helper) in the same hygiene pass.

## Deferred from: code review of story 1.7 (2026-09-03)

- **Test helpers `Windows1252(string)` and `Row(...)` duplicated verbatim across 4+ test files** — `ConversionResultContractTests`, `NormalizedXmlTests`, `LineLengthTests`, `BlockAssignmentTests`, `InputDecodingTests` each carry their own copy (comments included). Extends the story 1.4 deferred note ("extract into a shared test utility when a third copy appears" — that threshold is now passed). When extracting: `Windows1252(string)` is actually an ASCII / Latin-1 byte cast (`text.Select(c => (byte)c)`), **not** CP1252 — bytes 0x80–0x9F map wrong (`€`, `Œ`, `™`, …). Rename to make the ASCII-only scope explicit, or switch to `Encoding.GetEncoding(1252)`. Reason: pre-existing pattern, not introduced by story 1.7, not blocking.
- **`LayoutInvalid` from a malformed descriptor embeds the raw `XmlException.Message` (English framework text)** — `DescriptorValidator.Validate` (`src/TextToXml/DescriptorValidator.cs:34`) produces `"Le descripteur XML n'est pas bien formé : <XmlException.Message>"`; the tail is English ("Unexpected end of file has occurred..."). Tension with AC-FR6-5 ("Message ... en français"). Introduced in story 1.2. The story 1.7 `AssertCleanFrenchMessage` check passes on the French prefix. Consider a fully French rendering (own wording for the well-formedness failure, or map common `XmlException` cases) in a story 1.2 hardening pass.

## Deferred from: code review of story 1.6 (2026-09-03)

- **`decimal` / `datetime` datatypes are silently treated as `string`** — `NormalizedXmlBuilder.Normalize` only special-cases `int`; `decimal` and `datetime` (both accepted by `DescriptorValidator`) fall through to `TrimEnd` and are emitted unnormalized. Story 1.8 (CTR-1/CTR-2) owns their normalization; no current descriptor or fixture uses them (D6: P60 has none), so not reachable today.
- **Blank `int` Champ emits `<Id></Id>` which will not deserialize into a non-nullable `int` DTO member** — tension between AC-FR5-4 ("" → empty element) / AC-FR5-6 and AC-FR5-12 ("deserializable without a custom converter"). To be resolved by the `P60.xsd` nullability / `minOccurs` decision in Story 2.3. AC-FR5-4 mandates the empty element for now.
- **Typed non-last Champ can be silently truncated** — `LineLengthChecker` (Story 1.5) only guarantees a non-last Champ's *starting* Position is covered, so `NormalizedXmlBuilder.ExtractRawValue` can clamp a middle Champ's slice mid-content (e.g. `"005"` out of `"0059000"`), and an `int` field then normalizes that to a plausible but wrong number. Pipeline-wide design point (FR-4 only requires start Position). Consider validating full declared `Size` for typed non-last Champs in a Story 1.5 / Épic 2 hardening pass.
- **Bloc→section-name mapping now duplicated three times** — `BlockAssigner.SegmentControlSections` (tuple array), `LineLengthChecker.SectionByBlock` (dict) and `NormalizedXmlBuilder.SectionByBlock` (identical dict). Extract one shared accessor in a hygiene pass; ties to the existing deferred note on the `BlockAssigner` comment.
- **Per-Ligne rework in `NormalizedXmlBuilder.Build`** — the section lookup, `.Elements("value")` enumeration and `int.Parse` of `Position` / `Size` are redone for every Detail Ligne (O(lignes × champs) tree walks and parses). Precompute the champ list and parsed offsets per section once. Trivial for P60 (3 Lignes); revisit if NFR-2 (500 Fichiers < 30 s) shows pressure.
- **Inconsistent offset access across the pipeline** — `NormalizedXmlBuilder` and `LineLengthChecker` re-parse `Position` / `Size` with `int.Parse((string)attr, NumberStyles.None, CultureInfo.InvariantCulture)`, while `BlockAssigner` uses the `(int)attribute` cast. Unify via a single accessor on the validated Descripteur in the same hygiene pass.

## Deferred from: code review of story 1.5 (2026-09-03)

- **Misleading comment in `BlockAssigner`** — the comment above `SegmentControlSections` (`src/TextToXml/BlockAssigner.cs:15`) states "Members are ordered alphabetically (CC-4)" but the tuple array is ordered `Header / Detail / Footer` (structural order, not alphabetical). Introduced in Story 1.4. Either fix the comment or genuinely sort the array in a Story 1.2/1.4 follow-up hygiene pass.

## Deferred from: code review of story 1.4 (2026-09-03)

- **`DescriptorValidator` accepts a zero-width Segment field (`Size="0"`)** — `IsNonNegativeInteger` allows `0`, so `CheckSegments` slices an empty `rawValue` that never equals a non-empty marker and emits a `SegmentMismatch` warning on every Ligne of that Bloc. Pathological descriptor; fold into a broader Story 1.2 descriptor-validation hardening pass (reject zero-width fields, and possibly non-empty `*Marker` when `segmentField` is set).
- **`Windows1252(string)` test helper duplicated verbatim** between `BlockAssignmentTests` and `InputDecodingTests` (comment included). Extract into a shared test utility when a third copy appears.

## Deferred from: code review of story 1.1 (2026-09-02)

- **`.editorconfig` absent** — house style (English comments per `CLAUDE.md`, naming, redundant `using` directives despite `ImplicitUsings`) is unenforced. Add a repo `.editorconfig` as a dedicated hygiene task.
- **No CI workflow** — the `SolutionStructureTests` build gate and the `Category=Unit` / `Category=Integration` split only guard anything if a pipeline runs them on push. Fold into Story 2.1 (which already needs a Docker-capable runner, AR-12) or a dedicated CI story. Ties to risk R-1.
- **`Kape22Importer.Tests` has no executing test** — no DI smoke test that `Host.CreateApplicationBuilder` composes and `Worker` is registered as an `IHostedService`. Add when the worker is built out in Épic 3.
- **Worker template dead code** — `Program.cs` / `Worker.cs` are the unmodified `dotnet new worker` scaffold (`Task.Delay(1000)` sample loop, no `OperationCanceledException` handling on shutdown). Replace in Épic 3 (FR-12 / FR-13 orchestration).
