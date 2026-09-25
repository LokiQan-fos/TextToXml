---
date: 2026-09-25
trigger: epic-4-retro-2026-09-25-open-items-d1-d4
mode: incremental
scope_classification: minor
status: approved (2026-09-25), documentation changes applied
---

# Sprint Change Proposal — Epic 4 retro #4 follow-up (AC-FR20-3 withdrawn, Story 4.14)

## 1. Issue Summary

The fourth Epic 4 retrospective (`epic-4-retro-2026-09-25.md`, commit `bb48717`) closed with
**accepted-with-open-items** and four open action items, all traced to two `fix(...)` commits made
during a manual test session outside the story/review loop (`1ab5ea7`, `547bf2a`, 2026-09-22/23):

- **F-1 / D-1 — contract drift.** `547bf2a` removed the AC-FR20-3 control (hot Coulee must start with
  `'0'`) after the process owner confirmed that hot/cold and Coulee origin (leading digit: `'0'` local
  steelworks, any other = external supplier) are independent dimensions; the control had rejected a
  real, legitimate P60 Fichier (external Coulee, hot enfournement). The code carries a removal note
  (`Kape22ImportBundleMapper.cs:108-113`), but `PRD.md`, `epics.md` and `epic-4-context.md` still
  declare the rejection.
- **F-2 / D-2 — missing parity lock.** `1ab5ea7` added `DownstreamColumnPrecisions` (23 hand-transcribed
  `(precision, scale)` entries) with no Unit-tier parity test against
  `scripts/schema/01-ascolsi-tables.sql`, unlike its two siblings. Correct today (checked by hand in
  the retro), unprotected tomorrow.
- **F-3 / D-3 — stale closure document.** `PROJECT-CLOSED.md` says epic-4 is `in-progress` and stops at
  Story 4.11; `sprint-status.yaml` says `epic-4: done` since 4.13.
- **F-4 / D-4 — process gap.** Behavior-changing fixes landed without review; that is how F-1 and F-2
  went unseen.

Evidence: retro findings F-1..F-4; build clean, 1023 Unit / 1092 Integration green at the retro.
No defect in the delivered behavior.

## 2. Impact Analysis

### Checklist

| Item | Status | Note |
|---|---|---|
| 1.1 Trigger | [x] | No story; manual-session commits `1ab5ea7`, `547bf2a` |
| 1.2 Problem | [x] | Business-rule change (process-owner decision) not reconciled into the contract + two follow-on debts |
| 1.3 Evidence | [x] | Retro F-1..F-4, code removal note, green test tiers |
| 2.1 Current epic | [x] | Epic 4 stays delivered; FR-20 contract text rewritten |
| 2.2 Epic changes | [!] | Story 4.5 / 4.11 AC text amended; new Story 4.14 (D-2) |
| 2.3–2.5 Future epics | [N/A] | No Epic 5 planned |
| 3.1 PRD | [!] | FR-20 description + AC-FR20-3 marked withdrawn |
| 3.2 Architecture | [N/A] | `ARCHITECTURE-SPINE.md` has no hot-Coulee rule; `.memlog.md` is a historical log, left as is |
| 3.3 UX | [N/A] | No UI |
| 3.4 Other artifacts | [!] | `epic-4-context.md:51`, `PROJECT-CLOSED.md`, process rule (project memory). Done story specs (4.5, 4.11) and past retros are historical records, left as is |
| 4.1 Direct adjustment | Viable | Low effort, low risk |
| 4.2 Rollback | Not viable | The removal is a legitimate business decision |
| 4.3 MVP review | N/A | MVP unaffected (a control is withdrawn, none added) |

### Technical Impact

- No production code change. Story 4.14 adds one Unit test class and extends the test-side
  `SqlColumn` record (`tests/Kape22Importer.Tests/SqlTableSchema.cs`) with `(precision, scale)`.
- `epic-4` goes `done → in-progress` until Story 4.14 is done.

## 3. Recommended Approach

**Direct adjustment.** Reconcile the contract now (D-1), route the one code change through a story and
its review (D-2 → Story 4.14, in keeping with D-4 itself), fix the closure banner now and re-close at
Story 4.14's closure (D-3), and record the process rule where agent sessions read it (D-4).

Q-1 of the retro (withdrawn vs deleted): **withdrawn** — the ID stays, struck through, with its reason,
because code and tests cite it (`Kape22ImportBundleMapper.cs:108`, `RejectionAtomicityIntegrationTests.cs:161`).

