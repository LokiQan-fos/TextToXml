---
date: 2026-09-23
trigger: production-parity-testing-l-d-consignes-underpopulation
mode: batch
scope_classification: moderate
---

# Sprint Change Proposal — Story 4.4-bis (décomposition `L_D_CONSIGNES`)

## 1. Issue Summary

En comparant, colonne par colonne, les données de `L_D_CONSIGNES` produites par
le pipeline actuel contre la production réelle (`AFV004-LSI`) pour un OF réel
(`2039771`, fichier `P60_847_682_001`), l'écart suivant a été constaté :

- **Pipeline actuel** (`ConsignesMapper.cs`) : **5 lignes** — une par section
  applicable (`XC1`, `LA1`, `PC1`, `XA1`, `XP1`), chacune avec `TypeConsigne=0`,
  `ConsigneGPAO=false`, portant le code consigne complet.
- **Production réelle**, même OF : **60 lignes**.

Ce n'est pas un bug ponctuel mais un **écart de fonctionnalité entièrement
décodable** depuis le code legacy source
(`Desktop/kape22/OrdreDeFabricationManager.cs`, méthode `AddOrModifyConsigne`,
lignes ~1362-1435 et les blocs par section ~1450-1660), avec deux causes
cumulatives :

1. **Décomposition en sous-champs** (facteur dominant) — pour chaque section,
   le legacy découpe la chaîne de code consigne brute en plusieurs sous-champs
   à des positions fixes (`Substring`), chacun stocké comme une ligne
   `L_D_CONSIGNES` distincte avec un `TypeConsigne` différent. Le mapper actuel
   ne produit **que** la ligne "code complet" (`TypeConsigne=13`), jamais les
   sous-champs.
2. **Duplication `ConsigneGPAO=0`/`1`** (facteur ×2) — déjà identifiée comme
   gap connu dans `ConsignesMapper.cs` ("legacy mirror-row duplication... not
   reproduced here"), mais sa sémantique métier vient d'être précisée par le
   donneur d'ordre (2026-09-23) : **ce n'est pas un doublon**.
   `ConsigneGPAO=0` porte la valeur **initiale prévue par l'OF**,
   `ConsigneGPAO=1` porte la valeur **possiblement modifiée par un opérateur**
   en fonction d'une contrainte de production temporaire (ex. four
   indisponible). Les deux doivent être tracées et distinguées.

Vérification chiffrée : 5 sections applicables × (sous-champs + valeur globale)
× 2 (`ConsigneGPAO`) = exactement 60 lignes, ce qui correspond pile au compte
production observé.

Ce gap était déjà anticipé, sans être chiffré, dans
`deferred-work.md` (« Deferred from: story-4.4 code review ») :
*« Revisit once `TypeConsigne`'s real value is known (would very likely
disambiguate the key by section, matching the annex's own note that
`CompleteConsignes2` assigns a distinct type constant per decoded sub-field) »*
— cette investigation lève cette ambiguïté.

## 2. Impact Analysis

### Epic Impact

Épic 4 (`epics.md`, `done` dans `sprint-status.yaml`, projet marqué `closed`
dans `PROJECT-CLOSED.md` non commité) reste le bon périmètre : aucun nouveau FR,
c'est une correction de conformité à `AC-FR19-3` déjà déclaré (« son
`TypeConsigne`/`CodeConsigne` selon l'annexe »). **Décision : rester sur Épic 4**
(repasse `in-progress`), même pattern que les 5 corrections précédentes
(4.2-bis, 4.3-bis, 4.9, 4.10, 4.11). `PROJECT-CLOSED.md` devra être mis à jour
en conséquence une fois la story livrée (hors périmètre de cette proposition —
action de clôture, pas de correction de cap).

### Story Impact

**1 nouvelle story : `4.4-bis`**, corrigeant `ConsignesMapper` (Story 4.4).
Nommage aligné sur le précédent `4.2-bis`/`4.3-bis` (correction ciblée d'une
story existante, par opposition à `4.9`/`4.10`/`4.11` qui regroupent des items
de hardening indépendants). Aucune story existante n'est rouverte/modifiée
rétroactivement.

### Artifact Conflicts

