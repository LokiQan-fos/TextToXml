# Review report — Story 4.12

Range : 21075e592abcec25f67b75929c805a638337beb8^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-4-12-libelle-consigne.md
Date : 2026-09-24
Verdict : REFUSÉ
Findings : D=2 P=9 F=3 R=13  (Decision, Patch, Defer, Rejetés)

Round 2 (re-review after the round-1 patches, commit 2842a06). The round-1 report (D=1 P=11 F=8 R=23, all resolved) is in git history at `2842a06:_bmad-output/implementation-artifacts/reviews/story-4-12/aggregated-report.md`.

Refusal grounds:
- CC-1: no AC → test attestation in either story-4.12 commit (D1).
- Project-profile § Langues: French identifiers left in the tests (P4).
- The "Never write to production" guard can be bypassed through a server alias (P1, high).

Severity count (kept findings): high 1 (P1) · medium 3 (D1, P2, P4) · low 10 (D2, P3, P5, P6, P7, P8, P9, F1, F2, F3).

## 1. Decision — à trancher par l'humain

### D1 — CC-1: no AC → test attestation in the story-4.12 commits
- Source : acceptance-auditor
- Location : commits `21075e5` and `2842a06` (messages)
- Description : both commit bodies contain only the `Co-Authored-By` trailer. There is no `AC-FR19-5 → Fichier.Test` line and no mention of « test ». The CI gate (epic-3-retro-item-2) rejects this for `chore(story-*)`. `master` is **ahead 2** of `origin/master`, so neither commit has been pushed.
- Options :
  1. Rewrite both messages before pushing (non-interactive `git rebase` with `reword` via `GIT_SEQUENCE_EDITOR`/`commit --amend`), adding `AC-FR19-5 → LibelleConsigneResolverTests.*`, `Kape22FichierProcessorIntegrationTests.Import_SeededReferenceRows_PersistResolvedLibelles_AcFr19_5` and `Kape22ProductionDataParityTests.MappedFichier_ConsignesRows_MatchLegacyProductionRows`.
  2. Leave history as is: carry the full attestation in the next `/commit-review` commit body and record a dated CC-1 exemption in the spec's Spec Change Log.
- État : à trancher par l'humain.

### D2 — CC-3 « preservation » vs a comment made false by a change elsewhere
- Source : acceptance-auditor + verification-gap
- Location : `src/Kape22Importer/InboxScanner.cs:314` (already reworded in the diff, code under it unchanged); `tests/Kape22Importer.Tests/WorkerLoopRobustnessIntegrationTests.cs:130-134,156-158` (not reworded, now false)
- Description : CC-3 says existing comments stay identical unless the code under them changes. Story 4.12 made `ConsigneReferenceData.Load` the first database access, so the statement "Only Kape22Persister emits PersistenceError" and "Kape22Persister catches the SqlException" became false. The InboxScanner comment was updated. The WorkerLoop test comments were not. Taken literally, the rule forbids the first edit and requires keeping the second as is.
- Options :
  1. Record a dated CC-3 clarification in the spec: a comment made factually false by a change elsewhere is updated. Keep InboxScanner as is and also update the two WorkerLoop test comments (reference-data read → processor → scanner).
  2. Apply CC-3 literally: revert the InboxScanner comment and leave both test comments unchanged, stale.
- État : à trancher par l'humain.

## 2. Patch

### P1 — Production guard bypassed by server aliases (high)
- Source : edge-case-hunter + blind-hunter + acceptance-auditor (out of mandate)
- Location : `scripts/sync-reference-consignes.ps1:94`
- Description : the guard compares the raw `Server`/`Database` strings. `AFV004-LSI` vs `AFV004-LSI.domain.local`, `tcp:AFV004-LSI,1433`, an IP or `.` all pass it, and CREATE/TRUNCATE/`bcp in` then run on production.
- Action : before any write, run `SELECT @@SERVERNAME, DB_NAME()` on both targets (a read-only SELECT on production is allowed) and refuse when the results match. Keep the string check as a first, offline guard.

### P2 — E2E label gate cannot catch unresolved labels
- Source : verification-gap
- Location : `scripts/e2e-worker-import.ps1:163`
- Description : `Resolve` never returns null (every miss gives `"?"`), so `nullLibelles` is always 0. Suppose the sync loads nothing, or the worker reads an empty snapshot. Every lookup label then persists as `"?"` and the gate passes. Step 7's parity theory maps in-process, so it never checks what the worker persisted.
- Action : also count `LibelleConsigne = '?'` and throw when it equals the row total (production has a real label on every row of a normal OF).

