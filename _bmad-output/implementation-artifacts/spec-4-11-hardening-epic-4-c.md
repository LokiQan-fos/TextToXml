---
title: 'Story 4.11 — Hardening Épic 4 (C-1..C-10) + business notes Q-3/Q-5'
type: 'refactor'
created: '2026-09-21'
status: 'done'
review_loop_iteration: 0
context: []
baseline_commit: 'c3593d7c0010810ab39a11f8ef9e2e1b9b5a98ef'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Épic 4 retrospective #3 (`epic-4-retro-2026-09-21.md`, accepted-with-open-items) found 10 remaining hardening/coverage/documentation gaps (C-1..C-10) in the same lineage as Stories 4.9/4.10, plus 2 business questions (Q-3, Q-5) the business owner has now answered: an OF cannot be resubmitted today (all-or-nothing; a failed OF is reissued under a new OF number), and a Coulee is created by the first OF that references it and shared as-is by later OFs of the same Coulee (never modified by a later OF). Both answers confirm current behavior is correct — nothing to fix, only to document.

**Approach:** For Q-3/Q-5, add one English one-sentence operational comment each at the two named code sites in `Kape22Persister.cs`, no behavior change. For C-1..C-10, close each item exactly as scoped in its own AC (test-only for C-1/C-5/C-9, code+test for C-2/C-4/C-6, comment-only for C-7/C-8/C-10, structural test-source dedup for C-3) — no new FRs, no scope beyond what each AC states.

## Boundaries & Constraints

**Always:**
- Every code comment (Q-3, Q-5, C-7, C-8) is English, sentence case, on its own line above the block it documents (CC-2/CC-3) — never a trailing comment.
- C-2's fix changes only equality semantics for `CodeOperation` in the A-5 pre-check; `OF`, `TypeConsigne`, `ConsigneGPAO` stay ordinal. The error/collision message must still show the group's *original*-case `CodeOperation` values, not an uppercased/normalized form — use an `IEqualityComparer`, not a pre-grouping `.ToUpperInvariant()` transform on the key.
- C-6 accumulates only the A-5 and B-5 checks (both already funnel through `RejectWithBusinessRuleViolation`, `Kape22Persister.cs:125-146`). The missing-Coulee check (`Kape22Persister.cs:94-112`) stays a untouched, independent early return before them — this is the exact scope epics.md states ("sans modifier le comportement du contrôle Coulée manquante, laissé en l'état") and matches the pre-existing deferred-work.md entry from Story 4.9's review this item resolves.
- C-3's shared source is a new, from-scratch static class (nothing today unifies mapper-file-name ↔ table-name as a reusable pair) consumed by all 3 current sites: `DownstreamColumnMagnitudesParityTests.TableByMapperFile` (`tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs:22-29`), the inline 5-call sequence in `MappingAnnexCompletenessTests.cs:298-303` (the AC names this site `MappingAnnexSchema.MapperScaleCallSites`, which is actually the regex-extraction *class* at `MappingAnnexSchema.cs:290-303` — the real hardcoded enumeration to replace is the test call-site list, not that class), and `DownstreamColumnMagnitudes.MaxAbsoluteValues`'s enumeration intent (`DownstreamColumnMagnitudes.cs:19-39`, currently column-keyed only — only its class-comment prose lists tables, so this third site needs no code change beyond optionally citing the new shared source in its comment).
- C-4's length guard is wired into `Kape22Persister.cs`, mirroring B-5's placement exactly: pre-`SaveChanges`, reflection-walk shape (alongside `FindMagnitudeOverflow`/`DownstreamDecimalEntities`, `Kape22Persister.cs:243-290`), routed through `RejectWithBusinessRuleViolation` (`Kape22Persister.cs:221-236`) on overflow — never a raw `DbUpdateException` truncation. It reuses the **already-existing** `DownstreamColumnLengths.MaxLengths` map (`src/Kape22Importer/Persistence/DownstreamColumnLengths.cs:19-70`, already parity-locked by `DownstreamColumnLengthsParityTests.cs`) — no new annex field, no new hardcoded length data.
- C-4's entity walk covers all 10 bundle entities (string columns exist outside the 5 decimal-bearing tables) — do not reuse `DownstreamDecimalEntities` as-is; it enumerates only the 5 decimal tables.
- C-9's new test performs two genuine, sequential `Persist` calls on two separate `DbContext` instances sharing one Coulee — not a direct EF/SQL seed of the `Coulee` row (the gap this item exists to close; see `TransactionalPersistenceTests.cs:342-366` for the existing seed-based test it complements, not replaces).
- C-5's 4 new integration tests follow `RejectionAtomicityIntegrationTests.cs`'s existing pattern exactly (`[Collection(SqlServerIntegrationCollection.Name)]`, `Category=Integration`, asserting all 11 tables + `L_D_LOG_COMMANDE` end state) — one test per newly-covered cause (AC-FR20-3, AC-FR20-4, A-5, B-5).
- Transverse: CC-1 (TDD, test-first) for every AC carrying a test; CC-2/CC-3 (English comments) for every comment-only AC; CC-4 (alphabetical ordering) on any new class member; CC-5 (glossary vocabulary) reused as-is.

