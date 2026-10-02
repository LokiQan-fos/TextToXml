# Epic 6 Context: Journal P60, robustesse des workers GPAO, export XML par format

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Pay down the debt shared by the P60 and P89 pipelines (ten "fix with P60 / fix once" deferred entries found by the Epic 5 retrospective) and give every format its own never-purged XML export folder. P60 (`Kape22Importer`) stops writing its own `L_D_LOG_COMMANDE` rows and goes through the `IFichierJournal` abstraction already used by P89, written after the business transaction for every outcome; both formats wait until a Fichier is stable before reading it; the shared `MicroService.Publisher` no longer stacks ticks; the two GPAO `Client` workers are hardened identically and covered by a real-instance integration test. Stories 6.1–6.8 are done; the epic was reopened after its retrospective to settle the remaining deferred worker debt (startup validation of risky configuration, an unreachable reception folder reported as a failed tick, a per-Fichier retry cap so a failing Fichier stops rewriting log rows every tick — all delivered) and to give the E2E harness a flushed `Logs` sink that actually asserts worker rows and exports (6.8, done 2026-10-02). The `TextToXml` library itself is not modified.

## Stories

- Story 6.1: Journal P60 via `IFichierJournal`
- Story 6.2: Garde de stabilité des Fichiers P60 + P89
- Story 6.3: Export XML P60 dans un dossier dédié
- Story 6.4: Robustesse commune des workers GPAO
- Story 6.4-bis: Réaligner le harnais E2E worker P60 sur la config 6.4
- Story 6.5: Test d'intégration d'un vrai `Client` GPAO
- Story 6.6: Validation de configuration et panne de partage des workers GPAO
- Story 6.7: Plafond de réessai par Fichier
- Story 6.8: Flush du sink Logs et assertions du harnais E2E
- Story 6.9: Contrôle Coulée froide sur le TypeConsigne 12
- Story 6.10: Renvoi d'un OF existant — remplacement ou refus explicite

## Requirements & Constraints