### P3 — `Get-SqlTarget`: user without password
- Source : edge-case-hunter
- Location : `scripts/sync-reference-consignes.ps1:81`
- Description : a `User ID` with no `Password` puts `$null` after `-P`. PowerShell drops it, so sqlcmd/bcp either prompt or read the next switch as the password.
- Action : `if ($user -and $null -eq $password) { throw "ConnectionStrings:$Name has a User ID but no Password." }`.

### P4 — French identifiers remain in the resolver tests
- Source : acceptance-auditor
- Location : `tests/Kape22Importer.Tests/LibelleConsigneResolverTests.cs:150,209,221,224`
- Description : round-1 D1/P12 renamed the French locals in the resolver only. The tests still have `profil`, `diametre`, `hasDiametre` and `Resolve_PitsParticulier_…`.
- Action : rename to `profile`, `diameter`, `hasDiameter` and `Resolve_PitsParticular_AppendsParticularLibelle_AcFr19_5`.

### P5 — Frozen matrix annotation "H2Coulee null: assumed, unverified" is outdated
- Source : acceptance-auditor
- Location : `_bmad-output/implementation-artifacts/spec-4-12-libelle-consigne.md:55` (frozen), `tests/Kape22Importer.Tests/LibelleConsigneResolverTests.cs:216`
- Description : round-1 P1 established that a NULL `H2Coulee` giving `"?"` is verified legacy behaviour. The frozen row and the test comment still call it "assumed, unverified", and the Spec Change Log is empty. The behaviour itself does not change.
- Action : add a dated Spec Change Log entry. Only "pits section absent" stays assumed, unverified; "H2Coulee null" is verified (`LibelleConsigneController.cs:224-225`, inside the try). Update the test comment to match.

### P6 — Four shared-branch sections are never exercised
- Source : blind-hunter
- Location : `tests/Kape22Importer.Tests/LibelleConsigneResolverTests.cs:79`
- Description : the switch sends FD1, FD2, FD3, FC1, XA2 and XA1 to one cooling branch, but only FD1 and XA1 are tested. If a `case` label were dropped, that section would silently give `"?"`.
- Action : add `InlineData` rows for FD2, FD3, FC1 and XA2, with snapshot rows to match.

### P7 — `PersistenceFailure` doc comment ignores its new caller
- Source : blind-hunter
- Location : `src/Kape22Importer/Persistence/Kape22Persister.cs:439`
- Description : the helper went from private to internal (code changed, so CC-3 allows the update). The comment still describes only the persister flow.
- Action : add one sentence. Kape22FichierProcessor also calls it for a failed reference-data read, with no prior errors, and overrides NormalizedXml and Warnings.

### P8 — PROJECT-CLOSED.md still says Story 4.12 "is open"
- Source : blind-hunter
- Location : `_bmad-output/implementation-artifacts/PROJECT-CLOSED.md:11`
- Description : the spec and `sprint-status.yaml` say `done`, and epic-4 stays `in-progress`.
- Action : reword the note: "Story 4.12 … is done; epic-4 stays in-progress until re-closure".

### P9 — `review_loop_iteration` not updated
- Source : blind-hunter
- Location : `_bmad-output/implementation-artifacts/spec-4-12-libelle-consigne.md:5`
- Action : set to `2`.

## 3. Defer

- F1 — The 13 reference table names are maintained by hand in 4 places plus the DDL: `scripts/sync-reference-consignes.ps1:27`, `ConsigneReferenceData.cs:77`, `PersistenceSmokeTests.cs:40` and `SqlServerIntegrationFixture.cs:93`. The same hand-maintained-enumeration smell is already deferred (PROJECT-CLOSED §4). The smoke test's table count catches part of the drift.
- F2 — The E2E label check hard-codes `-S localhost -d AscoLSI_Test` (`scripts/e2e-worker-import.ps1:161`), while the sync reads `appsettings.Test.json`. This follows the pre-existing `Invoke-Sql` pattern (`:68`) that the whole script uses. Not a secret, so no CC-7 breach.
- F3 — `ConsigneReferenceData.Load` catches only `DbException` (`Kape22FichierProcessor.cs:82`). A pool-timeout `InvalidOperationException` or a `SqlNullValueException` from a schema drift is classified `UnexpectedFailure` (goes to error/). This is the same filter as the pre-existing persister catch, and extends round-1 F1.

## 4. Rejetés (bruit)