- **PRD** : aucun changement de fond. `FR-19`/`AC-FR19-3` restent inchangés.
- **epics.md** : ajout de `4.4-bis` dans la sous-section « Corrections
  post-rétrospective Épic 4 », à la suite de `4.11`.
- **`annexe-mapping-dispatch-epic4.md` § L_D_CONSIGNES** : `TypeConsigne` et
  `SizeCodeConsigne` passent d'`à_clarifier` à `règle`, documentant les
  décompositions par section. La ligne `ConsigneGPAO` est reformulée : le mot
  « miroir » (qui suggère un doublon à éliminer) est remplacé par la
  sémantique confirmée (valeur initiale vs. valeur modifiée).
- **`deferred-work.md`** : l'entrée « Deferred from: story-4.4 code review »
  (`annexe-mapping-dispatch-epic4.md § L_D_CONSIGNES`) est liée à `4.4-bis`
  plutôt que dupliquée.
- **`Kape22ProductionDataParityTests.cs`** : `L_D_CONSIGNES` n'est aujourd'hui
  couvert par **aucun** test de parité production (seuls `L_D_KAPE22` et les
  colonnes `decimal` de `L_D_ORDRE_FABRICATION`/`L_D_SECTIONCHARGE_*` le sont)
  — c'est la raison pour laquelle cet écart de 5 vs 60 lignes n'a jamais été
  détecté par la suite automatisée. `4.4-bis` doit combler ce trou.
- **UX** : sans objet (v1 sans UI).

### Technical Impact

