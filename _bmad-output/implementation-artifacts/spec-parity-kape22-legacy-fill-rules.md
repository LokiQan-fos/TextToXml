---
title: 'Kape22Mapper legacy blank-Champ fill rules (production parity)'
type: 'bugfix'
created: '2026-09-07'
status: 'done'
baseline_commit: '96ed23326c1c2952b0b75e8acc4e3d93ea0cf36b'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-2-context.md'
  - '{project-root}/_bmad-output/implementation-artifacts/deferred-work.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `Kape22ProductionDataParityTests` replays the 100 sample P60 Fichiers through the new
pipeline and diffs the mapped `L_D_KAPE22` row against the row the legacy import wrote in production
(the reference of truth, FR-7). Four columns diverge because `Kape22Mapper` copies a blank Detail
Champ verbatim instead of applying the legacy fill: `MatriculeClient` / `ChutagePied` (legacy `0`,
new `NULL`), `OForiginInterne` (legacy `NULL`, new `''`), `AcompteSolde` (legacy `'S'`, new `''`).

**Approach:** Three rules, derived from and confirmed by a full-table production profile (17 710
rows, read-only), applied in `Kape22Mapper.Map` after the Annexe B copy:

1. **General** — a blank typed integer Champ maps to `0`, not `NULL`. Production has **no `NULL` in
   any of the ~28 `int?` columns**, so the legacy import zero-fills every blank int Champ. This
   replaces the current `int? → NULL` behaviour for the whole set, not just the two divergent columns.
2. **Column-scoped** — `OForiginInterne` blank Champ maps to `NULL`. It is the **only** string column
   ever `NULL` in production (100 % of rows); its siblings `OFOrigin` / `OFDestination` /
   `OFDestinationInterne` are always `''`, so this is genuinely specific to `OForiginInterne`.
3. **Column-scoped** — `AcompteSolde` blank Champ maps to `'S'`. Production holds `'S'` in 100 % of
   17 710 rows (the only value ever seen).

Then drop `MatriculeClient` / `ChutagePied` / `OForiginInterne` / `AcompteSolde` from
`KnownLegacyDivergences` in the parity test (`Client` stays), and record the rules in Annexe B /
deferred-work.md.

## Evidence basis & what it does NOT prove

- **Investigation done** (temporary read-only harness against `AscoLSI_Production`, results below).
- **Rule 1 (int → 0):** strongly supported — 0 `NULL` across ~28 `int?` columns × 17 710 rows.
- **Rule 3 (`AcompteSolde` → 'S'):** the populated-Champ branch is **never observed** (100 % `'S'`,
  the sample Champ is always blank). Implement blank→`'S'`, populated→verbatim copy; mark the
  populated-branch AC/test `assumed, unverified`.
- **Rule 2 (`OForiginInterne` → NULL):** likewise the populated branch is unobserved; same treatment.
- **Legacy import source is NOT available** — it is the external current application, not in the
  database (no proc/view/job references `KAPE22` / `L_D_KAPE22`). The "Bounded risk" clause applies:
  the assumed populated-branch behaviour is verbatim copy, revisit when a populated sample appears.

## Boundaries & Constraints

**Always:**
- TDD strict (CC-1): one xUnit test per rule branch, named after its AC, `[Trait("AC", "...")]`,
  written and seen red before the production code. `Category=Unit`, no database.
- Rule 1 fires only when the typed Champ is blank/omitted (DTO `int?` is `null`); a real value,
  including a real `0`, is copied unchanged. Rules 2–3 fire only on a blank/whitespace string Champ.
- Reuse the "documented exception to the default copy" pattern: expose the two string-rule columns as
  a named set (`LegacyBlankFillColumns`, **entity** names) so the AC-FR7-2 sweep skips them. Rule 1
  needs no skip — folding `int? → 0` into `DefaultForNonNullable` keeps the sweep's own expectation
  aligned.
- Comments in English, one sentence above the code (`CLAUDE.md`, CC-2).

**Ask First:**
- If, during implementation, any `int?` column turns out to legitimately need `NULL` (contradicting
  the profile) — stop and report.
- Any change touching more than `Kape22Mapper` + its tests + the parity test + Annexe B/docs.

**Never:**
- Do NOT reproduce the `Client` mojibake. Raw bytes `53 4B 46 20 D6 …` confirm `0xD6` = `Ö`
  (Windows-1252); `SKF Österreic` is the correct decode, production is corrupt. `Client` stays in
  `KnownLegacyDivergences`. **Decision (b): `Client`-only**, accept that a wider parity run could
  surface other legacy-mojibake columns as false positives — diagnosable, revisit then.
- No new derived-field machinery, no `TextToXml` change, no schema/EF-model change.

## I/O & Edge-Case Matrix

Legend: **[obs]** observed in production; **[assumed]** populated branch unobserved, verbatim-copy
assumed, test carries an `assumed, unverified` comment + deferred-work note.

