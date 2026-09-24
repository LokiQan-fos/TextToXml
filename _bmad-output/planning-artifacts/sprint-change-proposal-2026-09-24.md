---
date: 2026-09-24
trigger: production-comparison-l-d-consignes-consignegpao-0-missing
mode: batch
scope_classification: moderate
---

# Sprint Change Proposal — Story 4.13 (ligne de travail `ConsigneGPAO=0` + libellés composites)

## 1. Issue Summary

Session de test n° 3 (2026-09-24). Deux Fichiers réels viennent d'être traités
par la production legacy (`P60/tmp/P60_847_682_355` → OF `2040231`,
`P60_847_682_356` → OF `2040232`). Pour chaque OF, `L_D_CONSIGNES` de
production porte **chaque consigne deux fois** : une ligne `ConsigneGPAO=1` et
une ligne `ConsigneGPAO=0`. Sur la ligne `0`, certains `LibelleConsigne`
composites valent un texte construit là où la ligne `1` porte `?`, et certains
codes diffèrent entre les deux lignes. Le même doublement existe sur l'OF
`2039771` (`P60_847_682_001`, reçu le 2026-07-19, avant tout déploiement du
worker). Il s'agit donc bien du comportement de l'existant, pas d'un effet des
Fichiers du jour.

Le worker (`ConsignesMapper`, Stories 4.4-bis/4.12) n'écrit **que** les lignes
`ConsigneGPAO=1` : 30 lignes pour l'OF `2039771` contre 60 en production. Ses
30 lignes sont identiques aux lignes `ConsigneGPAO=1` de production (codes et
libellés, `?` compris), à l'exception des espaces de fin de `CodeConsigne`
(voir §4, AC-4).

### Cause racine (lue dans le code legacy, `Desktop/kape22/`)

1. `InterfaceManager.ImportGPAO` (`InterfaceManager.cs:65`) appelle
   `OrdreDeFabricationManager.CompleteConsignes2(_of, gpao: true)`.
2. Pour chaque consigne (ligne de code complet et sous-champs),
   `AddOrModifyConsigne` (`OrdreDeFabricationManager.cs:1362-1435`) crée ou met
   à jour **deux** lignes avec le **même** `codeconsigne` et le même
   `tailleconsigne` : une ligne `ConsigneGPAO=false` (l.1367-1386, 1420-1431)
   et, `gpao` étant vrai, une ligne `ConsigneGPAO=true` (l.1388-1417). Chacune
   reçoit son libellé `GetLibelle` (`?` si vide).
3. Après chaque section, `OrdreFabrication.BuildLibelleConsigne`
   (`OrdreFabrication.cs:700+`) assemble le libellé composite et l'écrit sur
   `of.GetConsignes(section, 13)` (et `24` pour XP1). Or `GetConsignes` a pour
   valeur par défaut `gpao = false` (`OrdreFabrication.cs:60`) : **seule la
   ligne `ConsigneGPAO=0` reçoit le libellé composite**, et la ligne
   `ConsigneGPAO=1` garde `?`.

### Les écarts de codes entre `0` et `1` ne viennent pas de l'import

`L_D_LOG_COMMANDE` de production (lu en SELECT) montre une retouche opérateur
(commande **MCC**) quelques minutes après chaque import :

| OF | Import GPAO | Retouche MCC |
|---|---|---|
| `2039771` | 2026-07-19 16:37:34 | 16:43:11 — « Changement de Consigne P900: 394 --> 999 » |
| `2040231` | 2026-09-24 12:22:11 | 12:24:28 — « Changement de Consigne P900: 438 --> 999 » |
| `2040232` | 2026-09-24 13:27:06 | 13:30:05 — « Commande MCC effectuée » (réenregistrement) |

La retouche ne modifie que la ligne `ConsigneGPAO=0`, par exemple LA1 type 8
`394`→`999` avec son code composite de type 13. Le réenregistrement MCC passe
aussi les décimales au format français sur XP1 (`4.0`→`4,0` en type 24/28,
`.`→`,` en type 29). La ligne `ConsigneGPAO=1` garde la valeur reçue.
**À l'import, les deux lignes portent exactement les mêmes codes.**