- **Journal = interface, LSI = one implementation.** A format never writes its application journal itself: it calls `IFichierJournal.Record(FichierJournalEntry)` (`Commande`, `FichierName`, `Instant` UTC, `NumeroFichier?`, `OF?`, `Reasons`; no reason = success). `AscoLsiFichierJournal` writes `L_D_LOG_COMMANDE` in its own autonomous write, outside any business transaction; `Record` throws on failure and the caller decides the Fichier's fate. No `OF` ⇒ no row.
- **P60 journal migration.** `Kape22Importer` holds no `L_D_LOG_COMMANDE` knowledge; it references `FichierJournal`, never `AscoLsiJournal`. Success = one `SaveChanges()` for business rows, then a success entry; rejection or SQL failure = no business row, one failure entry (SQL reason names table, column, cause when available). `Message` values stay byte-identical to pre-migration.
- **Anti-duplicate guard.** An existing `L_D_KAPE22` row with the same `NumeroFichier` + `OF` ⇒ no re-insert; success entry written if missing; Fichier archived; `Warning`.
- **Journal failure after commit** ⇒ Fichier stays in `processing/`, `Warning`, retried next tick where the guard completes it; never duplicate business data.
- **Stability gate** (P60 `Import:StabilityQuietPeriod`, P89 `P89:StabilityQuietPeriod`, default 10 s): a Fichier whose last write is more recent than the quiet period is left in place, no error, retried next tick; clock via `TimeProvider`.
- **XML export per format:** `<nom>_<yyyyMMddHHmmss>.xml` in a dedicated folder, never overwritten, never purged by the worker; only Fichiers that converted without error; P60 key `Import:XmlExportPath` (absolute, distinct, validated at startup); P89 already complies via `P89:XmlPath`. Existing `archive/` / `error/` XML unchanged. Export write failure ⇒ Fichier stays in `processing/`.
- **Worker robustness:** no overlapping ticks (skipped, not queued); `Client.Actions()` leaks no exception (broker connect included); no access to a disposed `Client` after the shutdown budget; `ConnectionStrings:AscoLSI` and `:MQTTnetServices` validated at startup with a message naming the key, no leaked SQL sink on refusal; no heartbeat after a failed tick; standalone `WorkerService` does not retry configuration errors and disposes the `Client`.
- **Startup validation (both workers, identical fix):** refuse with a message naming the key when `ConnectionStrings:AscoLSI` names no database (`Initial Catalog` / `Database`), or the stability quiet period is negative or above 1 h; P60 only: `Import:Commande` longer than 50 chars or `Import:RetentionDays` outside 1..3650. "Export folder must exist" is deliberately not a rule.
- **Unreachable reception folder** (P60 inbox, P89 source) fails the tick with no heartbeat; log levels unchanged (`Warning` for a listing fault, `Error` for a missing folder); the existence check runs inside the tick task, hence within the 4 s shutdown budget. Signal shape (Story 6.6): `InboxScanner.RunTick` returns `false` when a reception folder could not be listed.
- **Retry cap:** a Fichier left in `processing/` (P60) or returned `Deferred` (P89) for `Import:MaxAttempts` / `P89:MaxAttempts` consecutive ticks (default 10, < 1 refused at startup) gets one `Error` log naming it and the last cause, then is no longer processed until the worker restarts (no new `Logs` / `L_D_LOG_COMMANDE` rows). Never moved to `error/` (no quarantine). Counter is in memory in each worker's `Client` (the P60 scanner is rebuilt every tick), reset on restart or when the Fichier leaves `processing/`. Accepted consequence: an `AscoLSI` outage longer than N ticks needs a worker restart.
- **E2E harness:** the worker's batched Logs sink is flushed (clean stop or explicit flush) before the Launcher stops; the harness requires at least one `GpaoImportP60` `Logs` row after the reference row and exactly one exported XML per seeded Fichier; the real-`Client` integration test writes no `Logs` row outside `MQTTnetServices_Test`; `Category=Integration` ends with 0 failures.
- **Cold Coulée (AC-FR20-5, Story 6.9):** a Coulée is cold when the first character of `CodeConsignePits` is `1` (TypeConsigne 12, as the legacy reads it), not when the whole 12-character code equals `1`.
- **Re-sent OF (D34, AC-FR20-6, AC-FR21-6, Story 6.10):** an OF already in `L_D_ORDRE_FABRICATION` is refused (explicit `BusinessRuleViolation`, no write) when its `Etat` is ENC 1, EVC 2, ENFOURNE 3, LAMINAGE 5 or LAMINE 8, or when it appears in `L_D_PLANS_FOURS.[OF]`, `L_D_FOURS.OFEnCours` or `L_D_PSO.[OF]` (read-only, `NCHAR(12)` zero-padded); otherwise its rows in `L_D_ORDRE_FABRICATION`, the 7 `L_D_SECTIONCHARGE_*` and `L_D_CONSIGNES` are deleted and re-inserted in the same `SaveChanges()`; `L_D_COULEE` and earlier `L_D_KAPE22` rows are kept.
- **`P60/error/`:** versioned folder of P60 Fichiers rejected in production; a generic theory reads each one's legacy reason from production (`SELECT` only), rebuilds the state it met and checks the worker rejects it for the same cause; retired at the switchover to the new worker.
- Any new or touched config key is grepped across both repos (`scripts/`, `tests/`, MicroServices) and documented in the worker JSON files (no worker README exists).
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
- Post-retro order 6.6 → 6.7 → 6.8, after 6.5. 6.7's `MaxAttempts` key joins the startup validation added by 6.6 and needs `InboxScanner.RunTick` to expose a per-Fichier outcome (both delivered). 6.8 builds on the 6.4-bis harness and the 6.5 `GpaoClientIntegrationTests`; a flush mechanism touching the Launcher or `SharedLogger` (MicroServices / PortalSharedLibrary) requires asking first.
- Post-recette order 6.9 → 6.10: 6.10's legacy-reason theory needs 6.9's cold-Coulée check to reject 407/408/430 for the right cause.
- Integration runs use `--blame-hang-timeout 2m` (killed runs orphan bcp / Launcher processes).
- Builds on Epic 5's `FichierJournal` / `AscoLsiJournal` projects (Story 5.0) and on the P89 worker composition (Story 5.2).
