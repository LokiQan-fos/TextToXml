---
title: 'Story 4.14 — Schema parity lock for DownstreamColumnPrecisions'
type: 'chore'
created: '2026-09-25'
status: 'done'
baseline_commit: '1fcfbf2404176014e27cff77fb40d2c72779e5a4'
review_loop_iteration: 2
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `DownstreamColumnPrecisions` has 23 hand-transcribed `(precision, scale)` entries. It was added by manual-session commit `1ab5ea7` and has no Unit-tier parity test against `scripts/schema/01-ascolsi-tables.sql`, unlike its siblings `DownstreamColumnLengths` and `DownstreamColumnMagnitudes`. A transcription error today would only show up at the Integration production-parity tier, as a decimal(18,2) rounding defect (retro #4, F-2 / D-2).

**Approach:**
- Extend the test-side `SqlColumn` so it exposes the parsed `(precision, scale)`, with no second parser.
- Add `DownstreamColumnPrecisionsParityTests` (Unit):
  - AC-1: set equality between the map and the schema, with a guard on shared column names.
  - AC-2: every magnitude equals 10^(p−s).
- Close by re-closing `PROJECT-CLOSED.md` (D-3).

## Boundaries & Constraints

**Always:**
- Scope is the 9 tables `L_D_ORDRE_FABRICATION`, `L_D_COULEE` and the 7 `L_D_SECTIONCHARGE_*`. `L_D_CONSIGNES` is out of scope because the DbContext applies no precisions to it.
- AC-1 compares every `DECIMAL(p,s)` / `NUMERIC(p,s)` column of those tables, not only the columns mappers scale.
- A column name repeated across tables with different `(p,s)` fails with a message that names both tables.
- `DecimalMagnitude` stays derived from the same parse, so its existing consumer is unchanged.
- Style rules:
  - Tests: Unit-only, `[Trait("Category", TestCategory.Unit)]`, one AC trait per test (`4.14-AC1`, `4.14-AC2`).
  - Comments: English (CC-2).
  - Ordering: case-insensitive alphabetical (CC-4).

**Ask First:**
- Running any broad `dotnet test` (`Category=Unit` or `Integration`). Per project memory, it wipes `AscoLSI_Test` through the SQL fixture. Only the targeted `--filter FullyQualifiedName~DownstreamColumn` run is pre-authorized.
- Any change to a map value, in case the test reveals a real gap. None is expected: I checked by hand and found 27 schema columns and 23 names, all matching.

**Never:**
- Production code change beyond the comment alignment in `DownstreamColumnPrecisions.cs`, and only if it is needed.
- A second SQL parser.
- Reflection over the EF model for this test. The schema file is the single source.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Map matches schema | Current map and schema | AC-1 and AC-2 green | N/A |
| Map entry drifts | One entry mistyped, e.g. `(5,2)` | AC-1 fails and names the column | Assert diff |
| Schema column not in map | New `DECIMAL` column | AC-1 fails (set mismatch) | Assert diff |
| Shared name, divergent (p,s) | Synthetic input: the same name at `(2,1)` in table A and `(3,1)` in table B | Failure message contains `A` and `B` | Exception or assert message |
| Magnitude drifts | Magnitude ≠ 10^(p−s), or its key is absent from the precisions | AC-2 fails and names the column | Assert message |

</frozen-after-approval>

## Code Map

- `tests/Kape22Importer.Tests/SqlTableSchema.cs:17` has the `SqlColumn` positional record, currently with `DecimalMagnitude`. `:88-99` holds `DecimalMagnitudeFor`, the only `p,s` parser, to turn into a `(int Precision, int Scale)?` parse. `DecimalMagnitude` then becomes a body property computed from it.
- `tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs:38` is the only consumer of `DecimalMagnitude`. `:27` has the comment "the six Tolerance* columns", which is wrong: the schema shares 4 (`Max/MinSection`, `Max/MinEpaisseur`), and `Max/MinLongueur` exist only in OF. Fix that comment.
- `tests/Kape22Importer.Tests/DownstreamColumnLengthsParityTests.cs:20-44,50-80` is the pattern to mirror: table-name array, cross-table collision check, ordered `Assert.Equal`.
- `src/Kape22Importer/Persistence/DownstreamColumnPrecisions.cs:14-16` has a comment saying "four Tolerance*". That is already correct against the schema, so there is no change unless the review disagrees.
- `src/Kape22Importer/Persistence/DownstreamColumnMagnitudes.cs` has 17 entries, all present in the precisions map.
- `src/Kape22Importer/Persistence/AscoLsiDbContext.cs:77-124,176-186` shows the consumer: 6 entities have DECIMAL columns, and REFROIDISSOIRS, POIDSMETRIQUE and SVT have none.
- `_bmad-output/implementation-artifacts/PROJECT-CLOSED.md` is where the D-3 re-close goes.

## Tasks & Acceptance

**Execution:**
- [x] `tests/Kape22Importer.Tests/DownstreamColumnPrecisionsParityTests.cs`: write it first (CC-1).
  - AC-1 test: build the expected map from the 9 tables through a small helper `ExpectedPrecisions(IEnumerable<(string Table, SqlColumn Column)>)`. On a divergent repeat, the helper throws with a message naming both tables. Then assert ordered equality with `Precisions`.
  - A second AC-1 test feeds the helper a synthetic collision and asserts that both table names appear in the message.
  - AC-2 test: for each magnitude, the key exists in `Precisions` and the value equals `10^(p−s)`.
- [x] `tests/Kape22Importer.Tests/SqlTableSchema.cs`: add `DecimalPrecision` to `SqlColumn` and derive `DecimalMagnitude` from it. This keeps a single parser. Update the class comment.
- [x] `tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs:27`: "six" → "four".
- [x] Red proof (CC-1): temporarily change one map entry, e.g. `LongueurCD` → `(5,2)`. Confirm that AC-1 and AC-2 both fail, then revert. Nothing of this is committed. Record the observed failure in the step-03 notes.
- [x] `PROJECT-CLOSED.md` (D-3), done at step-05 before the commit:
  - Set `status: closed` and `last_commit`.
  - Epic 4 row: add 4.4-bis, 4.12, 4.13 and 4.14, and four retro passes.
  - §5: current test counts.
  - §3: the D-1..D-4 reconciliation.
  - Drop the reopened banner.

**Acceptance Criteria:**
- Given the current map and schema, when `DownstreamColumnPrecisionsParityTests` runs, then the three tests pass.
- Given a scratch mutation of one precision entry, when the class runs, then AC-1 fails naming the column. The mutation is reverted.
- Given the story is closed, when `PROJECT-CLOSED.md` is read, then it says `closed` and is consistent with `sprint-status.yaml` (`epic-4: done`).

## Design Notes

Using `SqlColumn` keeps the helper free of I/O, so the collision branch can be tested without a fake schema file:

```csharp
internal sealed record SqlColumn(Type ClrType, (int Precision, int Scale)? DecimalPrecision, bool IsNullable, int? MaxLength, string Name, string SqlType)
{
    public decimal? DecimalMagnitude => DecimalPrecision is (int p, int s) ? (decimal)Math.Pow(10, p - s) : null;
}
```

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror`: expected 0 warnings or errors.
- `dotnet test tests/Kape22Importer.Tests --filter "FullyQualifiedName~Kape22Importer.Tests.DownstreamColumn&Category=Unit"`: expected 6 green (the precisions, magnitudes and lengths parity classes), with no SQL fixture. Corrected at step-03: the bare `~DownstreamColumn` filter also matches `Kape22ProductionDataParityTests` (`...ScaledDownstreamColumns...`, Integration, SQL fixture). It ran once and recreated the `AscoLSI_Test` tables.

**Step-03 notes (red proof, CC-1):** after the test was written, the build failed on the missing `SqlColumn.DecimalPrecision` (CS0117/CS1503). Once that compiled, a scratch mutation `LongueurCD` → `(5, 2)` made AC-1 fail with an `Assert.Equal` diff on `["LongueurCD"] (5, 3)` vs `(5, 2)`, and AC-2 fail with "LongueurCD: magnitude 100, expected 1000 from (5,2)". The mutation was reverted, and the strict build and the 6 scoped tests are green.
- The full `Category=Unit` run (1026 passed), for the PROJECT-CLOSED.md §5 counts, was authorized by the user on 2026-09-25 (Ask First gate).

## Suggested Review Order

**Parity lock**

- Entry point: every DECIMAL column of the 9 tables must match the map, and vice versa.
  [`DownstreamColumnPrecisionsParityTests.cs:34`](../../tests/Kape22Importer.Tests/DownstreamColumnPrecisionsParityTests.cs#L34)

- A shared column name with divergent (p,s) throws, naming both tables.
  [`DownstreamColumnPrecisionsParityTests.cs:79`](../../tests/Kape22Importer.Tests/DownstreamColumnPrecisionsParityTests.cs#L79)

- The collision branch is proven on synthetic columns, without I/O.
  [`DownstreamColumnPrecisionsParityTests.cs:50`](../../tests/Kape22Importer.Tests/DownstreamColumnPrecisionsParityTests.cs#L50)

- AC-2: magnitudes derive from precisions, so the two maps cannot drift apart.
  [`DownstreamColumnPrecisionsParityTests.cs:66`](../../tests/Kape22Importer.Tests/DownstreamColumnPrecisionsParityTests.cs#L66)

**Single parser**

- One `(p,s)` parse; a DECIMAL without an explicit `(p,s)` throws (review patch P-2).
  [`SqlTableSchema.cs:90`](../../tests/Kape22Importer.Tests/SqlTableSchema.cs#L90)

- Magnitude is now derived from the parsed precision, not parsed twice.
  [`SqlTableSchema.cs:19`](../../tests/Kape22Importer.Tests/SqlTableSchema.cs#L19)

**Peripherals**

- Comment fix: four shared Tolerance* columns, not six.
  [`DownstreamColumnMagnitudesParityTests.cs:27`](../../tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs#L27)

- D-3 re-close: status, Epic 4 row, D-1..D-4 reconciliation, test counts.
  [`PROJECT-CLOSED.md:1`](PROJECT-CLOSED.md#L1)
