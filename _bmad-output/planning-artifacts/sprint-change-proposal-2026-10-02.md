---
date: 2026-10-02
trigger: legacy-rejected-p60-replay
mode: batch
scope_classification: moderate
status: approved (2026-10-02), documentation changes applied
---

# Sprint Change Proposal — P60 Fichiers the legacy rejected (stories 6.9–6.10)

## 1. Issue Summary

Six P60 Fichiers were rejected by the legacy application in production between 2026-09-28 and
2026-10-01: `P60_847_682_407`, `408`, `430`, `446`, `447`, `449` (now in `P60/error/`). They were
replayed on 2026-10-02 through the real `Kape22FichierProcessor` against `AscoLSI_Test`, seeded with
the production state each one met (coulées, existing OF, `L_P_CONSIGNES_*`; production read-only).
The legacy rejection reasons come from production `L_D_LOG_COMMANDE` (`Commande = 'KAP22'`).

| Fichier | Legacy outcome and reason | Worker today |
|---|---|---|
| 407 | Rejected: cold Coulée `063241` does not exist (received 2026-09-29 08:02) | **Accepted** |
| 408 | Rejected: same reason | Rejected, wrong cause (`PK_L_D_CONSIGNES`, because 407 was accepted) |
| 430 | Rejected: cold Coulée `063196` does not exist (received 2026-09-30 16:48) | Rejected, wrong cause (`PK_L_D_CONSIGNES`) |
| 446, 447, 449 | Rejected: OF `2040310` / `2040311` already exist in state EVC (2), "0 OF sauvés" | Rejected, raw SQL `PK_L_D_CONSIGNES`, state never named |
| 448 (control) | **Accepted**: OF `2040312` existed in state GPAO, replaced | **Rejected** (`PK_L_D_CONSIGNES`) |
| 412 (control) | Accepted: Coulée `063241` on file | Accepted |

Two root causes:

1. **Defect — the cold-Coulée check never fires on a real Fichier.**
   `src/Kape22Importer/Persistence/Kape22Persister.cs:97` compares the whole 12-character
   `CodeConsignePits` (e.g. `"1 207 00 000"`) with `"1"`. The legacy
   (`InterfaceManager.ImportGPAO`, `GetConsignes("ConsignesEnfournementPits", 12)`) reads
   TypeConsigne 12, which is `CodeConsignePits.Substring(0, 1)` — the same slice
   `ConsignesMapper.cs:73` already decodes. Existing tests seed a synthetic `CodeConsignePits = "1"`,
   so AC-FR20-5 was green while dead on real data. A local one-line fix (first character) was
   validated on 2026-10-02 then reverted: 407, 408 and 430 are rejected with
   "OF '…' : la coulée '…' est introuvable dans L_D_COULEE.", 412 stays accepted.

2. **Gap — re-sending an existing OF was never specified.** D7 says a re-deposited Fichier is
   re-imported, but every downstream table is keyed on the OF, so a second Fichier for the same OF
   fails on a primary key. The legacy (`OrdreFabricationController.AddRange2`) replaces the OF unless
   its `Etat` is ENC (1), EVC (2), ENFOURNE (3), LAMINAGE (5) or LAMINE (8), in which case it refuses
   it. `EtatOF` values from `Lsi.Net/DALLevel3/EtatOF.cs`.

### User decisions (2026-10-02, this workflow)

- **Replace** an existing OF whose state allows it, as the legacy does. Scope = the user's own
  manual deletion script: `L_D_SECTIONCHARGE_LINGOT`, `_CHUTAGE`, `_DECOUPE`, `_REFROIDISSOIRS`,
  `_PITS`, `L_D_CONSIGNES`, `L_D_ORDRE_FABRICATION`, plus `L_D_SECTIONCHARGE_POIDSMETRIQUE` and
  `L_D_SECTIONCHARGE_SVT` (not in the script, but the worker writes them, so a replace that skipped
  them would fail on their primary key — confirmed by the user). `L_D_COULEE` and earlier
  `L_D_KAPE22` rows are kept.
- **Additional refusal preconditions** (user): the OF must not be in `L_D_PLANS_FOURS.[OF]`, in
  `L_D_FOURS.OFEnCours`, nor positioned on an ingot (`L_D_PSO.[OF]`). All three are `NCHAR(12)`,
  zero-padded (checked on production 2026-10-02; `L_D_PSO` holds 24 rows for `2040310`/`2040311`
  today).