**Never:**
- No change to `Kape22Persister`'s SQL exception filter (AD-4 boundary, unchanged since Story 3.5/4.10) — C-4 and C-6 are pre-checks, not widened catches.
- No modifying the missing-Coulee check's behavior or message shape (C-6's explicit non-goal).
- No touching `SectionChargeRefroidissoirsMapper`/`PoidsMetriqueMapper`/`SvtMapper` or their tables — confirmed no `decimal` columns, out of scope for C-1/C-3/C-4's decimal-guard-adjacent work (string-length work on these 3 tables is in scope for C-4 only if they carry string columns already in `DownstreamColumnLengths.MaxLengths`, which is a pre-existing fact this story doesn't change).
- No renumbering or re-deriving `DownstreamColumnLengths.MaxLengths`'s existing 46 entries — C-4 consumes it as-is.
- No new FRs and no annex changes beyond what C-3/C-4 need (none — both reuse existing data).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Chutage/Decoupe/Pits magnitude overflow (C-1) | A bundle with an out-of-gabarit `ChutagePied`/`ChutageTete`/`LongueurMoyenne`/`H2Coulee` value | `DecimalMagnitudeGuardTests` gains a case per branch; existing guard (unchanged) rejects it | Existing `ConversionError`/REJETÉ path, now test-proven for these 3 branches |
| Case-differing Consignes collision (C-2) | Two `L_D_CONSIGNES` rows in one bundle whose `CodeOperation` differ only by case, same `(OF, TypeConsigne, ConsigneGPAO)` | A-5 pre-check now detects the collision in memory | `ConversionError`/REJETÉ via `RejectWithBusinessRuleViolation`, original casing preserved in the message |
| String column exceeds its max length (C-4) | A bundle entity string property longer than its `DownstreamColumnLengths.MaxLengths` bound | Pre-`SaveChanges` guard rejects it | `ConversionError`/REJETÉ via `RejectWithBusinessRuleViolation`, zero rows inserted |
| Simultaneous A-5 + B-5 violation (C-6) | A bundle failing both the Consignes-collision and magnitude checks | One REJETÉ message names both violations | Single `ConversionError`/`SaveChanges`, not two sequential ones |
| Hot-Coulee reused across two real dispatches (C-9) | Two OFs of the same Coulee, each dispatched via a separate `Persist` call on a fresh `DbContext` | Second dispatch reuses the Coulee row created by the first, does not modify or duplicate it | N/A — success path, asserted via row count/content |
| 4 previously-Unit-only rejection causes, now proven atomic end-to-end (C-5) | Malformed hot-Coulee, missing Pits, Consignes collision, magnitude overflow, each driven through `Persist` | All 11 tables + `L_D_LOG_COMMANDE` end state matches the existing atomicity pattern | Existing REJETÉ/dual-logging circuit, now integration-proven for these 4 causes |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Persistence/Kape22Persister.cs:77-81` -- Q-3: add a one-sentence English comment above the `OkLogRowExists` call site (or on `OkLogRowExists` itself, `:211-215`) stating an OF cannot be resubmitted today.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:92` -- Q-5: add a one-sentence English comment above `couleeAlreadyExists` stating Coulee-reuse semantics (created once, shared as-is, never modified by a later OF).
- `tests/Kape22Importer.Tests/DecimalMagnitudeGuardTests.cs` (123 lines, 3 existing tests at `:31,64,94`) -- C-1: add 3 cases for `SectionChargeChutage` (`ChutagePied`/`ChutageTete`), `SectionChargeDecoupe.LongueurMoyenne`, `SectionChargePits.H2Coulee`; production code (`Kape22Persister.cs:243-290`) unchanged.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:125-127` -- C-2: `GroupBy(row => (row.OF, row.CodeOperation, row.TypeConsigne, row.ConsigneGPAO))`, plain tuple equality today; needs an `IEqualityComparer` making `CodeOperation` case-insensitive while preserving original casing in the resulting message.
- `tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs:22-29` (`TableByMapperFile`), `tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs:298-303` (inline 5-call sequence, the AC's real target despite naming `MappingAnnexSchema.MapperScaleCallSites`), `src/Kape22Importer/Persistence/DownstreamColumnMagnitudes.cs:6-15` (class-comment prose) -- C-3: introduce one new shared static source (mapper-file-name ↔ table-name, 5 entries) and derive all 3 from it.
- `src/Kape22Importer/Persistence/DownstreamColumnLengths.cs` (72 lines, `MaxLengths`, flat `IReadOnlyDictionary<string,int>`, `OrdinalIgnoreCase`, 46 entries, `:19-70`) -- C-4: reuse as-is, no new data. Currently only consumed by `AscoLsiDbContext.cs`'s `ApplyDownstreamColumnLengths` (EF `HasMaxLength` metadata) -- net-new wiring into the persister.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:243-290` (`FindMagnitudeOverflow`/`DownstreamDecimalEntities`, the B-5 precedent) -- C-4: add a sibling length-overflow check here, walking all 10 bundle entities (not the 5-entity decimal-only iterator), routed through `RejectWithBusinessRuleViolation` (`:221-236`).
- `tests/Kape22Importer.Tests/DownstreamColumnLengthsParityTests.cs` -- C-4: existing parity-test precedent for the length map; read before writing the new guard's own test.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:94-112` (missing-Coulee, untouched), `:125-134` (A-5), `:141-146` (B-5), `:221-236` (`RejectWithBusinessRuleViolation`, currently single-message) -- C-6: change the A-5/B-5 pair to accumulate into one call instead of two sequential early returns; missing-Coulee stays first and independent.
- `src/Kape22Importer/Persistence/Kape22Persister.cs:156-162` (7 `AddIfPresent` calls, Pits is the 4th at `:159`) -- C-7: add a comment above the Pits line noting its non-nullity is already guaranteed by AC-FR20-4 upstream (`Kape22ImportBundleMapper`).
- `tests/Kape22Importer.Tests/EndToEndPerformanceTests.cs:102-106` -- C-8: update the stale comment (still names "Converter, Kape22Mapper") to describe the real pipeline (`Kape22FichierProcessor` → `Kape22ImportBundleMapper` → `Kape22Persister`); NFR-1/NFR-2 budgets/assertions unchanged.
- `_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md:123-124` -- C-10: "9 entités avales nullables" → "10 entités avales nullables".
- `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:342-366` (existing seed-based Coulee-reuse test, for contrast) -- C-9: add a new test doing two real sequential `Persist` calls (separate `DbContext`s) sharing one Coulee, in this file or a sibling integration test file.
- `tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs` (232 lines, 3 existing tests at `:73,100,129`) -- C-5: add 4 tests for AC-FR20-3 (malformed hot-Coulee), AC-FR20-4 (missing Pits), A-5 (Consignes collision), B-5 (magnitude overflow).

