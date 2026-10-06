---
date: 2026-10-06
trigger: epic-6-retro2-A-3-pre-deployment-gpao
mode: incremental
scope_classification: moderate
status: approved (2026-10-06), documentation changes applied
---

# Sprint Change Proposal — Pre-deployment GPAO (stories 6.11–6.12)

## 1. Issue Summary

The second Epic 6 retrospective (`epic-6-retro-2026-10-06.md`, F-4, action A-3) bundled four
`deferred-work.md` entries that had each been deferred on their own with the reason "workers never
deployed". Together they are the gate before the first deployment of a GPAO worker:

| Cluster | Source entries | Finding (verified 2026-10-06) |
|---|---|---|
| (a) Production FK order, INSERT and `DeleteOf` | 6.10 step-04 D-2, 6.10 first pass F-1 | The schema mirror `scripts/schema/01-ascolsi-tables.sql` has no foreign key. `Kape22Persister.DeleteOf` deletes children before `L_D_ORDRE_FABRICATION`, but no test guards that order. With no EF relation (AD-7), the INSERT order rests on EF Core's command sort. |
| (b) Existing-OF read/transaction race | 6.10 step-04 D-1, 6.10 second pass F-1 (duplicate) | The D34 preconditions are read before `BeginTransaction` (`Kape22Persister.cs:130-144` vs `:220`). Legacy `AddRange2` has the same window. |
| (c) Blank `Import:Commande` | 6.6 D-1 | `GpaoImportP60` `ReadConfig` checks length only; `Kape22Persister` falls back to `P60` on `null` only, so `""` reaches `L_D_LOG_COMMANDE`. |
| (d) Non-I/O exception escaping the retry cap | 6.7 implementation review | `InboxScanner` and `P89FolderConverter` catch only `IOException` / `UnauthorizedAccessException` around read and filing. Any other exception aborts the tick before the Fichier is counted: it is never frozen, and later Fichiers are starved every tick. |

**Production foreign keys, read 2026-10-06** (`sys.foreign_keys` / `sys.foreign_key_columns`,
`AFV004-LSI/AscoLSI`, SELECT only), restricted to the dispatch:

| Child.column | Parent.column |
|---|---|
| `L_D_ORDRE_FABRICATION.Coulee` | `L_D_COULEE.IdCoulee` |
| `L_D_ORDRE_FABRICATION.ProfilProduit` | `L_P_PROFIL_PRODUIT.ID` |
| `OF` of the 7 `L_D_SECTIONCHARGE_*`, `L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PLANS_FOURS`, `L_D_PSO` | `L_D_ORDRE_FABRICATION.OF` |
| `CodeOperation` of the 7 `L_D_SECTIONCHARGE_*` | `L_P_TEXT_OPERATIONS.CodeOperation` |

All are `NO_ACTION`, enabled and trusted. `L_D_CONSIGNES`, `L_D_MAM_QUAL`, `L_D_PRODUITS_OUTIL` and
`L_D_KAPE22` carry none. The 2026-10-05 list (6.10 D-2) missed `→ L_D_COULEE` and the two `L_P_*`
tables. This read surfaced a fifth cluster:

| Cluster | Finding |
|---|---|
| (e) Unknown `ProfilProduit` / `CodeOperation` | `L_P_PROFIL_PRODUIT` (4 rows) and `L_P_TEXT_OPERATIONS` (19 rows) are never checked. An unknown value fails `SaveChanges` as a `PersistenceError`: the Fichier stays in `processing/` and is retried until the AC-FR25-8 cap freezes it. Never observed: 0 FK message in legacy `L_D_LOG_COMMANDE` `KAP22`. |

## 2. Impact Analysis

- **Epic impact:** Epic 6 reopens with a section "Corrections pré-déploiement GPAO" (same pattern
  as 6.6–6.8 and 6.9–6.10). No other epic is planned; this batch gates the first deployment.
- **Story impact:** two new stories, 6.11 (persistence, test-only unless the test reveals a
  violation) and 6.12 (worker loop, both libraries and `GpaoImportP60`). No completed story is
  reverted.
