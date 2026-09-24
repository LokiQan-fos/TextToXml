# Review report — Story 4.13

Range : bee5328535e702f9755033757d99c0078d210dca^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-4-13-ligne-consignegpao-0-libelles-composites.md
Date : 2026-09-24
Verdict : REFUSÉ
Findings : D=1 P=5 F=0 R=17  (Decision, Patch, Defer, Rejetés)
Decision resolution : D1 → option 2 (2026-09-24), now P6. Patches to apply: 6.

Refusal grounds:
- Frozen AC widened without a dated Spec Change Log note (audit rule 1): D1.
- CC-3: a comment made false by this story was left stale. This violates the CC-3 clarification recorded in the 2026-09-24 Spec Change Log of story 4.12 (P1).

Severity count (kept findings): high 0 · medium 2 (D1, P1) · low 4 (P2, P3, P4, P5).

CC-1 check: passed. The `bee5328` commit body carries 6 `AC-FR19-6 → …` attestation lines. `AcCoverageCompletenessTests` FR-19 = 6.

## 1. Decision — à trancher par l'humain

### D1 — MCC-edited section detection is wider than the frozen AC
- Source : acceptance-auditor + blind-hunter
- Location : `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:436-439` (the `.Concat(productionRows.Where(received => Match(productionWorking, received) is null))` clause)
- Description : the frozen AC (Acceptance Criteria, bullet 2) says the label of a `0` row is compared "only if no code in that (`OF`, `CodeOperation`) differs between production `0` and `1`". The implementation also marks a section as edited when a production `ConsigneGPAO=1` row has **no** `0` counterpart. That clause came from a step-04 build-review patch ("MCC detection of missing 0 rows", in the commit body). The Suggested Review Order describes it, but the Spec Change Log has no dated note for it. Side effect (blind-hunter): a production data anomaly (a missing `0` row) is downgraded to a skip of every label in the section, with no dedicated message.
- Options :
  1. Keep the behaviour. Add a dated 2026-09-24 Spec Change Log entry: "a production `1` row with no `0` counterpart counts as a differing code (MCC deletion), and its section's labels are skipped". Optionally add a distinct skip message ("ligne ConsigneGPAO=0 absente en production") so the anomaly stays visible.
  2. Apply the frozen AC literally: remove the `.Concat(...)` clause. A section missing a production `0` row is then compared strictly.