| Scenario | Input / State | Expected | 
|----------|--------------|----------|
| Blank typed int Champ **[obs]** | e.g. `MatriculeClient` / `ChutagePied` element omitted (DTO `null`) | entity column `0` |
| Real int value incl. `0` **[obs]** | `ChutagePied` = `2` (or a real `0`) | copied unchanged |
| Blank `OForiginInterne` **[obs]** | `<OForiginInterne></OForiginInterne>` (DTO `""`) | `entity.OForiginInterne == null` |
| Populated `OForiginInterne` **[assumed]** | Champ = `"0000012345"` | copied verbatim |
| Blank `AcompteSolde` **[obs]** | Champ blank / whitespace | `entity.AcompteSolde == "S"` |
| Populated `AcompteSolde` **[assumed]** | Champ = `"A"` | `entity.AcompteSolde == "A"` |
| Other blank string Champ **[obs]** | e.g. `GazScarfing` blank | `''` (unchanged behaviour) |

</frozen-after-approval>

## Investigation findings (2026-09-07, read-only, attached)

- **int? columns:** all ~28 have `NULL count = 0` over 17 710 rows. `PriseDeFer` is `0` in every row;
  `DiametreProduit` / `SectionLaminage` never `0` (always populated). => blank int Champ → `0`, general.
- **string? columns:** only `OForiginInterne` is ever `NULL` (17 710/17 710). `OFOrigin` /
  `OFDestination` / `OFDestinationInterne` are `''` in every row. All other string columns: `NULL count = 0`.
- **`AcompteSolde`:** `'S'` in 17 710/17 710 rows — no other value.
- **`ChutagePied`:** real distribution `2:7985, 1:4176, 0:3372, 3:1024, …` — `0` is both a real value
  and the blank fill; indistinguishable, but Rule 1 covers it.
- **Legacy import code:** absent from the database. `sys.sql_modules` matches (`ps_lsi_production_of`,
  `ps_lsi_LPB`) are reporting extracts reading `AcompteSolde` from `L_D_ORDRE_FABRICATION`, not the import.
- **Mojibake:** `P60_847_682_095` / `_151` raw `Client` = `53 4B 46 20 D6 73 74 65 72 72 65 69 63`
  → CP1252 `SKF Österreic`. Confirmed correct; production stored the mojibake.

## Code Map

- `src/Kape22Importer/Kape22Mapper.cs` -- `Map` (~74-156). Reflective Annexe B copy loop ~89-102
  (`value = source.GetValue(message) ?? DefaultForNonNullable(target.PropertyType)`); derived rules
  follow ~106-146. `DefaultForNonNullable` (~298) currently returns `null` for `int?` — change it so
  a nullable **integer** target also yields its zero value (Rule 1); this keeps the AC-FR7-2 sweep's
  `expected` in step. Add the two string fixes right after the copy loop:

  ```csharp
  // Legacy import default for two abandoned Champs (production parity, deferred-work 2026-09-07):
  // OForiginInterne is the one string column it leaves NULL; AcompteSolde it always writes 'S'.
  if (string.IsNullOrWhiteSpace(message.OFOriginInterne)) entity.OForiginInterne = null;
  if (string.IsNullOrWhiteSpace(message.AcompteSolde))    entity.AcompteSolde    = "S";
  ```

  Add `public static readonly IReadOnlySet<string> LegacyBlankFillColumns = { "OForiginInterne", "AcompteSolde" }`
  (entity names, sweep-skip only) mirroring `NamingExceptions` / `IgnoredProperties` (~40-69).
- `src/Kape22Importer/Kape22File.cs` -- generated DTO (do not edit). `AcompteSolde` / `OFOriginInterne`
  are `string`; the int Champs are `int?`.
- `src/Kape22Importer/Persistence/L_D_KAPE22.cs` -- entity; the four columns are all nullable, so
  `RequiredFieldCheck` never touches them and Rule 1 never blanks a NOT NULL column (only `Indice` is
  NOT NULL int and it is already `0`-defaulted + required-checked from the DTO).
- `tests/Kape22Importer.Tests/Kape22MapperTests.cs` -- `Map_ValidFichier_CopiesEveryHomonymousDetailProperty_AcFr7_2`
  sweep: skip `Kape22Mapper.LegacyBlankFillColumns` **by resolved entity name** (`ResolveTargetName(source.Name)` —
  `OFOriginInterne`→`OForiginInterne`). `Map_OFOriginInterneChamp_LandsOnEntityOForiginInterne_AcFr7_4`:
  switch to a mutated non-blank `OForiginInterne` so it still proves the rename.
- `tests/Kape22Importer.Tests/DerivedFieldsTests.cs` -- `PersistenceTestSupport.MapMutatedFichier` /
  `NormalizedXmlWithoutDetailChamps` are the pattern for the new tests.
- `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs` -- `KnownLegacyDivergences` (~55):
  remove the four; keep `Client`; update the header comment.
- `_bmad-output/planning-artifacts/PRD.md` -- Annexe B (~1048-1075): add a "Legacy blank-Champ defaults" note.
- `_bmad-output/implementation-artifacts/deferred-work.md` -- move to a "Resolved" note; keep the `Client` permanent line.

