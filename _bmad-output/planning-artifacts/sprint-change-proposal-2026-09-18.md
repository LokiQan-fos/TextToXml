---
date: 2026-09-18
trigger: epic-4-retrospective-2
mode: batch
scope_classification: moderate
---

# Sprint Change Proposal — Corrections post-rétrospective Épic 4 (rétro #2)

## 1. Issue Summary

La deuxième rétrospective Épic 4
(`_bmad-output/implementation-artifacts/epic-4-retro-2026-09-18.md`, verdict
**accepted-with-open-items**) a audité le statut réel des 5 items ouverts par
la revue de code de Story 4.3-bis (2026-09-17/18) et en a trouvé 4 **mal
routés** — tracés dans `sprint-status.yaml` avec un `ref` pointant vers
`story 4-9-hardening-epic-4`, alors que Story 4.9 (livrée, AC figées sur A-2,
A-3, A-4, A-5 uniquement) ne les a jamais couverts — plus 1 découverte
**jamais tracée** du tout dans `sprint-status.yaml`.

1. **B-1** (`story-4-3-bis-review-item-1`, mal routé) — les 21 littéraux
   `DecimalScale.Apply(source.X, N)` codés en dur dans 5 mappers
   (`OrdreFabricationMapper.cs`, `SectionChargeChutageMapper.cs`,
   `SectionChargeDecoupeMapper.cs`, `SectionChargeLingotMapper.cs`,
   `SectionChargePitsMapper.cs`) ne sont jamais comparés à la colonne `Scale`
   de l'annexe de mapping (Story 4.2-bis) — un chiffre copié-collé de travers,
   cohérent entre un mapper et son test, passerait silencieusement.
2. **B-2** (`story-4-3-bis-review-item-2`, mal routé) — rien n'empêche un futur
   6e mapper d'assigner un `int`/`int?` KAPE22 brut directement à une colonne
   EF `decimal` étroite sans passer par `DecimalScale.Apply`, reproduisant le
   défaut corrigé par Story 4.3-bis.
3. **B-3** (`story-4-3-bis-review-item-3`, mal routé) —
   `Kape22ProductionDataParityTests` ne round-trip que `L_D_KAPE22` ; aucun
   test ne compare une colonne aval `decimal` mise à l'échelle
   (`L_D_ORDRE_FABRICATION`, `L_D_SECTIONCHARGE_*`) à une valeur de production
   réelle connue.
4. **B-4** (`story-4-3-bis-review-item-4`, mal routé) — l'invocation finale
   `dotnet test ... Kape22ProductionDataParityTests` de
   `scripts/e2e-worker-import.ps1` ne vérifie jamais `$LASTEXITCODE`,
   contrairement à l'étape `dotnet build` antérieure du même script (ligne
   105-106) ; un échec de parité production ne ferait pas échouer le script
   englobant.
5. **B-5** (`epic-4-retro2-item-b5-decimalscale-magnitude-guard`, nouveau) —
   `DecimalScale.Apply` corrige le placement décimal (`scale`) mais ne valide
   jamais le résultat mis à l'échelle contre la précision totale
   `DECIMAL(p,s)` de la colonne cible ; un entier KAPE22 brut hors gabarit peut
   encore lever un débordement SQL brut non diagnostiqué — même classe de
   défaut que Story 4.3-bis a fixée, réduite mais pas fermée.

Aucun de ces 5 items n'est un échec d'AC déclaré : ce sont, comme pour la
rétro #1, des découvertes de revue transversale post-clôture, explicitement
mises hors périmètre de Story 4.3-bis (« Ask First: None ») et de Story 4.9
(4 AC figées sur A-2..A-5 uniquement).

## 2. Impact Analysis

### Epic Impact

Épic 4 (`epics.md`, `done` dans `sprint-status.yaml`) reste le bon périmètre —
même conclusion qu'à la rétro #1 : aucun de ces 5 items n'introduit de nouveau
FR ni de nouvelle table, ce sont des garde-fous de robustesse sur du code déjà
livré (FR-17..FR-21). **Décision : rester sur Épic 4** (repasse
`in-progress`), pas de nouvel Épic 5.

### Story Impact

**1 nouvelle story : 4.10**, regroupant B-1..B-5 — décision utilisateur
(pas 5 stories séparées, même rationale que Story 4.9 pour A-2/A-4/A-5 : un
seul cycle de revue plutôt que 5 micro-PRs). Aucune dépendance de données
entre B-1..B-5 ni envers les stories déjà livrées (4.2-bis/4.3-bis/4.9) :
contrairement à la séquence stricte imposée à la rétro #1, **aucun ordre
n'est imposé** entre les 5 AC de Story 4.10.

### Artifact Conflicts

