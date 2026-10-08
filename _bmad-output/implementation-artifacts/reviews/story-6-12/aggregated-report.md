# Review report — Story 6.12

Range : 81f7491^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-12-exception-fichier-plafond-commande-blanche.md
Date : 2026-10-08
Verdict : REFUSÉ
Findings : D=1 P=5 F=2 R=17  (Decision, Patch, Defer, Rejetés) — D-1 tranché 2026-10-08 : option 2 → P-6 + P-7 ; F-2 promu en P-7

> Story key resolution: the only commit for the story is `81f7491 fix(story-6.12): …`, so no subject
> matches `chore(story-6.12)`. A strict reading of the rule would select `90144dd chore(story-6.11)`,
> which has already been reviewed and closed. Story 6.12 was taken from the explicit `/run-review 6.12`
> argument, and the range was built from the `fix(story-6.12)` commit.
>
> The MicroServices part of the story (`GPAO/ImportP60/Client.cs`, `GPAO/ImportP60.Tests/ClientConfigurationTests.cs`)
> was committed to SVN in r551 on 2026-10-08. Its `svn diff -c 551 GPAO` (45 lines) was appended to the git
> diff sent to the lenses.

## 1. Verdict

**REFUSÉ.** Reason:

- **D-1 (medium, CC-1).** Two behaviors that the review applied during the build (P-1 and P-2 of the Spec Change Log) shipped without a test, and therefore with no recorded red run.
  - P-1: the Warning carries the exception only for a non-I/O cause. It guards the frozen Always rule "I/O keeps today's behavior".
  - P-2: `TryDelete` catches any exception.

  The spec records this gap ("P-1 and P-2 have no test") but does not justify it against CC-1. For P-1 the justification is weak: `RecordingLogger` only needs to keep the `Exception?` it already receives, a one-line change.

What conforms:

- Frozen block respected:
  - P60 cause `"<Type>: <Message>"` for a non-I/O cause; a transient I/O cause keeps its bare message, pinned by `DoesNotContain(nameof(IOException))`.
  - P89 reason `Fichier : <Type> : <Message>`.
  - `Func<byte[], P89Conversion>` seam, as listed in the Code Map.
- Always / Never respected:
  - export deleted for every filing-failure type;
  - no move to `error/`;
  - `processor.Process` quarantine unchanged;
  - no catch around the inbox → `processing/` move or the listings;
  - no new log channel;
  - counter not persisted;
  - `Commande` neither trimmed nor defaulted in the library.
- Ask First respected: `GpaoConvertP89` not touched.
- AD-3 and AD-4 respected.
- CC-2: English comments. CC-4: P89 fields and constructor assignments are in order. CC-7: no secrets.
- Reachability checked against the code:
  - `Import:MaxAttempts` is validated `>= 1` in `ReadConfig` (`Client.cs:133`).
  - The `Move` calls are the last statements of `Archive`, `Reject` and P89 `Accept`, so there is no state change after the point of no return, apart from P89 `Reject`, already deferred as D-1 of the build.

| Severity | Count | IDs |
|---|---|---|
| high | 0 | — |
| medium | 2 | D-1, F-1 |
| low | 6 | P-1, P-2, P-3, P-4, P-5, F-2 |

## 2. Decision (to be settled by the human)

### D-1 — P-1 / P-2 (build review) shipped without a test (CC-1, medium) — settled 2026-10-08: option 2 → P-6 + P-7

- **Location:**
  - `src/Kape22Importer/InboxScanner.cs:272`, `:348` (`IsTransient(exception) ? null : exception`)
  - `src/Kape22Importer/InboxScanner.cs:435` (`TryDelete`, `catch (Exception)`)
  - `tests/Kape22Importer.Tests/InboxScannerTestSupport.cs:173` (`RecordingLogger` drops the exception)