**Unchanged, and why:** `Kape22MapperCompletenessTests` (props still name-map); `RequiredFieldCheck`
(all four columns nullable); `StartupCompatibilityCheck` (datatype↔CLR mapping unchanged);
`Kape22Persister` (values fit the columns; the parity round-trip exercises the insert).

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/Kape22MapperTests.cs` -- 9 tests added (blank + populated per I/O row; the 2 `[assumed]` ones carry the `assumed, unverified` comment); AC-FR7-2 sweep now skips `LegacyBlankFillColumns` by resolved entity name; AC-FR7-4 rename test switched to a non-blank `OForiginInterne`. Seen red first (3 blank-fill tests failed against an empty `LegacyBlankFillColumns` before the mapper change).
- [x] `src/Kape22Importer/Kape22Mapper.cs` -- Rule 1 folded into `DefaultForNonNullable` (integer target, nullable or not → `0`); Rules 2–3 as two explicit assignments after the copy loop; `LegacyBlankFillColumns` = `{AcompteSolde, OForiginInterne}`. Note: DTO/entity property is `OForiginInterne` (not `OFOriginInterne`); the `NamingExceptions` entry resolves case-insensitively.
- [x] `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs` -- `KnownLegacyDivergences` reduced to `{Client}`; comment updated.
- [x] `_bmad-output/planning-artifacts/PRD.md` -- Annexe B "Valeurs par défaut du legacy pour un Champ vide" table added.
- [x] `_bmad-output/implementation-artifacts/deferred-work.md` -- section retitled "Resolved by: spec-parity-kape22-legacy-fill-rules"; "Still deferred" (populated branches) and "Permanent" (`Client`) subsections.

**Acceptance Criteria:**
- Given a blank typed integer Champ, when mapped, then its `L_D_KAPE22` column is `0` (not `NULL`).
- Given a typed integer Champ carrying a real value (including `0`), when mapped, then the value is unchanged.
- Given a blank `OForiginInterne` Champ, when mapped, then `entity.OForiginInterne` is `null`.
- Given a blank `AcompteSolde` Champ, when mapped, then `entity.AcompteSolde` is `"S"`.
- Given `OForiginInterne` / `AcompteSolde` carrying a real value, when mapped, then it is copied verbatim (`assumed, unverified` — no populated sample).
- Given a blank non-`OForiginInterne` string Champ, when mapped, then the column is `''` (unchanged).
- Given the 100 sample Fichiers + both databases, when `Kape22ProductionDataParityTests` runs, then 100 pass with only `Client` in `KnownLegacyDivergences`.
- Given the full `Category=Unit` suite, when run, then green.

## Verification

**Commands:**
- `dotnet test TextToXml.sln --filter "Category=Unit"` -- all green, new tests + adjusted AC-FR7-2 / AC-FR7-4 included.
- `dotnet test TextToXml.sln --filter "FullyQualifiedName~Kape22ProductionDataParityTests"` -- with both DBs: 100 passed, 0 failed.

**CI gap (accepted):** the parity test is `Category=Integration`; `ci.yml` runs `Category=Unit` only,
so the parity guard is manual. The always-run coverage is the new mapper unit tests — those must
assert the profile-confirmed values.

## Suggested Review Order

**The three fill rules**

- Entry point — Rule 1 (blank integer Champ → 0): one branch on the target type, folded into the existing helper.
  [`Kape22Mapper.cs:327`](../../src/Kape22Importer/Kape22Mapper.cs#L327)

- Rules 2–3 — the two column-scoped string defaults, applied right after the Annexe B copy loop.
  [`Kape22Mapper.cs:122`](../../src/Kape22Importer/Kape22Mapper.cs#L122)

- `LegacyBlankFillColumns` — the two string columns exposed only so the completeness sweep skips them.
  [`Kape22Mapper.cs:67`](../../src/Kape22Importer/Kape22Mapper.cs#L67)

**Test contract**

- The AC-FR7-2 completeness sweep now skips the two string-fill columns by resolved entity name.
  [`Kape22MapperTests.cs:59`](../../tests/Kape22Importer.Tests/Kape22MapperTests.cs#L59)

- The new `LegacyBlankFill_*` / `Map_*Champ_*_AcFr7_2` tests — blank + populated per I/O-matrix row.
  [`Kape22MapperTests.cs:96`](../../tests/Kape22Importer.Tests/Kape22MapperTests.cs#L96)

- The parity gate: `KnownLegacyDivergences` down to `{Client}` only.
  [`Kape22ProductionDataParityTests.cs:65`](../../tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs#L65)

**Docs**

- PRD Annexe B — the "Valeurs par défaut du legacy pour un Champ vide" table.
  [`PRD.md:1072`](../planning-artifacts/PRD.md#L1072)

- deferred-work.md — resolution note, the two `assumed, unverified` branches, and the permanent `Client` divergence.
  [`deferred-work.md:3`](deferred-work.md#L3)
