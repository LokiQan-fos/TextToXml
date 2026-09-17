---
date: 2026-09-17
trigger: epic-4-retrospective
mode: incremental
scope_classification: moderate
---

# Sprint Change Proposal — Corrections post-rétrospective Épic 4

## 1. Issue Summary

La rétrospective Épic 4 (`_bmad-output/implementation-artifacts/epic-4-retro-2026-09-17.md`,
commit `bb46d8b`) a rendu le verdict **accepted-with-open-items** : les 7/7 stories
d'Épic 4 (FR-17..FR-21, dispatch transactionnel vers les 10 tables aval) satisfont
leurs propres critères d'acceptation déclarés, mais 6 items restent ouverts avant
qu'Épic 4 soit **production-ready** :

1. `story-4-6-decimal-scale-defect-story-4-3-bis` (déjà tracé, priorité de
   production) — 5 mappers écrivent des entiers KAPE22 bruts non mis à l'échelle
   dans des colonnes `DECIMAL` étroites ; bloque l'import de tout Fichier P60 réel
   dont les sections `SectionCharge` applicables portent une valeur hors échelle.
2. **A-1** (`epic-4-retro-item-1`) — l'annexe de mapping (Story 4.2) n'a pas de
   champ scale/precision ; c'est la cause racine structurelle du défaut ci-dessus,
   découvert indépendamment dans 5 mappers faute de garde-fou dans le format
   d'annexe lui-même.
3. **A-2** (`epic-4-retro-item-2`) — les tests `Category=Unit` n'assertent jamais
   les 9 tables aval sur le chemin succès ; seuls des `[SkippableFact]` Integration
   le font, et ils s'ignorent proprement sans SQL Server.
4. **A-3** (`epic-4-retro-item-3`) — `Kape22Mapper.Map` (code Épic 2, jamais
   retouché par Épic 4) ne trim pas `entity.OF`/`entity.Coulee` ; chaque
   consommateur aval trim (ou pas) à son propre site de lecture, avec un risque
   avéré de désynchronisation `IdCoulee` (edge-case lens, PK-violation possible).
5. **A-4** (`epic-4-retro-item-4`) — le marqueur hot/cold Coulee
   (`CodeConsignePits == "1"`) est dupliqué : une constante nommée dans
   `Kape22Persister`, un littéral inline dans `Kape22ImportBundleMapper`.
6. **A-5** (`epic-4-retro-item-5`) — une collision `CodeOperation` dans
   `L_D_CONSIGNES` (risque déjà documenté, non confirmé) ferait lever une
   `InvalidOperationException` EF Core côté client, hors du `catch` du Persister
   (`DbUpdateException or DbException`) — violation d'AD-4 sur ce seul chemin.

Aucun de ces items n'est un échec d'AC déclaré : ce sont des découvertes de la
revue transversale post-clôture, tracées dans `sprint-status.yaml` (`action_items`,
tous `status: open`) et `deferred-work.md`.

## 2. Impact Analysis

### Epic Impact

Épic 4 (`epics.md`, `done` dans `sprint-status.yaml`) reste le bon périmètre :
aucun de ces 6 items n'introduit de nouveau FR ni de nouvelle table — ce sont des
corrections de conformité aux `AC-FR17-x`..`AC-FR21-x` déjà déclarés, ou du
hardening du chemin `FichierProcessor → BundleMapper → Persister` déjà livré.
**Décision : rester sur Épic 4** (repasse `in-progress`), pas de nouvel Épic 5.
Aucun épic futur n'existe encore au PRD à impacter.

### Story Impact

3 nouvelles stories, séquencées **strictement en série** (voir §3) :
`4.2-bis` → `4.3-bis` → `4.9`. Aucune story existante (4.1..4.7) n'est modifiée
en rétroactif — les 3 nouvelles stories les complètent sans les rouvrir.

### Artifact Conflicts

