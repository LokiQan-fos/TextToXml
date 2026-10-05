# Epic 6 Context: Journal P60, robustesse des workers GPAO, export XML par format

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Pay down the debt shared by the P60 and P89 pipelines and give every format its own never-purged XML export folder. P60 (`Kape22Importer`) journals through `IFichierJournal` like P89, both formats wait for a stable Fichier, `MicroService.Publisher` no longer stacks ticks, and the two GPAO workers are hardened, validated at startup, retry-capped and covered by real-instance and E2E tests (6.1–6.8, done). The epic was reopened on 2026-10-02 after six P60 Fichiers rejected by the legacy in production (now versioned in `P60/error/`) were replayed against the worker: the cold-Coulée check never fired on real data (fixed by 6.9, done), and re-sending an existing OF was never specified, so it failed on a raw primary-key error instead of being replaced or refused with a reason (Story 6.10, the remaining work). The `TextToXml` library is not modified.

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

**Re-sent OF (D34; AC-FR20-6, AC-FR21-6) — Story 6.10:**
- When `L_D_ORDRE_FABRICATION` already holds the Fichier's OF, the Fichier is **refused** (explicit `BusinessRuleViolation`, no write at all) if the OF's `Etat` is ENC (1), EVC (2), ENFOURNE (3), LAMINAGE (5) or LAMINE (8), or if the OF appears in `L_D_PLANS_FOURS.[OF]`, `L_D_FOURS.OFEnCours` or `L_D_PSO.[OF]`. The message names the OF and the reason (the state, or the table). Own wording, same shape as the cold-Coulée rejection — not the legacy text.
- Otherwise the OF is **replaced**: its rows in `L_D_ORDRE_FABRICATION`, the 7 `L_D_SECTIONCHARGE_*` (including `POIDSMETRIQUE` and `SVT`) and `L_D_CONSIGNES` are deleted and the new rows inserted in the same single `SaveChanges()`. `L_D_COULEE` and earlier `L_D_KAPE22` rows are kept. A SQL failure during the replace leaves the previous OF intact in all 9 tables.
- Legacy parity reference: `OrdreFabricationController.AddRange2`; `EtatOF` values from the legacy DAL.
- Reference cases: 446 (OF `2040310`, created by 443, EVC) and 447/449 (OF `2040311`, created by 444) are refused; 448 replaces OF `2040312` (created by 445, `Etat` 0 GPAO, not in the 3 tables) and is accepted. One refusal case per precondition table.
- **Generic `P60/error/` theory:** one case per Fichier in the folder. Reads the legacy processing trace from production `L_D_LOG_COMMANDE` (`SELECT` only, `ApplicationIntent=ReadOnly`, key `ConnectionStrings:AscoLSI_Production`), rebuilds in `AscoLSI_Test` the state the Fichier met (Coulée present if received before processing; existing OF and its `Etat` at that instant from the OF's log history; `L_P_CONSIGNES_*` copied), imports with the real `Kape22FichierProcessor`, and asserts the rejection cause matches the legacy reason (cold Coulée → AC-FR20-5; "0 OF sauvés" → AC-FR20-6; no furnace consignes → AC-FR20-4; ingot/product split → AC-FR20-2; hot Coulée not starting with `0` → accepted, reported as a known gap since AC-FR20-3 is withdrawn). Unknown reason or different cause fails with both in the message; no legacy trace or no production config → `Skip`. Integration category; marked for removal at the switchover (no legacy trace afterwards). State reconstruction is the delicate part, to be fixed at the spec checkpoint.
- `P60/error/` is outside the production-parity theory (which only enumerates top-level `P60/`).

**Cold Coulée (AC-FR20-5, done in 6.9):** a Coulée is cold when the first character of `CodeConsignePits` is `1` (TypeConsigne 12); 407/408/430 rejected for this cause, 412 accepted.

**Delivered earlier in the epic (constraints still binding):**
- Journal via `IFichierJournal.Record` after the business commit, for every outcome; `Kape22Importer` never references `AscoLsiJournal`. Anti-duplicate guard D22: an existing `L_D_KAPE22` row with the same `NumeroFichier` + `OF` ⇒ no re-insert, success entry written if missing, Fichier archived.
- Stability quiet period, per-format XML export folder (`Import:XmlExportPath`), startup config validation, unreachable inbox fails the tick, retry cap `Import:MaxAttempts` / `P89:MaxAttempts` (in-memory, no quarantine), flushed Logs sink in the E2E harness.
- Any new or touched config key is grepped across both repos and documented in the worker JSON files.
- Cross-cutting: strict TDD with `[Trait("AC", ...)]` tests; English comments; alphabetical ordering; glossary vocabulary; no secrets in code; revised ACs reconciled in the same change.

## Technical Decisions

- **Single transaction:** `L_D_KAPE22` + the 9 downstream tables commit or roll back together in one `SaveChanges()`. The dispatch is insert-only **except** the D34 replace path (delete + insert in that same `SaveChanges()`). The journal is written after, outside the transaction.
- **Placement of the 6.10 branch in `Kape22Persister`:** after the D22 guard and the cold-Coulée check (legacy order), before the A-5/B-5/C-4 pre-checks.
- **Read-only precondition tables:** `L_D_PLANS_FOURS`, `L_D_FOURS`, `L_D_PSO` become read-only entities with only the columns used, never written; minimal mirrors added to `scripts/schema/01-ascolsi-tables.sql`. All three OF columns are `NCHAR(12)` zero-padded — compare via `DownstreamOf.Pad`.
- Database-first entities from the real schema (`sys.columns`), no EF migration.
- Every failure is a distinct `ConversionError` routed through the existing circuit (`L_D_LOG_COMMANDE` via the journal, `MQTTnetServices.Logs`, `error/`); no new channel.
- Legacy code is read-only reference, never referenced or called.
- 6.10 touches `Kape22Persister` only (TextToXml): no change to the workers or MicroServices, so no SVN commit. It is the first code path that deletes AscoLSI rows; risk mitigated by the preconditions and the single transaction.
- Integration tests use local SQL Server `AscoLSI_Test` (reset by test runs), never Docker; `[SkippableFact]`/`Skip` when unreachable; run with `-m:1` and `--blame-hang-timeout 2m`. Production database is `SELECT`-only.

## Cross-Story Dependencies

- 6.10 depends on 6.9: its legacy-reason theory needs 407/408/430 rejected for the cold-Coulée cause.
- 6.10 resolves deferred W-1 of 6.8: a second run of the E2E script now replaces the OF instead of failing on `PK_L_D_CONSIGNES`.
- 6.10's theory mirrors the skip/production-read pattern of `Kape22ProductionDataParityTests`.
- Earlier order 6.1 → 6.5 → 6.6 → 6.7 → 6.8 is complete; the project stays open, re-closure at the user's decision.
