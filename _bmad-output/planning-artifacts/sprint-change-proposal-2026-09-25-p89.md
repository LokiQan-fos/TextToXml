---
date: 2026-09-25
trigger: new-requirement-p89-conversion
mode: batch
scope_classification: moderate
status: approved (2026-09-25), documentation changes applied
---

# Sprint Change Proposal — Epic 5: P89 conversion (raw Fichier → normalized XML + XSD)

## 1. Issue Summary

New requirement from the donneur d'ordre (2026-09-25): convert the P89 Fichiers
(`LP89_682_617_<nnn>`, `nnn` = 001..999 then back to 001) received in `P89/raw` into normalized XML
with its XSD, written to `P89/xml`. The rest of the P89 processing (mapping, persistence, worker) is
explicitly **deferred** ("sera écrit plus tard").

The project is closed (`PROJECT-CLOSED.md`, 2026-09-25; "No Epic 5 is planned"). A first version was
built in a **manual session** on 2026-09-25, outside the story/review loop, uncommitted:

- `Templates/P89.xml` (added by the user) did not match the real Fichiers: every Position from
  `AnomaliePitsFour1` to `LG24` was 5 characters short (`Reserve9` Size 6 is correct, so the anomalies
  start at 130, not 125). Fixed (+5); `LG24` description corrected. With the fix the message Ligne is
  exactly 3442 characters, which every one of the 248 reference Fichiers is.
- `scripts/gen.ps1` gained `-Format` (default `P60`, P60 output byte-identical, `-Check` green) and maps
  `datatype="datetime"` to `xs:date` / `xs:dateTime` + `minOccurs="0"` (D27, D28).
- `Templates/P89.xsd` generated from it.
- `scripts/p89-to-xml.cs` (single-file `dotnet run` app referencing `TextToXml`): 248/248 Fichiers
  converted and XSD-valid.

Evidence found on the way:

- **Encoding.** The P89 Fichiers are **UTF-8** (`é` = `c3 a9`), not Windows-1252. Measured in bytes
  the message Ligne varies from 3442 to 3454; in characters it is always 3442. PRD §5 freezes
  Windows-1252 for "all inbound Fichiers".
- **Rotation.** With `nnn` wrapping 999 → 001, a name-based output (`<name>.xml`) overwrites an older
  conversion. The user decided: timestamped output name + move the converted raw Fichier to `P89/Done`.
- **Testability.** Logic inside a `dotnet run` script cannot be covered by xUnit (CC-1, strict TDD).

## 2. Impact Analysis

### Checklist

| Item | Status | Note |
|---|---|---|
| 1.1 Trigger | [x] | No story; new requirement + manual-session work (uncommitted) |
| 1.2 Problem | [x] | New requirement: a second format, Step 1 only |
| 1.3 Evidence | [x] | 248 real Fichiers, UTF-8 byte dump, legacy `InterfaceManager.SetP89` (P89 is the legacy LSI→GPAO export) |
| 2.1 Current epic | [N/A] | Epics 1–4 done and untouched |
| 2.2 Epic changes | [!] | New Epic 5 + Story 5.1 |
| 2.3–2.5 Future epics | [!] | P89 Step 2 (mapping/persistence/worker) = a later epic, not planned now |
| 3.1 PRD | [!] | New §4.7 / FR-22; D29, D30; §5 encoding non-goal scoped to the library; Annexe E |
| 3.2 Architecture | [N/A] | The spine covers the P60 dispatch only |
| 3.3 UX | [N/A] | No UI |
| 3.4 Other artifacts | [!] | `sprint-status.yaml`, `PROJECT-CLOSED.md` (reopened), `README.md` (Story 5.1 task), `TextToXml.sln` (Story 5.1 task) |
| 4.1 Direct adjustment | Viable | Low effort, low risk |
| 4.2 Rollback | Not viable | The template fix and the XSD generator are correct and needed |
| 4.3 MVP review | N/A | P60 MVP unaffected |

### Technical Impact

- `TextToXml` library: **no change** (AC-FR16-4 holds: Step 1 of a new format = 0 library line).
- New console project `src/P89Converter` (references `TextToXml` only) replaces
  `scripts/p89-to-xml.cs`, so the logic is testable; new `tests/P89Converter.Tests` with a few
  committed P89 fixtures.
- `scripts/gen.ps1` change is kept; P60 artifacts unchanged.
- `epic-5` goes `backlog → in-progress` when Story 5.1 starts; the project is reopened until then.

## 3. Recommended Approach

**Direct adjustment: add Epic 5 with one story (5.1).** The manual-session work is not accepted as-is:
it changes behavior (output naming, move to `Done`) and has no test, so it goes through Story 5.1 →
`build-story` → `run-review` (project rule `manual-session-fix-review`). The template fix, `P89.xsd`
and `gen.ps1 -Format` are carried into the story as its starting point.

Effort: low (one small console project + tests). Risk: low (the library is untouched). Timeline: one
story.