Ampleur en production (sections dont un code `0` ≠ `1`, parmi les OF des
Fichiers `001..300`) : LA1 191/192, XP1 334/472, PC1 1/496, XA1 2/196,
XA2 1/300, LA9 6/304, XC1 0/496.

### Contradiction avec la sémantique retenue le 2026-09-23

`sprint-change-proposal-2026-09-23.md` (AC-2 de la Story 4.4-bis) et
l'annexe (`annexe-mapping-dispatch-epic4.md` § L_D_CONSIGNES, ligne
`ConsigneGPAO`) posent : « `0` = valeur initiale prévue par l'OF, écrite par
un processus antérieur hors périmètre ; `1` = valeur possiblement modifiée par
un opérateur ». Les données de production montrent **l'inverse** :

- `ConsigneGPAO=1` = valeur **telle que reçue du GPAO**, jamais retouchée ;
- `ConsigneGPAO=0` = **copie de travail**, créée par l'import lui-même avec
  les mêmes codes, puis retouchée par les opérateurs (MCC).

Aucun autre processus n'écrit les lignes `0` : c'est l'import qui les crée.
Cette proposition corrige la sémantique dans l'annexe, dans `epics.md` et dans
les commentaires de code.

## 2. Impact Analysis

### Checklist

| # | Statut | Constat |
|---|---|---|
| 1.1 | [x] | Déclencheur : comparaison production du 2026-09-24 (Stories 4.4-bis/4.12 livrées). |
| 1.2 | [x] | Type : incompréhension d'une exigence (sémantique `ConsigneGPAO` inversée, ligne `0` jugée hors périmètre à tort). |
| 1.3 | [x] | Preuves : §1 (code legacy, 3 OF, journal MCC, décompte par section). |
| 2.1 | [x] | Épic 4 (`in-progress` depuis la Story 4.12) reste le bon périmètre. |
| 2.2 | [x] | Ajout de la Story 4.13 dans « Corrections post-clôture (session de test n° 2, 2026-09-24) ». |
| 2.3-2.5 | [N/A] | Pas d'épic futur planifié. |
| 3.1 | [x] | PRD : FR-19 inchangé sur le fond. Un AC est ajouté (`AC-FR19-6`), comme `AC-FR19-5` pour la Story 4.12. |
| 3.2 | [x] | Architecture : aucun nouvel AD. `ConsignesMapper` reste pur (AD-2). Aucun changement de schéma : la clé `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` distingue déjà les deux lignes. |
| 3.3 | [N/A] | Pas d'UI. |
| 3.4 | [!] | Annexe de mapping, `Kape22ProductionDataParityTests`, `scripts/e2e-worker-import.ps1` (contrôle `LibelleConsigne` à `?`), commentaires `ConsignesMapper.cs:12-13` et `Kape22ProductionDataParityTests.cs:272-274`. |
| 4.1 | Viable | Ajustement direct : 1 story, effort Medium, risque Low. |
| 4.2 | Non viable | Rien à annuler : les lignes `1` restent justes. |
| 4.3 | Non viable | MVP inchangé. |

### Technical Impact