## Tasks & Acceptance

**Execution:**
- [x] `Kape22Persister.cs` -- add Q-3 comment above the `OkLogRowExists`/D22 call site -- documentation-only, no test.
- [x] `Kape22Persister.cs` -- add Q-5 comment above `couleeAlreadyExists` -- documentation-only, no test.
- [x] `DecimalMagnitudeGuardTests.cs` -- add Chutage/Decoupe/Pits overflow cases -- C-1, TDD red→green.
- [x] `Kape22Persister.cs` -- make the A-5 `GroupBy` key's `CodeOperation` comparison case-insensitive via a comparer -- C-2, TDD red→green.
- [x] new shared static source (mapper-file ↔ table) + `DownstreamColumnMagnitudesParityTests.cs` + `MappingAnnexCompletenessTests.cs` -- derive the 3 lists from one source -- C-3, TDD red→green (existing 21-call-site coverage must stay green).
- [x] `Kape22Persister.cs` -- add pre-`SaveChanges` length-overflow guard reusing `DownstreamColumnLengths.MaxLengths` -- C-4, TDD red→green.
- [x] `Kape22Persister.cs` -- accumulate A-5 + B-5 into one REJETÉ message when both fire; missing-Coulee untouched -- C-6, TDD red→green.
- [x] `TransactionalPersistenceTests.cs` (or sibling) -- add the hot-Coulee sequential cross-`Persist` test -- C-9, TDD red→green (Integration).
- [x] `RejectionAtomicityIntegrationTests.cs` -- add 4 tests (AC-FR20-3, AC-FR20-4, A-5, B-5) -- C-5, TDD red→green (Integration). **3 of 4 delivered** (AC-FR20-3, AC-FR20-4, A-5); the 4th (B-5) is not realistically producible via a real fixed-width fixture — see `deferred-work.md` § "Deferred from: implementation of story-4.11", accepted by the human at implementation time.
- [x] `Kape22Persister.cs` -- comment above the Pits `AddIfPresent` line -- C-7, documentation-only.
- [x] `EndToEndPerformanceTests.cs` -- update the stale pipeline comment -- C-8, documentation-only.
- [x] `ARCHITECTURE-SPINE.md` -- "9" → "10" entités avales -- C-10, documentation-only.

