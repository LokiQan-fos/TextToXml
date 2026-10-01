---
date: 2026-10-01
trigger: epic-6-retro-A-2-unplanned-deferred-clusters
mode: incremental
scope_classification: moderate
status: approved (2026-10-01), documentation changes applied
---

# Sprint Change Proposal — Epic 6 post-retro corrections (stories 6.6–6.8)

## 1. Issue Summary

The Epic 6 retrospective (`implementation-artifacts/epic-6-retro-2026-10-01.md`, finding F-1, action
A-2, commit `bc9cf7d`) found 19 open Epic 6 entries in `implementation-artifacts/deferred-work.md`
(lines 1332–1426), 11 of them tagged "UNPLANNED 2026-09-29 (Story 6.4 scope decision)". The retro
ruled that "UNPLANNED" is not a disposition: every entry needs a vehicle (story, epic) or an explicit
acceptance. Its Q-1 flagged cluster (a) as the most exposed: a Fichier can loop forever in
`processing/`, rewriting `Logs` (and `L_D_LOG_COMMANDE`) rows each tick.

Evidence re-checked on 2026-10-01 against the code:

- `GPAO/ImportP60/Client.cs` `ReadConfig` (MicroServices) validates `ConnectionStrings:AscoLSI`
  (present, parseable), `Import:InboxPath`, `Import:XmlExportPath`, `Import:InitiatingServer`, but not
  the length of `Import:Commande`, the range of `Import:RetentionDays`, the presence of
  `Initial Catalog`, nor any quiet-period bound.
- `src/Kape22Importer/InboxScanner.cs` has no per-Fichier attempt counter; every persistence or
  filing failure leaves the Fichier in `processing/` "for retry" (`:225-230`, `:262-270`). The P60
  scanner is rebuilt every tick (`Client.RunTickCore`), the P89 converter lives as long as its
  `Client`.
- `scripts/e2e-worker-import.ps1` ends the Launcher with `Stop-Process -Force` (`:235`), lists the
  export folder with `-ErrorAction SilentlyContinue` and asserts nothing (`:186`), and counts any
  `Logs` row with `Id > $logsIdBefore` (`:223`).
- `Commande` and `RetentionDays` exist for P60 only (P89 has `Commande = "P89"` as a constant and no
  purge).

### User decisions (2026-10-01, this workflow)

- **Vehicle:** reopen Epic 6 with a "Corrections post-rétrospective Épic 6" section (stories 6.6,
  6.7, 6.8), as done for Epic 4. No Epic 7.
- **Cluster (a), retry cap:** after N consecutive failed ticks, one `Error` log, then the Fichier is
  frozen in place (not retried) until the worker restarts; never moved to `error/`. Accepted
  consequence: an `AscoLSI` outage longer than N ticks (10 × 30 s = 5 min with the current
  configuration) needs a worker restart for the waiting Fichiers to resume.
- **Cluster (b), startup validation rules kept:** `Import:Commande` ≤ 50, `Import:RetentionDays` in
  1..3650, `Initial Catalog` required on `ConnectionStrings:AscoLSI`, `StabilityQuietPeriod` in
  0..1 h. Rule "export folder must exist" **not** kept (`:1368` accepted).
- **Cluster (c):** planned (sink flush + E2E assertions).
- **The 9 entries outside the clusters:** `:1390`, `:1394` planned in Story 6.6; `:1352` partly
  resolved by the quiet-period bound; `:1344`, `:1364`, `:1378`, `:1386`, `:1410`, `:1420` accepted.

## 2. Impact Analysis

### Checklist

| Item | Status | Note |
|---|---|---|
| 1.1 Trigger | [x] | Epic 6 retro F-1 / A-2; no failing story |
| 1.2 Problem | [x] | Tracked technical debt with no delivery vehicle |
| 1.3 Evidence | [x] | `deferred-work.md:1332-1426` + code checks above |
| 2.1 Current epic | [x] | Epic 6 is `done`, 25 ACs green; nothing to redo, only additions |
| 2.2 Epic-level change | [x] | Epic 6 reopened, 3 stories added |
| 2.3–2.4 Future epics | [N/A] | None planned |
| 2.5 Order | [N/A] | |
| 3.1 PRD | [x] | FR-25 gains `AC-FR25-8..10`; `AC-FR15-3` unchanged (the cap never quarantines) |
| 3.2 Architecture | [N/A] | No architecture document; D32 in the PRD already covers "fix both workers identically" |
| 3.3 UX | [N/A] | |
| 3.4 Other artifacts | [x] | `scripts/e2e-worker-import.ps1`; both worker JSONs + READMEs (new keys); possibly Launcher / `SharedLogger` for the flush; SVN commits on MicroServices |
| 4.1 Direct adjustment | Viable | Effort medium, risk low |
| 4.2 Rollback | Not viable | Nothing to undo |
| 4.3 MVP review | Not viable | MVP unaffected |
| 4.4 Selected | [x] | Option 1, direct adjustment |

