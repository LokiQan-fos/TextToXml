# Review report — Story 4.4-bis

Range : 48c92f614f0c082466de0640185859fd0c155c25^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-4-4-bis-decomposition-l-d-consignes-sous-champs-consignegpao.md
Date : 2026-09-23
Verdict : REFUSÉ
Findings : D=4 P=11 F=2 R=9  (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**REFUSÉ.** One high-severity functional gap (D-1: 6 of the 30 production `ConsigneGPAO=1` rows are still not produced), one high-severity verification gap that hides it (P-1), plus confirmed CC-1 and CC-3 deviations.

| Severity | Count |
|---|---|
| high | 2 (D-1, P-1) |
| medium | 6 (D-2, D-3, D-4, P-2, P-3, P-4) |
| low | 9 (P-5..P-11, F-1, F-2) |

## 2. Decision findings (to be decided by the human)

### D-1 — XP1 size-18 block: 6 production rows still missing (high)
- **Location:** `src/Kape22Importer/ConsignesMapper.cs:158-172`, spec `:41-42` (frozen I/O matrix), `tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs:231`
- **Source:** blind-hunter (+ edge-case, acceptance-auditor out_of_mandate), verified by the parent against the legacy source and the sprint-change-proposal.
- **Description:** the proposal justifies the 60 production rows as "5 sections × (sub-fields + full code) × 2". With the Code Map that gives XC1 6 + LA1 5 + PC1 6 + XA1 4 + XP1 = 21 + XP1. You only get 30 rows per `ConsigneGPAO` value when XP1 counts 9 rows (13/16/17 + 24..29). Without the size-18 block it is 24, which gives 48, not 60. So production does carry the types 24–29 rows for OF 2039771, while the mapper produces 24 rows (confirmed by the E2E assertion `6+3+5+6+4`). The `rawCode.Length >= 13` gate on `CodeConsigneDecoupe` is unreachable and does not match the real source: legacy reads `GetConsignes("ConsignesDecoupeLingot", 24, gpao)`, which is an existing consigne of the OF, not the P60 field (`OrdreDeFabricationManager.cs:1560`). Knock-on effects: the frozen I/O matrix (rows 41–42) contradicts the renegotiation, and row 13 would store more than 12 characters with `SizeCodeConsigne=12` if the gate ever fired. AC-4 ("30 of the 60 rows identical") is therefore **not met**. The parity test does not see it (see P-1).
- **Options:**
  1. Find the real source of code 24 (positions 526–636 of the Fichier, not described by the Descripteur; or another KAPE22 field) → the story stays `in-progress`.
  2. Accept the gap: remove the speculative `>=13` block, restate AC-4 as "24 of 30, XP1 24–29 excluded" with a dated renegotiation note, exclude XP1 24–29 explicitly from the parity reverse check, and add a deferred-work entry.
  3. Keep it as is (not recommended: AC-4 is false as written).
- **State:** to be decided by the human.

### D-2 — Blank code → zero rows, no renegotiation note (medium)
- **Location:** `src/Kape22Importer/ConsignesMapper.cs:121,147`
- **Source:** acceptance-auditor
- **Description:** for the 6 decoded sections, an applicable section with a blank code no longer produces any row (before: one row with `CodeConsigne=""`). The frozen block (Intent + matrix) does not cover this case and has no dated note. Legacy supports the behavior (`if (!string.IsNullOrEmpty(_global))`, lines 1457/1493/1524/1563/1618/1646).
- **Options:** (1) add a dated renegotiation note citing the legacy guard (recommended); (2) restore the old behavior.
- **State:** to be decided by the human.

### D-3 — Self-admitted CC-1 deviation (medium)
- **Location:** `tests/Kape22Importer.Tests/ConsignesMapperTests.cs:43,173,288,313`
- **Source:** acceptance-auditor
- **Description:** the blank-code skip, `PadForSlicing` and the Decoupe 12/13 threshold went into production code before the tests that drive them. The comments say so themselves ("code-review patch … never actually exercised before").
- **Options:** (1) accept as an exception and record it in the commit attestation of `/commit-review`; (2) refuse and require a documented red→green cycle.
- **State:** to be decided by the human.

### D-4 — Annex: `règle` status hides SVT `à_clarifier` (medium)
- **Location:** `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md:210-211`
- **Source:** acceptance-auditor (+ blind-hunter: the annex points to the spec's Code Map instead of carrying it, and says nothing about XP1 24–29 being `assumed, unverified`)
- **Description:** `TypeConsigne`/`SizeCodeConsigne` are marked `règle` as a whole, while SVT stays `à_clarifier` in the cell text. The `MappingAnnexCompletenessTests` gate therefore does not see the partial `à_clarifier`.
- **Options:** (1) split SVT into its own row/status `à_clarifier` and move the per-section table into the annex; (2) accept and document the mixed status.
- **State:** to be decided by the human.

## 3. Patch findings

| ID | Sev. | Location | Description | Proposed fix |
|---|---|---|---|---|
| P-1 | high | `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:307` | The parity test only checks mapped→production. Under-production (the very bug of this story, and D-1) goes undetected, and an empty `testRows` passes vacuously. Sources: edge-case + verification-gap + blind-hunter. | Add the reverse check: production `ConsigneGPAO=1` rows of the mapped `CodeOperation`s with no mapped `(CodeOperation, TypeConsigne)` go into `regressions`. Also assert `testRows.Count == consignes.Count` after `RoundTripConsignes`. Depends on D-1 (explicit XP1 24–29 exclusion if option 2). |
| P-2 | medium | `src/Kape22Importer/ConsignesMapper.cs:188` (and `:97-102`) | The "assumed, unverified" marker citing deferred-work.md (AC-FR19-4 pattern) disappeared for LibelleConsigne and SVT TypeConsigne/SizeCodeConsigne. | Restore the marker in both places. |
| P-3 | medium | `tests/Kape22Importer.Tests/Kape22FichierProcessorTests.cs:231` | The E2E test only counts rows. | Assert `ConsigneGPAO=true` and the `(CodeOperation, TypeConsigne, SizeCodeConsigne)` set on the persisted rows. |
| P-4 | medium | `tests/Kape22Importer.Tests/ConsignesMapperTests.cs:35` (and following) | Expected values are computed with `padded.Substring(...)`, the same logic as the mapper (self-agreeing). The deferred-work claim "pinned to human-reviewed values" is false. | Replace with literal expected values for the reference fixture. |
| P-5 | low | `tests/Kape22Importer.Tests/ConsignesMapperTests.cs:134` (and SVT ~197) | CC-3 (comment preservation): the comments above the PoidsMetrique/SVT not-applicable tests were rewritten, with unchanged bodies. | Restore the original comments word for word. |
| P-6 | low | `src/Kape22Importer/Persistence/Kape22Persister.cs:125` | The A-5 comment cites `ConsignesMapper.Build` (removed) and has a broken wrap ("at" on a short line). | Cite `Row`, rewrap. |
| P-7 | low | `src/Kape22Importer/ConsignesMapper.cs:15-17` | The header states the blank-code rule for every section; SVT still emits a blank-code row (spec: unchanged). | Limit the sentence to the 6 decoded sections. |
| P-8 | low | `tests/Kape22Importer.Tests/ConsignesMapperTests.cs:392` | The AC-FR19-4 comment says only "one column" stays à_clarifier (Decoupe size-18). In fact LibelleConsigne and SVT TypeConsigne/SizeCodeConsigne do. | Fix the comment. |
| P-9 | low | `_bmad-output/implementation-artifacts/deferred-work.md:1132` | The SVT collision entry points at "another undecoded section". The real overlap is Chutage's `TypeConsigne=0` sub-field row. | Append a correcting entry (append-only). |
| P-10 | low | `_bmad-output/implementation-artifacts/epic-4-context.md:74` | "each of the 7 sections decodes", plus an independent optional Decoupe code, contradicts the spec. | Align: 6 sections, renegotiated XP1. |
| P-11 | low | spec `:98` (Suggested Review Order / Code Map) | Stale line numbers (`ConsignesMapperTests.cs:410/507/581` do not exist). | Update them. |

## 4. Defer findings

- **F-1** — Rows imported by the pre-4.4-bis mapper (`ConsigneGPAO=false`, `TypeConsigne=0`) cannot be told apart from OF-initial rows; there is no remediation note. Pre-existing (old mapper behavior), only relevant if an import reached a shared database. Written to deferred-work.md.
- **F-2** — `L_D_COULEE` has no column-by-column parity and no tracker entry (`epic-4-context.md:91`). Pre-existing, outside this story. Written to deferred-work.md.

## 5. Rejected findings (noise)

1. `sprint-status.yaml` committed at `backlog`: the working copy is already at `review`, and `/commit-review` will set `done`.
2. Raw code made only of whitespace: the XML normalization right-trims it, so it becomes empty and the guard catches it.
3. Empty or blank sub-field rows "not matching legacy": false. Legacy `AddOrModifyConsigne` (lines 1362–1435) writes the row whatever the value.
4. Case-insensitive `CodeOperation` comparison in the parity test: production and mapper use the same case, so this is theoretical.
5. `FirstOrDefault` on production duplicates: deliberate choice, commented in the code.
6. Parity test without an AC trait: follows the file's convention (no other parity test carries one).
7. `review_loop_iteration: 0` in the spec: cosmetic, handled at closure.
8. No multi-section collision test: the existing A-5 tests still fire through row 13 (confirmed by verification-gap).
9. Removal of Build's null→"" fallback: intentional (legacy guard), covered by D-2.

## 6. Self-checks

- Lenses run: 4/4 (blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor). No failed layer.
- Diff: 10 files, +787 / −128 (1211 lines).
- Checks passed per the auditor: offsets match legacy (1452–1664), the Decoupe renegotiation note is dated, deferred-work is append-only, AD-3 (no legacy reference), AD-2, AD-7, CC-4 (initializer order).
- Parent-side verifications: legacy `CompleteConsignes2` and `AddOrModifyConsigne` read directly; the 60/30/24 arithmetic cross-checked against the proposal (lines 42–43, 254) and the E2E assertion.

## 7. Outcome (2026-09-23)

### Decisions resolved by the human

| ID | Choice | Result |
|---|---|---|
| D-1 | Option 1: find the real source of code 24 | **Source found**: `LibelleConsigneDecoupe` (P60.xml Position 287, Size 18). Legacy `OrdreFabrication.cs:627-634` turns the 2nd Decoupe consigne into type 24/size 18. All 324 Decoupe records of the 350 `P60/` fixtures carry a structured code there (`.LLLLL BC X.XM`). Human-approved renegotiation → patch **P-12** |
| D-2 | Option 1: dated renegotiation note | Patch **P-13** (note in the spec's Boundaries, no code change) |
| D-3 | Option 1: accept the CC-1 exception | **To record in the /commit-review commit body**: "CC-1 exception: blank-skip and PadForSlicing tests added post-implementation during review". The P-12 tests themselves followed a documented red→green cycle (6 red, then green). |
| D-4 | Option 1: split SVT | Patch **P-14** (separate `à_clarifier` SVT rows; per-section decode list moved into the annex as bullets, since the parser rejects any 5-cell `|` table under a `###` heading) |

### Patches applied (14/14)

| ID | Files |
|---|---|
| P-1 | `Kape22ProductionDataParityTests.cs`: reverse check + `Assert.Equal(consignes.Count, testRows.Count)` |
| P-2 | `ConsignesMapper.cs`: "assumed, unverified" marker restored on LibelleConsigne and SVT |
| P-3 | `Kape22FichierProcessorTests.cs`: 30 rows, ConsigneGPAO, size 12/18, Decoupe type set |
| P-4 | `ConsignesMapperTests.cs`: literal expected values (Chutage, short Chutage, Lingot, Pits, Refroidissoirs, Decoupe); `Pad12` removed |
| P-5 | `ConsignesMapperTests.cs`: original PoidsMetrique/SVT comments restored word for word |
| P-6 | `Kape22Persister.cs`: `Build` → `Row`, wrap fixed |
| P-7 | `ConsignesMapper.cs`: blank-code rule limited to the 6 sections in the header |
| P-8 | `ConsignesMapperTests.cs`: AC-FR19-4 comment corrected |
| P-9 | `deferred-work.md`: corrective entry appended (collision with Chutage type-0 row) |
| P-10 | `epic-4-context.md`: 6 sections, independent `LibelleConsigneDecoupe` source |
| P-11 | spec: Suggested Review Order / Code Map line numbers updated |
| P-12 | `ConsignesMapper.AddDecoupe(…, sizeTwelveCode, sizeEighteenCode)`; `>=13` gate removed; 4 Decoupe tests rewritten (reference literals, size 12 alone, size 18 alone, 18 characters full width); 12/13 threshold tests removed; all-six = 34; spec renegotiation note, I/O matrix and Code Map; annex; resolution entry in deferred-work.md |
| P-13 | spec: dated blank-code note (Boundaries) |
| P-14 | annex: SVT split (TypeConsigne/SizeCodeConsigne `à_clarifier`), "Décodage par section" list |

### Final verification

- `dotnet build TextToXml.sln -warnaserror`: 0 errors, 0 warnings.
- `dotnet test TextToXml.sln`: TextToXml.Tests 192/192, Kape22Importer.Tests 849/849, 0 failures. Local SQL Server reachable: Integration tier executed.
- **AC-4 actually run against production**: `MappedFichier_ConsignesRows_MatchLegacyProductionRows` (now checking both directions) **341 passed, 9 skipped** over the 350 fixtures (no applicable section or no production row), 0 failures, `P60_847_682_001` (OF 2039771) included. This confirms D-1 against production data.

### Status

- Spec: every finding checked; status `done`.
- `sprint-status.yaml`: `4-4-bis-…: done`. `epic-4` left `in-progress` (switch back to `done` at closure / PROJECT-CLOSED.md update, outside this review).
- Not committed (commit via `/commit-review`).

### Addendum: 250 new real P60 fixtures (2026-09-23)

- `P60/` extended from 100 to 350 real Fichiers (250 staged with `git add`).
- 3 new fixtures (240/241/242, OF 2040057-2040059) diverged from production on `L_D_SECTIONCHARGE_LINGOT.SectionLaminage` (P60 250.0, production 245.0). This is not a mapper defect: the P60 is a forecast, and the operator adjusted the value through the legacy CommandeENC screen, which logged it. Production read-only SELECT confirmed `Changement de Consigne Section: 250,0 --> 245` (MCC, 2026-09-15) for all three OFs.
- Outside the 4.4-bis scope, `MappedFichier_ScaledDownstreamColumns_MatchLegacyProductionRow` now accepts a scale-guarded column difference only when the latest logged CommandeENC edit (`L_D_LOG_COMMANDE`, Commande `MCC`) of that column's label ends on exactly the production value. The 6 columns editable on that screen are mapped explicitly (`OperatorAdjustedColumns`); every other column keeps the strict comparison.
- Final run: build `-warnaserror` clean. TextToXml.Tests 192/192. Kape22Importer.Tests 1831 passed, 18 skipped (no production row for the OF), 0 failures. Production parity 1032 passed, 18 skipped, 0 failures.