- `ConsignesMapper.Map` produit, pour chaque ligne `ConsigneGPAO=1` actuelle,
  une ligne `ConsigneGPAO=0` de même `CodeOperation`, `TypeConsigne`,
  `CodeConsigne`, `SizeCodeConsigne` et même `LibelleConsigne` (résolu par
  `LibelleConsigneResolver`, Story 4.12). Seuls les libellés composites
  (type 13 de chaque section décodée, type 24 de XP1) diffèrent : ils sont
  assemblés par un port pur de `BuildLibelleConsigne`. Le nombre de lignes
  double (30 → 60 pour l'OF `2039771`).
- Aucun changement pour `Kape22Persister` (contrôle de clé naturelle déjà
  compatible), pour le schéma ou pour `MicroServices.sln`.
- La section SVT (ligne unique non décodée) : à trancher dans la story en
  lisant le legacy (`CompleteConsignes2` ne traite pas SVT). Si aucune ligne
  `0` n'existe en production pour SVT, elle n'en produit pas.

## 3. Recommended Approach

**Option 1 — Ajustement direct.** Une Story 4.13 dans l'Épic 4, qui reste
`in-progress`. Effort **Medium** : port de `BuildLibelleConsigne` pour 6
sections et extension de la parité. Risque **Low** : les lignes `1` existantes
ne changent pas, et la source legacy est disponible et lue.

## 4. Detailed Change Proposals

### 4.1 epics.md — nouvelle Story 4.13 (à la suite de 4.12)

```
### Story 4.13 : Ligne de travail `ConsigneGPAO=0` + libellés composites (port de `BuildLibelleConsigne`)

As a `Kape22Importer`,
I want que chaque consigne produite existe aussi en ligne `ConsigneGPAO=0`
(copie de travail), avec les libellés composites que le legacy construit par
`BuildLibelleConsigne`,
So that `L_D_CONSIGNES` porte, à l'import, les mêmes 2 × N lignes que la
production au lieu de la seule moitié `ConsigneGPAO=1`.

**Prérequis :** Stories 4.4-bis et 4.12 (livrées).

**Acceptance Criteria:**

**AC-FR19-6 (ligne de travail) :**
Given un Fichier `P60/` dont `ConsignesMapper` produit N lignes `ConsigneGPAO=1`
When il est mappé
Then il produit aussi N lignes `ConsigneGPAO=0`, une par ligne `1`, de même
  `OF`, `CodeOperation`, `TypeConsigne`, `CodeConsigne`, `SizeCodeConsigne`
  (source : `AddOrModifyConsigne`, `OrdreDeFabricationManager.cs:1362-1435`,
  un seul `codeconsigne` pour les deux lignes)
And le `LibelleConsigne` de chaque ligne `0` non composite est celui de sa
  ligne `1` (`LibelleConsigneResolver`, Story 4.12)
And SVT suit ce que fait le legacy (lecture de `CompleteConsignes2` et de la
  production) ; faute de règle trouvée, SVT est documentée `à_clarifier`
  sans règle inventée

**AC-2 (libellés composites, port exact de `BuildLibelleConsigne`) :**
Given `OrdreFabrication.BuildLibelleConsigne` (`Desktop/kape22/OrdreFabrication.cs:700+`)
When les lignes `0` de type 13 (6 sections décodées) et de type 24 (XP1) sont produites
Then leur `LibelleConsigne` est reproduit **à l'identique** du legacy, sans
  normalisation : l'ordre des sous-types par section (lire le tableau `__tabCodes*`
  de chaque `case`), le séparateur `"\t\t"` après chaque élément, séparateur
  final compris, le suffixe `"°C"` sur les types 10/11 de PC1, le `"\n"` suivi
  du libellé du type 6 de PC1, et les sous-libellés `?` recopiés tels quels
  — lire le code source directement pour chaque section plutôt qu'une
  retranscription
And les lignes `ConsigneGPAO=1` de type 13/24 gardent `?` (inchangé, comme le legacy)
And un port pur (fonction statique, AD-2), sans accès base

**AC-3 (sémantique `ConsigneGPAO` corrigée) :**
Given la sémantique retenue le 2026-09-23 (`0` = valeur initiale hors
  périmètre, `1` = valeur modifiée), contredite par la production (§1 de
  sprint-change-proposal-2026-09-24.md)
When la story est livrée
Then l'annexe § L_D_CONSIGNES (ligne `ConsigneGPAO`), les commentaires
  `ConsignesMapper.cs` et `Kape22ProductionDataParityTests.cs`, et le
  chapeau de section « tests de parité production, 2026-09-23 » d'epics.md
  portent : `1` = valeur reçue du GPAO, jamais retouchée ; `0` = copie de
  travail créée par l'import avec les mêmes codes, retouchée ensuite par les
  opérateurs (MCC)

**AC-4 (espaces de fin de `CodeConsigne`, écart accepté) :**
Given le legacy conserve les espaces de fin (ex. `"00 1        "`)
When une ligne est produite
Then `CodeConsigne` reste sans espaces de fin (comportement actuel, décision
  utilisateur 2026-09-24) et l'annexe documente cet écart accepté

**AC-5 (parité production) :**
Given `Kape22ProductionDataParityTests` (compare aujourd'hui les lignes `1` seulement)
When la suite est étendue
Then chaque ligne `0` produite a sa correspondante en production (clé naturelle)
And ses `CodeConsigne`/`SizeCodeConsigne` sont comparés à la ligne `1` de
  production (à l'import, `0` et `1` portent les mêmes codes)
And son `LibelleConsigne` n'est comparé à la ligne `0` de production que pour
  les sections (`OF`, `CodeOperation`) dont aucun code `0` ne diffère du code
  `1` en production ; une section retouchée après l'import (MCC) est signalée
  comme ignorée, pas en échec

**AC-6 (E2E) :**
Given `scripts/e2e-worker-import.ps1`
When il est rejoué sur `P60_847_682_001`
Then il vérifie aussi que les lignes `ConsigneGPAO=0` existent en même nombre
  que les lignes `1`, et que leurs libellés de type 13 ne sont pas `?`

**Tests xUnit (TDD, CC-1) :** un test « ligne 0 miroir des codes » ; un test par
section pour le libellé composite (fixtures `P60/` réelles, valeurs attendues
tirées de la production sur une section non retouchée) ; un test XP1 type 24
présent/absent ; un test « lignes 1 composites restent `?` » ; extension
`Kape22ProductionDataParityTests` (AC-5).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5.

Owner : Dev.
```

### 4.2 PRD — FR-19

Ajout de `AC-FR19-6` à la liste des AC de FR-19 et à la matrice de traçabilité
d'`epics.md` (ligne FR-19 : `4.13 (AC-FR19-6, ligne ConsigneGPAO=0)`).

### 4.3 annexe-mapping-dispatch-epic4.md § L_D_CONSIGNES

- `ConsigneGPAO` — OLD : « `true` pour toute ligne produite… `false` porte la
  valeur initiale prévue par l'OF, un processus antérieur… »
  NEW : « deux lignes par consigne : `true` = valeur reçue du GPAO, jamais
  retouchée ; `false` = copie de travail, mêmes codes à l'import, retouchée
  ensuite par MCC (`AddOrModifyConsigne`, OrdreDeFabricationManager.cs:1362-1435). »
- `LibelleConsigne` — ajouter : « lignes `false` de type 13/24 : libellé
  composite, port exact de `BuildLibelleConsigne` (OrdreFabrication.cs:700+). »
- `CodeConsigne` — ajouter l'écart accepté : espaces de fin supprimés.

### 4.4 sprint-status.yaml

Ajouter `4-13-ligne-consignegpao-0-libelles-composites: backlog` après
`4-12-libelle-consigne`. `epic-4` reste `in-progress`.

## 5. Implementation Handoff

**Classification : Moderate.** Une story ajoutée dans un épic ouvert, avec une
correction de sémantique documentée. Aucun replan.

| Étape | Responsable | Livrable |
|---|---|---|
| Ajouter la Story 4.13 à `epics.md`, `AC-FR19-6` au PRD, mettre à jour l'annexe (§4.3) | PM / Dev | artefacts à jour |
| Ajouter `4-13-…: backlog` dans `sprint-status.yaml` | SM | `sprint-status.yaml` |
| Implémenter en TDD (`/build-story`) | Dev | tests `Unit`/`Integration` verts |
| Revue (`/run-review`), puis `/commit-review` | Dev + revue | rapport de revue, commit |

**Critères de succès :**
- `dotnet build TextToXml.sln -warnaserror` : 0 warning/erreur.
- `dotnet test --filter Category=Unit` et `Category=Integration` : 100 % vert.
- Import réel de `P60_847_682_001` : 60 lignes `L_D_CONSIGNES` (30 `1` + 30 `0`).
  Les lignes `0` sont identiques à la production, sauf la section LA1 retouchée
  par MCC (`394`→`999`) et les espaces de fin de `CodeConsigne`.