Effort: low (documentation + one Unit test). Risk: low. Timeline: one short story.

## 4. Detailed Change Proposals

### 4.1 PRD.md — FR-20 (P-1, approved)

Description, OLD:
```
sont vérifiées explicitement : existence de la Coulée froide, format de la
Coulée chaude, cohérence de la répartition des lingots entre fours vs le
nombre de demi-produits de l'Ordre de Fabrication.
```
NEW:
```
sont vérifiées explicitement : existence de la Coulée froide, cohérence de
la répartition des lingots entre fours vs le nombre de demi-produits de
l'Ordre de Fabrication.
```

AC-FR20-3, OLD:
```
- `AC-FR20-3` : Coulée chaude mal formée (ne commence pas par `'0'`) → rejet,
  cause explicite.
```
NEW:
```
- ~~`AC-FR20-3` : Coulée chaude mal formée (ne commence pas par `'0'`) → rejet,
  cause explicite.~~ **Retiré le 2026-09-22** (`547bf2a`, décision du
  responsable process) : chaud/froid et origine de la Coulée (premier chiffre :
  `'0'` aciérie locale, autre = fournisseur externe) sont des dimensions
  indépendantes ; le contrôle rejetait un Fichier P60 réel et légitime.
  Aucun contrôle de format sur la Coulée — sprint-change-proposal-2026-09-25.md.
```

### 4.2 epics.md — AC-FR20-3 passages (P-2, approved)

- l. 62 (inventory): `format coulée chaude` → `AC-FR20-3 retiré 2026-09-22`.
- Story 4.5 statement (l. 1863): `format de la coulée chaude` → `présence de la consigne d'enfournement`.
- Story 4.5 AC (l. 1884-1888): Given/When/Then struck through, followed by
  "**AC-FR20-3 retiré le 2026-09-22** (`547bf2a`, décision du responsable process) : chaud/froid et
  origine de la Coulée sont indépendants — une coulée chaude dont le numéro ne commence pas par `'0'`
  (Coulée externe) est acceptée."
- Story 4.5 tests (l. 1897-1899): `AC-FR20-1` … `AC-FR20-4` → `AC-FR20-1`, `AC-FR20-2`, `AC-FR20-4`
  (AC-FR20-3 retiré); drop "coulée chaude mal formée" from the faulty variants.
- Story 4.11 C-5 (l. 2420): `AC-FR20-3 (hot-Coulee malformée)` struck through, "(retiré 2026-09-22 avec
  la règle, `547bf2a`)".
- l. 311 (coverage table, `AC-FR20-1 … AC-FR20-5`) unchanged.

### 4.3 epic-4-context.md:50-52 (P-3, approved)

OLD:
```
- Blocking business checks run before persistence: the cold Coulee must exist,
  the hot Coulee format must be valid, and the ingot/furnace distribution must be
  consistent.
```
NEW:
```
- Blocking business checks run before persistence: the cold Coulee must exist,
  the ingot/furnace distribution must be consistent, and the enfournement
  instruction (Pits) must be present. There is no Coulee number format check:
  AC-FR20-3 was withdrawn on 2026-09-22 (`547bf2a`, process-owner decision)
  because hot/cold and Coulee origin are independent.
```

### 4.4 epics.md — new Story 4.14, after 4.13 (P-4 + P-5b, approved)