- R1 NULL `DateMaj`/`DateReception` makes `consulted > dateReception` false, so the row counts as a regression. Conservative: it fails rather than skips silently, and parity is green.
- R2 Second `Resolve` call in the parity test just to get `LatestDateMaj`. Same inputs, documented at the call site.
- R3 An extra production round-trip per Fichier in the parity test. Opt-in tier, and the cost is irrelevant.
- R4 `int.Parse(code)` before the null-input check in `Degazage`. Both orders give `"?"`, so the result is identical.
- R5 A null `profilProduit` gives `"0"`, not `"?"`. Faithful to legacy: the null profile never equals a row, so legacy also gave `"0"`.
- R6 The early-return journal has `numeroFichier: null`. `fichierName` is journaled, and the Fichier is retried.
- R7 The AcCoverageCompletenessTests comment names only `LibelleConsigneResolverTests.cs`. Still true, just not exhaustive.
- R8 `ConsignesMapperTests` uses `Assert.NotNull`. Specific labels are asserted by the resolver tests, and forwarding by the integration test (round-1 P4).
- R9 The integration test seeds only 2 of the 13 tables. Duplicate of round-1 F7 (deferred).
- R10 The `!IsRelational()` → `Empty` branch has no dedicated test. Every Unit processor test uses the in-memory context and goes through it.
- R11 CC-2 flags the "`# --- 0. … ---`" heading in `e2e-worker-import.ps1:86`. It follows the file's pre-existing step-heading convention (`--- 1.` to `--- 7.`).
- R12 The P60 fixtures `_351` to `_354` are real, distinct production Fichiers (different OFs) that the `P60Fichiers` theory picks up. The extra `63211` is the file's OF, not an anomaly.
- R13 Spec, deferred-work and report cite different E2E run ids (`_001`, `_351`, `_352`). Every run uses a fresh Fichier, so each citation is accurate for its own run.

## 5. Auto-vérifications

- Layers launched: 4/4 (blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor). Failed layers: none.
- Diff: 28 files, +1888/−47. The review diff is 2349 lines, `P60/` fixtures included.
- Checks run during triage:
  - Commit bodies of `21075e5`/`2842a06`: trailer only. Precedents `4abad8c`/`48c92f6` carry AC lines.
  - `master` is ahead 2 of origin, not pushed.
  - Persister catch filter: `DbUpdateException or DbException`.
  - The resolver switch sections compared against the `InlineData` sections.
  - The e2e step-heading convention.
  - The `P60/_351` to `_354` contents (distinct OFs).
  - `sprint-status.yaml`: epic-4 `in-progress`, 4-12 `done`.
  - The CC-3 wording (`epics.md:263`).

## 6. Resolution (2026-09-24)

- D1 → option 1, became **P11**. Reword both commit messages (`21075e5`, `2842a06`) to add the attestation: `AC-FR19-5 → LibelleConsigneResolverTests.*`, `Kape22FichierProcessorIntegrationTests.Import_SeededReferenceRows_PersistResolvedLibelles_AcFr19_5` and `Kape22ProductionDataParityTests.MappedFichier_ConsignesRows_MatchLegacyProductionRows`. The history rewrite runs only after an explicit human go, before any push.
- D2 → option 1, became **P10**. Add a dated CC-3 clarification to the Spec Change Log: a comment made factually false by a change elsewhere is updated. The InboxScanner comment stays as is, and the comments at `WorkerLoopRobustnessIntegrationTests.cs:130-134` and `:156-158` are updated.
- Patches P1–P10 applied (option 1, "apply every patch"):
  - P1: `Get-ServerIdentity` compares `@@SERVERNAME/DB_NAME()` on both targets. Production is opened with `-K ReadOnly`, SELECT only.
  - P2: the gate fails when every label is `"?"`.
  - P3: a User ID without a Password throws.
  - P4: renames `profile`, `diameter`, `hasDiameter`, `Resolve_PitsParticular_…`.
  - P5 and P10: two dated Spec Change Log entries; the resolver test comment and both WorkerLoop comments are updated.
  - P6: FD2, FD3, FC1 and XA2 type-21 `InlineData`.
  - P7: `PersistenceFailure` comment.
  - P8: PROJECT-CLOSED note.
  - P9: `review_loop_iteration: 2`.
- P11 (history rewrite): run by the user on 2026-09-24 (the rebase was blocked for the agent). `21075e5` → `ec65715` and `2842a06` → `d51055c`, both with AC-FR19-5 → test lines. `git diff backup/story-4-12-pre-reword HEAD` is empty (same content), and the working tree was restored through autostash. The range is now `ec65715^..HEAD`.
- Verification after patches:
  - `dotnet build -warnaserror`: 0 warnings, 0 errors.
  - Unit: 192 + 819 green (+4 from P6).
  - Both scripts parse with 0 errors.
  - Sync run: the P1 identity guard passed (both identity queries answered, and the targets differ). The run was then stopped: `bcp in` waited more than 5 min on `RESOURCE_SEMAPHORE`, because the host had 543 MB free RAM out of 12 GB. This is environmental, and the `bcp` path did not change. The 13 test reference tables are left truncated until the next sync.
  - Integration tier and E2E not rerun.
