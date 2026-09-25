# Review report — Story 4.14

Range : f425eb5743f1d0f58ac20952196c8ec00e5d6359^..HEAD (f425eb5, 1c58af6)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-4-14-verrou-parite-downstream-column-precisions.md
Date : 2026-09-25
Verdict : ACCEPTÉ
Findings : D=1 P=4 F=1 R=20  (Decision, Patch, Defer, Rejetés)
Mise à jour 2026-09-25 : D-1 tranché (option 1) → P-5. Reste : D=0 P=5 F=1 R=20

## 1. Verdict

**ACCEPTÉ**, sous réserve de trancher D-1 et d'appliquer P-1..P-4 dans le commit de clôture (`/commit-review`).

- Code de test : correct. Aucune déviation CC-2 (commentaires anglais, capitale + point) ni CC-4
  (paramètres positionnels de `SqlColumn` triés : ClrType < DecimalPrecision < IsNullable < MaxLength
  < Name < SqlType). Parseur unique respecté, pas de réflexion EF, aucun code de production touché.
- Les findings retenus portent tous sur la cohérence des artefacts de clôture (sprint-status, spec,
  PROJECT-CLOSED.md) et sur une question de processus (autorisation du run `Category=Unit` complet).

| Sévérité | Nombre |
|---|---|
| high | 0 |
| medium | 2 (D-1, P-1) |
| low | 4 (P-2, P-3, P-4, F-1) |

## 2. Decision — à trancher par l'humain

### D-1 — Le run complet `Category=Unit` (1026 tests) était-il autorisé ? (medium)

- **Source** : acceptance-auditor + blind-hunter
- **Localisation** : `_bmad-output/implementation-artifacts/PROJECT-CLOSED.md:95-96`, spec `:191` (Ask First), spec `:261`
- **Description** : §5 de PROJECT-CLOSED.md affirme un run `Category=Unit` « re-run » à 1026 passed. La spec
  classe ce run en **Ask First** (il vide `AscoLSI_Test` via la fixture SQL), et la ligne spec:261 dit
  qu'il n'a lieu « qu'après confirmation humaine ». Ni la spec ni le diff n'enregistrent cette confirmation.
- **État** : **tranché par l'humain le 2026-09-25 → option 1** (le run était autorisé). Devient le patch P-5.
- **Options** :
  1. La confirmation a été donnée → ajouter une note datée dans la section Verification de la spec (« full Unit run authorized by the user on 2026-09-25 »).
  2. Le run n'a pas eu lieu → corriger §5 : remplacer « re-run » par le compte attendu, marqué « not re-run ».
  3. Le run a eu lieu sans confirmation → le consigner comme écart au Boundary Ask First (spec Verification + PROJECT-CLOSED §5), et vérifier l'état de `AscoLSI_Test`.

## 3. Patch

### P-1 — Le tracker contredit PROJECT-CLOSED.md et l'AC-3 de la spec (medium)

- **Source** : edge-case-hunter + acceptance-auditor + blind-hunter
- **Localisation** : `_bmad-output/implementation-artifacts/sprint-status.yaml:70,87,556,564` ; `PROJECT-CLOSED.md:8,63,71-72`
- **Description** : à HEAD, `epic-4: in-progress`, `4-14-…: review`, et les action items
  `epic-4-retro4-item-d2-precisions-schema-parity-test` / `-d3-project-closed-refresh` sont `open`.
  Or PROJECT-CLOSED.md dit `status: 'closed'`, « All action items are done (re-checked 2026-09-25) » et
  « all four are done ». L'AC-3 de la spec (« consistent with sprint-status.yaml (epic-4: done) ») n'est
  pas satisfait tant que le tracker n'est pas mis à jour.
- **Action corrective** : dans le commit de clôture, mettre `4-14-verrou-parite-downstream-column-precisions: done`,
  `epic-4: done`, `d2` et `d3` → `status: done` avec un `ref` pointant Story 4.14 (DONE 2026-09-25),
  `last_updated` au format `MM-DD-YYYY HH:MM`.

### P-2 — Métadonnées de revue de la spec non à jour (low)

- **Source** : acceptance-auditor + blind-hunter + edge-case-hunter
- **Localisation** : `_bmad-output/implementation-artifacts/spec-4-14-verrou-parite-downstream-column-precisions.md:7`
- **Description** : `review_loop_iteration: 0` alors qu'une passe de revue a déjà produit le patch P-2
  (commit 1c58af6), et cette revue est la deuxième.
- **Action corrective** : passer `review_loop_iteration` à `2`. `status: 'done'` reste correct une fois P-1 appliqué.