- État : **tranché 2026-09-24 — option 2** (appliquer l'AC à la lettre). Production check (read-only SELECT on AFV004-LSI/AscoLSI): 1 unpaired `1` row out of 899 997 (15 175 OFs, OF `000006534400` XA1/13), 0 unpaired `0` rows. MCC edits the working copy but never deletes it, so a missing `0` row is a data anomaly and must be reported, not skipped. → becomes P6.

## 2. Patch

### P1 — Stale `ConsigneGPAO is always true` comment in the persister (CC-3) (medium)
- Source : edge-case-hunter + verification-gap
- Location : `src/Kape22Importer/Persistence/Kape22Persister.cs:124-126`
- Description : the A-5 collision comment says "ConsigneGPAO is always true since Story 4.4-bis". Since Story 4.13, every decoded row exists with both `true` and `false`. The comment is factually false, and the 4.12 CC-3 clarification requires updating it.
- Action : reword it to "ConsigneGPAO is true or false per decoded row since Story 4.13 (the working copy), so the two halves never collide with each other". Keep the rest of the collision reasoning, which still holds because the `1` and `0` rows differ by `ConsigneGPAO` in the key.

### P2 — Test name still claims every row is `ConsigneGPAO=true` (low)
- Source : blind-hunter + verification-gap + acceptance-auditor (out of mandate)
- Location : `tests/Kape22Importer.Tests/ConsignesMapperTests.cs:490`
- Description : `Map_EveryProducedConsigne_HasConsigneGpaoTrueAndSizeCodeConsigneSet_AcFr19_4` now asserts equal `true`/`false` halves (`:495`). The name contradicts the body.
- Action : rename it to `Map_EveryProducedConsigne_HasMirroredGpaoHalvesAndSizeCodeConsigneSet_AcFr19_4`. Keep the `_AcFr19_4` suffix and the trait.

### P3 — The `ConsigneGPAO=1` reverse check is satisfied by a working copy (low)
- Source : blind-hunter + edge-case-hunter + verification-gap
- Location : `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:348`
- Description : `testRows` now holds both halves, but `testRows.Any(row => row.CodeOperation… && row.TypeConsigne…)` has no `ConsigneGPAO` filter. Neighbouring lines 345 and 351 are scoped. A missing `1` row would be masked by its `0` row. Today the mapper derives every `0` row from a `1` row, so this cannot happen yet, but the check no longer proves what its comment says.
- Action : add `row.ConsigneGPAO &&` to the predicate.

### P4 — E2E gate does not enforce the "Never a `ConsigneGPAO=0` row for SVT" rule (low)
- Source : blind-hunter + edge-case-hunter
- Location : `scripts/e2e-worker-import.ps1:160` (counts query) and `:167-172` (throws)
- Description : the pairing subquery filters `SizeCodeConsigne <> 0`, so a stray SVT working copy (type 0, size 0) is never counted. The Never boundary is covered only by `ConsignesMapperTests.Map_SvtSectionApplicable_ProducesNoWorkingCopyRow_AcFr19_6`, and not on the persisted path.
- Action : add `SUM(CASE WHEN ConsigneGPAO = 0 AND SizeCodeConsigne = 0 THEN 1 ELSE 0 END)` to the query, as `$svtWorking`, and add `if ([int]$svtWorking -ne 0) { throw "$svtWorking SVT L_D_CONSIGNES row(s) persisted with ConsigneGPAO=0." }`.

### P5 — Rewritten comment not re-wrapped (low)
- Source : blind-hunter
- Location : `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:282`
- Description : the line edited by 4.13 ("…must be produced. Its codes are compared to the production ConsigneGPAO=1 row, since both rows carry the same") is 177 characters. The surrounding comment block wraps at about 110 characters.
- Action : re-wrap the comment block at the file's width. The text stays unchanged.

### P6 — Remove the missing-counterpart clause from MCC detection (from D1, option 2) (medium)
- Source : acceptance-auditor + blind-hunter (D1, resolved by the human)
- Location : `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:436-439`, comment at `:418-419`, spec Suggested Review Order (entry "MCC-edited section detection")
- Description : `editedSections` must contain only sections where a production code differs between `0` and `1`, as the frozen AC says. A production `1` row with no `0` counterpart then falls through to the per-row loop and is reported as a regression ("aucune ligne de production correspondante (ConsigneGPAO=0)").
- Action : delete `.Concat(productionRows.Where(received => Match(productionWorking, received) is null))`. Drop "or has no counterpart on the other side" from the `CompareWorkingRows` comment. Replace "including a 1 row with no 0 counterpart" in the spec's Suggested Review Order.

## 3. Defer

None. Every kept finding comes from this change.

## 4. Rejetés (bruit)

- R1 — spec `status: 'done'` vs `sprint-status.yaml` `review` vs unchecked sprint-status task (blind-hunter, auditor out of mandate) : by design. The task explicitly says "move to `done` in step-05", which `/commit-review` handles.
- R2 — `review_loop_iteration: 0` vs two Change Log entries (blind-hunter) : a bookkeeping field that nothing reads. The Change Log entries come from the build-time review, not from a review loop.
- R3 — Suggested Review Order anchors stale (blind-hunter) : verified against the post-change files. `ConsignesMapper.cs:124` (working copies), `:158` (SVT) and `:181` (SetLabel), plus parity `:423`, `:436` and `:480`, all point at the right code.
- R4 — E2E has no exact 60-row gate (blind-hunter, auditor out of mandate) : the script takes any `-Fichiers`, so a hard-coded 60 would be wrong for other files. The AC is met by the run output, and the per-key pairing already checks both halves structurally.
- R5 — E2E pairing does not detect duplicate working copies (blind-hunter, edge-case-hunter) : unreachable. The natural key (OF, CodeOperation, TypeConsigne, ConsigneGPAO) is the table PK, and the A-5 in-memory pre-check (`Kape22Persister.cs:137-142`) rejects a duplicate before persistence.
- R6 — E2E accepts an empty composite label (edge-case-hunter) : unreachable. `AddSection`/`AddDecoupe` emit every sub-field of a non-blank code, so every composite has at least one input row.
- R7 — `SectionRows` isolates a section by CodeOperation only (blind-hunter, edge-case-hunter, auditor out of mandate) : two sections that share a CodeOperation already collide on the type 13 `1` row, and the A-5 pre-check rejects that bundle as a ConversionError. Nothing mixed is ever persisted.
- R8 — `WorkingCopy` copies a hand-picked property list (blind-hunter) : it copies all 7 columns of the frozen database-first entity (`L_D_CONSIGNES.cs`, no migration, AR-8/AR-12).
- R9 — the mapper composite test takes its expected value from the composer (blind-hunter) : the test is intentionally about wiring. The composite content is pinned by production literals in `LibelleConsigneComposerTests`.
- R10 — no LA9/XA2 mapper test (blind-hunter) : the composer is selected by the section object (`lingot`, `refroidissoirs`), never by the CodeOperation value, so the value cannot change the routing.
- R11 — missing-type rule tested only for Lingot, and a null input label (blind-hunter) : every section goes through the same `Join` helper. A null label concatenates as `""`, the same as legacy C# `+`.
- R12 — composite detection written three ways (blind-hunter) : two are in tests and one in a script. All three are consistent today, and a shared definition across C# and SQL is not worth adding.
- R13 — the processor test checks only the `0` counts (blind-hunter) : codes and labels are covered by the mapper tests and by `Kape22FichierProcessorIntegrationTests.cs:237-238`, which reads the persisted composite.
- R14 — the deferred-work entry names no owner (blind-hunter) : the ledger format (`source_spec`/`summary`/`evidence`, project-profile § Ledger de dette) has no owner field.
- R15 — `dateReception` null makes every label compare strictly (edge-case-hunter) : this is the documented intent (comment at `Kape22ProductionDataParityTests.cs:334-336`).
- R16 — the composite DateMaj guard reads every row of the section, including the other XP1 block and type 29 (blind-hunter, edge-case-hunter) : the whole-section scope is recorded in the Spec Change Log (2026-09-24, second entry, surfaced to the human at step-04).
- R17 — PC1 without a type 5 row keeps `?` and would fail the E2E gate (auditor out of mandate) : unreachable, as documented in the first Spec Change Log entry. A non-blank PC1 code always produces type 5.

## 5. Auto-vérifications

- Lenses run (4/4, parallel, same model as the session): blind-hunter (19 findings), edge-case-hunter (7), verification-gap (0 gaps + 3 other findings), acceptance-auditor (1 finding + 5 out of mandate + a verified list of no-finding checks: legacy port order against `OrdreFabrication.cs:709-1213`, AD-1/2/3/4/6/7, Never boundaries, CC-2/CC-3/CC-4 on new code). `failed_layers`: none.
- Diff passed to the subagents through a scratchpad file (`diff-4-13.patch`, 1096 lines) that holds only the diff. Blind-hunter was told to read only that file.
- Diff stats: 13 files, +730 / −67 (production: `ConsignesMapper.cs`, new `LibelleConsigneComposer.cs`; tests: 6 files; script: `e2e-worker-import.ps1`; artifacts: spec, epics.md, sprint-status.yaml, deferred-work.md).
- Triage read the code at every cited location: mapper, composer, entity, persister A-5, parity test, E2E script, spec, and the 4.12 CC-3 precedent.
- No `dotnet test` or E2E run: test runs wipe `AscoLSI_Test`.

## 6. Application (2026-09-24)

All 6 patches were applied (option 1, "apply every patch"). They are uncommitted and ready for `/commit-review`.

- P1 `Kape22Persister.cs:124-126`: the comment now says "true or false per decoded row since Story 4.13 (the working copy), so the two halves never collide with each other".
- P2 `ConsignesMapperTests.cs:490`: renamed `Map_EveryProducedConsigne_HasMirroredGpaoHalvesAndSizeCodeConsigneSet_AcFr19_4`. No other reference in the repo.
- P3 `Kape22ProductionDataParityTests.cs:348`: added `row.ConsigneGPAO &&`.
- P4 `e2e-worker-import.ps1`: added the `$svtWorking` count (ConsigneGPAO = 0 AND SizeCodeConsigne = 0) and its throw, plus a comment.
- P5 `Kape22ProductionDataParityTests.cs:280-285`: re-wrapped the comment. The text is unchanged.
- P6 `Kape22ProductionDataParityTests.cs:417-439`: removed `.Concat(...)`. The `Where` now also requires a matching `1` row (`is { } received`). A production `0` row without a `1` row therefore no longer marks the section as edited either, because a null code used to count as "differs". Both unpaired directions now fail as a regression in the per-row loop. The `CompareWorkingRows` comment and the spec's Suggested Review Order were updated.
- Spec: D1 and P1-P6 are checked in `### Review Findings`.
- Verification: `dotnet build TextToXml.sln -warnaserror` → 0 warnings, 0 errors. No test run, because runs wipe `AscoLSI_Test`. `Category=Unit` and parity should be run once the test data can be dropped.
- Sprint status: not touched. Moving 4-13 to `done` is left to `/commit-review` (spec task "step-05").