- **PRD:** new decision D35; new `AC-FR21-7`; `AC-FR25-8` and `AC-FR25-9` extended.
- **Architecture:** a note under AD-7; AD-7 itself is unchanged.
- **UX:** none.
- **Other:** `deferred-work.md` dispositions, `sprint-status.yaml`, `epic-6-context.md`,
  `PROJECT-CLOSED.md`; SVN commit of `GPAO/ImportP60` by the user for 6.12.

## 3. Recommended Approach

**Direct adjustment** (new stories in Epic 6). Effort low, risk low, no rollback, no MVP change.

| Cluster | Disposition (user, 2026-10-06) | Rationale |
|---|---|---|
| (a) | Planned — Story 6.11 | An integration test adds the real production FKs to `AscoLSI_Test` for its duration and replays a creation and a D34 replace; a mutation (OF deleted first) must fail it. Guards INSERT and `DeleteOf` without touching production code or AD-7; the fixture's `TRUNCATE` keeps working. |
| (b) | Accepted — D35 | Legacy parity; a window of milliseconds with one Fichier at a time; the production FKs `L_D_PLANS_FOURS` / `L_D_PSO` → `L_D_ORDRE_FABRICATION` already turn a concurrent enfournement into a failed DELETE and a rollback; locking tables the MCC writes would add a blocking risk. |
| (c) | Planned — Story 6.12 | One startup check in `ReadConfig`, consistent with AC-FR25-9. |
| (d) | Planned — Story 6.12 | One poison Fichier would stop a whole P60 or P89 line; both libraries, D32. |
| (e) | Accepted | Never observed; the GPAO shares the referential; the cap bounds the retries with an `Error` carrying the SQL cause. |

Order: 6.11 → 6.12 (independent; 6.11 first, its risk lands in production data).

## 4. Detailed Change Proposals

### 4.1 Stories — `epics.md` (append after Story 6.10)