### P-3 — PROJECT-CLOSED.md suppose un seul commit Story 4.14 ; lignes non repliées (low)

- **Source** : blind-hunter
- **Localisation** : `_bmad-output/implementation-artifacts/PROJECT-CLOSED.md:23-24` (bandeau), `:63`, `:83`, `:180-182`
- **Description** : le bandeau (« the re-close itself ships in the Story 4.14 commit ») et §6 (« the Story
  4.14 commit on top of `1fcfbf2` ») parlent d'un commit unique. La story compte déjà `f425eb5`, `1c58af6`
  et le commit de clôture. Les lignes 63, 83 et 181 dépassent la largeur de repli du reste du document.
- **Action corrective** : écrire « the Story 4.14 commits (`f425eb5`, `1c58af6` and the review-closure
  commit) » dans le bandeau et §6, et replier les lignes 63, 83 et 181 à ~105 colonnes.

### P-4 — Attribution de la Story 4.14 dans le chapeau §2 (low)

- **Source** : blind-hunter
- **Localisation** : `_bmad-output/implementation-artifacts/PROJECT-CLOSED.md:44-45`
- **Description** : le chapeau attribue la réouverture à la session de test production « for
  `L_D_CONSIGNES` parity », puis la liste inclut 4.14, qui vient de la retro #4 / correct-course
  2026-09-25 (et ne touche pas `L_D_CONSIGNES`).