```
### Story 4.14 : Verrou de parité schéma pour `DownstreamColumnPrecisions`

As a mainteneur de `Kape22Importer`,
I want que `DownstreamColumnPrecisions` soit verrouillée contre
`scripts/schema/01-ascolsi-tables.sql` par un test Unit, comme ses deux voisines,
So that une erreur de transcription (precision, scale) échoue au tier Unit au
lieu de réapparaître seulement au tier Integration de parité production
(défaut d'arrondi decimal(18,2) de `1ab5ea7`).

**Origine :** rétro Épic 4 du 2026-09-25, F-2 / D-2 ;
sprint-change-proposal-2026-09-25.md.

**Acceptance Criteria:**

**AC-1 (parité precision/scale) :**
Given `DownstreamColumnPrecisions.Precisions` et les tables aval du schéma
  (`L_D_ORDRE_FABRICATION`, `L_D_COULEE`, les 7 `L_D_SECTIONCHARGE_*`)
When le test lit toutes les colonnes `DECIMAL(p,s)` de ces tables
Then chaque colonne a une entrée de même (precision, scale), et chaque entrée
  a au moins une colonne correspondante (égalité d'ensemble, dans les deux sens)
And un nom de colonne répété entre deux tables avec des (p,s) différents fait
  échouer le test avec un message nommant les deux tables

**AC-2 (cohérence avec les magnitudes) :**
Given `DownstreamColumnMagnitudes.MaxAbsoluteValues`
When le test la compare à `DownstreamColumnPrecisions`
Then chaque entrée vaut 10^(p−s) de sa (p,s) dans `DownstreamColumnPrecisions`

**Notes dev :** `SqlColumn` (`tests/Kape22Importer.Tests/SqlTableSchema.cs`)
n'expose aujourd'hui que `DecimalMagnitude` ; l'étendre avec (precision, scale)
sans dupliquer le parsing. Test Unit uniquement, aucun changement de code de
production attendu ; si le test révèle un écart, corriger la map. Aligner au
passage le commentaire de `DownstreamColumnPrecisions.cs` (« four Tolerance* »
contre « six » côté magnitudes) sur ce que le schéma montre.

**Tests xUnit (TDD, CC-1) :** `DownstreamColumnPrecisionsParityTests`
(AC-1, AC-2), `Category=Unit`, `[Trait("AC", "4.14-…")]`.

**Tâche de clôture (D-3) :** à la clôture de la story, re-fermer
`PROJECT-CLOSED.md` : `status: closed`, `last_commit`, ligne Épic 4 (4.4-bis,
4.12, 4.13, 4.14 ; quatre passes de rétro), §5 avec les comptes de tests
courants, §3 avec la réconciliation D-1..D-4.

**Critères transverses :** CC-1, CC-2, CC-4.

Owner : Dev.
```

### 4.5 PROJECT-CLOSED.md — banner (P-5a, approved)

Frontmatter `reopened_reason` → `'test/correction session 2 — Stories 4.4-bis, 4.12, 4.13; then Story
4.14 (correct-course 2026-09-25)'`. Banner replaced with:
```
> **Reopened 2026-09-24 (test/correction session 2).** Delivered since the 2026-09-22 closure: Story
> 4.4-bis (L_D_CONSIGNES sub-fields), 4.12 (LibelleConsigne), 4.13 (ConsigneGPAO=0 working copy), two
> fix commits from a manual session (`1ab5ea7` EF decimal precision, `547bf2a` AC-FR20-3 withdrawn,
> downstream OF padding, blank-user fallback), and a fourth retro pass (`epic-4-retro-2026-09-25.md`).
> The 2026-09-25 correct-course adds Story 4.14; epic-4 is `in-progress` until it is done.
> Sections 1–6 below still describe the 2026-09-22 closure (commit `8f6e79e`).
```
Full re-close is Story 4.14's closing task (4.4).

### 4.6 Process rule — project memory `manual-session-fix-review` (P-6, approved)

A `feedback` memory (English) next to `commit-authorization`: a behavior-changing `fix(...)` from a
manual session goes through `/run-review` (or a story) before commit, and a removed/changed declared AC
is reconciled in PRD/epics/epic-context in the same commit. Indexed in `MEMORY.md`. Known ceiling: it
binds agent sessions only.

### 4.7 sprint-status.yaml

- `epic-4: done` → `in-progress`; add `4-14-verrou-parite-downstream-column-precisions: backlog`.
- `epic-4-retro4-item-d1-…`: `done` (ref: this proposal §4.1–4.3).
- `epic-4-retro4-item-d2-…`: stays `open`, ref → Story 4.14.
- `epic-4-retro4-item-d3-…`: stays `open`, ref → banner fixed (§4.5), re-close is Story 4.14's closing task.
- `epic-4-retro4-item-d4-…`: `done` (ref: memory `manual-session-fix-review`).

## 5. Implementation Handoff

**Scope: Minor.**

- **Developer (this session):** apply §4.1–4.3, 4.5–4.7 (documentation only) once the proposal is approved.
- **Developer (next):** Story 4.14 via `/build-story`, then `/run-review`, then `/commit-review`
  (which runs the D-3 re-close).
- **User:** authorizes each commit (no commit without explicit request).

**Success criteria:** `grep AC-FR20-3` in PRD/epics/epic-4-context shows only withdrawn mentions;
Story 4.14 done with its parity test green; `PROJECT-CLOSED.md` re-closed and consistent with
`sprint-status.yaml`; all four `epic-4-retro4-item-d*` action items `done`.
