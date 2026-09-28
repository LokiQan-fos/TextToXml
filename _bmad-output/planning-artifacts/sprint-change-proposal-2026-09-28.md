---
date: 2026-09-28
trigger: epic-5-retro-A-3-shared-worker-debt
mode: batch
scope_classification: moderate
status: approved (2026-09-28, with the D33 XML-export addition), documentation changes applied
---

# Sprint Change Proposal — Epic 6: P60 journal migration + shared GPAO worker hardening

## 1. Issue Summary

The Epic 5 retrospective (`implementation-artifacts/epic-5-retro-2026-09-28.md`, finding F-4, action
A-3, commit `c8ce083`) found ten open `deferred-work.md` entries that concern the P60 and P89 workers
together, all marked "fix with P60" / "fix once", with no story, no action item and no planned epic to
carry them. On 2026-09-28 the user answered Q-1 "oui": plan them as Epic 6.

The ten entries (`implementation-artifacts/deferred-work.md`, line of each `summary:`):

| Line | Entry | Repo |
|---|---|---|
| 1249 | No file-stability gate in `P89FolderConverter.RunTick`; the P60 gate `InboxScanner.TryStableInboxFichiers` is a near no-op on a real share (two probes, no delay) | TextToXml |
| 1253 | `Publisher` timer re-entrancy: a second tick can start while the previous one runs | MicroServices (`MicroService.Publisher`, 5 workers) |
| 1259 | Migrate P60 (`Kape22Importer`) onto `IFichierJournal` / `AscoLsiJournal` (D31) | TextToXml + `GpaoImportP60` |
| 1305 | `GpaoImportP60.Client.Actions` calls `Connect()` outside its `try` | MicroServices |
| 1309 | A tick outliving the 4 s `ShutdownBudget` keeps running against a disposed `Client` | MicroServices (both GPAO workers) |
| 1313 | No test builds a real `Client` of either GPAO worker (P60 `Client.Actions` test owed since Epic 3) | MicroServices |
| 1317 | Connection strings only checked for being non-blank; `MQTTnetServices` not checked | MicroServices (both GPAO workers) |
| 1321 | Heartbeat publishes "alive" after a failed tick | MicroServices (both GPAO workers) |
| 1325 | Standalone `WorkerService` retries a configuration error 10 times, never disposes the `Client` | MicroServices (both GPAO workers) |
| 1329 | A `ReadConfig` throw after the base constructor leaks the `AbstractService` SQL sink | MicroServices (both GPAO workers) |

### User decisions (2026-09-28, this workflow)

- **Re-entrancy:** fixed once, in the shared `MicroService.Publisher` (covers ImportP60, ConvertP89,
  Laminoir/OrdresFabricationSync, Video/Shears/ConvertAndSave, Zumbach/ImportFiles).
- **P60 journal write fails after the AD-1 commit:** automatic recovery. The Fichier stays in
  `processing/`; at the next tick the D22 guard (moved onto `L_D_KAPE22`) finds the committed row,
  writes the missing "— OK" journal entry, then archives the Fichier.
- **Supervision items in scope:** heartbeat after a failed tick (1321) and the standalone
  `WorkerService` (1325).
- **Mode:** batch.
- **Proposal approved 2026-09-28, with one addition by the user:** every format (P60, P89 and
  future formats, import or export) saves the normalized XML of every converted Fichier in its own
  dedicated export folder, so the files can be sent to third parties who verify the imported or
  exported data. That folder is never purged. Converted Fichiers only: a Fichier that `TextToXml`
  cannot convert has no XML. P89 already complies (`P89:XmlPath`, `AC-FR22-4`); P60 does not (its
  XML sits next to the archived Fichier, `archive/<yyyy>/<MM>/<nom>.xml`, `AC-FR12-3`, only on
  success, and is purged after `Import:RetentionDays`, `AC-FR12-9`). Added as D33, FR-26, AC-FR16-5
  and Story 6.3.

## 2. Impact Analysis

### Checklist