**Acceptance Criteria:** the 10 Given/When/Then ACs (C-1..C-10) plus the 2 Given/When/Then business notes (Q-3, Q-5), verbatim in `epics.md` § "Story 4.11 : Hardening Épic 4 (C-1..C-10) + notes métier Q-3/Q-5".

### Review Findings

- [x] [Review][Decision] C-6's frozen scope silently widened to include C-4 — resolved 2026-09-22: option 1, documented retroactively in the Spec Change Log (shipped behavior is correct, C-4's inclusion is intentional). [`Kape22Persister.cs:151-179`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L151)
- [x] [Review][Patch] "Peripherals" section of this spec miscounts deferred-work.md entries — claims "7 entries" for this story's code-review section; the delivered diff adds 6 (5 tagged "code review" + 1 tagged "implementation"). Fixed: corrected to "8 entries across both review passes" (5 original + 3 from this review loop). [`spec-4-11-hardening-epic-4-c.md:199`](spec-4-11-hardening-epic-4-c.md#L199)
- [x] [Review][Patch] `RejectWithBusinessRuleViolation`'s combined message repeats the `"OF '{of}' : "` prefix inside every joined cause when C-6 accumulates more than one, instead of stating the OF once. Fixed: the prefix is now added once by `RejectWithBusinessRuleViolation`; the three cause-builders no longer repeat it. [`Kape22Persister.cs:163-175,265`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L163)
- [x] [Review][Patch] `RejectionAtomicityIntegrationTests.ReadDetailChamp` has no bounds check on `Split("\r\n")`/`Substring` and duplicates fixed-width extraction logic instead of a symmetrical read-side helper next to `TestSupport.WithDetailChamp`. Fixed: moved to `TestSupport.ReadDetailChamp` next to `WithDetailChamp`, with a bounds check on the split result. [`TestSupport.cs`](../../tests/Kape22Importer.Tests/TestSupport.cs)
- [x] [Review][Patch] `ConsignesNaturalKeyComparer` is appended as a second top-level type inside `Kape22Persister.cs`, the only file in `src/Kape22Importer/Persistence/` with more than one top-level type — breaks the folder's established one-type-per-file convention. Fixed: extracted to its own file. [`ConsignesNaturalKeyComparer.cs`](../../src/Kape22Importer/Persistence/ConsignesNaturalKeyComparer.cs)
- [x] [Review][Defer] `FindLengthOverflow`/`FindMagnitudeOverflow` report only the first overflowing column when several overflow simultaneously — pre-existing pattern since Story 4.10 (already logged in deferred-work.md for the magnitude case), C-4 mirrors it by explicit design. [`Kape22Persister.cs:339-358`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L339) — deferred, pre-existing
- [x] [Review][Defer] AC-trait naming for the new C-1/C-2/C-4/C-6/C-9/A-5-tagged tests (`_AcC1`, `_AcC2`, `_AcC4`, `_AcC6`, `_AcC9`, `_A5`) doesn't match `AcTraitCoverage`'s regex (`AcFr\d+_\d+`/`Ctr`/`Nfr`/`Sm` only), so the AC-trait coverage gate silently skips all of them — pre-existing gap since Story 4.10's `_AcB5` tests, only extended by this story. [`tests/Kape22Importer.Tests/AcTraitCoverage.cs:17-19`](../../tests/TextToXml.Tests/AcTraitCoverage.cs#L17) — deferred, pre-existing
- [x] [Review][Defer] `Configuration()` `IConfiguration`-builder boilerplate duplicated verbatim across the 3 new test files — pre-existing pattern already duplicated across 10 other test files repo-wide, not specific to this story. [`ConsignesCaseInsensitiveCollisionTests.cs`, `StringLengthGuardTests.cs`, `DecimalMagnitudeGuardTests.cs`](../../tests/Kape22Importer.Tests/) — deferred, pre-existing