- **Sources:** verification-gap + blind-hunter.
- **Finding:** inverting the ternary, or always passing `exception`, leaves every test green. A transient I/O fault would then log a stack trace on every attempt, against the frozen Always rule. Widening `TryDelete` has no seam (`File.Delete`). The spec records the gap without giving a CC-1 reason.
- **Options:**
  1. P-1 tested, P-2 accepted untested. `RecordingLogger` keeps the `Exception?`. The `RunTick_FichierLeftInProcessingForMaxAttemptsTicks_LogsOneErrorOnTheLast_AcFr25_8` theory (`InboxScannerTests.cs:722`) asserts a null exception on I/O rows and a non-null one on non-I/O rows. Red is shown by temporarily inverting the ternary. P-2 stays untested and is recorded in the Spec Change Log, since there is no `File.Delete` seam. → becomes a Patch.
  2. Both tested. Option 1, plus a delete seam (`Action<string>` or an `InMemoryFileSource` hook) so a non-I/O exception can be thrown from `TryDelete`. → a larger Patch, a new seam in production.
  3. Accept as is. Record a dated CC-1 exception for P-1 and P-2 in the Spec Change Log. → no code change.

## 3. Patch

### P-1 — Stale comment in P89 `Accept` (low)

- **Location:** `src/P89Converter/P89FolderConverter.cs:198`
- **Sources:** acceptance-auditor (out_of_mandate).
- **Finding:** "Process turns the rethrown file-system fault into a Deferred outcome." Since 6.12, `Process` defers any exception, not only a file-system fault. The header comment (line 19) was updated, this one was not.
- **Corrective action:** "Process turns the rethrown exception into a Deferred outcome (Story 6.12: any type)."

### P-2 — P89 `TryDelete` still filters I/O only, while P60's was widened (D32 parity, low)

- **Location:** `src/P89Converter/P89FolderConverter.cs:158`
- **Sources:** edge-case-hunter + verification-gap.
- **Finding:** a non-I/O exception from `File.Delete` in `Accept`'s `catch` would replace the original cause. The widened `Process` catch would then give a `Deferred` with a misleading reason. P60 widened its own `TryDelete` (build P-2). D32 requires the same fix in both formats.
- **Corrective action:** `catch (Exception)` with no filter. Update the comment above (lines 150-151) to say "any exception".

### P-3 — No test checks that the blank-`Commande` message names `P60` (build P-5, low)

- **Location:** `../MicroServices/GPAO/ImportP60.Tests/ClientConfigurationTests.cs:315`
- **Sources:** verification-gap + blind-hunter.
- **Finding:** `AssertThrowsNaming` only checks the key. The mention of the `'P60'` default (`Client.cs:145`), added by build P-5, can disappear without any test failing.
- **Corrective action:** in `ReadConfig_CommandeBlank_ThrowsNamingTheKey_AcFr25_9`, also check that the message contains `P60`, either through `Assert.Throws` and `Assert.Contains("'P60'", …)` or through a variant of the helper. **SVN commit by the user.**

### P-4 — Ambiguous comment in the `processor.Process` catch (low)

- **Location:** `src/Kape22Importer/InboxScanner.cs:288`
- **Sources:** blind-hunter.
- **Finding:** in "since that is a defect, unlike a read fault, handled above", the words "handled above" can attach to "a defect" instead of to "a read fault", which blurs the AC-FR13-4 / AC-FR25-8 distinction the sentence explains. The line was edited by build P-6.
- **Corrective action:** "since that is a defect, unlike a read fault, which is handled above".

### P-5 — Spec Verification leaves out `ConvertP89.Tests` (low)

- **Location:** `_bmad-output/implementation-artifacts/spec-6-12-exception-fichier-plafond-commande-blanche.md:84`
- **Sources:** blind-hunter.
- **Finding:** the internal constructor of `P89FolderConverter` changed, so the schema seam became a convert seam. The Spec Change Log says `ConvertP89.Tests` was run (68 green), but the reproducible Verification list does not include it.
- **Corrective action:** add `dotnet test ../MicroServices/GPAO/ConvertP89.Tests -- expected: 0 failures`. Verification sits outside the frozen block.

### P-6 — Test the I/O-only exception on the read and filing Warnings (ex-D-1, build P-1, medium)

- **Location:** `tests/Kape22Importer.Tests/InboxScannerTestSupport.cs:173`; `tests/Kape22Importer.Tests/InboxScannerTests.cs:722`; covers `src/Kape22Importer/InboxScanner.cs:272`, `:348`.
- **Origin:** D-1, option 2, settled 2026-10-08.
- **Corrective action:**
  - `RecordingLogger` keeps the `Exception?` it receives next to each entry.
  - The `RunTick_FichierLeftInProcessingForMaxAttemptsTicks_LogsOneErrorOnTheLast_AcFr25_8` theory asserts a null exception on the I/O rows' Warnings and a non-null `InvalidOperationException` on the non-I/O rows.
  - Red shown by temporarily inverting the ternary (CC-1), recorded in the Spec Change Log.