| Item | Status | Note |
|---|---|---|
| 1.1 Trigger | [x] | Epic 5 retro F-4 / A-3; no failing story |
| 1.2 Problem | [x] | Tracked technical debt with no delivery vehicle, plus a deferred design decision (D31) |
| 1.3 Evidence | [x] | `deferred-work.md:1249-1329`; `MicroService/Publish/Publisher.cs` (timer callback has no in-flight guard); `Kape22Persister.cs:188` (the "— OK" row is added to the AD-1 context), `:259` (D22 guard reads `L_D_LOG_COMMANDE`) |
| 2.1 Current epic | [N/A] | Epics 1–5 done |
| 2.2 Epic changes | [!] | New Epic 6, 5 stories (10 debt entries + the XML export addition) |
| 2.3–2.5 Future epics | [N/A] | None planned; P89 Step 2 still unplanned (D30) |
| 3.1 PRD | [!] | D8, D22, D31 revised; new D32; AC-FR11-3, AC-FR11-6, AC-FR14-7, AC-FR12-5 revised; new D33; new §4.8 with FR-24, FR-25, FR-26; AC-FR22-9 and AC-FR16-5 added |
| 3.2 Architecture | [!] | Spine AD-1 rule drops `L_D_LOG_COMMANDE`; capability row "Journalisation d'échec" |
| 3.3 UX | [N/A] | No UI |
| 3.4 Other artifacts | [!] | `sprint-status.yaml`, `PROJECT-CLOSED.md`, `epic-5-context.md:40`, `deferred-work.md` (entries marked "planned: Story 6.x"), `project-profile.md` (layout: `Kape22Importer` gains a `FichierJournal` reference) |
| 4.1 Direct adjustment | Viable | Medium effort, medium risk (touches a closed epic's transaction contract and a shared MicroServices library) |
| 4.2 Rollback | Not viable | Nothing to revert; the debt is additive |
| 4.3 MVP review | N/A | MVP unaffected |

### Technical Impact

- **TextToXml repo.** `Kape22Persister` loses its `L_D_LOG_COMMANDE` writes and its copies of the
  entity, column lengths, `ParisTime` and row rules; it calls `IFichierJournal` after the AD-1
  `SaveChanges()`. The D22 guard queries `L_D_KAPE22` (`NumeroFichier` + `OF`, both non-unique
  columns, `PRD.md` Annexe C.1; no schema change, AD-5). `P89FolderConverter` and `InboxScanner` get a
  real quiet-period gate. `TextToXml` library untouched.
- **MicroServices repo (SVN, committed by the user).** `MicroService.Publisher` skips a tick while the
  previous one runs (all 5 Publisher workers). `GpaoImportP60` and `GpaoConvertP89` `Client`s: guarded
  connect, safe stop/dispose, connection-string validation, no heartbeat after a failed tick, no sink
  leak, standalone host fix; one shared integration test on a real `Client` of each.
- **Risk to verify in Story 6.1's spec:** a D22 guard on `L_D_KAPE22` also sees rows written by the
  legacy importer, while today's guard only sees "— OK" log rows. During a parallel run, a Fichier
  already imported by the legacy chain would be archived as "already imported". This is the intended
  anti-duplicate behavior, but the spec checkpoint must confirm it (read-only `SELECT` on production
  allowed).

## 3. Recommended Approach

**Direct adjustment: add Epic 6 with five stories**, sequenced 6.1 → 6.2 → 6.3 → 6.4 → 6.5:

1. **6.1** — P60 journal migration (FR-24). First, because it rewires `GpaoImportP60`'s journal and
   changes the D22 guard that the worker hardening relies on.
2. **6.2** — File-stability gate, P60 + P89 (AC-FR12-5 revised, AC-FR22-9). TextToXml repo only.
3. **6.3** — Dedicated XML export folder per format (FR-26, AC-FR16-5). P60 gains
   `Import:XmlExportPath`; `GpaoImportP60` validates it at startup.
4. **6.4** — Shared worker hardening (FR-25, AC-FR25-1..6). MicroServices repo.
5. **6.5** — Integration test on a real `Client` of each GPAO worker (AC-FR25-7). Last, so it locks
   the behavior 6.1, 6.3 and 6.4 produce.

Effort: medium. Risk: medium. 6.1 revises closed ACs of Epics 2–4 (AC-FR11-3/6, AC-FR14-7), and 6.4
changes `MicroService.Publisher`, which workers outside GPAO also use. Each story goes through
`/build-story` (route `plan-code-review`), `/run-review`, `/commit-review`.

## 4. Detailed Change Proposals

### 4.1 PRD.md — §0bis decisions

D8, append at the end of the cell's text (before `| utilisateur + schémas réels`):

```
Depuis l'Épic 6 (FR-24), la ligne `L_D_LOG_COMMANDE` est écrite par `IFichierJournal` (D31) hors de la transaction AD-1.
```

D22, OLD:
```
| D22 | Avant l'`INSERT` `L_D_KAPE22`, le worker vérifie qu'aucune ligne `L_D_LOG_COMMANDE` `… — OK` n'existe déjà pour ce `NumeroFichier` + `OF` (garde‑fou anti‑doublon sur crash post‑commit). Si trouvée → fichier déplacé en `archive/`, log `Warning`, pas de ré‑insertion. | utilisateur (Q21) |
```
NEW:
```
| D22 | Avant l'`INSERT` `L_D_KAPE22`, le worker vérifie qu'aucune ligne `L_D_KAPE22` n'existe déjà pour ce `NumeroFichier` + `OF` (garde‑fou anti‑doublon sur crash post‑commit ; révisé 2026-09-28, Épic 6 — la garde portait sur la ligne `L_D_LOG_COMMANDE … — OK`, qui n'est plus dans la transaction). Si trouvée → pas de ré‑insertion ; si le journal n'a pas d'entrée de succès pour ce Fichier, elle est écrite ; fichier déplacé en `archive/`, log `Warning`. | utilisateur (Q21 ; 2026‑09‑28) |
```

D31, OLD (end of cell):
```
P89 l'utilise dès l'Épic 5 ; la migration de P60 (ligne `— OK` aujourd'hui dans la transaction AD-1, garde D22 sur la ligne de log, cause SQL détaillée absente du journal) est une story à planifier par correct-course.
```
NEW:
```
P89 l'utilise dès l'Épic 5 ; P60 y migre par l'Épic 6 (FR-24, sprint-change-proposal-2026-09-28.md).
```

New D32, appended after D31:
```
| D32 | **Robustesse commune des workers.** La ré‑entrance du timer se corrige une fois dans `MicroService.Publisher` (tous les workers `Publisher`) : un tick ne démarre pas tant que le précédent tourne. Les défauts propres aux `Client` GPAO (`GpaoImportP60`, `GpaoConvertP89`) se corrigent dans les deux à l'identique (FR-25). Après un tick en échec, un worker GPAO ne publie pas de heartbeat. | utilisateur (2026‑09‑28) |
```

New D33, appended after D32:
```
| D33 | **Export XML par format.** Chaque format (P60, P89, formats futurs, en import comme en export) écrit le XML normalisé de **chaque** Fichier converti dans son **propre** dossier d'export, pour transmission à des tiers qui vérifient les données importées ou exportées. Nom `<nom>_<yyyyMMddHHmmss>.xml` (jamais d'écrasement). Ce dossier n'est **jamais purgé** par le worker ; son nettoyage relève de l'exploitation. Un Fichier que `TextToXml` ne sait pas convertir n'a pas de XML. P89 s'y conforme déjà (`P89:XmlPath`) ; P60 par l'Épic 6 (FR-26). | utilisateur (2026‑09‑28) |
```

Front matter: `updated: 2026-09-28`.

### 4.2 PRD.md — revised ACs of closed epics

AC-FR11-3, OLD:
```
- `AC-FR11-3` : **succès** → insert `L_D_KAPE22` + insert `L_D_LOG_COMMANDE`
  (` — OK`) dans **une même transaction** ; échec de l'un ⇒ rollback des deux.
```
NEW:
```
- `AC-FR11-3` : *(révisé Épic 6, remplacé par `AC-FR24-2`)* **succès** → insert
  `L_D_KAPE22` (+ tables aval, AD-1) puis entrée de journal de succès, hors
  transaction (D31).
```

AC-FR11-6, OLD:
```
- `AC-FR11-6` : garde‑fou — le retraitement d'un fichier dont un
  `L_D_LOG_COMMANDE … — OK` existe déjà (même `NumeroFichier` + `OF`) →
  **0 insert**, fichier déplacé en `archive/`, log `Warning`
  « déjà importé, ignoré ».
```
NEW:
```
- `AC-FR11-6` : garde‑fou — le retraitement d'un fichier dont une ligne
  `L_D_KAPE22` existe déjà (même `NumeroFichier` + `OF`, D22 révisé Épic 6) →
  **0 insert**, fichier déplacé en `archive/`, log `Warning`
  « déjà importé, ignoré » (voir aussi `AC-FR24-4`).
```

AC-FR14-7, OLD:
```
- `AC-FR14-7` : `Logs` indisponible n'empêche pas l'insertion `L_D_KAPE22` (log
  best‑effort) ; `L_D_LOG_COMMANDE` fait partie de la transaction de succès
  (`AC-FR11-3`).
```
NEW:
```
- `AC-FR14-7` : `Logs` indisponible n'empêche pas l'insertion `L_D_KAPE22` (log
  best‑effort) ; `L_D_LOG_COMMANDE` est écrit après la transaction de succès
  (`AC-FR24-2`, révisé Épic 6).
```

AC-FR12-5, OLD:
```
- `AC-FR12-5` : Fichier encore en cours d'écriture (taille instable entre deux
  lectures) → laissé dans l'inbox, retenté au tick suivant, aucune erreur loggée.
```
NEW:
```
- `AC-FR12-5` : Fichier encore en cours d'écriture (dernière écriture plus
  récente que `Import:StabilityQuietPeriod`, défaut 10 s ; révisé Épic 6) →
  laissé dans l'inbox, retenté au tick suivant, aucune erreur loggée.
```

### 4.2bis PRD.md — FR-16, new AC (after AC-FR16-4)

```
- `AC-FR16-5` : chaque format déclare son propre dossier d'export XML (D33),
  distinct de ceux des autres formats et de ses dossiers de travail, jamais
  purgé ; un Fichier converti y laisse `<nom>_<yyyyMMddHHmmss>.xml`
  (P60 : `Import:XmlExportPath`, FR-26 ; P89 : `P89:XmlPath`, `AC-FR22-4`).
```

### 4.3 PRD.md — FR-22, new AC (after AC-FR22-8)

```
- `AC-FR22-9` : un Fichier dont la dernière écriture est plus récente que
  `P89:StabilityQuietPeriod` (défaut 10 s) est laissé dans le dossier source,
  sans XML, sans entrée de journal, et retenté au tick suivant (Épic 6).
```

### 4.4 PRD.md — new §4.8 (after FR-23, before §5)

```
### 4.8 Migration du journal P60, robustesse commune des workers GPAO et export XML (Épic 6)

#### FR-24 : Journal P60 via `IFichierJournal`

**Description :** `Kape22Importer` n'écrit plus lui-même `L_D_LOG_COMMANDE` :
il appelle `IFichierJournal` (D31), dont `GpaoImportP60` injecte
l'implémentation LSI `AscoLsiFichierJournal`. Le journal s'écrit après la
transaction AD-1, pour chaque issue.

**Consequences (testables) :**
- `AC-FR24-1` : `Kape22Importer` ne contient plus d'entité, de longueurs de
  colonnes, de `ParisTime` ni de règle de ligne `L_D_LOG_COMMANDE` ; il
  référence `FichierJournal`, pas `AscoLsiJournal`.
- `AC-FR24-2` : succès → un seul `SaveChanges()` (AD-1) sans
  `L_D_LOG_COMMANDE`, puis une entrée de journal de succès (`NumeroFichier`,
  `OF`) ; la ligne produite est celle de D8.
- `AC-FR24-3` : rejet métier ou échec SQL → aucune ligne métier, une entrée
  d'échec ; pour un échec SQL, la raison nomme la table, la colonne et la
  cause (troncature, type, contrainte) quand SQL Server les fournit.
- `AC-FR24-4` : garde D22 (ligne `L_D_KAPE22` même `NumeroFichier` + `OF`) →
  aucune ré‑insertion ; si le journal n'a pas d'entrée de succès pour ce
  Fichier, elle est écrite, puis le Fichier est archivé.
- `AC-FR24-5` : journal en échec après le commit → Fichier laissé dans
  `processing/`, log `Warning`, retraité au tick suivant (`AC-FR24-4` le
  termine) ; aucune donnée métier en double.
- `AC-FR24-6` : sur les Fichiers P60 de référence, les `Message` de
  `L_D_LOG_COMMANDE` sont identiques à ceux d'avant la migration.

#### FR-25 : Robustesse commune des workers GPAO

**Description :** défauts partagés par `GpaoImportP60` et `GpaoConvertP89`,
corrigés à l'identique dans les deux (D32) ; la ré‑entrance, une fois dans
`MicroService.Publisher`.

**Consequences (testables) :**
- `AC-FR25-1` : `Publisher` ne démarre jamais un tick tant que le précédent
  tourne ; le tick manqué est ignoré, pas empilé.
- `AC-FR25-2` : `Client.Actions()` ne laisse échapper aucune exception, la
  connexion au broker comprise.
- `AC-FR25-3` : un tick qui dépasse le budget d'arrêt n'accède plus au
  `Client` une fois celui-ci libéré (aucun log, aucun publish après
  `Dispose`).
- `AC-FR25-4` : `ConnectionStrings:AscoLSI` et `ConnectionStrings:MQTTnetServices`
  absentes ou mal formées empêchent le démarrage avec un message nommant la
  clé ; une configuration refusée ne laisse aucun sink SQL ouvert.
- `AC-FR25-5` : un tick en échec ne publie pas de heartbeat.
- `AC-FR25-6` : l'hôte autonome `WorkerService` ne retente pas une erreur de
  configuration et libère le `Client` à l'arrêt.
- `AC-FR25-7` : un test d'intégration construit un vrai `Client` de chaque
  worker GPAO (`AscoLSI_Test`) et vérifie `Frequency`, le confinement des
  erreurs dans `Actions`, l'annulation par `Stop()` et le log `Warning` d'un
  Fichier `Deferred`.

#### FR-26 : Export XML P60 dans un dossier dédié

**Description :** `Kape22Importer` écrit le XML normalisé de chaque Fichier
converti dans `Import:XmlExportPath` (D33), en plus des emplacements existants
(`archive/`, `error/`, `AC-FR12-3`, `AC-FR13-3`, inchangés).

**Consequences (testables) :**
- `AC-FR26-1` : tout Fichier que `Converter.Convert` convertit sans Error
  laisse `<nom>_<yyyyMMddHHmmss>.xml` dans `Import:XmlExportPath`, quelle que
  soit la suite : importé, rejeté (mapping, contrôles métier, SQL) ou ignoré
  par la garde D22.
- `AC-FR26-2` : un Fichier en échec de conversion (Étape 1) n'y laisse rien.
- `AC-FR26-3` : deux traitements d'un même `<nom>` à des instants différents
  ne s'écrasent pas (horloge injectée).
- `AC-FR26-4` : la purge `Import:RetentionDays` (`AC-FR12-9`) ne touche
  jamais `Import:XmlExportPath`.
- `AC-FR26-5` : `Import:XmlExportPath` absent, relatif, mal formé ou
  identique à un autre dossier du worker empêche le démarrage de
  `GpaoImportP60`, avec un message nommant la clé.
- `AC-FR26-6` : une écriture impossible dans le dossier d'export laisse le
  Fichier dans `processing/`, log `Warning`, retraité au tick suivant ; aucune
  donnée métier en double (garde D22).
```

### 4.5 Architecture spine — `ARCHITECTURE-SPINE.md`

AD-1 Rule, OLD:
```
- **Rule:** `L_D_KAPE22`, `L_D_LOG_COMMANDE`, `L_D_ORDRE_FABRICATION`,
  `L_D_COULEE`, `L_D_CONSIGNES` et les `L_D_SECTIONCHARGE_*` concernées sont
```
NEW:
```
- **Rule:** `L_D_KAPE22`, `L_D_ORDRE_FABRICATION`,
  `L_D_COULEE`, `L_D_CONSIGNES` et les `L_D_SECTIONCHARGE_*` concernées sont
```
and append to AD-1: `` - **Note (Épic 6, D31) :** `L_D_LOG_COMMANDE` est écrit par `IFichierJournal` après ce `SaveChanges()` ; la garde anti-doublon lit `L_D_KAPE22` (D22 révisé). ``

Capability table, OLD:
```
| Journalisation d'échec (cause précise, exploitant averti) | `Kape22Persister` + `L_D_LOG_COMMANDE` + `MQTTnetServices.Logs` | AD-4 (réutilise FR-14/Story 3.3) |
```
NEW:
```
| Journalisation d'échec (cause précise, exploitant averti) | `Kape22Persister` → `IFichierJournal` (`AscoLsiFichierJournal`, `L_D_LOG_COMMANDE`) + `MQTTnetServices.Logs` | AD-4 (réutilise FR-14/Story 3.3 ; FR-24) |
```

### 4.6 epics.md — Epic List entry + Epic 6 section

Epic List, append after Épic 5:
```
### Épic 6 : Journal P60, robustesse des workers GPAO, export XML par format
Solder la dette partagée P60/P89 relevée par la rétro de l'Épic 5 (F-4) :
`Kape22Importer` journalise via `IFichierJournal` hors de la transaction AD-1
(garde D22 sur `L_D_KAPE22`), les deux formats attendent qu'un Fichier soit
stable, `MicroService.Publisher` n'empile plus les ticks, et les `Client`
`GpaoImportP60` / `GpaoConvertP89` sont durcis et testés sur une vraie
instance ; chaque format exporte son XML dans son propre dossier (D33). `AC-FR24-1..6`, `AC-FR25-1..7`, `AC-FR26-1..6`, `AC-FR16-5`, `AC-FR12-5` (révisé) et
`AC-FR22-9` sont verts.
**FRs couverts :** FR-24, FR-25, FR-26 (+ FR-11, FR-12, FR-14, FR-16, FR-22 révisés).
```

Epic 6 section, appended at the end of `epics.md`:
```
## Épic 6 : Journal P60, robustesse des workers GPAO, export XML par format

Solder les 10 entrées « fix with P60 / fix once » de `deferred-work.md`
(rétro Épic 5, F-4) et ajouter un dossier d'export XML par format (D33)
(sprint-change-proposal-2026-09-28.md). `TextToXml` n'est pas modifiée.

**FRs couverts :** FR-24, FR-25, FR-26 (+ `AC-FR11-3`, `AC-FR11-6`, `AC-FR12-5`,
`AC-FR14-7` révisés, `AC-FR16-5`, `AC-FR22-9`).

**Séquencement :** 6.1 → 6.2 → 6.3 → 6.4 → 6.5.

### Story 6.1 : Journal P60 via `IFichierJournal`

As an exploitant,
I want que le journal P60 s'écrive comme celui de P89, après la transaction
métier et pour chaque issue, y compris un échec SQL,
So that chaque Fichier P60 laisse une ligne `L_D_LOG_COMMANDE` lisible, avec la
cause SQL précise en cas d'échec, sans deux copies des règles LSI.

**Acceptance Criteria:** `AC-FR24-1` à `AC-FR24-6` ; `AC-FR11-3`, `AC-FR11-6`,
`AC-FR14-7` révisés (PRD §4.2, §4.3, §4.8).

**Notes dev :**
- `Kape22Persister` reçoit un `IFichierJournal` ; plus aucun `LogCommandeRows`
  dans son contexte ; supprimer les copies (`deferred-work.md:1259`).
- `GpaoImportP60` (MicroServices) compose `AscoLsiFichierJournal`, comme
  `GpaoConvertP89`. Commit SVN par l'utilisateur.
- Au checkpoint spec : confirmer par `SELECT` en lecture seule sur la
  production que la garde `L_D_KAPE22` (`NumeroFichier` + `OF`) n'archive pas
  à tort un Fichier (lignes legacy).
- Tests d'intégration existants lisant `L_D_LOG_COMMANDE` : adapter, pas
  supprimer.

**Tests xUnit (TDD, CC-1) :** `Kape22Importer.Tests` Unit + Integration (AR-12),
`[Trait("AC", "FR24-…")]`.

**Critères transverses :** CC-1, CC-2, CC-4, CC-5, CC-7 ; AD-1, AD-4, AD-5.

Owner : Dev.

### Story 6.2 : Garde de stabilité des Fichiers P60 + P89

As an exploitant,
I want qu'un Fichier encore en cours de copie ne soit pas lu,
So that il ne part pas en `error/` tronqué.

**Acceptance Criteria:** `AC-FR12-5` (révisé), `AC-FR22-9`.

**Notes dev :**
- Même règle dans `InboxScanner` et `P89FolderConverter` : dernière écriture
  plus récente que la période de calme ⇒ Fichier ignoré ce tick. Horloge =
  `TimeProvider` (`deferred-work.md:1249`).
- Clés `Import:StabilityQuietPeriod` et `P89:StabilityQuietPeriod`, défaut
  10 s ; README à jour.

**Tests xUnit (TDD, CC-1) :** `Kape22Importer.Tests`, `P89Converter.Tests`,
`Category=Unit`.

**Critères transverses :** CC-1, CC-2, CC-4.

Owner : Dev.

### Story 6.3 : Export XML P60 dans un dossier dédié

As an exploitant,
I want que le XML normalisé de chaque Fichier P60 converti soit sauvegardé
dans un dossier d'export dédié, jamais purgé,
So that je peux l'envoyer à des tiers qui vérifient les données importées,
comme je peux déjà le faire pour P89.

**Origine :** ajout de l'utilisateur à l'approbation de
sprint-change-proposal-2026-09-28.md (D33).

**Acceptance Criteria:** `AC-FR26-1` à `AC-FR26-6`, `AC-FR16-5`.

**Notes dev :**
- `ImportOptions.XmlExportPath` (`Import:XmlExportPath`), chemin absolu ;
  horloge `TimeProvider` pour le suffixe `yyyyMMddHHmmss`, comme
  `P89FolderConverter`.
- L'export s'ajoute aux XML existants de `archive/` et `error/`, qui ne
  changent pas.
- `GpaoImportP60` (MicroServices) : clé ajoutée à `GpaoImportP60.json` et
  validée au démarrage (`AC-FR26-5`). Commit SVN par l'utilisateur.
- `AC-FR16-5` : vérifier que P89 s'y conforme déjà (`P89:XmlPath`), sans le
  modifier ; README : section « Export XML ».

**Tests xUnit (TDD, CC-1) :** `Kape22Importer.Tests` (`Category=Unit`,
dossiers temporaires), `GPAO/ImportP60.Tests` ; `[Trait("AC", "FR26-…")]`.

**Critères transverses :** CC-1, CC-2, CC-4, CC-7.

Owner : Dev.

### Story 6.4 : Robustesse commune des workers GPAO

As an exploitant,
I want que les workers GPAO ne s'arrêtent pas, ne se chevauchent pas et ne
signalent pas « alive » à tort,
So that la supervision du Launcher reflète l'état réel.

**Acceptance Criteria:** `AC-FR25-1` à `AC-FR25-6`.

**Notes dev :**
- `MicroService.Publisher` : garde anti‑ré‑entrance (`deferred-work.md:1253`),
  testée dans `MicroService.Tests` ; vérifier que Laminoir, Video et Zumbach
  compilent et que leurs tests passent.
- `GpaoImportP60` et `GpaoConvertP89` : `deferred-work.md:1305`, `:1309`,
  `:1317`, `:1321`, `:1325`, `:1329`, corrigés à l'identique.
- Commit SVN par l'utilisateur.

**Tests xUnit :** `MicroService.Tests`, `GPAO/ImportP60.Tests`,
`GPAO/ConvertP89.Tests` ; `[Trait("AC", "FR25-…")]`.

**Critères transverses :** CC-1, CC-2, CC-4, CC-7.

Owner : Dev.

### Story 6.5 : Test d'intégration d'un vrai `Client` GPAO

As a développeur,
I want un test qui construit un vrai `Client` de chaque worker GPAO,
So that le câblage d'instance (fréquence, confinement d'erreur, arrêt,
forwarder de log) ne régresse plus en silence.

**Acceptance Criteria:** `AC-FR25-7`.

**Notes dev :**
- Un seul harnais partagé pour P60 et P89 (`deferred-work.md:1313`) ; base
  `AscoLSI_Test` (sink SQL d'`AbstractService`), `[SkippableFact]` si absente.
- Solde aussi le test `Client.Actions` P60 dû depuis l'Épic 3.

**Critères transverses :** CC-1, CC-2, CC-4, CC-7.

Owner : Dev.
```

### 4.7 sprint-status.yaml

Append to `development_status`, after `epic-5-retrospective`:
```
  epic-6: backlog
  6-1-journal-p60-via-ifichierjournal: backlog
  6-2-garde-stabilite-fichiers-p60-p89: backlog
  6-3-export-xml-p60-dossier-dedie: backlog
  6-4-robustesse-commune-workers-gpao: backlog
  6-5-test-integration-client-gpao: backlog
  epic-6-retrospective: optional
```

Epic 5 retro action items, on approval of this proposal (the user's approval is the confirmation):
- `epic-5-retro-item-3-correct-course-shared-worker-debt` → `done` (this proposal).
- `epic-5-retro-item-1-reclose-project-closed-and-context` → `done` via §4.8 (the project is reopened
  for Epic 6 instead of re-closed; the Epic 5 row and `epic-5-context.md:40` are fixed).

Both go through `sprint_status.py update --set-action-status`, not a hand edit.

### 4.8 PROJECT-CLOSED.md and epic-5-context.md

`PROJECT-CLOSED.md`: front matter `reopened_reason: 'Epic 6 — P60 journal migration + shared GPAO
worker hardening (sprint-change-proposal-2026-09-28.md)'`, `reopened_date: '2026-09-28'`,
`previous_reopened_date: '2026-09-25'`. Replace the banner "Reopened 2026-09-25 for Epic 5 … Re-close
at Story 5.1 closure." with:

```
> **Reopened 2026-09-28 for Epic 6** (P60 journal migration + shared GPAO worker hardening) —
> `sprint-change-proposal-2026-09-28.md`. Epic 5 closed 2026-09-28 (retro `epic-5-retro-2026-09-28.md`,
> `accepted-with-open-items`). Re-close at Epic 6 closure.
```

Add an Epic 5 row to the epics table: stories 5.0–5.2 done, `accepted-with-open-items`,
`epic-5-retro-2026-09-28.md`.

`epic-5-context.md:40`, OLD: `5.0 and 5.1 are done, 5.2 is in review.` NEW: `5.0, 5.1 and 5.2 are done.`

### 4.9 deferred-work.md

Prefix each of the ten `source_spec:` lines (entries at 1249, 1253, 1259, 1305, 1309, 1313, 1317,
1321, 1325, 1329) with the tag `**PLANNED 2026-09-28: Story 6.x**`, where x is the story that owns the
entry per §4.6. The entries stay; closure marks them RESOLVED as usual.

## 5. Implementation Handoff

- **Scope:** moderate. One new epic, backlog addition, revised ACs of closed epics. No replan of
  Epics 1–5.
- **Now (this workflow, after approval):** apply §4.1–§4.9 to the documents and commit them
  (`chore(epic-6): correct-course 2026-09-28 — …`), same pattern as `51a93b9`.
- **Next:** `/build-story 6.1` (Dev), then `/run-review`, then `/commit-review`; then 6.2 to 6.5.
- **Success criteria:** `AC-FR24-1..6`, `AC-FR25-1..7`, `AC-FR26-1..6`, `AC-FR16-5`, `AC-FR12-5`, `AC-FR22-9` green; the ten
  `deferred-work.md` entries RESOLVED; `TextToXml.sln` and `MicroServices.sln` build with
  `-warnaserror`; Laminoir, Video and Zumbach tests still green after the `Publisher` change.