## Spec Change Log

- **2026-09-21, during step-03 implementation.** Trigger: C-5's frozen boundary requires 4 new `RejectionAtomicityIntegrationTests` cases, one per newly-covered cause (AC-FR20-3, AC-FR20-4, A-5, B-5). Investigation during implementation showed the B-5 (magnitude-overflow) case is not producible through a real fixed-width byte fixture: every one of the 21 real `DecimalScale.Apply` KAPE22 source fields is, by construction, narrower than the raw-value width needed to overflow its target column's bound, so no legal byte content in a real P60 fixture can trigger it — confirmed by checking all 21 call sites' field widths against their `DownstreamColumnMagnitudes` bounds. Amendment: C-5's scope is reduced from 4 to 3 delivered Integration tests (AC-FR20-3, AC-FR20-4, A-5); the human was asked live during implementation and explicitly approved this reduction over the alternative (a synthetic, non-representative fixture built solely to force the path). Known-bad state avoided: either silently shipping 3-of-4 without a record, or fabricating byte content that does not correspond to any real P60 export just to satisfy a test count. KEEP: the 3 delivered tests' pattern (byte-position mutation via `WithDetailChamp`/`InsertableReferenceFichier`, real `Kape22FichierProcessor.Import` pipeline) is correct and should not be revisited; B-5 remains proven at the Persister-unit level (`DecimalMagnitudeGuardTests`, pre-existing since Story 4.10) — see `deferred-work.md` § "Deferred from: implementation of story-4.11" for the full rationale.