### P-7 — Delete seam + test for the widened P60 `TryDelete` (ex-D-1 + ex-F-2, build P-2, low)

- **Location:** `src/Kape22Importer/InboxScanner.cs:435` (`TryDelete`, `File.Delete`); a new test in `tests/Kape22Importer.Tests/InboxScannerTests.cs`.
- **Origin:** D-1, option 2, settled 2026-10-08. F-2 is promoted here.
- **Corrective action:**
  - An internal delete seam (an `Action<string>` defaulting to `File.Delete`) in place of the direct `File.Delete` call.
  - A test: filing fails (non-I/O) and the delete throws `InvalidOperationException`. The tick goes on, the Fichier stays in `processing/` and is counted, and the "could not be deleted" Warning is logged.
  - Red shown by narrowing the catch back to I/O (CC-1).

## 4. Defer

### F-1 — P89: no test reaches the XML cleanup in `Accept` after the XML is written (medium)

- **Location:** `src/P89Converter/P89FolderConverter.cs:195-201` (bare `catch`, `TryDelete(xmlPath)`); `tests/P89Converter.Tests/P89FolderConverterTests.cs`
- **Sources:** verification-gap.
- **Finding:** no test makes `Accept` throw after `new FileStream(xmlPath, FileMode.CreateNew)`, whatever the exception type. If the `catch` were narrowed or the `TryDelete` dropped, nothing would turn red, and a retried Fichier would leave one orphan XML per attempt, up to `P89:MaxAttempts`. P60 pins the same case: `RunTick_UnexpectedFilingException_CountsTheFichierAndFilesTheNext_AcFr25_8`.
- **Justification:** the gap predates the story. The bare `catch` and its cleanup date from Story 5.x and were never tested for I/O either. The 6.12 change does not touch this code, which already handles every type. Closing the gap needs a new seam (a write or move after the XML) outside the Code Map, which is Ask First. Candidate for a test-hardening story.

### F-2 — P60 `TryDelete` widened (build P-2) without a test (low) — promoted to P-7 (D-1 = option 2, 2026-10-08)

- **Location:** `src/Kape22Importer/InboxScanner.cs:435`
- **Sources:** verification-gap + blind-hunter.
- **Finding:** a non-I/O exception from `File.Delete` would have aborted the tick before the widening. No test exercises it.
- **Justification:** `File.Delete` has no seam. `ExportToFolder` writes through `FileStream`, outside `IFileSource`. The spec already records the gap. Settled with D-1: if D-1 = option 2, F-2 becomes a Patch; otherwise it stays deferred.

## 5. Rejected (noise)