## 4. Detailed Change Proposals

### 4.1 PRD.md — §0bis, new decisions D29, D30

Append after D28:

```
| D29 | **Encodage P89 : UTF-8.** Les Fichiers P89 sont en UTF-8 (mesuré sur 248 Fichiers réels). `TextToXml` reste figée en Windows-1252 (§5, AC-FR16-4) : le format P89 transcode **strictement** UTF-8 → Windows-1252 **avant** `Converter.Convert` (octet UTF-8 invalide ou caractère hors Windows-1252 ⇒ Fichier en échec, jamais de caractère de remplacement). | utilisateur + données réelles (2026-09-25) |
| D30 | **P89 : Étape 1 seule.** Le format P89 est livré jusqu'au XML normalisé validé par `P89.xsd` (Épic 5). Mapping, persistance et worker P89 : **plus tard**, épic non planifié. Le layout réel décale de +5 toutes les Positions à partir de `AnomaliePitsFour1` par rapport au template fourni (`Reserve9` Size 6) ; Ligne Détail = 3442 caractères. | utilisateur (2026-09-25) |
```

D24, OLD: `v1 = **P60 uniquement**.` NEW: `v1 = **P60 uniquement** ; P89 ajouté en Étape 1 seule par l'Épic 5 (D30).`

### 4.2 PRD.md — new §4.7 (after FR-21)

```
### 4.7 Conversion P89 — Étape 1 seule (`P89Converter`)

#### FR-22 : Conversion d'un dossier de Fichiers P89 en XML normalisé

**Description :** l'exécutable console `P89Converter` (`src/P89Converter`, ne
référence que `TextToXml`) traite chaque Fichier `LP89_*` du dossier source
(défaut `P89/raw`) : transcodage strict UTF-8 → Windows-1252 (D29),
`Converter.Convert` avec `Templates/P89.xml`, validation contre
`Templates/P89.xsd`, écriture de `<nom>_<yyyyMMddHHmmss>.xml` dans le dossier
de sortie (défaut `P89/xml`), puis déplacement du Fichier source dans le dossier
des Fichiers traités (défaut `P89/Done`) sous le même suffixe horodaté. Le reste
du traitement P89 est hors périmètre (D30).

**Consequences (testables) :**
- `AC-FR22-1` : les Fichiers P89 de référence (fixtures) se convertissent sans
  Error et leur XML est valide contre `P89.xsd` ; le dernier Champ du bloc
  message (`LG24`, Position 3438, Size 4) finit à 3442 caractères.
- `AC-FR22-2` : `P89.xsd` est à jour de `P89.xml` (`gen.ps1 -Check -Format P89`
  sans écart) ; `gen.ps1 -Check` (P60) reste sans écart.
- `AC-FR22-3` : un Fichier contenant un octet UTF-8 invalide, ou un caractère
  absent de Windows-1252, est en échec avec une raison d'encodage ; aucun
  caractère n'est remplacé.
- `AC-FR22-4` : le XML est écrit sous `<nom>_<yyyyMMddHHmmss>.xml` (horloge
  injectée) ; deux conversions d'un même `<nom>` à des instants différents ne
  s'écrasent pas (rotation 999 → 001).
- `AC-FR22-5` : succès ⇒ le Fichier source quitte le dossier source et se
  retrouve dans le dossier des traités sous `<nom>_<yyyyMMddHHmmss>`.
- `AC-FR22-6` : échec (Error de conversion, XSD, encodage) ⇒ aucun XML écrit,
  le Fichier reste dans le dossier source, chaque raison est affichée sur la
  sortie d'erreur ; les autres Fichiers du dossier sont tout de même traités
  (pas de fail-fast, §5) ; code retour 1 dès qu'un Fichier échoue, 0 sinon.
- `AC-FR22-7` : `TextToXml` n'est pas modifiée (AC-FR16-4 inchangé).
```

### 4.3 PRD.md — §5 Non-Goals (encoding)

OLD:
```
- Pas de **détection d'encodage** ni d'option d'encodage : tous les fichiers
  entrants sont `Windows-1252` (figé).
```
NEW:
```
- Pas de **détection d'encodage** ni d'option d'encodage **dans `TextToXml`** :
  la bibliothèque lit du `Windows-1252` (figé). Un format dont les Fichiers sont
  dans un autre encodage connu transcode avant l'appel (P89 : UTF-8, D29).
```

### 4.4 PRD.md — Annexe E

Append:
```
- **`P89`** (`Fixed`, header/message/footer, 12 anomalies de 260 caractères,
  24 paires `PS`/`LG`) → **traité en Étape 1** par l'Épic 5 (FR-22, D29, D30).
  Fichiers UTF-8 ; layout corrigé (+5 à partir de `AnomaliePitsFour1`).
```

Front matter: `updated: 2026-09-25`.

### 4.5 epics.md — Epic List entry + Epic 5 section