- **PRD** : aucun changement de fond. FR-17..FR-21 restent inchangés ; seul
  `AC-FR17-5` (test de complétude de l'annexe) gagne une branche.
- **epics.md** : ajout d'une sous-section **« Corrections post-rétrospective
  Épic 4 »** après la Story 4.7, pour ne pas renuméroter/casser la séquence
  affichée `4.1 → 4.2 → {4.3, 4.4} → 4.5 → 4.6 → 4.7`.
- **ARCHITECTURE-SPINE.md** : aucun nouvel AD. A-5 est une implémentation plus
  stricte d'AD-4 existant, pas un nouvel invariant.
- **`annexe-mapping-dispatch-epic4.md`** : étendu par 4.2-bis (nouveau champ
  `scale`).
- **`deferred-work.md` / `sprint-status.yaml`** : les entrées existantes sont
  **liées** aux 3 nouvelles stories (pas dupliquées), passeront `in-progress`
  puis `done` au fil de l'implémentation.
- **UX** : sans objet (v1 sans UI).

### Technical Impact

- `TestSupport.ZeroOutOfScaleDimensions` / `InsertableFichier` (contournement de
  test introduit par la Story 4.6) est **retiré** une fois 4.3-bis livrée — les
  suites d'intégration reviennent aux fixtures réelles non mutées.
- `GpaoImportP60WorkerEndToEndTests` (actuellement skip documenté) doit repasser
  vert après 4.3-bis, sur les fixtures réelles `P60_847_682_081/082`.
- Aucun changement de `MicroServices.sln` (SVN) — les 3 stories restent dans
  `TextToXml.sln`.

## 3. Recommended Approach

**Option 1 — Ajustement direct** (checklist §4) : ajout de 3 stories dans le
périmètre Épic 4 existant. Effort **Medium**, risque **Low** — dépôt stable
(662 tests `Category=Unit` verts, 136/137 `Integration`), aucune story
n'implique `MicroServices.sln`.

**Ordre d'exécution imposé — séquence linéaire, pas de parallélisation :**

```
4.2-bis  →  4.3-bis  →  4.9
(annexe)    (fix décimal)  (hardening A-2/A-3/A-4/A-5)
```

- **4.2-bis avant 4.3-bis** : l'annexe étendue sert de garde-fou de complétude —
  4.3-bis doit pouvoir s'appuyer dessus pour prouver qu'aucune autre colonne
  `decimal←int` que les 5 déjà connues n'a le même défaut, plutôt que corriger 5
  colonnes et en re-découvrir d'autres plus tard (rétro, Q-1).
- **4.3-bis avant 4.9** : A-3 (trim `OF`/`Coulee` à la source dans
  `Kape22Mapper.Map`) modifie exactement les valeurs que les fixtures et
  assertions de 4.3-bis manipulent. Faire les deux en parallèle imposerait de
  réécrire les tests de 4.3-bis une seconde fois une fois A-3 livrée — un double
  travail sans bénéfice, la dépendance de données étant directe, pas seulement de
  risque.
- A-2/A-4/A-5 (regroupés dans 4.9) n'ont pas de dépendance technique entre eux
  ni envers 4.2-bis/4.3-bis, mais restent groupés en fin de séquence pour garder
  un seul cycle de revue de code sur le hardening, plutôt que 4 micro-PRs.

## 4. Detailed Change Proposals

### Story 4.2-bis — Extension de l'annexe de mapping (champ `scale`)

```
As a développeur de Kape22Importer,
I want que l'annexe _bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md
enregistre, pour toute colonne cible `decimal` sourcée d'un champ KAPE22 `int`/`int?`,
son échelle sous un champ nommé `scale` (valeur = entier, nombre de décimales à
appliquer, ex. `1` pour DECIMAL(2,1)) — et qu'un test de complétude échoue si ce
champ manque, que la colonne soit déjà connue du défaut ou nouvellement découverte,
So that aucun mapper, présent ou futur, ne puisse reproduire le défaut de mise à
l'échelle découvert indépendamment dans 5 mappers (story-4-6-decimal-scale-defect-story-4-3-bis).

**Acceptance Criteria:**

Given une colonne `sourcée` de type cible `decimal` dont la source KAPE22 est `int`/`int?`
When l'annexe est étendue
Then elle porte un champ `scale` (entier, nombre de décimales à appliquer) explicite
  pour cette colonne — format figé : nom de champ `scale`, valeur entière, pas
  d'unité ni de facteur multiplicatif texte libre (AC-FR17-1 étendu)

Given le modèle EF des 10 tables aval (Story 4.1) et l'annexe étendue
When MappingAnnexCompletenessTests (famille AC-FR17-5) s'exécute
Then il échoue si une colonne de type CLR `decimal` sourcée d'un `int`/`int?`
  KAPE22 n'a pas de champ `scale` renseigné dans l'annexe — sans distinction entre
  une colonne déjà identifiée par la rétro (les 5 mappers connus) et une colonne
  nouvellement détectée par le test lui-même (fusion des deux AC précédemment
  distinctes en une seule assertion de complétude)
And une colonne `decimal` dont la source n'est pas `int`/`int?` (ex. déjà `decimal`
  côté KAPE22) n'est pas soumise à cette règle

Given l'annexe étendue
When elle sert de référence à la Story 4.3-bis
Then elle couvre explicitement, avec leur `scale`, les 12 colonnes déjà connues
  (OrdreFabricationMapper : DiametreProduit, 6×Tolerance*, LongueurCD,
  PoidsDemiProduitUnitaire, PoidsPrevuDemiProduit ; SectionChargeLingotMapper :
  SectionLaminage, EpaisseurEnLaminage, 4×Tolerance*1 ; SectionChargeChutageMapper :
  ChutageTete, ChutagePied ; SectionChargeDecoupeMapper : LongueurMoyenne ;
  SectionChargePitsMapper : H2Coulee)
And elle note explicitement, par une ligne dédiée par table, que
  `L_D_SECTIONCHARGE_REFROIDISSOIRS`, `L_D_SECTIONCHARGE_POIDSMETRIQUE` et
  `L_D_SECTIONCHARGE_SVT` ne portent **aucune** colonne `decimal` (vérifié par
  lecture directe des 3 entités EF, `src/Kape22Importer/Persistence/L_D_SECTIONCHARGE_{REFROIDISSOIRS,POIDSMETRIQUE,SVT}.cs`)
  — hors périmètre de 4.3-bis, pas un trou de l'annexe

**Tests xUnit (TDD) :** extension de `MappingAnnexCompletenessTests.cs`
(`Category=Unit`, même famille qu'`AC-FR17-5`, exempté du rouge→vert propre
comme test-barrière-à-la-compilation) : un cas colonne `decimal←int` avec
`scale` présent (succès), un cas sans `scale` (échec), un cas colonne `decimal`
non sourcée d'un `int` (pas de règle, succès).

**Critères transverses :** CC-1 (test de complétude), CC-2, CC-5.
*(CC-3/CC-4/CC-6/CC-7 sans objet : pas de code de production.)*

Owner : Lead Architecte / PM (décision de format d'annexe).
```

### Story 4.3-bis — Correctif de mise à l'échelle décimale

```
As a Kape22Importer,
I want que OrdreFabricationMapper, SectionChargeLingotMapper, SectionChargeChutageMapper,
SectionChargeDecoupeMapper et SectionChargePitsMapper appliquent la mise à l'échelle
documentée par le champ `scale` de l'annexe étendue (Story 4.2-bis) au lieu
d'écrire l'entier KAPE22 brut,
So that tout Fichier P60 réel dont les sections SectionCharge applicables portent
une valeur dans l'échelle attendue s'insère sans dépassement SQL.

**Prérequis :** Story 4.2-bis livrée — l'annexe étendue et son test de
complétude sont la référence unique du facteur d'échelle par colonne.

**Acceptance Criteria:**

Given l'annexe étendue (Story 4.2-bis) portant le `scale` par colonne
When chacun des 5 mappers ci-dessus est corrigé
Then la valeur écrite dans la colonne DECIMAL cible respecte l'échelle documentée
  (ex. DECIMAL(2,1) max 9.9 ne déborde plus pour un entier KAPE22 dans la plage
  réelle observée sur les fixtures P60/)

Given SectionChargeRefroidissoirsMapper, SectionChargePoidsMetriqueMapper et
  SectionChargeSvtMapper
When le périmètre de cette story est vérifié
Then ils sont explicitement **hors périmètre** — confirmé par 4.2-bis (aucune
  colonne `decimal` dans leurs 3 tables cibles) — pas de mise à l'échelle à coder

Given les suites d'intégration qui contournaient le défaut
  (`Kape22FichierProcessorIntegrationTests`, `DoubleJournalIntegrationTests`,
  `EndToEndImportIntegrationTests`, `WorkerLoopRobustnessIntegrationTests` —
  toutes patchées Story 4.6 via `TestSupport.ZeroOutOfScaleDimensions`/`InsertableFichier`)
When le correctif est en place
Then le contournement `TestSupport` est **retiré**, ces suites repassent sur les
  fixtures réelles `P60/` non mutées, et restent vertes

Given `GpaoImportP60WorkerEndToEndTests` (actuellement skip documenté, fixtures
  partagées byte-for-byte avec `Kape22ProductionDataParityTests`)
When le correctif est en place
Then ce test cesse d'être skip et passe vert sur les fixtures réelles
  `P60_847_682_081/082`

**Tests xUnit (TDD) :** un test par colonne mise à l'échelle (fixtures `P60/`
existantes, valeur brute connue → valeur decimal attendue), + les 4 suites
d'intégration ci-dessus repassées sans contournement, + `GpaoImportP60WorkerEndToEndTests`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5.

Owner : Dev.
```

### Story 4.9 — Hardening Épic 4 (A-2, A-3, A-4, A-5)

```
As a Kape22Importer,
I want fermer les 4 gaps de robustesse identifiés par la rétro Épic 4 sur le
chemin FichierProcessor → BundleMapper → Persister,
So that l'épic ne laisse aucune régression silencieuse ni cause d'échec non
journalisée derrière lui.

**Prérequis :** Story 4.3-bis livrée (séquence linéaire imposée, §3 — pas de
parallélisation avec A-3, qui modifie la source des valeurs que 4.3-bis teste).

**AC-A2 (verification-gap, Kape22FichierProcessorTests) :**
Given `Import_CleanFichier_RunsThroughToTheInsert_AcFr13_1` et
  `Import_Success_ResultShape_AcFr13_5` (`Category=Unit`, in-memory `AscoLsiDbContext`)
When un import réussit
Then les 10 `DbSet` aval sont assertés en plus de `Kape22Rows`/`LogCommandeRows` :
  `OrdreFabricationRows`, `CouleeRows`, `ConsignesRows`,
  `SectionChargeChutageRows`, `SectionChargeDecoupeRows`, `SectionChargeLingotRows`,
  `SectionChargePitsRows`, `SectionChargePoidsMetriqueRows`,
  `SectionChargeRefroidissoirsRows`, `SectionChargeSvtRows`
  (liste exhaustive, `src/Kape22Importer/Persistence/AscoLsiDbContext.cs`)

**AC-A3 (root-cause, code Épic 2) :**
Given `Kape22Mapper.Map` et sa boucle réflective par Champ
When elle copie `OF` et `Coulee` sur `entity`
Then `entity.OF` et `entity.Coulee` sont trim **une seule fois, à la source**,
  dans cette boucle (ou immédiatement après) — chaque consommateur aval (9
  mappers + `Kape22Persister`) cesse de trim/ne-pas-trim à son propre site de
  lecture ; `CouleeMapper.IdCoulee` en particulier n'a plus besoin de sa propre
  garde, le risque de désynchronisation avec `Kape22Persister.couleeAlreadyExists`
  disparaît structurellement

**AC-A4 (reformulé — extraction, pas création) :**
Given `Kape22Persister.cs:36` qui possède déjà `private const string
  ColdConsignePits = "1";` et `Kape22ImportBundleMapper.cs:113` qui porte le
  littéral inline `"1"` pour la même règle métier
When le marqueur hot/cold Coulee est consulté par les deux collaborateurs
Then `ColdConsignePits` est **extrait vers une source partagée** que les deux
  référencent (ex. `Kape22ImportBundle` ou un petit type déjà référencé par les
  deux) — il ne s'agit pas de créer une nouvelle constante, mais de faire cesser
  la duplication de celle qui existe déjà côté `Kape22Persister`

**AC-A5 (AD-4, décidé — pré-vérification, pas de catch élargi) :**
Given le risque déjà documenté (`ConsignesMapper.cs:82-88`) qu'une collision sur
  la clé naturelle `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` de
  `L_D_CONSIGNES` survienne au sein d'un même bundle
When `Kape22Persister.PersistMapped` prépare `context.ConsignesRows.AddRange(bundle.Consignes)`
Then une **pré-vérification de la clé naturelle** s'exécute avant l'`AddRange` :
  toute collision détectée produit un `ConversionError` + une ligne
  `L_D_LOG_COMMANDE` REJETÉ via le circuit AD-4 existant, **sans jamais appeler**
  `AddRange` sur les entrées en collision
And le filtre `catch (... when (exception is DbUpdateException or DbException))`
  de `Kape22Persister` **n'est pas élargi** — élargir le filtre à
  `InvalidOperationException` risquerait d'avaler des `InvalidOperationException`
  sans rapport avec cette collision (violation de la frontière `UnexpectedFailure`
  du contrat `ErrorCode`, Story 3.5) ; la pré-vérification est la seule
  implémentation retenue

**Tests xUnit (TDD) :** un test par AC (A-2 : assertions étendues des 2 tests
existants ; A-3 : `Kape22MapperTests` sur un Champ `OF`/`Coulee` paddé ; A-4 :
test de non-régression que les deux collaborateurs lisent la même source ; A-5 :
un test d'intégration simulant une collision `CodeOperation` → `ConversionError`
+ ligne REJETÉ, zéro exception non catchée).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5.

Owner : Dev.
```

### epics.md — emplacement

Les 3 stories sont ajoutées dans une nouvelle sous-section **« Corrections
post-rétrospective Épic 4 »**, insérée **après** la Story 4.7 (fin de la
séquence `4.1 → 4.7` existante) — la séquence affichée en tête d'Épic 4
(`**Séquencement (7 stories) :** 4.1 → 4.2 → {4.3, 4.4} → 4.5 → 4.6 → 4.7.`)
n'est pas renumérotée ; une ligne est ajoutée juste en dessous :
`**Corrections post-rétrospective :** 4.2-bis → 4.3-bis → 4.9.`

## 5. Implementation Handoff

**Classification : Moderate** — réorganisation du backlog (3 stories ajoutées à
un épic `done`, `sprint-status.yaml` à rouvrir `in-progress`) + implémentation
de code standard, pas de replan stratégique ni de décision d'architecture
nouvelle (AD-1..AD-7 inchangés).

| Étape | Responsable | Livrable |
|---|---|---|
| Rédiger le format `scale` et la branche de complétude dans `epics.md`/l'annexe | Lead Architecte / PM | Story 4.2-bis rédigée + annexe étendue |
| Repasser `epic-4: in-progress` dans `sprint-status.yaml`, ajouter les 3 entrées `development_status` (`backlog`) | PO / SM | `sprint-status.yaml` à jour |
| Implémenter 4.2-bis → 4.3-bis → 4.9 en TDD (`bmad-build`), dans l'ordre imposé | Dev | 3 PRs, tests `Category=Unit`/`Integration` verts, `GpaoImportP60WorkerEndToEndTests` dé-skip après 4.3-bis |
| Revue de code par story (`bmad-code-review` / `/run-review`) | Dev + revue | rapports dans `_bmad-output/implementation-artifacts/reviews/` |
| Clore les `action_items` correspondants (`story-4-6-decimal-scale-defect-story-4-3-bis`, `epic-4-retro-item-1..5`) à `done` au fil des merges | SM | `sprint-status.yaml` à jour |
| Rebasculer `epic-4: done` une fois 4.9 mergée | SM | `sprint-status.yaml` à jour |

**Critères de succès :**
- `dotnet build TextToXml.sln -warnaserror` : 0 warning/erreur.
- `dotnet test --filter Category=Unit` : 100 % vert, y compris les nouvelles
  assertions 9→10 tables aval.
- `dotnet test --filter Category=Integration` : 100 % vert, **0 skip** restant
  (`GpaoImportP60WorkerEndToEndTests` dé-skip).
- `MappingAnnexCompletenessTests` échoue si une future colonne `decimal←int`
  omet son `scale`.
- Les 6 `action_items` de `sprint-status.yaml` liés à Épic 4 passent `done`.