```markdown
## Corrections pré-déploiement GPAO (2026-10-06)

Points de passage avant le premier déploiement d'un worker GPAO (rétro Épic 6 #2,
F-4 / A-3 ; sprint-change-proposal-2026-10-06.md). Trois grappes planifiées
ci-dessous ; la course lecture/transaction de l'OF existant est acceptée (D35).

### Story 6.11 : Ordre des FK de production gardé par les tests

As an exploitant,
I want que l'ordre des INSERT et des DELETE du dispatch soit vérifié contre les
clés étrangères de production,
So that un import ou un remplacement D34 vert en test ne puisse pas échouer en
production sur une FK que le miroir de schéma n'a pas.

**Acceptance Criteria :** `AC-FR21-7`.

**Given** `AscoLSI_Test` où les 13 FK de production qui touchent le dispatch sont
ajoutées pour la durée du test — `L_D_ORDRE_FABRICATION.Coulee` → `L_D_COULEE`,
`.ProfilProduit` → `L_P_PROFIL_PRODUIT` ; `OF` des 7 `L_D_SECTIONCHARGE_*`,
`L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PLANS_FOURS`, `L_D_PSO` → `L_D_ORDRE_FABRICATION` ;
`CodeOperation` des 7 `L_D_SECTIONCHARGE_*` → `L_P_TEXT_OPERATIONS` — avec les
lignes de référence `L_P_PROFIL_PRODUIT` / `L_P_TEXT_OPERATIONS` copiées de
production (SELECT seul)
**When** un Fichier de référence crée un OF sur une Coulée nouvelle, puis un second
Fichier le remplace (D34, OF en `Etat` GPAO, avec des lignes `L_D_OF_SUIVI` /
`L_D_REBUT` existantes)
**Then** les deux imports réussissent, sans violation de FK.

**Given** le même montage et `DeleteOf` modifié pour supprimer
`L_D_ORDRE_FABRICATION` en premier (mutation)
**When** le remplacement s'exécute
**Then** le test échoue (le garde est réel).

**And** les FK sont retirées en fin de test, y compris en cas d'échec ; le
`TRUNCATE` de `SqlServerIntegrationFixture` reste inchangé.

**Notes dev :**
- Liste des FK relue dans `sys.foreign_keys` de production le 2026-10-06
  (sprint-change-proposal-2026-10-06.md § 1) ; elle remplace celle de
  `deferred-work.md` 6.10 D-2, qui ignorait `→ L_D_COULEE` et les deux tables `L_P_*`.
- `L_P_PROFIL_PRODUIT` / `L_P_TEXT_OPERATIONS` : miroir minimal (clé seule) dans
  `scripts/schema/01-ascolsi-tables.sql` si absentes, sans FK.
- Aucun changement de code de production attendu : `DeleteOf` suit déjà l'ordre
  enfants puis OF ; l'ordre des INSERT tient aujourd'hui au tri des commandes
  par EF Core, sans relation déclarée (AD-7 inchangé). Si le test révèle une
  violation, la correction entre dans la story.
- Miroir `scripts/schema/01-ascolsi-tables.sql` sans FK (elles y bloqueraient
  le `TRUNCATE` du fixture).
- Résout `deferred-work.md` 6.10 D-2 et 6.10 première passe F-1.

**Tests xUnit (TDD, CC-1) :** `Kape22Importer.Tests` (Integration),
`[Trait("AC", "FR21-7")]`.
**Critères transverses :** CC-1, CC-2, CC-4, CC-7. Owner : Dev.

### Story 6.12 : Toute exception sur un Fichier compte pour le plafond ; `Import:Commande` blanc refusé

As an exploitant,
I want qu'un Fichier qui fait lever une exception inattendue soit compté, signalé
puis gelé comme tout Fichier en échec, et qu'un `Import:Commande` blanc empêche le
démarrage,
So that un seul Fichier toxique n'arrête pas toute la ligne P60 ou P89, et que
`L_D_LOG_COMMANDE` ne reçoive jamais une `Commande` vide.

**Acceptance Criteria :** `AC-FR25-8` (étendu), `AC-FR25-9` (étendu).

**Given** un Fichier P60 dont la lecture, l'export ou le classement (`Archive`,
`Reject`) lève une exception autre que `IOException` / `UnauthorizedAccessException`
(source de Fichiers de test qui lève `InvalidOperationException`), et un second
Fichier sain
**When** les ticks s'enchaînent
**Then** le Fichier fautif reste dans `processing/`, chaque tick le compte ; le
second Fichier est traité dès le premier tick ; à `Import:MaxAttempts` le Fichier
fautif est gelé avec **un** log `Error` nommant le Fichier et l'exception.

**Given** le même cas côté P89 (`P89FolderConverter.Process`, conversion ou
écriture)
**Then** même comportement sous `P89:MaxAttempts` (D32).

**Given** `Import:Commande` présente mais vide ou blanche
**When** `GpaoImportP60` démarre
**Then** le démarrage est refusé avec un message nommant la clé ; absente, elle
garde son repli `P60`.

**Notes dev :**
- `src/Kape22Importer/InboxScanner.cs` (lecture `:269`, export/classement `:329`,
  `:424`) et `src/P89Converter/P89FolderConverter.cs` (`:157`, `:220`) : les
  filtres `IOException or UnauthorizedAccessException` restent pour la reprise
  transitoire ; une autre exception est comptée pour ce Fichier et la boucle passe
  au suivant au lieu d'interrompre le tick. Forme exacte au checkpoint spec.
  `src/` = Ask First.
- `GPAO/ImportP60/Client.cs` `ReadConfig` (`:139`) : `string.IsNullOrWhiteSpace`
  sur une valeur non nulle. Grep de la clé (règle A-3). Commit SVN par
  l'utilisateur.
- Résout `deferred-work.md` 6.7 (exception non I/O, revue d'implémentation) et
  6.6 D-1.

**Tests xUnit (TDD, CC-1) :** `Kape22Importer.Tests`, `P89Converter.Tests`,
`GPAO/ImportP60.Tests` ; `[Trait("AC", "FR25-8")]`, `[Trait("AC", "FR25-9")]`.
**Critères transverses :** CC-1, CC-2, CC-4, CC-7. Owner : Dev.
```

### 4.2 PRD — `PRD.md`

**Decisions table, new row after D34:**

```markdown
| D35 | **Course de renvoi d'un OF acceptée.** Les préconditions D34 (`Etat`, `L_D_FOURS.OFEnCours`, `L_D_PLANS_FOURS`, `L_D_PSO`) sont lues sans verrou, avant la transaction de remplacement : un OF passé ENC par le MCC dans cette fenêtre (quelques ms, un Fichier à la fois) peut encore être remplacé. Accepté : parité legacy `AddRange2` ; les FK de production `L_D_PLANS_FOURS` / `L_D_PSO` → `L_D_ORDRE_FABRICATION` font échouer puis annulent un remplacement concurrent d'un enfournement ; verrouiller (`UPDLOCK, HOLDLOCK`) des tables écrites par le MCC ajouterait un risque de blocage. — sprint-change-proposal-2026-10-06.md. | utilisateur (2026-10-06) |
```