- `ConsignesMapper.Map` doit produire, par section applicable, une ligne
  « code complet » (comme aujourd'hui) **plus** une ligne par sous-champ décodé
  (règles différentes par section — voir §4), toutes taguées `ConsigneGPAO=1`
  (§4, AC-2) au lieu de `0`/`false`.
- La clé naturelle `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` reste
  inchangée (Story 4.1) ; `TypeConsigne` cesse simplement de valoir `0` pour
  toute ligne, et `ConsigneGPAO` passe de `0` à `1`.
- Aucun changement de `MicroServices.sln` (SVN) — la story reste dans
  `TextToXml.sln`.
- Aucun changement de schéma (`L_D_CONSIGNES` porte déjà les 7 colonnes
  nécessaires, Story 4.1).

## 3. Recommended Approach

**Option 1 — Ajustement direct** (checklist §4) : ajout d'**une** story dans le
périmètre Épic 4 existant. Effort **Medium** (5-6 règles de décomposition
distinctes à coder + tests, mais aucun changement de schéma ni d'architecture),
risque **Low** — la sémantique `ConsigneGPAO=0`/`1` est tranchée (§4, AC-2) :
le dispatch P60 produit les lignes `ConsigneGPAO=1`, les lignes `ConsigneGPAO=0`
restent hors périmètre de ce mapper.

Pas d'Option 2 (rollback) : rien à annuler, c'est une fonctionnalité manquante,
pas une régression. Pas d'Option 3 (revue MVP) : le PRD/FR-19 ne change pas de
périmètre.

## 4. Detailed Change Proposals

### Story 4.4-bis — Décomposition `L_D_CONSIGNES` en sous-champs + sémantique `ConsigneGPAO`

```
As a Kape22Importer,
I want que ConsignesMapper produise, pour chaque section applicable, une ligne
par sous-champ décodé du code consigne (en plus de la ligne "code complet"
déjà produite), taguée ConsigneGPAO=1 (valeur telle qu'injectée par le
dispatch P60, confirmé par le donneur d'ordre) plutôt que ConsigneGPAO=0,
So that L_D_CONSIGNES porte, pour chaque OF, les mêmes lignes ConsigneGPAO=1
que la production legacy, plutôt qu'un sous-ensemble ne portant que le code
consigne brut non décodé sous le mauvais tag ConsigneGPAO.

**Prérequis :** aucun — indépendante des stories 4.2-bis/4.3-bis/4.9/4.10/4.11
déjà livrées.

**Acceptance Criteria:**

**AC-1 (décomposition en sous-champs, par section) :**
Given le code source legacy `Desktop/kape22/OrdreDeFabricationManager.cs`,
  méthode `AddOrModifyConsigne` (lignes ~1362-1435) et les blocs de décodage
  par section (`ConsignesChutage` ~1520-1545, `ConsignesLingot` ~1488-1510,
  `ConsignesEnfournementPits` ~1450-1478, `ConsignesRefroidissoir` ~1642-1660,
  `ConsignesDecoupeLingot` ~1557-1599, `ConsignesPoidsMetrique` ~1612-1632)
When `ConsignesMapper.Map` est étendu
Then chaque section produit, en plus de sa ligne `TypeConsigne=13` (code
  complet, déjà produite aujourd'hui), une ligne par sous-champ décodé à
  l'offset exact documenté dans le code source ci-dessus — **lire le code
  source directement pour chaque offset plutôt que s'appuyer sur une
  retranscription**, notamment pour `ConsignesEnfournementPits` (PC1) dont les
  types 10 et 11 partagent apparemment le même `Substring(2,3)` sous une
  condition différente à élucider en lisant les lignes 1465-1472
And `ConsignesDecoupeLingot` (XP1) produit ses sous-champs à partir de **deux**
  codes consigne distincts (`GetConsignes("ConsignesDecoupeLingot", 13)` et
  `GetConsignes("ConsignesDecoupeLingot", 24)`, lignes 1557-1599) — le second
  code et ses propres sous-champs (types 25-29) ne sont produits que s'il est
  renseigné (garde `!string.IsNullOrEmpty(_global2)`, ligne 1577)
And les 7 sections sont couvertes ; si `ConsignesSVT` ne présente aucune règle
  de décomposition trouvée dans le code legacy fourni (seule une ligne
  commentée `//_current = ordre.GetConsignes("ConsignesSVT", 13);` a été
  repérée à ce jour), la story le documente explicitement comme
  `à_clarifier` plutôt que d'inventer une règle — pas de blocage total de la
  story (pattern `AC-FR19-4` déjà établi)

**AC-2 (sémantique ConsigneGPAO=0/1 — tranché par le donneur d'ordre 2026-09-23) :**
Given la confirmation du donneur d'ordre : `ConsigneGPAO=0` porte la valeur
  initiale prévue par l'OF, `ConsigneGPAO=1` porte la valeur possiblement
  modifiée par un opérateur pour une contrainte de production temporaire — ce
  n'est pas un doublon à dédupliquer — **et les données injectées par le
  dispatch P60 portent `ConsigneGPAO=1`**
When `ConsignesMapper.Map` est corrigé
Then chaque ligne produite par ce mapper (code complet et sous-champs, AC-1)
  porte `ConsigneGPAO=1` — pas `0`/`false` comme aujourd'hui — puisqu'elle
  reflète la valeur telle qu'elle arrive par le P60, potentiellement déjà
  ajustée en amont de l'enregistrement du Fichier
And les lignes `ConsigneGPAO=0` (valeur initiale prévue par l'OF) sont
  **hors périmètre de ce mapper** — elles sont portées par un autre
  processus, antérieur au dispatch P60 (probablement la création initiale de
  l'OF), que `ConsignesMapper` ne doit ni produire ni modifier ; la story
  n'invente aucune règle pour les créer
And `Kape22ImportBundleMapper`/`Kape22Persister` ne doivent pas non plus
  supposer l'existence d'une ligne `ConsigneGPAO=0` correspondante (pas de
  contrôle d'existence ajouté pour ces lignes) — cohérent avec AD-2/AD-7 (pas
  de connaissance croisée entre mappers, pas de propriété de navigation)

**AC-3 (annexe de mapping) :**
Given `annexe-mapping-dispatch-epic4.md` § L_D_CONSIGNES, dont les colonnes
  `TypeConsigne`/`SizeCodeConsigne` sont aujourd'hui `à_clarifier` et dont la
  ligne `ConsigneGPAO` emploie le mot « miroir »
When l'annexe est mise à jour
Then `TypeConsigne` documente la règle par section (renvoi vers AC-1 et les
  lignes source), `SizeCodeConsigne` documente sa valeur par section (12 ou
  18, paramètre `tailleconsigne` de `CompleteConsignes2`) si déterminable, et
  la ligne `ConsigneGPAO` est reformulée selon la sémantique confirmée en AC-2
  (valeur initiale vs. modifiée, pas un miroir)

**AC-4 (couverture de parité production, comblant le trou qui a masqué ce gap) :**
Given `Kape22ProductionDataParityTests.cs`, qui ne compare aujourd'hui ni
  `L_D_CONSIGNES` ni `L_D_COULEE` — seuls `L_D_KAPE22` (ligne complète) et les
  colonnes `decimal` de `L_D_ORDRE_FABRICATION`/`L_D_SECTIONCHARGE_*` le sont
When la suite est étendue
Then elle compare également `L_D_CONSIGNES` (par `OF` zero-paddé, comme les
  autres tables aval — voir `DownstreamOf.Pad`) pour les 100 fichiers `P60/`
  réels, colonne par colonne, contre la production — afin qu'une future
  régression sur ce mapper ne reste plus silencieuse comme celle-ci l'a été

**Tests xUnit (TDD — écrits en premier, CC-1) :** un test par section
(sous-champs produits avec les bons `TypeConsigne`/valeurs, sur fixtures
`P60/` réelles) ; un test sur le second code consigne XP1 (présent et absent) ;
un test que chaque ligne produite porte `ConsigneGPAO=1` (AC-2) ; extension de
`MappingAnnexCompletenessTests`/`AcCoverageCompletenessTests` si l'annexe gagne
une colonne structurée ; les nouveaux cas `Kape22ProductionDataParityTests`
(AC-4) comparés uniquement contre les lignes production `ConsigneGPAO=1`
(AC-2 exclut `ConsigneGPAO=0` du périmètre de ce mapper).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5. *(CC-6/CC-7 sans
objet : pas de nouvel accès base, `ConsignesMapper` reste pur.)*

Owner : Dev.
```

### epics.md — emplacement

`4.4-bis` est ajoutée dans la sous-section « Corrections post-rétrospective
Épic 4 » existante, **après** `4.11` (dernière story de la séquence à ce
jour). La ligne de séquencement en tête d'Épic 4 gagne une entrée :
`**Corrections post-rétrospective :** 4.2-bis → 4.3-bis → 4.9 → 4.10 → 4.11 → 4.4-bis`
(ordre chronologique de découverte, pas de dépendance technique avec 4.9/4.10/4.11).

## 5. Implementation Handoff

**Classification : Moderate** — réouverture d'un épic `done` + 1 story ajoutée,
pas de replan stratégique ni de nouvel AD d'architecture. La question métier
(AC-2) est déjà tranchée par le donneur d'ordre (2026-09-23), aucune décision
Ask First ne reste ouverte au cadrage.

| Étape | Responsable | Livrable |
|---|---|---|
| Rédiger `4.4-bis` dans `epics.md` (texte ci-dessus) | Lead Architecte / PM | `epics.md` à jour |
| Repasser `epic-4: in-progress` dans `sprint-status.yaml`, ajouter `4-4-bis-...: backlog` | PO / SM | `sprint-status.yaml` à jour |
| Implémenter `4.4-bis` en TDD (`bmad-build`) | Dev | PR, tests `Category=Unit`/`Integration` verts |
| Revue de code (`bmad-code-review` / `/run-review`) | Dev + revue | rapport dans `_bmad-output/implementation-artifacts/reviews/` |
| Lier l'entrée `deferred-work.md` (« Deferred from: story-4.4 code review », § annexe L_D_CONSIGNES) à `4.4-bis` | SM | `deferred-work.md` à jour |
| Rebasculer `epic-4: done` une fois `4.4-bis` mergée ; mettre à jour `PROJECT-CLOSED.md` en conséquence | SM | `sprint-status.yaml` / `PROJECT-CLOSED.md` à jour |

**Critères de succès :**
- `dotnet build TextToXml.sln -warnaserror` : 0 warning/erreur.
- `dotnet test --filter Category=Unit` : 100 % vert, y compris les nouveaux cas
  par section et par sous-champ.
- `dotnet test --filter Category=Integration` : 100 % vert, y compris les
  nouveaux cas `Kape22ProductionDataParityTests` sur `L_D_CONSIGNES`.
- Sur l'OF réel `2039771` (`P60_847_682_001`), un import réel produit les
  lignes `ConsigneGPAO=1` de `L_D_CONSIGNES` (30 des 60 lignes production
  observées — les 30 `ConsigneGPAO=0` restent hors périmètre, AC-2),
  identiques colonne par colonne à la production.
