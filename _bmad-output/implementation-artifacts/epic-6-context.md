# Epic 6 Context: Journal P60, robustesse des workers GPAO, export XML par format

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Pay down the debt shared by the P60 and P89 pipelines and give every format its own never-purged XML export folder. P60 (`Kape22Importer`) journals through `IFichierJournal` like P89, both formats wait for a stable Fichier, `MicroService.Publisher` no longer stacks ticks, and the two GPAO workers are hardened, validated at startup, retry-capped and covered by real-instance and E2E tests (6.1–6.8, done). Replaying the six P60 Fichiers rejected by the legacy in production (versioned in `P60/error/`) led to the cold-Coulée fix (6.9) and to an explicit replace-or-refuse rule for a re-sent OF (6.10), both done. The last batch (6.11–6.12) is the gate before the first GPAO worker deployment: guard the dispatch's INSERT/DELETE order against the real production foreign keys, make any per-Fichier exception count toward the retry cap, and refuse a blank `Import:Commande`. The `TextToXml` library is not modified.

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
- Story 6.11: Ordre des FK de production gardé par les tests
- Story 6.12: Toute exception sur un Fichier compte pour le plafond ; `Import:Commande` blanc refusé

## Requirements & Constraints

**Pre-deployment gate (6.11–6.12):**
- **Production FK order guarded (AC-FR21-7, 6.11):** an Integration test adds to `AscoLSI_Test`, for its duration only, the 20 production FKs touching the dispatch (2 on `L_D_ORDRE_FABRICATION`, 11 child `OF`, 7 `CodeOperation`): `L_D_ORDRE_FABRICATION.Coulee` → `L_D_COULEE.IdCoulee`, `.ProfilProduit` → `L_P_PROFIL_PRODUIT.ID`; `OF` of the 7 `L_D_SECTIONCHARGE_*`, `L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PLANS_FOURS`, `L_D_PSO` → `L_D_ORDRE_FABRICATION.OF`; `CodeOperation` of the 7 sections → `L_P_TEXT_OPERATIONS.CodeOperation` (all `NO_ACTION`; `L_D_CONSIGNES`, `L_D_MAM_QUAL`, `L_D_PRODUITS_OUTIL`, `L_D_KAPE22` carry none). It seeds both `L_P_*` tables from production (`SELECT` only) or, without production access (CI), with the codes read on 2026-10-06, then a reference Fichier creates an OF on a new Coulée and a second one replaces it (D34, `Etat` GPAO, with existing `L_D_OF_SUIVI`/`L_D_REBUT` rows): both succeed with no FK violation. A mutation making `DeleteOf` delete `L_D_ORDRE_FABRICATION` first must fail the test. FKs are dropped at the end, even on failure. Tests: `Kape22Importer.Tests`, `[Trait("AC", "FR21-7")]`.
- **Any per-Fichier exception counts (AC-FR25-8 extended, 6.12):** an exception other than `IOException`/`UnauthorizedAccessException` raised while reading, exporting or filing (`Archive`, `Reject`) a P60 Fichier (`InboxScanner`) or converting/writing a P89 one (`P89FolderConverter.Process`) is counted for that Fichier; the tick moves on, so a healthy second Fichier is processed on the first tick. The faulty Fichier stays in `processing/` and freezes at `Import:MaxAttempts` / `P89:MaxAttempts` with exactly one `Error` log naming the Fichier and the exception. The I/O filters stay for transient recovery. Same fix in both formats (D32).
- **Blank `Import:Commande` refused (AC-FR25-9 extended, 6.12):** present but empty or whitespace refuses `GpaoImportP60` startup with a message naming the key; absent keeps the `P60` fallback. Tests in `Kape22Importer.Tests`, `P89Converter.Tests`, `GPAO/ImportP60.Tests`.
- **Accepted, not planned:** unknown `ProfilProduit`/`CodeOperation` fails `SaveChanges` as a `PersistenceError` and freezes at the cap (never observed in production; no pre-check added).

**Re-sent OF (D34; AC-FR20-6, AC-FR21-6, done in 6.10):** an existing OF is refused (`BusinessRuleViolation`, no write, message names the OF and the reason) when its `Etat` is ENC (1), EVC (2), ENFOURNE (3), LAMINAGE (5) or LAMINE (8), or it appears in `L_D_PLANS_FOURS.[OF]`, `L_D_FOURS.OFEnCours` or `L_D_PSO.[OF]`; otherwise it is replaced like the legacy `DeleteOF` (13 tables deleted children first, `L_D_OF_SUIVI` later `Rang` shifted, then inserts; `L_D_COULEE` and earlier `L_D_KAPE22` rows kept). A SQL failure leaves the previous OF intact.

**Cold Coulée (AC-FR20-5, done in 6.9):** cold when the first character of `CodeConsignePits` is `1` (TypeConsigne 12).

**Still binding from earlier stories:**
- Journal via `IFichierJournal.Record` after the business commit, for every outcome; anti-duplicate guard D22 (`NumeroFichier` + `OF`).
- Stability quiet period, per-format XML export folder (`Import:XmlExportPath`), startup config validation, unreachable inbox fails the tick, in-memory retry cap (no quarantine), flushed Logs sink in the E2E harness.
- Any new or touched config key is grepped across both repos and documented in the worker JSON files.
- Cross-cutting: strict TDD with `[Trait("AC", ...)]` tests; English comments; alphabetical ordering; glossary vocabulary; no secrets in code; revised ACs reconciled in the same change.

## Technical Decisions

- **No EF relations (AD-7, unchanged):** INSERT order relies on EF Core's command sort; `DeleteOf` explicitly deletes children before the OF. 6.11 guards both orders by test only; no production change expected, but if the test reveals a violation the fix belongs to 6.11.
- **Schema mirror stays FK-less:** `scripts/schema/01-ascolsi-tables.sql` must not carry FKs (they would block `SqlServerIntegrationFixture`'s `TRUNCATE`, which stays unchanged). Add minimal key-only mirrors of `L_P_PROFIL_PRODUIT` / `L_P_TEXT_OPERATIONS` if absent.
- **D35, existing-OF race accepted:** D34 preconditions are read without a lock before the replace transaction (legacy `AddRange2` parity). The production `L_D_PLANS_FOURS`/`L_D_PSO` FKs roll back a replace racing an enfournement; no `UPDLOCK`/`HOLDLOCK` on tables the MCC writes.
- **Single transaction:** `L_D_KAPE22` + downstream tables commit or roll back together; the D34 replace wraps `ExecuteDelete` + `SaveChanges()` in one explicit transaction; the journal is written after, outside it.
- Every failure is a distinct `ConversionError` through the existing circuit (`L_D_LOG_COMMANDE` via the journal, `MQTTnetServices.Logs`, `error/`); no new channel.
- 6.12's `ReadConfig` check is `string.IsNullOrWhiteSpace` on a non-null value. `src/` changes are Ask First; `GPAO/ImportP60` changes need an SVN commit by the user.
- Integration tests use local SQL Server `AscoLSI_Test` (reset by test runs), never Docker; `Skip` when unreachable; run with `-m:1` and `--blame-hang-timeout 2m`. Production database is `SELECT`-only. Legacy code is read-only reference.

## Cross-Story Dependencies

- 6.11 → 6.12 order (independent; 6.11 first). Both gate the first GPAO worker deployment.
- 6.11 builds on 6.10's `DeleteOf` and D34 replace path; 6.12 builds on 6.7's retry cap and 6.6's startup validation.
- 6.10 depended on 6.9 (cold-Coulée rejections for 407/408/430) and resolved 6.8's W-1 (a second E2E run now replaces the OF).
