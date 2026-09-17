# Review report — Story 4.7

Range : 14c41df245026c0553001c91f54c8f22ba45248c^..HEAD
Spec : _bmad-output/implementation-artifacts/spec-4-7-e2e-suite-downstream-tables.md
Date : 2026-09-17
Verdict : ACCEPTÉ
Findings : D=0 P=2 F=3 R=11  (Decision, Patch, Defer, Rejetés)

## 1. Verdict et décompte par sévérité

**ACCEPTÉ** — aucun finding bloquant (`high`) ni `decision-needed`. 2 `patch` de sévérité `low`/`medium` (corrections de documentation/nommage, aucun défaut fonctionnel), 3 `defer` de sévérité `low`/`medium`, 11 findings rejetés comme bruit après lecture du code.

| Sévérité | Nombre |
|---|---|
| high | 0 |
| medium | 2 |
| low | 3 |

## 2. Findings Decision (à trancher par l'humain)

Aucun.

## 3. Findings Patch

### P-1 — Les noms des 3 tests de rejet sous-comptent les tables vérifiées
- **Localisation :** `tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs:73,100,129`
- **Description :** Les trois méthodes `Import_*_LeavesAllTenTablesEmptyWithReadableCause_AcFr21_5` appellent toutes `AssertAllElevenTablesEmpty` (ligne 92, 119, 146), qui vérifie 11 tables (`L_D_KAPE22` + les 10 tables avales), pas 10. Le nom de la méthode partagée (`AssertAllElevenTablesEmpty`, ligne 160) et le commentaire d'en-tête de la classe (ligne 22 : « 11 AscoLSI dispatch tables ») sont corrects ; seuls les 3 noms de méthode de test restent sur l'ancien compte.
- **Action corrective :** Renommer les 3 méthodes de `...LeavesAllTenTablesEmptyWithReadableCause_AcFr21_5` vers `...LeavesAllElevenTablesEmptyWithReadableCause_AcFr21_5` (le suffixe `_AcFr21_5` reste inchangé, donc `AcTraitCoverageTests` n'est pas affecté).

### P-2 — Le bloc frozen de la spec contient une incohérence interne sur le nombre de tables avales, sans note de renégociation
- **Localisation :** `_bmad-output/implementation-artifacts/spec-4-7-e2e-suite-downstream-tables.md:15,17,39,42` (bloc `<frozen-after-approval>`)
- **Description :** Le paragraphe *Problem* dit « the 9 downstream tables » tandis que *Approach*, la matrice I/O et le Code Map disent « 10 tables » / « all 10 tables (L_D_KAPE22 + 9 downstream) ». La réalité du code (confirmée par `AscoLsiDbContext.cs` : 12 `DbSet`, moins `Kape22Rows` et `LogCommandeRows` = 10 tables avales, soit 11 au total) et par `epics.md:1630` (« les 10 nouvelles entités ») montre que c'est bien 10 tables avales / 11 au total — la mention « 9 » est un artefact hérité d'une coquille préexistante dans le libellé AD-1 (`project-profile.md` : « L_D_KAPE22 + les 9 tables aval »). Le code est correct ; c'est le texte frozen de la spec qui contient une déviation non documentée par une note datée, contrairement à la note de substitution AC-FR21-5 déjà présente dans les Design Notes (règle d'audit n°1 de l'Acceptance Auditor).
- **Action corrective :** Ajouter une note de renégociation datée dans les Design Notes de `spec-4-7-e2e-suite-downstream-tables.md` (même format que la note AC-FR21-5 du 2026-09-17) reconnaissant que le compte réel est 10 tables avales / 11 au total, et référençant la coquille préexistante dans AD-1. La correction du libellé AD-1 lui-même dans `project-profile.md`/`epics.md` est hors périmètre de ce patch (voir Defer D-1).

## 4. Findings Defer

### D-1 — Coquille préexistante « 9 tables aval » dans AD-1 (project-profile.md / epics.md)
- **Justification :** `project-profile.md` (AD-1) et `epics.md:189-190` disent « L_D_KAPE22, L_D_LOG_COMMANDE et les 9 tables aval » en énumérant pourtant 10 tables (`L_D_ORDRE_FABRICATION`, `L_D_COULEE`, `L_D_CONSIGNES`, 7×`L_D_SECTIONCHARGE_*`). Cette coquille précède la Story 4.7 (héritée de la Story 4.1/4.6) et dépasse le périmètre « test-only » de cette story. Non bloquant — le code s'appuie sur le compte réel (10), pas sur le libellé erroné.

### D-2 — Les tests de rejet contournent `InboxScanner` ; aucune preuve de déplacement vers `error/`
- **Justification :** Les 3 tests de `RejectionAtomicityIntegrationTests.cs` appellent `Kape22FichierProcessor.Import` directement (comme `DoubleJournalIntegrationTests` le fait déjà), jamais via `InboxScanner.RunTick()` — contrairement au test SM-2 du chemin de succès. Aucune assertion ne prouve qu'un Fichier rejeté est déplacé vers `error/` avec son sidecar, ni que `inbox`/`processing` sont vides après un rejet. Ce raccourci est le même que celui déjà accepté par décision humaine dans les Design Notes de la spec (2026-09-17) pour la substitution de canal AC-FR21-5 ; l'étendre à une preuve complète du cycle de vie dossier sur le chemin de rejet demanderait un nouveau test dédié, hors du périmètre « test-only, pas de nouvelle plomberie » de cette story. Non bloquant.

### D-3 — Les assertions sur les 9 tables avales (SM-2) ne vérifient que la présence/le compte par OF, pas les valeurs de champs
- **Justification :** `EndToEndImportIntegrationTests.cs:130-160` ne vérifie que « une ligne existe (ou 0/1) pour cet OF », jamais les valeurs de colonnes des 9 tables avales elles-mêmes (contrairement à `L_D_KAPE22` où Client/Coulee/Nuance sont comparés). C'est exactement le patron imposé par la spec (« Reuse existing assertion patterns... the count matches bundle.X is null ? 0 : 1 pattern from `TransactionalPersistenceTests.cs` ») — confirmé identique aux lignes 390-394 de ce fichier précédent. Pas une régression introduite par cette story ; changer ce niveau de profondeur violerait la contrainte « Always: reuse existing assertion patterns rather than inventing new plumbing ». Non bloquant.

## 5. Findings rejetés (bruit)

1. **Design Notes déjà couvre la substitution errors.json** — la note du 2026-09-17 dans la spec documente déjà, avec date et raisonnement, pourquoi aucun `*.errors.json` n'est produit/vérifié ; satisfait la règle « toute déviation doit avoir une note de renégociation datée ».
2. **`OrdreFabricationRows`/`CouleeRows` en `Assert.Single` non conditionnel vs pattern `is null ? 0 : 1`** — faux positif : `OrdreFabricationMapper.Map`/`CouleeMapper.Map` retournent des types non-nullable ; dans `Kape22ImportBundle`, ces deux champs ne sont `null` que dans le court-circuit total (échec du mapping), déjà écarté par `Assert.True(bundle.Success, ...)` avant l'assertion. L'asymétrie avec les tables `SectionCharge*`/`Consignes` (réellement optionnelles par règle métier) est correcte par conception.
3. **`expected.Kape22!` sans message d'assertion dédié** — faux positif : `Assert.True(bundle.Success, $"{fichierName} failed Step 2.")` s'exécute et échoue avec message avant tout déréférencement, donc aucune régression de diagnosticabilité en pratique.
4. **`WithDetailChamp` serait de la « plomberie non documentée »** — faux positif : l'helper existe déjà dans `TestSupport.cs:185`, non touché par ce diff ; c'est bien la réutilisation d'un helper préexistant, pas une invention.
5. **`review_loop_iteration: 0` resté à 0 malgré une revue documentée** — faux positif : convention établie dans tout le dépôt (`spec-4-4`, `spec-4-5`, etc. restent aussi à 0 après leurs propres passes de revue) ; ce compteur ne suit pas les Design Notes post-revue.
6. **`deferred-work.md` sans entrée pour la substitution de canal AC-FR21-5** — pas d'action requise : c'est une décision fermée et acceptée (documentée dans les Design Notes de la spec), pas un travail différé au sens du ledger.
7. **Commentaire citant AC-FR11-3/AC-FR21-2/AC-FR21-5 alors qu'un seul trait `FR21-5` est posé** — faux positif : convention établie dans tout le fichier (chaque commentaire de test cite le contexte métier élargi, le trait ne portant que l'AC principal comptée par la porte de couverture) ; les 3 méthodes suivent ce même style.
8. **Horloges différentes (`FixedClock` vs `WinterClock`) entre les deux fichiers de test** — faux positif : chacune est la convention préexistante et documentée de son propre fichier (`Now` fixe le dossier d'archive `2026/09` dans le test E2E successeur ; `WinterClock()` est « the clock most mapper tests want » pour les fixtures `MapMutatedBundle`).
9. **Offsets d'octets magiques (146/12, 417/2) sans auto-vérification** — vérifié faux positif par la lentille Verification Gap : les offsets correspondent exactement à `Templates/P60.xml`, et le patron (offset documenté par commentaire seul) est déjà celui de `WithDetailChamp` ailleurs dans le dépôt.
10. **« Suggested Review Order » épingle des numéros de ligne sans détection de dérive** — hors mandat : section d'aide à la revue dans la spec, aucune conséquence sur le code ou les tests.
11. **`expected.Consignes` pourrait déclencher une NRE (Edge Case Hunter)** — faux positif : `Consignes` est un `List<L_D_CONSIGNES>` non-nullable avec valeur par défaut `= []` dans `Kape22ImportBundle.cs:16` ; ne peut jamais être `null`.

## 6. Auto-vérifications

**4 lentilles lancées :**
- Blind Hunter — 14 findings bruts soumis, 2 retenus (fusionnés en P-1/P-2/D-1), 1 retenu en D-2, 1 retenu en D-3, 9 rejetés après lecture du code source (mappers, TestSupport.cs, autres specs, epics.md, AscoLsiDbContext.cs).
- Edge Case Hunter — 1 finding soumis (NRE potentielle sur `Consignes`), rejeté après lecture de `Kape22ImportBundle.cs` (type non-nullable, défaut `[]`).
- Verification Gap Reviewer — 0 finding ; a positivement confirmé (traçage sur fichiers réels) : cohérence `AcCountByFr[21]=5`, alignement AD-1 des tests avec `Kape22FichierProcessor`/`Kape22Persister`/`Kape22ImportBundleMapper`, exactitude des offsets `Templates/P60.xml`, non-nouveauté des patrons Serilog/assertion réutilisés.
- Acceptance Auditor — 1 finding JSON soumis (sévérité `Patch`, incohérence de comptage AD-1/frozen-spec), fusionné dans P-2/D-1 ; `out_of_mandate` : 2 items déjà suivis dans `deferred-work.md` correctement non re-signalés.

**Diff stats :** 6 fichiers modifiés, +437/−28 (557 lignes de diff unifié) :
- `_bmad-output/implementation-artifacts/deferred-work.md` (+24)
- `_bmad-output/implementation-artifacts/spec-4-7-e2e-suite-downstream-tables.md` (nouveau, +126)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (+2/−2)
- `tests/Kape22Importer.Tests/AcCoverageCompletenessTests.cs` (+4/−2)
- `tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs` (+48 net, refactor `Visible` → `Kape22ImportBundle`)
- `tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs` (nouveau, +231)

**baseline_commit de la spec :** `a7fb70de27eedeca3b244d4f2ecd014f12b9c508` — cohérent avec `14c41df^`.