- **Refusal message:** explicit business rejection (`BusinessRuleViolation`, no write), same shape
  as AC-FR20-5 — not the legacy wording.
- **Vehicle:** reopen Epic 6, new section with stories 6.9 (cold Coulée) then 6.10 (OF re-send).
- **`P60/error/`:** permanent versioned folder for P60 Fichiers rejected in production (§3).
- **Automatic coverage of `P60/error/` (user):** a generic test reads, read-only from production,
  the legacy rejection reason of every Fichier in the folder and checks the worker rejects it for
  the same cause. Accepted limit: it only makes sense while the legacy runs; after the switchover to
  the new worker no legacy trace exists and the test is retired (Story 6.10).

## 2. Impact Analysis

### Checklist

| Item | Status | Note |
|---|---|---|
| 1.1 Trigger | [x] | Replay of 6 legacy-rejected production Fichiers, 2026-10-02 |
| 1.2 Problem | [x] | One defect (AC-FR20-5 dead on real data), one unspecified behaviour (OF re-send) |
| 1.3 Evidence | [x] | Replay output, production `L_D_LOG_COMMANDE` rows 11856855, 11856950, 11878657, 11885316, 11885320, 11885330 |
| 2.1 Current epic | [x] | Epic 6 `done`; additions only |
| 2.2 Epic-level change | [x] | Epic 6 reopened, 2 stories added |
| 2.3–2.4 Future epics | [N/A] | None planned |
| 2.5 Order | [x] | 6.9 before 6.10 (6.10's tests need a correct cold-Coulée check ahead of it) |
| 3.1 PRD | [x] | AC-FR20-5 made precise; new AC-FR20-6, AC-FR21-6; D7 note; new decision D34 |
| 3.2 Architecture | [!] | `architecture-kape22-dispatch-2026-09-14` assumes insert-only dispatch; one paragraph added for the replace path |
| 3.3 UX | [N/A] | |
| 3.4 Other artifacts | [x] | Test schema gains 3 read-only mirror tables; `deferred-work.md` W-1 (6.8) resolved by 6.10 |
| 4.1 Direct adjustment | Viable | Effort: 6.9 low, 6.10 medium; risk medium (6.10 deletes rows) |
| 4.2 Rollback | Not viable | Nothing to undo |
| 4.3 MVP review | Not viable | MVP unaffected |
| 4.4 Selected | [x] | Option 1, direct adjustment |

### Epic impact

Epic 6 goes back to `in-progress` for 6.9 → 6.10. No retrospective is required by this proposal; the
user decides when to re-close the project.

### Technical impact

- `Kape22Persister` only (TextToXml). No change to `TextToXml`, `InboxScanner`, the workers or
  MicroServices, so no SVN commit.
- 6.10 reads three tables the worker never touched (`L_D_PLANS_FOURS`, `L_D_FOURS`, `L_D_PSO`):
  read-only entities with only the columns used, mirrored in `scripts/schema/01-ascolsi-tables.sql`
  (minimal columns) for the integration fixture.
- 6.10 deletes rows in AscoLSI. The delete and the re-insert happen in the single `SaveChanges()` of
  AC-FR21-1, so a SQL failure leaves the previous OF intact (AC-FR21-2).

## 3. Recommended Approach

**Direct adjustment**, two stories in the reopened Epic 6.

- **6.9 first:** a one-line defect fix plus real-Fichier regression tests. It also makes 408/430
  fail for the right reason, which 6.10's tests rely on.
- **6.10 second:** existing-OF branch in `Kape22Persister`, after the D22 guard and the cold-Coulée
  check (legacy order: the Coulée check runs before `AddRange2`).
- **Effort:** 6.9 low, 6.10 medium. **Risk:** 6.10 is the first code path that deletes AscoLSI rows;
  mitigated by the state + 3-table preconditions and the single transaction.
- **`P60/error/` (user, 2026-10-02):** a permanent, versioned folder where the user drops P60
  Fichiers that production rejected. It is committed with the six current Fichiers. The parity
  theory (`Kape22ProductionDataParityTests`) only enumerates top-level `P60/`, so these Fichiers
  never enter it. Every Fichier in it is covered automatically by the generic legacy-reason test
  of Story 6.10 (one theory case per Fichier).
- **Acceptance fixtures:** the six Fichiers in `P60/error/`, plus 412 and 448 as controls, with the
  production state seeded in the test (no production dependency in the suite). The throwaway
  `tests/Kape22Importer.Tests/ZzLegacyRejectionReplayTests.cs` is deleted, not committed.

## 4. Detailed Change Proposals

### 4.1 PRD — §0bis decisions (`planning-artifacts/PRD.md`)

**D7** — append:

OLD:
```
| D7 | **Pas de déduplication** de fichiers. Un fichier redéposé est **réimporté** (nouvelle ligne, `Id` identity). La sûreté de reprise vient du dossier **`processing/`** (FR‑12), pas d'une clé. | utilisateur |
```

NEW:
```
| D7 | **Pas de déduplication** de fichiers. Un fichier redéposé est **réimporté** (nouvelle ligne, `Id` identity). La sûreté de reprise vient du dossier **`processing/`** (FR‑12), pas d'une clé. Un Fichier qui porte un OF déjà présent en base **remplace** cet OF ou est refusé selon D34 (2026-10-02). | utilisateur |
```

**D34** — new row after D33:

```
| D34 | **Renvoi d'un OF existant.** Si `L_D_ORDRE_FABRICATION` contient déjà l'OF : refus métier explicite, aucune écriture, si son `Etat` vaut ENC (1), EVC (2), ENFOURNE (3), LAMINAGE (5) ou LAMINE (8), ou si l'OF figure dans `L_D_PLANS_FOURS.[OF]`, `L_D_FOURS.OFEnCours` ou `L_D_PSO.[OF]`. Sinon l'OF est **remplacé** : ses lignes `L_D_ORDRE_FABRICATION`, `L_D_SECTIONCHARGE_*` (7 tables) et `L_D_CONSIGNES` sont supprimées puis réinsérées dans le même `SaveChanges()` (AC-FR21-1). `L_D_COULEE` et les lignes `L_D_KAPE22` antérieures sont conservées. Parité legacy `OrdreFabricationController.AddRange2`. | utilisateur (2026-10-02) |
```

### 4.2 PRD — FR-20 (after `AC-FR20-5`)

OLD:
```
- `AC-FR20-5` : Coulée froide introuvable en base → rejet, cause explicite,
  aucune écriture.
```

NEW:
```
- `AC-FR20-5` : Coulée froide introuvable en base → rejet, cause explicite,
  aucune écriture. *(Précisé 2026-10-02, Story 6.9 :)* une Coulée est froide
  quand le **premier caractère** de `CodeConsignePits` vaut `1` (TypeConsigne 12
  de la Section de charge Pits, comme le legacy `GetConsignes("ConsignesEnfournementPits", 12)`) ;
  vérifié sur les Fichiers réels `P60_847_682_407`, `408` et `430`.
- `AC-FR20-6` : OF déjà présent et non remplaçable (D34 : `Etat` ENC/EVC/
  ENFOURNE/LAMINAGE/LAMINE, ou OF présent dans `L_D_PLANS_FOURS`, `L_D_FOURS`
  `OFEnCours` ou `L_D_PSO`) → rejet, cause explicite nommant l'OF et la raison
  (état ou table), aucune écriture. Vérifié sur `P60_847_682_446`, `447`, `449`.
```

### 4.3 PRD — FR-21 (after `AC-FR21-5`)

```
- `AC-FR21-6` : OF déjà présent et remplaçable (D34) → les lignes existantes de
  l'OF dans `L_D_ORDRE_FABRICATION`, les 7 `L_D_SECTIONCHARGE_*` et
  `L_D_CONSIGNES` sont supprimées et les nouvelles insérées dans le même
  `SaveChanges()` ; `L_D_COULEE` et les `L_D_KAPE22` antérieures sont intactes ;
  un échec SQL laisse l'ancien OF inchangé. Vérifié sur `P60_847_682_448`
  (après `445`).
```

### 4.4 epics.md

**Coverage table** (lines 313–314):

OLD:
```
| FR-20 | Épic 4 — 4.5 (contrôles purs), 4.6 (`AC-FR20-5`, existence coulée) | AC-FR20-1 … AC-FR20-5 |
| FR-21 | Épic 4 — 4.6 (persister), 4.7 (E2E) | AC-FR21-1 … AC-FR21-5 |
```

NEW:
```
| FR-20 | Épic 4 — 4.5 (contrôles purs), 4.6 (`AC-FR20-5`, existence coulée) ; Épic 6 — 6.9 (`AC-FR20-5` précisé), 6.10 (`AC-FR20-6`) | AC-FR20-1 … AC-FR20-6 |
| FR-21 | Épic 4 — 4.6 (persister), 4.7 (E2E) ; Épic 6 — 6.10 (`AC-FR21-6`) | AC-FR21-1 … AC-FR21-6 |
```

**Epic 6 list entry** — the "FRs couverts" line gains "FR-20, FR-21 révisés (6.9, 6.10)".

**New section** after Story 6.8:

```
## Corrections post-recette (Fichiers refusés par le legacy, 2026-10-02)

Rejeu des 6 Fichiers P60 refusés par le legacy en production (`P60/error/`) avec
l'état production reconstitué (sprint-change-proposal-2026-10-02.md) : le contrôle
Coulée froide ne se déclenche jamais sur un Fichier réel, et le renvoi d'un OF
existant n'était pas spécifié.

### Story 6.9 : Contrôle Coulée froide sur le TypeConsigne 12

As an exploitant,
I want qu'un Fichier dont la Coulée froide n'existe pas soit refusé avec cette cause,
So that un OF ne soit jamais créé sur une Coulée absente du stock.

**Acceptance Criteria :** `AC-FR20-5` (précisé).

**Given** `P60_847_682_407` et aucune ligne `L_D_COULEE` `063241`
**When** il est importé
**Then** il est refusé (`BusinessRuleViolation`, « la coulée '063241' est introuvable
dans L_D_COULEE »), aucune ligne dans les 10 tables.

**Given** la même base, puis `408` ; et `430` avec l'OF `2040297` déjà créé par `428`
et sans Coulée `063196`
**When** ils sont importés
**Then** même refus, même cause (jamais `PersistenceError`).

**Given** la Coulée `063241` présente
**When** `P60_847_682_412` est importé
**Then** il est accepté.

**Notes dev :**
- `Kape22Persister.cs:97` : comparer le premier caractère de `CodeConsignePits`
  (même tranche que `ConsignesMapper.cs:73`) à `Kape22ImportBundle.ColdConsignePits`.
- `P60/error/` : dossier versionné des Fichiers refusés en production (alimenté
  par l'utilisateur) ; ses 6 Fichiers actuels servent de fixtures. Hors du
  périmètre de `Kape22ProductionDataParityTests` (qui ne lit que `P60/`).
- Les tests existants qui sèment `CodeConsignePits = "1"` restent valides ; ajouter
  un cas `"1 207 00 000"` et un cas chaud `"3 148 00 740"`.

**Tests xUnit (TDD, CC-1) :** `Kape22Importer.Tests`, `[Trait("AC", "FR20-5")]`.
**Critères transverses :** CC-1, CC-2, CC-4, CC-7. Owner : Dev.

### Story 6.10 : Renvoi d'un OF existant — remplacement ou refus explicite

As an exploitant,
I want qu'un Fichier qui renvoie un OF existant remplace cet OF s'il n'est pas
engagé en production, et soit refusé avec la raison sinon,
So that les renvois GPAO se comportent comme avec le legacy et chaque refus
dise pourquoi.

**Acceptance Criteria :** `AC-FR20-6`, `AC-FR21-6` (D34).

**Given** l'OF `2040310` créé par `443`, `Etat` = 2 (EVC)
**When** `P60_847_682_446` est importé
**Then** refus explicite nommant l'OF et l'état, aucune écriture ; idem `447` et
`449` sur l'OF `2040311` (créé par `444`).

**Given** un OF remplaçable par son état, mais présent dans `L_D_PLANS_FOURS`,
`L_D_FOURS.OFEnCours` ou `L_D_PSO` (un cas par table)
**When** un Fichier le renvoie
**Then** refus explicite nommant la table, aucune écriture.

**Given** l'OF `2040312` créé par `445`, `Etat` = 0 (GPAO), absent des 3 tables
**When** `P60_847_682_448` est importé
**Then** il est accepté : les lignes de l'OF dans les 9 tables portent les valeurs
de `448`, la Coulée et la ligne `L_D_KAPE22` de `445` sont inchangées.

**Given** un remplacement dont le `SaveChanges()` échoue (échec SQL simulé)
**When** l'import se termine
**Then** l'ancien OF est intact dans les 9 tables (AC-FR21-2).

**Given** chaque Fichier de `P60/error/` (un cas de théorie par Fichier) et la
production configurée (`ConnectionStrings:AscoLSI_Production`, lecture seule,
`ApplicationIntent=ReadOnly`, SELECT uniquement)
**When** le test lit dans `L_D_LOG_COMMANDE` de production le traitement legacy
de ce Fichier (ligne `GPAO` « Traitement du fichier GPAO '…\<nom>' » jusqu'à
« s'est terminé avec une erreur », et les lignes `KAP22` entre les deux), puis
reconstitue dans `AscoLSI_Test` l'état que le Fichier a rencontré (Coulée
présente si sa `DateReception` de production précède le traitement ; OF existant
et son `Etat` à cet instant d'après l'historique `L_D_LOG_COMMANDE` de l'OF ;
`L_P_CONSIGNES_*` copiées), et l'importe avec le vrai `Kape22FichierProcessor`
**Then** le Fichier est refusé avec la cause qui correspond à la raison legacy :

| Raison legacy (`KAP22`) | Cause attendue |
|---|---|
| « demande une coulée froide … qui n'existe pas » | `AC-FR20-5` |
| « nombre d'OF sauvés : 0 » (OF existant, état protégé) | `AC-FR20-6` |
| « sans consignes d'enfournement » | `AC-FR20-4` |
| « nombre de produit … different de la somme des lingots » / « pas de consignes pour la répartition » | `AC-FR20-2` |
| « coulée chaude … ne commance pas par le caractère '0' » | aucune : accepté (`AC-FR20-3` retiré), signalé comme écart connu |

**And** une raison legacy absente de la table, ou un refus pour une autre cause,
fait échouer le cas avec la raison legacy et la cause obtenue dans le message ;
aucune trace legacy pour le Fichier, ou production non configurée → cas
`Skip` avec la raison (comme `Kape22ProductionDataParityTests`).
**And** le test est marqué à retirer à la bascule vers le nouveau worker (plus
aucun refus legacy à comparer) — limite acceptée par l'utilisateur le 2026-10-02.

**Notes dev :**
- Branche après la garde D22 et le contrôle Coulée froide, avant les pré-contrôles
  A-5/B-5/C-4.
- 3 entités lecture seule (`L_D_PLANS_FOURS`, `L_D_FOURS`, `L_D_PSO`, colonnes
  utilisées seulement) + miroir minimal dans `scripts/schema/01-ascolsi-tables.sql`.
  OF comparé au format `NCHAR(12)` complété de zéros (`DownstreamOf.Pad`).
- Suppression des 9 tables = script de l'utilisateur + `POIDSMETRIQUE` et `SVT`.
- Test générique `P60/error/` : catégorie Integration (base de test réinitialisée,
  production en SELECT seul — mémoire « Production DB read-only ») ; la
  reconstitution de l'état à l'instant T est la partie délicate, à figer au
  checkpoint spec (le rejeu manuel du 2026-10-02 en donne les 6 cas de référence).
- Résout `deferred-work.md:1454` (W-1 de la 6.8) : un second run du script E2E
  remplace l'OF au lieu d'échouer sur `PK_L_D_CONSIGNES`.

**Tests xUnit (TDD, CC-1) :** `Kape22Importer.Tests` (Integration),
`[Trait("AC", "FR20-6")]`, `[Trait("AC", "FR21-6")]`.
**Critères transverses :** CC-1, CC-2, CC-4, CC-7. Owner : Dev.
```

### 4.5 Architecture (`architecture-kape22-dispatch-2026-09-14`)

Add to the persistence section: the dispatch is insert-only **except** the D34 replace path (delete
of the OF's rows in 9 tables + insert, one `SaveChanges()`), and three read-only precondition tables.

### 4.6 Tracking

- `sprint-status.yaml`: `epic-6: in-progress`; add `6-9-controle-coulee-froide-typeconsigne-12:
  backlog` and `6-10-renvoi-of-existant-remplacement-ou-refus: backlog`.
- `PROJECT-CLOSED.md`: status `reopened`, reason = this proposal; re-close at the user's go.
- `epic-6-context.md`: stories 6.9, 6.10 and D34 in "Requirements & Constraints".
- `deferred-work.md:1454` (W-1): tag "planned — Story 6.10".

## 5. Implementation Handoff

- **Scope:** Moderate (backlog addition, one new business rule).
- **Recipient:** Developer agent, via `/build-story` on 6.9 then 6.10, each followed by
  `/run-review` and `/commit-review`.
- **Success criteria:** the six `P60/error/` Fichiers are rejected with the legacy cause (coulée
  froide for 407/408/430, OF state for 446/447/449), checked both by the explicit 6.9/6.10 tests
  and by the generic legacy-reason theory; 412 and 448 are accepted;
  `dotnet test TextToXml.sln` green (Unit + Integration).