| # | Source | Finding | Rejection reason |
|---|---|---|---|
| R-1 | blind-hunter | `catch (Exception)` swallows `OperationCanceledException` / OOM | No `CancellationToken` reaches `Read`, `Archive`, `Reject` or `convert`, so no cancellation can arise there (the InboxScanner token is still deferred since 3.4). OOM is caught the same way by the existing `processor.Process` catch (AC-FR13-4). |
| R-2 | blind-hunter + edge-case-hunter | The cause drops `InnerException` (wrappers) | For a non-I/O cause the Warning carries the full exception object, inner exception and stack included. The cap's Error is a summary that points to it. The format is frozen by the spec. |
| R-3 | edge-case-hunter | `attempts` null or `MaxAttempts <= 0`: a non-I/O fault retries forever | `ReadConfig` refuses `Import:MaxAttempts < 1` (`Client.cs:133`). A null counter is only a library use without a cap, and documented as such (`InboxScanner.cs:152`). |
| R-4 | blind-hunter | Fault after `Archive` / `Reject` moved the Fichier, counted wrongly | `fileSource.Move` is the last statement of `Archive` (`:400`) and `Reject` (`:466`). A `Move` that throws has not moved the Fichier. |
| R-5 | blind-hunter | P89 fault after the point of no return in `Accept` | `File.Move` is the last statement before `return`. The P89 `Reject` case is already deferred (build D-1). |
| R-6 | blind-hunter | Spec `status: done` vs sprint-status `review` | Workflow convention: bmad-build closes the spec, and sprint-status goes to `done` at /commit-review. 6.11 shows the same pair. |
| R-7 | blind-hunter | `review_loop_iteration: 0` despite the step-04 review | The counter counts loopbacks, and the Change Log says "no loopback". 6.11 is identical. |
| R-8 | blind-hunter + acceptance-auditor | RESOLVED dated 2026-10-07, commit on 2026-10-08 | The date is when the build started (spec `created: 2026-10-07`). Precedent: 6.11, RESOLVED 2026-10-06 and committed on 2026-10-07. |
| R-9 | blind-hunter | Old `PLANNED` clause kept after `RESOLVED` | Ledger convention, the history is kept (the two 6.11 RESOLVED lines also keep their PLANNED). |
| R-10 | blind-hunter | `Kape22Persister` cited at `:383`, `:269` and `:59` | `:59` is `CommandeKey` and `:383` the current site. `:269` is the line as it was at the 6.6 review (historical evidence, not to be rewritten). |
| R-11 | blind-hunter | `GPAO/…` vs `MicroServices/GPAO/…` paths | Both forms point to the same file without ambiguity. |
| R-12 | blind-hunter | Two-Fichier test missing for a non-I/O READ fault | The I/O matrix (frozen) only asks for the healthy-B case on FILING. On READ, the single-Fichier theory up to tick 4 would throw if the exception escaped the tick, so containment is proven. |
| R-13 | blind-hunter | Tick-1 filing test does not check the Warning type or the absence of an Error | The type and message are pinned by the cap's Error in the theory. A premature Error would break the "one Error on the last" assertion of the same theory. |
| R-14 | blind-hunter | P89 tick 2 not checked, no tick 4 | `Assert.Single(third)` + `Frozen` would fail if A had been frozen at tick 2, since there would be no outcome at tick 3. Tick N+1 is covered by `RunTick_FrozenFichier_GetsNoOutcomeNorJournalCall_AcFr25_8`. |
| R-15 | blind-hunter + acceptance-auditor | P89 repeats the I/O test inline, the format differs from P60 | The two formats (`"<Type>: <Message>"` / `Fichier : <Type> : <Message>`) are set by the spec's Tasks. A shared helper across two assemblies would be an unrequested abstraction. |
| R-16 | blind-hunter | `IsTransient` misnamed (`UnauthorizedAccessException`) | Spec vocabulary ("transient retry"). The pair predates the story (`TryList`, `PurgeRetention`). |
| R-17 | blind-hunter | `'P60'` hardcoded in `Client.cs`; `" P60 "` accepted | `DefaultCommande` is `private` in the library, and the wording was approved at build P-5. Padding is already deferred (build D-2). |

## 6. Self-checks

- **Lenses run (4/4, none failed):** blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor (JSON: `findings: []`, 4 `out_of_mandate`, of which 1 → P-1 and 3 → R-8, R-6, R-15).
- **Diff stats (git):** 7 files, +309 / −47
  - `src/Kape22Importer/InboxScanner.cs` 51
  - `src/P89Converter/P89FolderConverter.cs` 37
  - `tests/Kape22Importer.Tests/InboxScannerTests.cs` 62
  - `tests/P89Converter.Tests/P89FolderConverterTests.cs` 58
  - spec (new) 130, `deferred-work.md` 14, `sprint-status.yaml` 4
- **SVN r551:** `GPAO/ImportP60/Client.cs`, `GPAO/ImportP60.Tests/ClientConfigurationTests.cs` (45 diff lines). Total sent to the lenses: 655 lines.
- **Code read before rating:** `InboxScanner.cs` 150-370, 393-467, 435; `P89FolderConverter.cs` 140-275; `InboxScannerTestSupport.cs` 155-175; `P89FolderConverterTests.cs` 470-510; `Client.cs` 133-151; `Kape22Persister.cs` 59-61, 383; `deferred-work.md` (6.6 / 6.7 / 6.11 / 6.12 entries).
- **Not run:** no `dotnet test` in this review. No finding relies on a test count.
- **Defers:** not yet written to `deferred-work.md`. /commit-review does that.