- **Action corrective** : reformuler le chapeau, par ex. « …reopened the epic for production parity
  (`L_D_CONSIGNES`, then the retro #4 follow-up): ».

### P-5 — Tracer l'autorisation du run complet `Category=Unit` (issu de D-1, low)

- **Source** : décision humaine sur D-1 (option 1)
- **Localisation** : `_bmad-output/implementation-artifacts/spec-4-14-verrou-parite-downstream-column-precisions.md:261`
- **Action corrective** : remplacer la puce « The full `Category=Unit` run… happens only after the human
  confirms » par une note datée : « The full `Category=Unit` run (1026 passed) was authorized by the user
  on 2026-09-25 (Ask First gate), for the PROJECT-CLOSED.md §5 counts. »

## 4. Defer

### F-1 — Branche `throw` de `DecimalPrecisionFor` sans test ; `DECIMAL(p)` à un argument rejeté (low)

- **Source** : blind-hunter + edge-case-hunter
- **Localisation** : `tests/Kape22Importer.Tests/SqlTableSchema.cs:89-100`
- **Justification** : le patch P-2 de la première revue fait lever une exception à un DECIMAL/NUMERIC sans
  `(p,s)`, et aucun test ne couvre cette branche. Un `DECIMAL(10)` valide (scale 0) lèverait aussi, avec un
  message inexact (« without an explicit (p,s) »), et casserait tous les consommateurs de
  `SqlTableSchema.Read`. Pas de conséquence aujourd'hui : les fichiers `scripts/schema/*.sql` générés ne
  contiennent que des `DECIMAL(p,s)` à deux arguments (vérifié : 34 occurrences, de (2,1) à (7,3)). À
  traiter si un script de schéma produit un jour la forme à un argument.

## 5. Rejetés (bruit)

| ID | Source | Finding | Justification du rejet |
|---|---|---|---|
| R-1 | edge | Overflow de `Math.Pow`→decimal si p−s ≥ 29 | Inatteignable : max p−s = 4 dans le schéma. |
| R-2 | edge | `Assert.Equal` compare les clés en respectant la casse, alors que la map est OrdinalIgnoreCase | Direction sûre : une dérive de casse échoue bruyamment ; les deux côtés viennent du même nommage. |
| R-3 | edge | Casse de la première table retenue en cas de collision | Même raisonnement que R-2 ; aucune collision de casse dans le schéma. |
| R-4 | auditor+blind | CC-4 : `TableNames` non trié | CC-4 vise les propriétés et les initialiseurs d'objet (epics.md:267), pas l'ordre d'un tableau de données. Même ordre métier que les tests frères. |
| R-5 | blind | CC-4 : méthodes de test non triées | Hors champ de CC-4 ; les tests frères ne trient pas non plus. |
| R-6 | blind | Chiffres Integration présentés comme actuels | §5 les marque explicitement « not re-run » et en cite la source (retro #4). |
| R-7 | blind+auditor | Run Integration accidentel (spec:258) | Le filtre était pré-autorisé par la spec elle-même. L'incident est documenté et le filtre corrigé. |
| R-8 | blind | « Drop the reopened banner » remplacé par un bandeau de re-close | L'intention (retirer l'état « reopened ») est remplie. |
| R-9 | blind | Formule 10^(p−s) dupliquée en AC-2 | AC-2 travaille sur le tuple de la map, pas sur un `SqlColumn`. Une seule ligne, exacte pour p−s ≤ 4. |
| R-10 | blind | AC-2 s'arrête au premier écart ; un seul sens | Le sens unique est voulu par la spec (magnitudes ⊆ precisions). L'échec nomme la colonne. |
| R-11 | blind | AC-1 ne nomme pas une colonne manquante ou en trop | Le diff de collection xUnit affiche les `KeyValuePair`, donc le nom. |
| R-12 | blind | Pas de test prouvant l'absence de DECIMAL hors des 9 tables | Hors périmètre de la spec. Le trou voisin (câblage EF) est déjà en deferred-work § story-4.14. |
| R-13 | blind | Chemin « même nom, même (p,s) » sans test synthétique | Exercé par les colonnes Tolerance* réelles dans AC-1. |
| R-14 | blind | La raison d'exclure L_D_CONSIGNES diffère entre le test et la spec | Les deux affirmations sont compatibles ; commentaire, pas de comportement. |
| R-15 | blind | Pas de contrôle scale ≤ precision | Le schéma est généré depuis SQL Server, qui rejette s > p. |
| R-16 | blind | Liens de la spec décalés | Vérifié : `SqlTableSchema.cs:19` est bien `DecimalMagnitude`. |
| R-17 | blind | Puce isolée spec:261 | Cosmétique, sans ambiguïté de sens. |
| R-18 | blind | Règle D-4 seulement en mémoire agent | Décision utilisateur actée (sprint-change-proposal §4.6). |
| R-19 | blind | Trou de câblage EF différé alors que D-2 est `done` | Déjà différé explicitement dans ce range (deferred-work § story-4.14). La spec interdit la réflexion EF. |
| R-20 | blind+auditor | « No story is in progress » ; nommage `_AcFrX_Y` | Vrai une fois P-1 appliqué. Traits `4.14-AC1/AC2` imposés par la spec, conformes au précédent Épic 4. |

## 6. Auto-vérifications

- **Lentilles lancées : 4/4**, sans échec (`failed_layers` vide).
  - blind-hunter : 27 findings (diff seul)
  - edge-case-hunter : 7 findings
  - verification-gap : 0 finding (« No verification gaps found »)
  - acceptance-auditor : 4 findings, plus 3 hors mandat
- **Diff stats** : 8 fichiers, +324 / −31 (502 lignes de diff). Code : `DownstreamColumnPrecisionsParityTests.cs`
  (nouveau, +106), `SqlTableSchema.cs` (±30), `DownstreamColumnMagnitudesParityTests.cs` (1 ligne).
  Artefacts : spec, PROJECT-CLOSED.md, deferred-work.md, epic-4-context.md, sprint-status.yaml.
- **Vérifications manuelles au triage** : état de sprint-status.yaml à HEAD, portée de CC-4 (epics.md:267,
  R-7), types DECIMAL présents dans `scripts/schema/*.sql`, cibles des liens de la spec, ordre de
  `SqlColumn`.
- **Non exécuté** : `dotnet test`, volontairement (il vide `AscoLSI_Test`, cf. mémoire projet).
- **Note** : le diff a été passé aux sous-agents par fichier (scratchpad), identique octet pour octet à
  `git diff f425eb5^..HEAD`. La consigne à Blind Hunter limitait sa lecture à ce seul fichier.
- **Défers** : non encore écrits dans `deferred-work.md` ; `/commit-review` s'en charge à partir de ce rapport.

## 7. Suivi — patchs appliqués (2026-09-25)

Choix humain : option 1, tous les patchs appliqués dans le working tree. Rien n'est commité ; le commit
de clôture est laissé à `/commit-review`.

- [x] **P-1** : `sprint-status.yaml`, avec `epic-4: done`, `4-14-…: done`, les action items `d2`/`d3` → `done`
  (ref complétée) et `last_updated: 09-25-2026 11:30`.
- [x] **P-2** : spec `review_loop_iteration: 2`.
- [x] **P-3** : PROJECT-CLOSED.md, avec le bandeau et §6 qui citent `f425eb5`, `1c58af6` et le commit de clôture ;
  §3 et §6 repliés.
- [x] **P-4** : PROJECT-CLOSED.md §2, chapeau reformulé (« production parity… then the retro #4 follow-up »).
- [x] **P-5** : spec:107, note datée qui atteste l'autorisation du run complet `Category=Unit`.
- [ ] **F-1** : reste à consigner dans `deferred-work.md` par `/commit-review`.

Statut story : `done` (spec et sprint-status). Aucun finding high ou medium restant.