**FR-21, new AC after `AC-FR21-6`:**

```markdown
- `AC-FR21-7` : avec les FK de production qui touchent le dispatch ajoutées à la
  base de test (`→ L_D_COULEE`, `→ L_D_ORDRE_FABRICATION`, `→ L_P_PROFIL_PRODUIT`,
  `→ L_P_TEXT_OPERATIONS`, relevé `sys.foreign_keys` du 2026-10-06), une création
  d'OF puis son remplacement D34 réussissent sans violation : l'ordre des INSERT
  et celui de `DeleteOf` sont gardés par un test, pas seulement par le miroir sans
  FK. *(Ajouté 2026-10-06, sprint-change-proposal-2026-10-06.md.)*
```

**`AC-FR25-8`** — OLD tail: "… repart de zéro au redémarrage ou quand le Fichier quitte
`processing/`. *(Ajouté 2026-10-01, sprint-change-proposal-2026-10-01.md.)*" — NEW tail:

```markdown
  … repart de zéro au redémarrage ou quand le Fichier
  quitte `processing/`. Toute exception levée pour un Fichier (lecture, export,
  classement, conversion P89), pas seulement une erreur d'E/S, est comptée pour ce
  Fichier et n'empêche pas le traitement des Fichiers suivants dans le même tick.
  *(Ajouté 2026-10-01, sprint-change-proposal-2026-10-01.md ; étendu 2026-10-06,
  sprint-change-proposal-2026-10-06.md.)*
```

**`AC-FR25-9`** — OLD clause: "`Import:Commande` dépasse 50 caractères (`L_D_LOG_COMMANDE.Commande`)
ou `Import:RetentionDays` sort de 1..3650 — `GpaoImportP60`. *(Ajouté 2026-10-01.)*" — NEW:

```markdown
  `Import:Commande` est présente mais vide ou blanche, ou dépasse 50 caractères
  (`L_D_LOG_COMMANDE.Commande`), ou `Import:RetentionDays` sort de 1..3650 —
  `GpaoImportP60`. *(Ajouté 2026-10-01 ; clause « vide ou blanche » ajoutée le
  2026-10-06, sprint-change-proposal-2026-10-06.md.)*
```

### 4.3 Architecture — `ARCHITECTURE-SPINE.md`, under AD-7's rule

```markdown
- **Note (2026-10-06, Story 6.11, AC-FR21-7) :** la production porte des FK que le
  miroir de test n'a pas (`L_D_ORDRE_FABRICATION.Coulee` → `L_D_COULEE`,
  `.ProfilProduit` → `L_P_PROFIL_PRODUIT` ; `OF` des 7 `L_D_SECTIONCHARGE_*`,
  `L_D_OF_SUIVI`, `L_D_REBUT`, `L_D_PLANS_FOURS`, `L_D_PSO` →
  `L_D_ORDRE_FABRICATION` ; `CodeOperation` des 7 sections → `L_P_TEXT_OPERATIONS`).
  Sans navigation EF, l'ordre des INSERT relève du tri des commandes par EF Core et
  `DeleteOf` supprime explicitement les enfants avant l'OF ; un test d'intégration
  qui ajoute ces FK le temps du test garde les deux ordres. AD-7 est inchangé.
```

### 4.4 Tracking artifacts