## 7. Application des patches (2026-10-08, option 1 « Apply every patch »)

| ID | État | Changement |
|---|---|---|
| P-1 | appliqué | `P89FolderConverter.cs` `Accept` catch comment : « the rethrown exception, of any type since Story 6.12 ». |
| P-2 | appliqué, sans test | `P89FolderConverter.TryDelete` : `catch (Exception)`, commentaire mis à jour. Pas de point d'injection sur `File.Delete` côté P89 ; consigné dans le Spec Change Log. |
| P-3 | appliqué | `ClientConfigurationTests.cs` : `AssertThrowsNaming` renvoie l'exception ; `ReadConfig_CommandeBlank_*` vérifie `'P60'`. Rouge vu en retirant `'P60'` du message (2 échecs), puis restauré. **Commit SVN à faire par l'utilisateur.** |
| P-4 | appliqué | `InboxScanner.cs:288` : « unlike a read fault, which is handled above ». |
| P-5 | appliqué | Verification de la spec : `dotnet test ../MicroServices/GPAO/ConvertP89.Tests`. |
| P-6 | appliqué | `RecordingLogger.Entries` devient `(Exception? Exception, LogLevel Level, string Message)` (CC-4). La theory AC-FR25-8 vérifie que la Warning ne porte pas d'exception pour une cause I/O, et qu'elle porte `fault` lui-même pour une cause non-I/O. Rouge vu en inversant le ternaire (4 lignes en échec). |
| P-7 | appliqué | `InboxScanner.DeleteExport` (`internal Action<string>`, init, `File.Delete` par défaut) ; nouveau test `RunTick_ExportDeleteThrowsAfterAFailedFiling_CountsTheFichierAndLogsTheExport_AcFr25_8`. Rouge vu en remettant le filtre I/O sur `TryDelete`, puis restauré. |

F-1 n'est pas modifié : il sera écrit dans `deferred-work.md` par /commit-review.

Vérifications après application :

| Commande | Résultat |
|---|---|
| `dotnet build TextToXml.sln -warnaserror` | 0 warning, 0 error |
| Unit | 1265 verts : TextToXml 192, P89Converter 57, Kape22Importer 995, AscoLsiJournal 21 |
| Integration (`-m:1 --blame-hang-timeout 2m`) | 1429 verts, 20 skipped |
| `ImportP60.Tests` | 72 verts |
| `ConvertP89.Tests` | 68 verts |

## Clôture (2026-10-08)

- D-1 → tranché (option 2 : P-6 + P-7 testent les deux comportements), appliqué.
- P-1 → appliqué (commentaire du catch de `Accept` P89).
- P-2 → appliqué (`P89FolderConverter.TryDelete` catch toute exception ; sans test, pas de point d'injection sur `File.Delete` côté P89, consigné au Spec Change Log).
- P-3 → appliqué côté SVN (`ClientConfigurationTests` vérifie `'P60'`) ; **commit SVN à faire par l'utilisateur**.
- P-4 → appliqué (commentaire du catch `processor.Process`).
- P-5 → appliqué (Verification de la spec : `ConvertP89.Tests`).
- P-6 → appliqué (`RecordingLogger` garde l'exception ; theory AC-FR25-8 vérifie null pour I/O, `fault` sinon).
- P-7 → appliqué (seam `InboxScanner.DeleteExport` + `RunTick_ExportDeleteThrowsAfterAFailedFiling_CountsTheFichierAndLogsTheExport_AcFr25_8`).
- F-1 → tracé dans `deferred-work.md` (non planifié ; candidat à une story de durcissement des tests).
- F-2 → promu en P-7, appliqué.

Vérification finale :

- `dotnet build TextToXml.sln -warnaserror` : 0 warning, 0 erreur.
- Unit : 1265 verts (TextToXml 192, Kape22Importer 995, P89Converter 57, AscoLsiJournal 21).
- Integration (`-m:1 --blame-hang-timeout 2m`) : 1429 verts, 20 skipped (Kape22Importer 1427, AscoLsiJournal 2).

Statut : story 6-12 → done ; epic-6 → done (rétrospective en attente). Commit de clôture = celui qui ajoute cette section.
