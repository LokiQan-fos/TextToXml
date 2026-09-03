# Deferred Work

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