- **2026-09-22, review loop 1 (Acceptance Auditor):** Finding — the frozen "Boundaries & Constraints" text scoped C-6 to accumulating "only the A-5 and B-5 checks", but the shipped code (and its own review test `Persist_SimultaneousConsignesCollisionAndLengthOverflow_...`) also accumulates C-4's length-overflow check into the same REJETÉ message. Amended: C-6's scope now explicitly includes C-4. KEEP: the shipped behavior is correct and intentional — leaving C-4 as a separate early-return would reintroduce the exact "first cause wins" precedence bug C-6 exists to close. This entry is a bookkeeping reconciliation of intent already documented in the spec's own Suggested Review Order section; no code change follows from it.

## Design Notes

**C-4 Ask-First, resolved:** guard lives in `Kape22Persister.cs`, mirroring B-5's placement — not elsewhere (not in `AscoLsiDbContext`, not in a mapper). Rationale: AD-2 keeps mappers pure with no error-reporting circuit; AD-4 requires every dispatch failure to flow through the single existing `ConversionError`/REJETÉ circuit, which only the persister can reach pre-`SaveChanges` — an EF-level truncation would still throw a raw, undiagnosed `DbUpdateException` if placed at the `DbContext` level, the exact bug this item exists to close for lengths (as B-5 already closed it for magnitude). Unlike B-5, no new data source is needed: `DownstreamColumnLengths.MaxLengths` already exists, already parity-locked, already exactly the right shape (flat, `OrdinalIgnoreCase`, column-name-keyed) — this decision is clearer-cut than B-5's, so no human halt is needed (same resolved-without-halt precedent as B-5 itself in Story 4.10).

**C-3 shared source, sketch (not prescriptive):** a small static class (e.g. a 5-entry `IReadOnlyDictionary<string,string>` or record list, mapper-file-name → table-name) that `TableByMapperFile`, the `MappingAnnexCompletenessTests.cs` call sequence, and (optionally, via updated comment) `DownstreamColumnMagnitudes`'s prose all read from — implementer's choice of exact type/location as long as all 3 sites derive from one definition.

