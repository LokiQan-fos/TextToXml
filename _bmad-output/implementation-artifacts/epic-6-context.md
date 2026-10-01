# Epic 6 Context: Journal P60, robustesse des workers GPAO, export XML par format

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Pay down the debt shared by the P60 and P89 pipelines (ten "fix with P60 / fix once" deferred entries found by the Epic 5 retrospective) and give every format its own never-purged XML export folder. P60 (`Kape22Importer`) stops writing its own `L_D_LOG_COMMANDE` rows and goes through the `IFichierJournal` abstraction already used by P89, written after the business transaction for every outcome; both formats wait until a Fichier is stable before reading it; the shared `MicroService.Publisher` no longer stacks ticks; the two GPAO `Client` workers are hardened identically and covered by a real-instance integration test. The `TextToXml` library itself is not modified.

## Stories

- Story 6.1: Journal P60 via `IFichierJournal`
- Story 6.2: Garde de stabilité des Fichiers P60 + P89
- Story 6.3: Export XML P60 dans un dossier dédié
- Story 6.4: Robustesse commune des workers GPAO
- Story 6.4-bis: Réaligner le harnais E2E worker P60 sur la config 6.4
- Story 6.5: Test d'intégration d'un vrai `Client` GPAO

## Requirements & Constraints

- **Journal = interface, LSI = one implementation.** A format never writes its application journal itself: it calls `IFichierJournal.Record(FichierJournalEntry)` (`Commande`, `FichierName`, `Instant` UTC, `NumeroFichier?`, `OF?`, `Reasons`; no reason = success). `AscoLsiFichierJournal` writes `L_D_LOG_COMMANDE` in its own autonomous write, outside any business transaction; `Record` throws on failure and the caller decides the Fichier's fate. No `OF` ⇒ no row.
- **P60 journal migration.** `Kape22Importer` must contain no `L_D_LOG_COMMANDE` entity, column lengths, Paris-time conversion or row-message rules; it references `FichierJournal`, never `AscoLsiJournal`. Success = one `SaveChanges()` covering `L_D_KAPE22` + downstream tables without any log row, then a success entry. Business rejection or SQL failure = no business row, one failure entry; for SQL failures the reason names table, column and cause (truncation, type, constraint) when SQL Server provides them. Resulting `Message` values on the reference P60 Fichiers must be byte-identical to pre-migration.
- **Anti-duplicate guard moves.** Before inserting, check for an existing `L_D_KAPE22` row with the same `NumeroFichier` + `OF` (previously: an "— OK" log row). Found ⇒ no re-insert; write the success journal entry if missing; archive the Fichier; log `Warning`. Risk to confirm at spec checkpoint: legacy-importer rows also match this guard (read-only `SELECT` on production allowed).
- **Journal failure after commit** ⇒ Fichier stays in `processing/`, `Warning`, retried next tick where the guard completes it; never duplicate business data.
- **Stability gate** (P60 `Import:StabilityQuietPeriod`, P89 `P89:StabilityQuietPeriod`, default 10 s): a Fichier whose last write is more recent than the quiet period is left in place, no error, retried next tick; clock via `TimeProvider`.
- **XML export per format:** `<nom>_<yyyyMMddHHmmss>.xml` in a dedicated folder, never overwritten, never purged by the worker; only Fichiers that converted without error; P60 key `Import:XmlExportPath` (absolute, distinct, validated at startup); P89 already complies via `P89:XmlPath`. Existing `archive/` / `error/` XML unchanged. Export write failure ⇒ Fichier stays in `processing/`.
- **Worker robustness:** no overlapping ticks (skipped, not queued); `Client.Actions()` leaks no exception (broker connect included); no access to a disposed `Client` after the shutdown budget; `ConnectionStrings:AscoLSI` and `:MQTTnetServices` validated at startup with a message naming the key, no leaked SQL sink on refusal; no heartbeat after a failed tick; standalone `WorkerService` does not retry configuration errors and disposes the `Client`.
- Cross-cutting: strict TDD with AC-named tests carrying `[Trait("AC", ...)]`; English comments; alphabetical ordering; glossary vocabulary; no secrets in code. Revised closed ACs (`AC-FR11-3`, `AC-FR11-6`, `AC-FR14-7`, `AC-FR12-5`) must be reconciled in the same change as the behavior.

## Technical Decisions

- AD-1: one `SaveChanges()` covers `L_D_KAPE22` + the 9 downstream tables; the journal is written after it, outside the transaction.
- AD-4: every failure cause goes through the existing circuit (`L_D_LOG_COMMANDE` via the journal + `MQTTnetServices.Logs` + `error/`), no new channel.
- AD-5: database-first entities, no migrations; the guard uses the existing non-unique `NumeroFichier` / `OF` columns of `L_D_KAPE22`, no schema change.
- Integration tests run against a local SQL Server (`AscoLSI_Test`), never Docker/Testcontainers; `[SkippableFact]` when unreachable; projects sharing the database run with `-m:1`.
- MicroServices repo (`GpaoImportP60`, `GpaoConvertP89`, `MicroService.Publisher`) is under SVN and committed by the user; the GPAO workers compose `AscoLsiFichierJournal` and inject it into the format library.
- Production database is read-only (`SELECT` only).

## Cross-Story Dependencies

- Strict order 6.1 → 6.2 → 6.3 → 6.4 → 6.4-bis → 6.5; 6.4-bis (test-harness only, no production code) realigns `scripts/e2e-worker-import.ps1` with the `MQTTnetServices` key removed by 6.4 and restores a green `Category=Integration` suite before 6.5. 6.1 rewires `GpaoImportP60`'s journal and changes the D22 guard that the later hardening and export stories rely on; 6.3's "ignored by the D22 guard" export case assumes 6.1's guard; 6.5 tests the behavior produced by 6.1, 6.3 and 6.4.
- Builds on Epic 5's `FichierJournal` / `AscoLsiJournal` projects (Story 5.0) and on the P89 worker composition (Story 5.2).