- **`deferred-work.md`** — prefixes on existing entries:
  - 6.6 D-1 → `**PLANNED 2026-10-06: Story 6.12** (AC-FR25-9 extended: present but blank → startup refused)`
  - 6.7 implementation review (non-I/O exception) → `**PLANNED 2026-10-06: Story 6.12** (AC-FR25-8 extended: any per-Fichier exception is counted, the tick goes on)`
  - 6.10 step-04 D-1 → `**ACCEPTED 2026-10-06 (D35)**: legacy AddRange2 parity; the PLANS_FOURS/PSO FKs roll back a replace racing an enfournement; a lock shared with the MCC would add a blocking risk`
  - 6.10 step-04 D-2 → `**PLANNED 2026-10-06: Story 6.11** (AC-FR21-7: production FKs added for the test's duration; list re-read 2026-10-06, see the correct-course entry below)`
  - 6.10 first pass F-1 → `**PLANNED 2026-10-06: Story 6.11** (same AC-FR21-7 guard)`
  - 6.10 second pass F-1 → `**ACCEPTED 2026-10-06 (D35)**, duplicate of the step-04 D-1 entry`

  each followed by ` — sprint-change-proposal-2026-10-06.md · `. Merging the two race entries stays
  with retro action A-2.
- **`deferred-work.md`** — new entry at the end:

  ```markdown
  ## Deferred from: correct-course pre-deployment GPAO (2026-10-06)

  - source_spec: **ACCEPTED 2026-10-06** (user decision): never observed in production (0 FK message in legacy `L_D_LOG_COMMANDE` `KAP22`), the GPAO shares this referential, and the AC-FR25-8 cap bounds the retries with an `Error` carrying the SQL cause · sprint-change-proposal-2026-10-06.md (cluster e)
    summary: `L_D_ORDRE_FABRICATION.ProfilProduit` → `L_P_PROFIL_PRODUIT` (4 rows) and `CodeOperation` of the 7 `L_D_SECTIONCHARGE_*` → `L_P_TEXT_OPERATIONS` (19 rows) are production FKs no pre-check covers; an unknown value fails `SaveChanges` as a `PersistenceError`, so the Fichier stays in `processing/` and is retried until the cap freezes it, instead of being rejected to `error/` on the first tick.
    evidence: production `sys.foreign_keys` / `sys.foreign_key_columns` read 2026-10-06 (SELECT only). The FK-less mirror `scripts/schema/01-ascolsi-tables.sql` and the import code never check either value; Story 6.11 seeds both tables for its FK test only.
  ```

- **`sprint-status.yaml`** — `epic-6: in-progress`; add
  `6-11-ordre-fk-production-garde-par-tests: backlog` and
  `6-12-exception-fichier-plafond-commande-blanche: backlog` after 6.10; `epic-6-retrospective`
  stays `done`; `epic-6-retro2-item-3-correct-course-pre-deployment` → `done`, ref extended with
  `-> _bmad-output/planning-artifacts/sprint-change-proposal-2026-10-06.md (stories 6.11-6.12
  planned, D35 + cluster e accepted)`; A-1 and A-2 stay `open` (re-close after 6.12).
- **`epic-6-context.md`** — stories 6.11 and 6.12 listed; Requirements bullets for AC-FR21-7,
  extended AC-FR25-8 / AC-FR25-9, D35 and the accepted cluster (e); order "Pre-deployment order
  6.11 → 6.12 (independent; gate for the first GPAO worker deployment)".
- **`PROJECT-CLOSED.md`** — `reopened_date: '2026-10-06'`, `reopened_reason: 'Pre-deployment
  GPAO — stories 6.11-6.12 (sprint-change-proposal-2026-10-06.md)'`,
  `previous_reopened_date: '2026-10-02'`; a "Reopened 2026-10-06" banner; `status` stays
  `'reopened'`.

## 5. Implementation Handoff

- **Scope:** Moderate — backlog reorganization within Epic 6 (two new stories, PRD and context
  updates), no replan.
- **Recipients:**
  - Developer: build 6.11 then 6.12 (`/build-story`), each followed by `/run-review` and
    `/commit-review`.
  - User: SVN commit of `GPAO/ImportP60` for 6.12; decides when to re-close the project (A-1).
- **Success criteria:**
  - `AC-FR21-7` green with the 13 production FKs in place, and red under the `DeleteOf` mutation.
  - `AC-FR25-8` extension: a non-I/O exception on one Fichier no longer starves the others (P60
    and P89) and freezes the faulty Fichier at the cap.
  - `AC-FR25-9` extension: blank `Import:Commande` refuses startup.
  - All suites green, 0 build warning; `deferred-work.md` entries above carry their disposition.
  - After 6.12: no open "workers never deployed" entry of A-3 remains unplanned or unaccepted;
    the first GPAO worker deployment is unblocked on these grounds.