**C-6 accumulation shape:** `RejectWithBusinessRuleViolation` (or a new sibling taking `IReadOnlyList<string>` messages) must support combining the A-5 and B-5 messages into one `ConversionError`/REJETÉ row when both fire; the missing-Coulee block keeps its own separate, untouched early return ahead of them.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` -- expected: clean build.
- `dotnet test TextToXml.sln --filter Category=Unit` -- expected: C-1/C-2/C-3/C-4/C-6 tests green, full suite green.
- `dotnet test TextToXml.sln --filter Category=Integration` -- expected: C-5's 4 new tests and C-9's new test green (or clean skip without local SQL Server, AR-12); no existing Integration test regresses.
- Manual read-through: Q-3, Q-5, C-7, C-8, C-10 comments — English, sentence case, above the block, accurate to current code/architecture.

## Suggested Review Order

**C-6/C-4: accumulated business-rule rejection (the core behavior change)**

- Entry point — A-5, B-5 and C-4 now compute unconditionally and accumulate into one message instead of three sequential early returns; missing-Coulee stays untouched above.
  [`Kape22Persister.cs:160`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L160)

- The shared rejection helper, now joining an arbitrary number of causes with `" ; "` into one `ConversionError`/REJETÉ row.
  [`Kape22Persister.cs:263`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L263)

- C-2: the A-5 natural-key comparer making only `CodeOperation` case-insensitive, original casing preserved in the message.
  [`Kape22Persister.cs:482`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L482)

- Review patch — hash/equals contract fix: `StringComparer.OrdinalIgnoreCase.GetHashCode` instead of a raw `.ToUpperInvariant()`.
  [`Kape22Persister.cs:498`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L498)

- The combined-rejection regression test — A-5 + B-5 simultaneously, one accumulated error.
  [`TransactionalPersistenceTests.cs:558`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L558)

- Review patch — the same combination test for A-5 + C-4, closing the precedence gap the first pass missed.
  [`TransactionalPersistenceTests.cs:605`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L605)

**C-4: new pre-persist length guard**

- `FindLengthOverflow`, the length-side sibling of the pre-existing magnitude guard.
  [`Kape22Persister.cs:339`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L339)

- `DownstreamStringEntities`, walking all 10 bundle entities (not the 5-entity decimal-only iterator `FindMagnitudeOverflow` uses).
  [`Kape22Persister.cs:365`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L365)

- The guard's own tests: an `OrdreFabrication` column, a `Consignes`-row column, and the exact-boundary success case.
  [`StringLengthGuardTests.cs:24`](../../tests/Kape22Importer.Tests/StringLengthGuardTests.cs#L24)

**C-1: magnitude-guard branch coverage**

- The 3 previously-untested branches (Chutage, Decoupe, Pits) added alongside the existing OrdreFabrication/Lingot cases.
  [`DecimalMagnitudeGuardTests.cs:114`](../../tests/Kape22Importer.Tests/DecimalMagnitudeGuardTests.cs#L114)

**C-3: mapper-file/table triplication removed**

- The one new shared source, replacing two independently hardcoded lists.
  [`DownstreamDecimalMapperTables.cs:1`](../../tests/Kape22Importer.Tests/DownstreamDecimalMapperTables.cs#L1)

- First consumer site.
  [`DownstreamColumnMagnitudesParityTests.cs:22`](../../tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs#L22)

- Second consumer site, the real 21-call-site gate this triplication protected.
  [`MappingAnnexCompletenessTests.cs:301`](../../tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs#L301)

**C-9: hot-Coulee reuse across two real dispatches**

- Two genuine sequential `Persist` calls (separate `DbContext`s) sharing one Coulee; the second must not modify it.
  [`TransactionalPersistenceTests.cs:380`](../../tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs#L380)

**C-5: rejection-atomicity integration coverage (3 of 4, see Spec Change Log)**

- Review patch — the collision test now reads the real `CodeOpeChutage` value off the fixture instead of a hardcoded literal.
  [`RejectionAtomicityIntegrationTests.cs:230`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L230)

- The read-side helper backing that patch.
  [`RejectionAtomicityIntegrationTests.cs:280`](../../tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs#L280)

**Business notes and documentation-only items (Q-3, Q-5, C-7, C-8, C-10)**

- Q-3: an OF cannot be resubmitted today.
  [`Kape22Persister.cs:81`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L81)

- Q-5: Coulee creation/reuse semantics.
  [`Kape22Persister.cs:97`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L97)

- C-7: the Pits `AddIfPresent` invariant made locally visible.
  [`Kape22Persister.cs:197`](../../src/Kape22Importer/Persistence/Kape22Persister.cs#L197)

- C-8: `EndToEndPerformanceTests`' stale pipeline comment, corrected.
  [`EndToEndPerformanceTests.cs:102`](../../tests/Kape22Importer.Tests/EndToEndPerformanceTests.cs#L102)

- C-10: the architecture spine's entity count, corrected — and re-corrected at review time to not overclaim "nullable".
  [`ARCHITECTURE-SPINE.md:123`](../../_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md#L123)

**Peripherals**

- Deferred items from this story's code review (8 entries across both review passes) and C-5's scope-reduction rationale.
  [`deferred-work.md`](../../_bmad-output/implementation-artifacts/deferred-work.md)