Epic List, append after Épic 4:
```
### Épic 5 : P89 — conversion Fichier → XML normalisé + XSD (Étape 1)
Convertir les Fichiers P89 (`LP89_682_617_<nnn>`) en XML normalisé validé par
`P89.xsd`, avec la bibliothèque `TextToXml` inchangée. À l'issue de l'épic :
`P89Converter` vide `P89/raw` vers `P89/xml` (sortie horodatée) et `P89/Done`,
`AC-FR22-1..7` sont verts. Mapping / persistance / worker P89 : plus tard.
**FRs couverts :** FR-22.
```

Epic 5 section + Story 5.1, appended at the end of `epics.md`:
```
## Épic 5 : P89 — conversion Fichier → XML normalisé + XSD (Étape 1)

Livrer l'Étape 1 du format P89 (D30) : descripteur `Templates/P89.xml` corrigé,
`Templates/P89.xsd` généré, exécutable `P89Converter`. `TextToXml` n'est pas
modifiée (AC-FR16-4). Fichiers UTF-8 transcodés avant conversion (D29).

**FRs couverts :** FR-22.

### Story 5.1 : `P89Converter` — dossier P89 brut → XML normalisé horodaté

As an exploitant,
I want convertir tous les Fichiers `LP89_*` de `P89/raw` en XML normalisé
validé par `P89.xsd` dans `P89/xml`, le Fichier converti étant rangé dans
`P89/Done`,
So that les P89 sont lisibles et validés dès maintenant, en attendant le reste
du traitement P89, sans qu'une rotation d'index 999 → 001 n'écrase une
conversion antérieure.

**Origine :** nouveau besoin du 2026-09-25 ;
sprint-change-proposal-2026-09-25-p89.md.

**Point de départ (session manuelle 2026-09-25, non commité) :**
`Templates/P89.xml` corrigé (+5, `LG24`), `Templates/P89.xsd`,
`scripts/gen.ps1 -Format`, `scripts/p89-to-xml.cs` (prototype, 248/248
Fichiers valides) — à remplacer par `src/P89Converter`.

**Acceptance Criteria:** `AC-FR22-1` à `AC-FR22-7` (PRD §4.7).

**Notes dev :**
- `src/P89Converter` : console `net10.0`, `ProjectReference` sur `TextToXml`
  seulement, descripteur et XSD lus depuis `Templates/` (ou embarqués — au
  choix, justifier). Arguments : `[raw] [xml] [done]`, défauts `P89/raw`,
  `P89/xml`, `P89/Done`. Horloge = `TimeProvider` (heure locale) pour le
  suffixe `yyyyMMddHHmmss`.
- Déplacement vers `Done` **après** l'écriture du XML (un crash entre les deux
  laisse le Fichier dans `raw`, reconverti au lancement suivant).
- Supprimer `scripts/p89-to-xml.cs` ; ajouter les deux projets à
  `TextToXml.sln` ; ajouter une section P89 au `README.md`.
- Fixtures : 3 Fichiers réels minimum dans `tests/P89Converter.Tests/fixtures/`
  (dont un avec accents, ex. `LP89_682_617_013`) + variantes fautives
  (UTF-8 invalide, caractère hors Windows-1252, Ligne tronquée).
- Test XSD ↔ descripteur (même principe que `P60XsdTests`) plutôt qu'un appel à
  `gen.ps1` depuis les tests.

**Tests xUnit (TDD, CC-1) :** `tests/P89Converter.Tests`, `Category=Unit`
(dossiers temporaires, aucune base), `[Trait("AC", "AC-FR22-…")]`.

**Critères transverses :** CC-1, CC-2, CC-4.

Owner : Dev.
```

### 4.6 sprint-status.yaml

Append to `development_status`:
```
  epic-5: backlog
  5-1-p89converter-dossier-p89-brut-xml-normalise-horodate: backlog
  epic-5-retrospective: optional
```
(`5-1` moves to `ready-for-dev` when `build-story` creates its spec.)

### 4.7 PROJECT-CLOSED.md

Front matter `status: 'reopened'`, `reopened_date: '2026-09-25'`,
`reopened_reason: 'new requirement — Epic 5 P89 conversion (sprint-change-proposal-2026-09-25-p89.md)'`,
and a banner under the title: "Reopened 2026-09-25 for Epic 5 (P89, Step 1 only)." Re-close at Story 5.1
closure.

## 5. Implementation Handoff

- **Scope:** moderate (new epic, backlog addition; no replan of Epics 1–4).
- **Now (this workflow, after approval):** apply §4.1–§4.7 to the documents.
- **Next:** `/build-story` on Story 5.1 (Dev), then `/run-review`, then `/commit-review`.
- **Success criteria:** `AC-FR22-1..7` green under `Category=Unit`; `gen.ps1 -Check` clean for P60 and
  P89; running `P89Converter` on the 248 real Fichiers empties `P89/raw` into `P89/xml` + `P89/Done`
  with exit code 0; `scripts/p89-to-xml.cs` removed.
