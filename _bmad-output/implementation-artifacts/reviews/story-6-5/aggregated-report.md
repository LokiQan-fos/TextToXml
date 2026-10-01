# Review report — Story 6.5

Range : 884bf4ca6e03815218a5fc489a473523ebc8fa7d^..HEAD (TextToXml) + MicroServices SVN r541:543
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-5-test-integration-client-gpao.md
Date : 2026-10-01
Verdict : REFUSÉ (initial) → ACCEPTÉ après résolution (voir § Résolution)
Findings : D=1 P=4 F=1 R=20  (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**REFUSÉ.** Le code de test est correct et couvre les 4 lignes de la matrice figée, pour les deux workers. Deux écarts bloquent toutefois l'acceptation :

- La frontière **Ask First** (« any change to existing test projects ») a été franchie sans trace d'accord humain (D-1).
- La propriété SVN `svn:global-ignores = *` posée sur le nouveau projet rend invisible tout fichier qu'on y ajouterait plus tard (P-1).

Le verdict sera revu une fois D-1 tranché et P-1 appliqué.

Sévérités après triage : high 0 · medium 2 (D-1, P-1) · low 4.

Note de périmètre : le range git ne contient que de la documentation. Le code de test vit dans MicroServices (SVN, r542 `.sln` + r543 projet), et la revue a porté sur le diff combiné `git diff 884bf4c^..HEAD` + `svn diff -r541:543`.

## 2. Findings Decision (à trancher par l'humain)

### D-1 — Projets de test existants modifiés sans gate Ask First (medium)
- **Source :** acceptance-auditor + blind-hunter
- **Localisation :** MicroServices `GPAO/ImportP60.Tests` et `GPAO/ConvertP89.Tests` (propriété `svn:global-ignores`, r543) ; spec §Boundaries & Constraints, Ask First
- **Description :** r543 ajoute `TestResults` à `svn:global-ignores` sur deux projets de test existants. Le spec classe « any change to existing test projects » en Ask First. Ni le spec, ni sa Verification, ni le ledger ne mentionnent ce changement ou un accord. Le changement est inoffensif sur le fond (il masque les dossiers `TestResults` générés), mais la frontière est explicite.
- **Options :**
  - (a) Ratifier : ajouter au spec, hors bloc figé, une note datée « Ask First accordé 2026-10-01 : `TestResults` ajouté à `svn:global-ignores` de ImportP60.Tests / ConvertP89.Tests ».
  - (b) Revenir en arrière : `svn propset` des deux dossiers à `bin`/`obj` dans une révision SVN séparée, puis livrer ce changement ailleurs.
- **État :** tranché le 2026-10-01 → **(a) ratifié.** Les dossiers `TestResults` contiennent les résultats d'une campagne de test et n'ont pas leur place dans un dépôt de sources ; l'utilisateur les a retirés du périmètre SVN. Une note datée « Ask First granted » a été ajoutée au spec, section Design Notes, hors bloc figé. Aucun changement de code.

## 3. Findings Patch

### P-1 — `svn:global-ignores = *` masque tout futur fichier du projet (medium)
- **Source :** blind-hunter + edge-case-hunter (+ acceptance-auditor, hors mandat)
- **Localisation :** MicroServices `GPAO/Gpao.IntegrationTests` (propriété `svn:global-ignores`, r543 : `*` + `TestResults`)
- **Description :** le motif `*` ignore tout élément non versionné du dossier. Un `.cs` ou un fichier de settings ajouté plus tard n'apparaîtra jamais dans `svn status` et risque de ne jamais être commité. Avec `*`, l'entrée `TestResults` ne sert à rien. Les projets voisins utilisent `bin`/`obj`/`TestResults`.
- **Action corrective :** `svn propset svn:global-ignores` avec trois lignes `TestResults` / `bin` / `obj` sur `GPAO/Gpao.IntegrationTests` (valeur identique à `GPAO/ImportP60.Tests`), puis commit SVN (par l'utilisateur).

### P-2 — « Contained failure » ne vérifie pas que l'exception nomme le dossier manquant (low)
- **Source :** acceptance-auditor + blind-hunter
- **Localisation :** MicroServices `GPAO/Gpao.IntegrationTests/GpaoClientIntegrationTests.cs:94`
- **Description :** la ligne figée de la matrice exige une ligne `Error` « whose exception names the folder ». L'assertion matche seulement le GUID de la racine temporaire. Une erreur portant sur un dossier frère (`export/`, `xml/`, `done/`, `error/`) la satisferait aussi. Le choix du segment GUID est justifié (la forme 8.3 de `%TEMP%` peut différer), mais rien n'empêche d'ajouter la feuille, que le GUID n'affecte pas.
- **Action corrective :** matcher `Path.Combine(Path.GetFileName(_root), worker == "P60" ? "inbox" : "source")` au lieu de `Path.GetFileName(_root)` (le séparateur `\` est un littéral pour `CHARINDEX`).

### P-3 — AC-FR25-7 et notes dev de la Story 6.5 non réconciliées (low)
- **Source :** acceptance-auditor
- **Localisation :** `_bmad-output/planning-artifacts/epics.md:3068` ; `_bmad-output/planning-artifacts/PRD.md:1155-1158`
- **Description :** les notes dev de epics.md disent « base `AscoLSI_Test` (sink SQL d'`AbstractService`), `[SkippableFact]` ». L'implémentation route le sink Logs vers `MQTTnetServices_Test` (spec, Design Notes) et utilise `[SkippableTheory]`. Le PRD n'indique que « (`AscoLSI_Test`) » entre parenthèses. C'est exact pour `ConnectionStrings:AscoLSI`, mais ambigu pour le sink.
- **Action corrective :** dans epics.md, remplacer par « `AscoLSI_Test` pour `ConnectionStrings:AscoLSI`, sink Logs sur `MQTTnetServices_Test` ; `[SkippableTheory]` si absente ». Dans le PRD, remplacer la parenthèse par « (`AscoLSI_Test`, logs sur `MQTTnetServices_Test`) ». À faire dans le commit de clôture.

### P-4 — Entrée de defer sans identifiant ni sévérité (low)
- **Source :** blind-hunter
- **Localisation :** `_bmad-output/implementation-artifacts/deferred-work.md:1420`
- **Description :** le spec (Suggested Review Order) l'appelle « D-1 », mais l'entrée du ledger ne porte ni identifiant ni sévérité, contrairement à ses voisines (`(F-2, low)`). Elle ne pointe pas non plus vers un rapport de revue.
- **Action corrective :** remplacer `source_spec` par `` `spec-6-5-test-integration-client-gpao.md` (D-1, low) ``.

## 4. Findings Defer

### F-1 — AC « no Logs row lands outside MQTTnetServices_Test » sans vérification (low)
- **Source :** blind-hunter
- **Localisation :** spec §Tasks & Acceptance, AC-1 ; `GpaoClientIntegrationTests.cs:36`
- **Justification :** le routage repose sur la variable d'environnement posée avant la construction du `Client`. `SharedLogger` met en cache un logger par couple (connection string, table) et relit la variable à chaque `GetDefault()`, donc le mécanisme est sain. Aucune assertion ne vérifie pourtant qu'aucune table `Logs` n'apparaît dans `AscoLSI_Test`. Une telle vérification demanderait une requête sur une seconde base. Elle n'est pas bloquante tant que le mécanisme n'est pas touché, et elle est à rattacher au D-1 de la Story 6.4-bis (flush du sink).

## 5. Findings rejetés (bruit)

1. **`status: 'done'` du spec vs `review` dans sprint-status** : convention du projet (specs 6.3 et 6.4-bis identiques). `/commit-review` synchronise.
2. **`review_loop_iteration: 0` alors que P-1..P-5 sont cités** : ces patchs viennent de la boucle interne de bmad-build. Écart cosmétique.
3. **`InitiatingServer` absent de `NewClient` alors que le Code Map le dit requis** : le constructeur passe `configuration[InitiatingServerKey]` (null) à `AscoLsiFichierJournal`, qui l'accepte (les tests passent). L'écart se limite au libellé du Code Map, sans conséquence.
4. **Mutation `Execute = null` planifiée mais pas enregistrée** : CC-1 exige un rouge par test, pas par assertion. Le test Frequency est rouge sous la mutation `Frequency`, et les 3 autres tests invoquent `Execute` et observent le comportement d'`Actions`.
5. **Mutation P60 `NullLoggerFactory` trop large** : `OnTickError` loggue via le `Logger` d'`AbstractService`, pas via `SerilogLoggerFactory`, donc « Contained failure » reste vert, comme le spec le décrit.
6. **`Stop() < 5 s` quasi vide** : c'est exactement la ligne figée de la matrice. Le tick en vol est déjà différé (D-1 6.5).
7. **« retried next tick » non testé** : il s'agit de la colonne Error Handling, pas d'Expected Output. Le comportement est couvert au niveau bibliothèque.
8. **Course sur `MICROSERVICE_LOG_CONNECTION_STRING` en parallèle** : le projet n'a qu'une classe, et xUnit exécute les tests d'une même classe en série. Spéculatif.
9. **Restauration inopérante si `SharedLogger` met en cache** : faux, le cache est indexé par connection string et la variable est relue à chaque appel (`SharedLogger.cs:40`).
10. **Probe `Lazy` qui cache le skip** : choix conforme à l'AC-2 (skip, pas échec).
11. **Gate sur le seul nom `_Test`, pas sur le serveur** : même gate que les fixtures TextToXml. Le settings versionné pointe sur `localhost`.
12. **`{column}` interpolé dans le SQL** : helper privé, appelé uniquement avec des littéraux.
13. **Ternaire `worker == "P60"` répété** : `InlineData` est fermé (`P60`/`P89`). Une table par worker ajouterait du code sans gain.
14. **Packages transitifs (SqlClient, Configuration.Json/Memory)** : même pattern que les projets de test voisins. Le build `-warnaserror` passe.
15. **`catch` vide dans `Dispose`** : best-effort documenté. Un dossier temporaire restant ne doit pas masquer le résultat.
16. **`Execute!` null-forgiving** : le test Frequency signale la cause si `Execute` n'est pas câblé.
17. **Ligne 1312 de deferred-work qui garde l'historique PARTIALLY/PLANNED** : convention d'historique du ledger.
18. **Ancres du Code Map sans révision SVN** : il s'agit de documentation de navigation, pas d'un contrat.
19. **Verification sans résultats datés** : la preuve CC-1 est datée. Les commandes sont les attendus standard des specs du projet.
20. **`Execute` sans borne de temps / mutation « Stop » qui écrirait dans `AscoLSI_Test`** : `--blame-hang-timeout 2m` est prescrit, et `AscoLSI_Test` est une base de test réinitialisable (écriture autorisée).

## Résolution (2026-10-01)

- **D-1 :** ratifié (note datée dans le spec, section Design Notes).
- **P-1 → appliqué :** `svn:global-ignores` de `GPAO/Gpao.IntegrationTests` = `TestResults` / `bin` / `obj` (copie de travail SVN, `M` en attente du commit SVN de l'utilisateur).
- **P-2 → appliqué :** `GpaoClientIntegrationTests.cs:93-96`, le test matche maintenant `<GUID>\inbox` ou `<GUID>\source`.
- **P-3 → appliqué :** `PRD.md:1156` (« `AscoLSI_Test`, logs sur `MQTTnetServices_Test` ») et `epics.md:3068-3069` (sink sur `MQTTnetServices_Test`, `[SkippableTheory]`).
- **P-4 → appliqué :** `deferred-work.md:1420`, ajout de `(D-1, low)`.
- **F-1 :** reste en defer, à reporter dans `deferred-work.md` par `/commit-review`.
- **Vérification :** `dotnet test GPAO/Gpao.IntegrationTests --blame-hang-timeout 2m -warnaserror` → 8 réussis, 0 échec, 0 ignoré (42 s).
- **Statut :** story `done` (sprint-status mis à jour). Verdict révisé : **ACCEPTÉ** après résolution.

## 6. Auto-vérifications

- **Lentilles lancées (4/4, aucune en échec) :**
  - blind-hunter : 25 findings
  - edge-case-hunter : 4 findings
  - verification-gap : 0 finding (« No verification gaps found »)
  - acceptance-auditor : 3 findings + 3 hors mandat
- **Déduplication :** global-ignores `*` (×3), Ask First sur les projets existants (×2), match du dossier (×2), statut spec/sprint (×2), gate serveur (×2), restauration de la variable (×2).
- **Vérifications faites dans le code :** `ImportP60/Client.cs:105-122` (InitiatingServer nullable) et `:300-307` (garde inbox lancée avant toute autre E/S) ; `MicroService/Logging/SharedLogger.cs:15-45` (cache par clé, variable relue) ; `svn propget svn:global-ignores GPAO/ImportP60.Tests` = `TestResults bin obj` ; `epics.md:3068` ; `PRD.md:1155-1158`.
- **Diff stats :**
  - TextToXml git (`884bf4c^..HEAD`) : 3 fichiers, +135/−3 (spec +126, deferred-work +5/−3, sprint-status +2/−2).
  - MicroServices SVN (`r541:543`) : 3 fichiers ajoutés (csproj 34, `GpaoClientIntegrationTests.cs` 265, `appsettings.Test.json` 6), `.sln` +15, 3 changements de propriété `svn:global-ignores`.
- **Conformité de forme :** 8 tests nommés `_AcFr25_7`, `[Trait("AC","FR25-7")]` + `[Trait("Category","Integration")]` sur chacun. Commentaires en anglais (CC-2). Connection strings uniquement dans `appsettings.Test.json` (CC-7). Ni constructeur interne, ni broker, ni Docker, ni Launcher (frontière Never respectée).

## Clôture (2026-10-01)

- D-1 → tranché (option a, ratifié) : note datée « Ask First granted » dans le spec, Design Notes.
- P-1 → appliqué : `svn:global-ignores` de `GPAO/Gpao.IntegrationTests` = `TestResults` / `bin` / `obj` (MicroServices, commit SVN fait par l'utilisateur).
- P-2 → appliqué : `GpaoClientIntegrationTests.cs:95`, le test matche `<GUID>\inbox` ou `<GUID>\source`.
- P-3 → appliqué : `PRD.md:1156`, `epics.md:3068-3069`.
- P-4 → appliqué : `deferred-work.md:1420`, `(D-1, low)`.
- F-1 → tracé dans `deferred-work.md` (pas de story cible ; à traiter avec le D-1 de la Story 6.4-bis).
- Vérification finale : `dotnet build TextToXml.sln -warnaserror` OK, 0 erreur. Unit : 1178 réussis (48 + 192 + 21 + 917), 0 échec. Integration (`-m:1`) : 1253 réussis, 18 ignorés, 0 échec (1251/18 Kape22Importer + 2 AscoLsiJournal). Côté MicroServices, `Gpao.IntegrationTests` : 8/8 (voir § Résolution).
- Statut : story 6-5 → done ; le commit de clôture est celui qui ajoute cette section.