### Epic impact

Epic 6 goes back to `in-progress` for stories 6.6 → 6.7 → 6.8; a second Epic 6 retro follows 6.8.

### Artifact conflicts

- **PRD** — three new ACs under FR-25 (§4 below). No existing AC is withdrawn or reworded.
- **epics.md** — Epic 6 sequencing line + new section with three stories.
- **deferred-work.md** — 19 disposition tags.
- **sprint-status.yaml**, **PROJECT-CLOSED.md**, **epic-6-context.md** — tracking updates.

### Technical impact

- MicroServices (SVN): `GpaoImportP60` and `GpaoConvertP89` `Client` (`ReadConfig`, tick body, attempt
  counter), JSONs, READMEs, tests; possibly the Launcher or `SharedLogger` for the flush (Ask First).
- TextToXml: `InboxScanner` / `P89FolderConverter` need to surface a share outage (6.6) and a
  per-Fichier outcome (6.7, `InboxScanner.RunTick` returns nothing today); the signal's shape is
  decided at each spec checkpoint. `scripts/e2e-worker-import.ps1` (6.8).

## 3. Recommended Approach

**Direct adjustment** (option 1): three stories in the reopened Epic 6.

- **Order:** 6.6 (config validation) first, because it removes the cheapest loop trigger
  (`Import:Commande` > 50, `:1334`); then 6.7 (retry cap) for the triggers that config cannot catch
  (deterministic journal failure, unreachable export folder); then 6.8 (test harness only).
- **Effort:** medium in total (6.6 medium, 6.7 medium, 6.8 low to medium depending on the flush
  mechanism).
- **Risk:** low. 6.7 changes the retry behaviour; the user accepted the restart-to-resume
  consequence. 6.8 may touch shared MicroServices code if a graceful Launcher stop is needed.
- **Timeline:** no external deadline; the project re-closes at Story 6.8 closure.

## 4. Detailed Change Proposals

### 4.1 PRD — FR-25 (`planning-artifacts/PRD.md`, after `AC-FR25-7`)

OLD: *(nothing)*

NEW:

```
- `AC-FR25-8` : un Fichier qui reste dans `processing/` (P60) ou ressort
  `Deferred` (P89) à `Import:MaxAttempts` / `P89:MaxAttempts` ticks consécutifs
  (défaut 10) est signalé par **un** log `Error` nommant le Fichier et la
  dernière cause, puis n'est plus retraité tant que le worker n'a pas redémarré :
  aucune nouvelle ligne `Logs` ni `L_D_LOG_COMMANDE` pour lui. Ce plafond ne
  déplace jamais un Fichier en `error/` (`AC-FR15-3` inchangé). Le compteur vit
  en mémoire du worker et repart de zéro au redémarrage ou quand le Fichier
  quitte `processing/`.
- `AC-FR25-9` : le démarrage est refusé, avec un message nommant la clé, si :
  `ConnectionStrings:AscoLSI` ne nomme pas de base (`Initial Catalog` /
  `Database` absent) — les deux workers ; `Import:StabilityQuietPeriod` /
  `P89:StabilityQuietPeriod` est négatif ou dépasse 1 h — les deux workers ;
  `Import:Commande` dépasse 50 caractères (`L_D_LOG_COMMANDE.Commande`) ou
  `Import:RetentionDays` sort de 1..3650 — `GpaoImportP60`.
- `AC-FR25-10` : un dossier de réception injoignable (P60 inbox, P89 source)
  fait échouer le tick : log `Warning` inchangé (`AC-FR15-2`), mais aucun
  heartbeat (`AC-FR25-5`) ; le test d'existence du dossier s'exécute dans la
  tâche du tick, donc dans le budget d'arrêt de 4 s (`AC-FR25-3`).
```

Rationale: `AC-FR25-8` covers `:1338`, `:1360` and the remainder of `:1334`; `AC-FR25-9` covers
`:1334`, `:1352` (config part), `:1374`, `:1382`; `AC-FR25-10` covers `:1390`, `:1394`.

### 4.2 Epics (`planning-artifacts/epics.md`)

Epic 6 header, sequencing line:

OLD:
```
**Séquencement :** 6.1 → 6.2 → 6.3 → 6.4 → 6.4-bis → 6.5 (6.4-bis ajoutée le 2026-09-29, bloquante avant 6.5).
```
NEW:
```
**Séquencement :** 6.1 → 6.2 → 6.3 → 6.4 → 6.4-bis → 6.5 (6.4-bis ajoutée le 2026-09-29, bloquante avant 6.5)
→ 6.6 → 6.7 → 6.8 (ajoutées le 2026-10-01, sprint-change-proposal-2026-10-01.md).
```