- **PRD** : aucun changement. FR-17..FR-21 inchangés.
- **epics.md** : nouvelle sous-section **« Corrections post-rétrospective
  Épic 4 (rétro #2) »**, ajoutée en fin de fichier après Story 4.9.
- **ARCHITECTURE-SPINE.md** : aucun nouvel AD attendu pour B-1..B-4. B-5 est
  potentiellement une extension d'AD-4 (diagnostic avant échec brut) mais pas
  un nouvel invariant — son emplacement exact (annexe étendue vs lecture
  directe du schéma SQL) est une décision de conception tranchée au spec figé
  de `bmad-build`, pas ici.
- **`annexe-mapping-dispatch-epic4.md`** : possiblement étendu par B-5 (champ
  `Precision`) — décision différée au spec.
- **`sprint-status.yaml`** : les 5 `action_items` existants sont **corrigés
  en place** (`ref` réécrit vers `story 4-10-hardening-epic-4-b`) — aucune
  duplication ; `epic-4` repasse `in-progress` ; nouvelle clé
  `4-10-hardening-epic-4-b: backlog`.
- **UX** : sans objet.

### Technical Impact

- `DecimalScale.cs` est le seul fichier de production potentiellement modifié
  (B-5 uniquement, si le spec retient une validation dans `Apply` lui-même
  plutôt qu'un contrôle pré-persistance côté `Kape22Persister`) — décision
  différée au spec figé.
- B-1..B-4 sont purement additifs côté tests/scripts, aucun changement de
  comportement du chemin `FichierProcessor → BundleMapper → Persister`.
- Aucun changement de `MicroServices.sln` (SVN).

## 3. Recommended Approach

**Option 1 — Ajustement direct** (checklist §4) : ajout d'1 story dans le
périmètre Épic 4 existant. Effort **Low-Medium**, risque **Low** — B-1..B-4
sont des ajouts de tests/scripts sans risque de régression ; B-5 est le seul
item touchant potentiellement du code de production, et son AC renvoie
explicitement la décision de conception au spec figé plutôt que de la figer
ici (cf. note du propriétaire à la rétro : « peut nécessiter un choix de
conception sur l'emplacement du garde-fou »).

**Aucun ordre d'exécution imposé** entre B-1..B-5 (contrairement à
4.2-bis → 4.3-bis → 4.9) : chacun est indépendant des autres et des stories
déjà livrées. Regroupés en une seule story pour un seul cycle de revue de
code, pas pour une raison de séquencement.

## 4. Detailed Change Proposals

### Story 4.10 — Hardening Épic 4 (B-1..B-5)

```
As a `Kape22Importer`,
I want fermer les 5 items de hardening restants identifiés par la rétro Épic 4
#2 autour de `DecimalScale.Apply` et de la couverture de parité production,
So that la mise à l'échelle décimale (Story 4.3-bis) reste garantie dans le
temps par des garde-fous automatiques plutôt que par vigilance manuelle.

**Prérequis :** aucun — B-1..B-5 sont indépendants entre eux et des stories
4.2-bis/4.3-bis/4.9 déjà livrées ; regroupés en une seule story pour un seul
cycle de revue de code (même rationale que Story 4.9 pour A-2/A-4/A-5).

**Acceptance Criteria:**

Given les 21 appels `DecimalScale.Apply(source.X, N)` codés en dur dans les 5
  mappers (`OrdreFabricationMapper.cs`, `SectionChargeChutageMapper.cs`,
  `SectionChargeDecoupeMapper.cs`, `SectionChargeLingotMapper.cs`,
  `SectionChargePitsMapper.cs`) et la colonne `Scale` de l'annexe de mapping
  (`MappingAnnexEntry.Scale`, Story 4.2-bis)
When un nouveau contrôle de complétude s'exécute (extension ou sibling de
  `MappingAnnexCompleteness.Check`, `MappingAnnexSchema.cs`)
Then il échoue si le littéral `N` passé par un mapper à `DecimalScale.Apply`
  diverge de la valeur `Scale` de l'annexe pour la même colonne — l'existant
  `MappingAnnexCompletenessTests` (famille AC-FR17-5) ne vérifie aujourd'hui
  que la présence d'un `Scale` dans l'annexe, jamais sa conformité au code
  mappeur réellement livré (B-1)

Given `DecimalScale.Apply(int rawValue, int scale)` /
  `Apply(int? rawValue, int scale)` (`DecimalScale.cs`), seul chemin
  sanctionné d'un `int`/`int?` KAPE22 brut vers une colonne `decimal` EF
When un test de complétude réflectif (ou dérivé de l'annexe) s'exécute
Then il échoue si une propriété EF `decimal` cible référencée par l'annexe et
  sourcée d'un `int`/`int?` est assignée sans passer par `DecimalScale.Apply`
  — garde-fou contre un futur 6e mapper reproduisant le défaut fixé par
  Story 4.3-bis (B-2)

Given `Kape22ProductionDataParityTests` (`RoundTripThroughTestDatabase`), qui
  ne round-trip aujourd'hui que `L_D_KAPE22`
When la suite est étendue
Then elle round-trip également `L_D_ORDRE_FABRICATION` et les tables
  `L_D_SECTIONCHARGE_*` mises à l'échelle par `DecimalScale`, chaque colonne
  scaled persistée étant comparée à la valeur de production réelle lue via
  `ReadProductionRows` (connexion `AscoLSI_Production`) — pas de fixture
  statique de substitution (B-3)

Given `scripts/e2e-worker-import.ps1`, dont l'étape `dotnet build` (lignes
  105-106) vérifie déjà `$LASTEXITCODE` et lève si non nul
When la même exécution atteint l'invocation finale
  `dotnet ... Kape22ProductionDataParityTests` (boucle
  `$SkipProductionCompare`, ligne 188)
Then cette invocation est gardée de la même façon — un `$LASTEXITCODE` non nul
  fait échouer le script englobant au lieu de continuer silencieusement (B-4)

Given `DecimalScale.Apply`, qui corrige le placement décimal (`scale`) mais ne
  connaît la précision totale `p` d'aucune colonne cible `DECIMAL(p,s)` — `p`
  n'est aujourd'hui capturé nulle part, ni dans l'annexe (`MappingAnnexEntry`
  ne porte que `Scale`) ni ailleurs
When un entier KAPE22 brut hors gabarit, une fois mis à l'échelle, dépasse
  encore la magnitude de sa colonne cible
Then un garde-fou pré-persistance détecte le dépassement et produit un
  `ConversionError` diagnostiqué (circuit AD-4 existant) au lieu de laisser
  remonter un débordement SQL brut non diagnostiqué — l'emplacement du
  garde-fou (nouvelle donnée `Precision` dans l'annexe vs. lecture directe de
  `scripts/schema/01-ascolsi-tables.sql`) est une décision de conception
  tranchée au spec figé de `bmad-build` (step-02), pas dans cette AC (B-5)

**Tests xUnit (TDD — écrits en premier, CC-1) :** un test par AC (B-1 :
extension `MappingAnnexCompletenessTests` cas divergence mapper/annexe ; B-2 :
cas bypass simulé détecté ; B-3 : au moins une colonne scaled par table aval
concernée comparée à une valeur de production réelle ; B-4 : vérification que
le `$LASTEXITCODE` de l'étape est gardé ; B-5 : un cas hors gabarit →
`ConversionError`, zéro exception SQL non catchée).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5.

Owner : Dev (B-5 : Dev / Lead Architecte pour le choix d'emplacement du
garde-fou).
```

### epics.md — emplacement

Nouvelle sous-section **« Corrections post-rétrospective Épic 4 (rétro #2) »**
ajoutée en fin de fichier, après la Story 4.9 (fin de la sous-section
« Corrections post-rétrospective Épic 4 » issue de la rétro #1) — ne
renumérote rien, ne rouvre aucune story existante.

### sprint-status.yaml

- `epic-4: done` → `epic-4: in-progress`
- Nouvelle clé `4-10-hardening-epic-4-b: backlog` (même statut initial que
  `4-9-hardening-epic-4` à sa création, commit `d559f85`)
- `ref` des 4 `action_items` déjà existants
  (`story-4-3-bis-review-item-1..4`) réécrit de `story 4-9-hardening-epic-4`
  vers `story 4-10-hardening-epic-4-b` — correction de trajectoire, pas de
  duplication d'entrée
- `ref` de `epic-4-retro2-item-b5-decimalscale-magnitude-guard` complété avec
  `-> story 4-10-hardening-epic-4-b`

## 5. Implementation Handoff

**Classification : Moderate** — réorganisation du backlog (1 story ajoutée à
un épic `done`, `sprint-status.yaml` à rouvrir `in-progress`, 4 `ref`
corrigés) + implémentation de code standard ; B-5 peut impliquer un petit
choix de conception (emplacement du garde-fou de magnitude) mais pas de
nouvel AD ni de replan stratégique.

| Étape | Responsable | Livrable |
|---|---|---|
| Ajouter Story 4.10 à `epics.md` | PM (ce document) | Sous-section « rétro #2 » ajoutée |
| Repasser `epic-4: in-progress`, ajouter `4-10-hardening-epic-4-b: backlog`, corriger les 4 `ref` B-1..B-4 | SM (ce document) | `sprint-status.yaml` à jour |
| Implémenter Story 4.10 en TDD (`bmad-build`, route `plan-code-review` imposée) | Dev | 1 PR, tests `Category=Unit`/`Integration` verts |
| Revue de code | Dev + revue | rapport dans `_bmad-output/implementation-artifacts/reviews/` |
| Clore B-1..B-5 à `done` au merge | SM | `sprint-status.yaml` à jour |
| Rebasculer `epic-4: done` une fois 4.10 mergée | SM | `sprint-status.yaml` à jour |

**Critères de succès :**
- `dotnet build TextToXml.sln -warnaserror` : 0 warning/erreur.
- `dotnet test --filter Category=Unit` : 100 % vert, y compris les nouveaux
  contrôles de complétude B-1/B-2.
- `dotnet test --filter Category=Integration` : 100 % vert, `Kape22ProductionDataParityTests`
  couvre désormais les tables aval decimal-scaled (B-3).
- `scripts/e2e-worker-import.ps1` échoue si `Kape22ProductionDataParityTests`
  échoue (B-4).
- Les 5 `action_items` B-1..B-5 de `sprint-status.yaml` passent `done`.