New section appended at the end of the file: "Corrections post-rétrospective Épic 6", with Story 6.6
(Validation de configuration et panne de partage des workers GPAO — `AC-FR25-9`, `AC-FR25-10`),
Story 6.7 (Plafond de réessai par Fichier — `AC-FR25-8`) and Story 6.8 (Flush du sink Logs et
assertions du harnais E2E — inline Given/When/Then ACs, no PRD AC, test harness). Full text as
approved in the workflow and applied to `epics.md`.

### 4.3 Deferred work (`implementation-artifacts/deferred-work.md`)

A tag is prepended to each entry's `source_spec`, chained with " · " before the existing tag (the
"UNPLANNED" history is kept):

| Line | Disposition |
|---|---|
| 1334 | PLANNED Story 6.6 (AC-FR25-9) · remainder Story 6.7 (AC-FR25-8) |
| 1338 | PLANNED Story 6.7 (AC-FR25-8) |
| 1344 | ACCEPTED — rule fixed by the PRD (AC-FR12-5, AC-FR22-9); open question: SAP drop mode |
| 1352 | PLANNED Story 6.6 (AC-FR25-9, quiet-period bound); share clock ahead: ACCEPTED |
| 1360 | PLANNED Story 6.7 (AC-FR25-8) |
| 1364 | ACCEPTED — folder collected by hand, never overwritten; a timestamped duplicate after a crash is harmless |
| 1368 | ACCEPTED — absolute, distinct path already validated (AC-FR26-5); "folder exists" rule not kept (user decision) |
| 1374 | PLANNED Story 6.6 (AC-FR25-9) |
| 1378 | ACCEPTED — standalone host only, never under the Launcher |
| 1382 | PLANNED Story 6.6 (AC-FR25-9; product decision: catalog required) |
| 1386 | ACCEPTED — new feature across 5 workers, out of scope |
| 1390 | PLANNED Story 6.6 (AC-FR25-10) |
| 1394 | PLANNED Story 6.6 (AC-FR25-10) |
| 1400 | PLANNED Story 6.8 |
| 1404 | PLANNED Story 6.8 |
| 1410 | ACCEPTED — script convention; the test string uses `Trusted_Connection` |
| 1414 | PLANNED Story 6.8 |
| 1420 | ACCEPTED — covered at library level (`InboxScanner.RunTick`, `P89FolderConverter.RunTick`) |
| 1424 | PLANNED Story 6.8 |

12 planned (1352 partly), 7 accepted; all 19 entries have a disposition.

### 4.4 Tracking files

- `sprint-status.yaml`: `epic-6: in-progress`; add `6-6-validation-config-panne-partage-workers-gpao`,
  `6-7-plafond-reessai-par-fichier`, `6-8-flush-sink-logs-assertions-harnais-e2e` as `backlog`;
  `epic-6-retro-item-2-correct-course-unplanned-clusters` → `done`, ref extended with this proposal.
- `PROJECT-CLOSED.md`: `status: 'reopened'`, `reopened_date: '2026-10-01'`, reason pointing here,
  `previous_reopened_date: '2026-09-28'`; new top banner "Reopened 2026-10-01 for stories 6.6–6.8 …
  Re-close at Story 6.8 closure"; Decision paragraph updated.
- `epic-6-context.md`: three stories added to § Stories; one "Post-retro (AC-FR25-8..10)" bullet in
  § Requirements & Constraints.

## 5. Implementation Handoff

**Scope: moderate** — backlog reorganization (reopened epic, three new stories), no replan.

| Role | Responsibility |
|---|---|
| PM / SM (this workflow) | Apply §4 documentation changes once approved; commit only on the user's explicit request |
| Dev (`/build-story`) | Stories 6.6 → 6.7 → 6.8 in order; each spec checkpoint decides the library signal shape (6.6, 6.7) and the flush mechanism (6.8) |
| User | SVN commits on MicroServices; Ask First decisions on `src/` and shared MicroServices code |
| SM (`/retro-epic 6`) | Second Epic 6 retro after 6.8; re-close `PROJECT-CLOSED.md` at Story 6.8 closure |

**Success criteria**

- `AC-FR25-8..10` traced by green tests in both solutions (`[Trait("AC", "FR25-…")]`).
- The E2E harness fails when the worker stops logging or exporting; Integration suite at 0 failures.
- No Epic 6 entry in `deferred-work.md` left without a PLANNED / ACCEPTED / RESOLVED tag; every
  PLANNED entry marked RESOLVED by its story.
- `PROJECT-CLOSED.md` re-closed at Story 6.8 closure.
