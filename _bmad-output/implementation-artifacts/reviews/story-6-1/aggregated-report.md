# Review report — Story 6.1

Range : f3d937d8874a5e0b45360b1e81b5c706965da558^..HEAD (f3d937d)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-1-journal-p60-via-ifichierjournal.md
Date : 2026-09-28
Verdict : ACCEPTÉ
Findings : D=0 P=4 F=1 R=28  (Decision, Patch, Defer, Rejetés)
Mise à jour 2026-09-28 : P-1..P-4 appliqués dans l'arbre de travail (option « tout appliquer », non commité) : `InboxScanner.cs:221-224`, `DoubleJournalTests.cs:216`, en-têtes de `TransactionalPersistenceTests`, `RejectionAtomicityIntegrationTests`, `PersisterConfigurationTests` et `DoubleJournalIntegrationTests`, `deferred-work.md` (entrée `Import:Commande`). Vérification : `dotnet build TextToXml.sln -warnaserror` donne 0 warning et 0 erreur. Les tests n'ont pas été relancés (seuls des commentaires et des espaces ont changé). F-1 reste à consigner par `/commit-review`, qui passera aussi sprint-status à `done`.

## 1. Verdict

**ACCEPTÉ**, à condition d'appliquer P-1..P-4 dans le commit de clôture (`/commit-review`). Les 4 patches sont low : un commentaire périmé, de la mise en forme et une entrée de dette incomplète. Aucun ne touche au comportement.

Constats qui fondent le verdict :
- **Bloc figé respecté.** L'Acceptance Auditor ne relève aucun finding. Les écarts D1 et D2 sont couverts par la note de renégociation datée du 2026-09-28. `Kape22Importer` ne référence que `FichierJournal`. `ParisTime` est conservé. Les clés `Import:Commande` et `Import:InitiatingServer` sont inchangées.
- **Matrice couverte.** Chaque ligne de la matrice I/O a son test dans `FichierJournalMigrationTests`. Le cas « journal en panne après commit » a son test dans `TransactionalPersistenceTests`. La violation de PK réelle est testée dans `RejectionAtomicityIntegrationTests`, et les golden values AC-FR24-6 dans `JournalMessageParityIntegrationTests`.
- **CC-2 / CC-4 / CC-7.** Aucune déviation relevée par l'auditeur. Commentaires en anglais, pas de chaîne de connexion en dur.
- **Côté hôte vérifié hors diff (SVN).**
  - `GpaoImportP60/Client.cs:87-99` compose `AscoLsiFichierJournal` et remonte une `InvalidOperationException` qui nomme la clé.
  - `ConvertP89.Tests/RunTickCoreTests.cs:115` implémente `HasSuccess`.

| Sévérité | Nombre |
|---|---|
| high | 0 |
| medium | 0 |
| low | 5 (P-1..P-4, F-1) |

## 2. Findings Decision — à trancher par l'humain

Aucun.

## 3. Findings Patch

### P-1 — Commentaire périmé à l'appel de `IsPersistenceFailure` (low)
- Source : verification-gap
- Localisation : `src/Kape22Importer/InboxScanner.cs:221-224`
- Constat : le commentaire du site d'appel dit toujours « Kape22Persister caught a DbException … AscoLSI is unreachable ». Depuis la Story 6.1, la même branche reçoit aussi un échec de lecture ou d'écriture du journal, y compris après un commit réussi (`[JournalPending]`). Le commentaire du helper (`:313-320`) a été mis à jour, celui-ci non : les deux se contredisent.
- Action : reformuler le commentaire pour couvrir les deux cas (AscoLSI injoignable, ou journal du Fichier non lu ou non écrit, AC-FR24-5) et renvoyer au commentaire du helper.

### P-2 — Espace manquant après une virgule (low)
- Source : blind-hunter + acceptance-auditor (hors mandat)
- Localisation : `tests/Kape22Importer.Tests/DoubleJournalTests.cs:216`
- Constat : `Processor(contexts,new RecordingLogger<Kape22FichierProcessor>())`
- Action : `Processor(contexts, new RecordingLogger<Kape22FichierProcessor>())`

### P-3 — En-têtes de commentaire modifiés mais pas re-wrappés (low)
- Source : blind-hunter + acceptance-auditor (hors mandat)
- Localisation :
  - `tests/Kape22Importer.Tests/DoubleJournalIntegrationTests.cs:25` (151 car.)
  - `tests/Kape22Importer.Tests/PersisterConfigurationTests.cs:17` (135 car.)
  - `tests/Kape22Importer.Tests/TransactionalPersistenceTests.cs:23`
  - `tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs:24-27` (une ligne courte orpheline suivie d'une ligne longue)
- Constat : les lignes éditées par la story dépassent la largeur habituelle (~105 car.) du reste de chaque bloc.
- Action : re-wrapper ces blocs à la largeur de leurs voisins, sans changer le texte.

### P-4 — L'entrée de dette `Import:Commande` sous-estime l'impact (low)
- Source : blind-hunter
- Localisation : `_bmad-output/implementation-artifacts/deferred-work.md:1335` (section « Deferred from: code review of story-6.1 »)
- Constat : l'entrée ne cite que les Fichiers commités qui bouclent. Avec un `Import:Commande` trop long, chaque écriture du journal échoue, donc chaque rejet métier avec un OF lisible reste lui aussi dans `processing/` et journalise `ImportRejected` en Error à chaque tick.
- Action : compléter le `summary:` avec ce second effet. La cible reste la Story 6.4 (AC-FR25-4).

## 4. Findings Defer

### F-1 — Un échec déterministe du journal fait boucler le Fichier sans limite (low)
- Source : edge-case-hunter + blind-hunter
- Localisation : `src/Kape22Importer/Persistence/Kape22Persister.cs:279-306` (`Recorded`, `JournalFailure`) et `:244` (`CompleteAlreadyImported`)
- Constat : toute exception du journal devient un `PersistenceError` File-level, donc réessayable (troncature, argument invalide, NRE comprises). Un défaut déterministe laisse le Fichier dans `processing/` sans borne, au lieu de le mettre en quarantaine dans `error/`.
- Justification du report :
  - Ce catch large est un choix explicite du spec : « same choice as `P89FolderConverter.TryRecord` », commentaire `:292-295`. Il est partagé avec P89 (Epic 5).
  - Un filtre par type d'exception ne suffirait pas : un serveur injoignable remonte plusieurs types.
  - La bonne correction est un plafond de tentatives par Fichier dans la boucle du worker. Cela relève du durcissement (Story 6.4), que la frontière « Never » du spec exclut de la 6.1.
  - Le seul déclencheur réaliste connu (`Import:Commande` trop long) est déjà reporté à la 6.4.

## 5. Findings rejetés (bruit)

| # | Source | Finding | Justification du rejet |
|---|---|---|---|
| R-1 | edge | Un `DbException` à la lecture du guard ou de `CouleeRows` écrit une ligne REJETÉ | Même traitement que l'échec SQL : l'AC-FR24-3 demande une entrée pour chaque issue avec un OF lisible. Le spec accepte une ligne REJETÉ par tick pour un échec SQL. Si la base est injoignable, le journal échoue aussi et rien n'est écrit. |
| R-2 | edge | Un échec transitoire de `SaveChanges` laisse une ligne REJETÉ puis une ligne OK | La ligne décrit fidèlement la tentative. Plafond accepté dans les Design Notes du spec. |
| R-3 | edge + blind | Pas de `ThrowIfNull` sur `journal` et `fichierName` (persister et processor) | Convention du code : les constructeurs primaires ne gardent aucune dépendance (`newContext` et `configuration` non plus). L'hôte compose toujours un journal non nul, et `Import` garde déjà `fichierName` (`ThrowIfNullOrEmpty`). |
| R-4 | edge + blind + auditor | `HasSuccess` filtre sur `Commande` : changer `Import:Commande` entre deux runs double la ligne OK | La Code Map du spec fige la requête « `Commande` + `OF` + OK message ». Il faudrait modifier la config entre un crash et le tick suivant. Conséquence : au pire une ligne OK en double, pas de doublon métier. |
| R-5 | edge + blind | Course entre `HasSuccess`/`Record` et le guard D22 (pas de contrainte d'unicité) | Préexistant : le guard était déjà une lecture séparée. Un seul worker réclame les Fichiers via le déplacement vers `processing/`. Le recouvrement de ticks relève de la Story 6.4. |
| R-6 | edge + blind | Les fakes MicroServices ne sont pas dans le diff | Hors du dépôt git (SVN). Vérifié : `RunTickCoreTests.cs:115` implémente `HasSuccess`. |
| R-7 | edge | Suppression de `ResolveUser` : un `User` trop long n'échoue qu'à l'écriture | Faux : le contrôle vit dans le constructeur `AscoLsiFichierJournal`, appelé au démarrage par `GpaoImportP60/Client.cs:87-99`. |
| R-8 | blind | Un guard hit sur une ligne legacy écrit un OK au nom de ce worker | Comportement prescrit par la matrice : « Guard hit, no success entry … legacy row ⇒ Record success ». |
| R-9 | blind | `HasSuccess` scanne `Message` NVARCHAR(MAX) | Appelé seulement sur un guard hit (rare), avec un filtre sur `Commande` et `OF`. Aucune mesure ne montre de problème. |
| R-10 | blind | `OF` : comparaison exacte en InMemory, sans espaces finaux en SQL | Aucune ligne `P60 … — OK` n'existe en production (Design Notes). Le persister passe l'OF tel que mappé ou trimé. Aucun scénario atteignable. |
| R-11 | blind | `HasSuccess` n'a pas sa place sur l'interface commune (le fake P89 lève) | Contrat figé par le spec (« `IFichierJournal` gains `bool HasSuccess` »). |
| R-12 | blind | Un rejet dont le journal échoue logue `ImportRejected` à chaque tick | Même forme qu'avant la story pour un échec SQL. Le rejet est réel et l'erreur de journal figure dans la liste. InboxScanner logue « will retry ». |
| R-13 | blind | `JournalPending` ne distingue pas insert frais et guard hit | Le champ `InsertedId` du message le distingue (vide sur un guard hit). |
| R-14 | blind | Deux `PersistenceError` si la base est injoignable | Forme correcte (deux causes, deux entrées). Aucun consommateur ne compte les erreurs. |
| R-15 | blind | Le message SQL brut peut contenir la valeur tronquée | C'est l'intention de l'AC-FR24-3 (« SQL Server's own message »). Les pré-contrôles C-4 et B-5 écartent la troncature en amont. |
| R-16 | blind | Le test unitaire « SQL failure » enveloppe une `InvalidOperationException` | Le test d'intégration `RejectionAtomicityIntegrationTests` provoque une vraie violation de PK et relit la cause. |
| R-17 | blind | Pas de test P60 pour la règle D1 (`NumeroFichier` vide) | La règle vit dans `AscoLsiFichierJournal` (F-1 Story 5.0), qui la teste. Le persister passe `NumeroFichier` tel quel. |
| R-18 | blind + auditor | L'épinglage `Date` → `datetime` change aussi l'écriture P89 | La colonne réelle est `datetime` : l'arrondi avait déjà lieu côté serveur. Correction documentée dans les Implementation notes, et le test de parité de schéma la verrouille. |
| R-19 | blind | Les golden values omettent les messages SQL et D1 | L'AC-FR24-6 porte sur les Fichiers de référence, qui ne produisent ni l'un ni l'autre. |
| R-20 | blind | `ReadProductionOperatorChanges` contourne une garde lecture seule | Même motif que `ReadProductionRows` (`UseSqlServer` + `AsNoTracking`, SELECT uniquement). Il n'existe aucune garde à contourner. |
| R-21 | blind | `InMemoryContextFactory` dépend d'une constante de fixture, et `AFS017` est dupliqué | Cosmétique, tests seulement, sans effet. |
| R-22 | blind | Les tests posent encore `Import:InitiatingServer` | Clé toujours valide côté hôte (le spec la garde inchangée). Réglage inerte et inoffensif. |
| R-23 | blind | `ImportOptions.InitiatingServer` n'est plus lu par la bibliothèque | Prescrit par la Code Map (« now read by the host »), et le commentaire le dit. |
| R-24 | blind | Tests de schéma `AscoLsiJournal` dans le projet importer | Déplacement voulu (Implementation notes : « relocated schema-parity test »). |
| R-25 | blind | `sprint-status` = review alors que le spec dit `done` | Flux normal : `/commit-review` synchronise sprint-status. |
| R-26 | blind | Le lien de Suggested Review Order sort du dépôt | Renvoi volontaire vers MicroServices (SVN), même pratique que la Story 5.2. |
| R-27 | auditor | Suffixe de nom de test ≠ tous les traits portés | Aucune règle n'exige un suffixe par trait. AcTraitCoverage ne vérifie que la présence du trait. |
| R-28 | auditor | Ligne courte dans le commentaire d'en-tête de `ImportResult.cs:9` | Dans la largeur du bloc, texte correct. Non actionnable. |

## 6. Auto-vérifications

- **Lentilles lancées : 4/4, aucun échec.**
  - blind-hunter : environ 25 findings.
  - edge-case-hunter : 9.
  - verification-gap : 0 écart de vérification, 1 autre finding.
  - acceptance-auditor : `[]`, plus 6 points hors mandat.
- **Écart de procédure.** Le diff (187 Ko) n'a pas été recopié dans les prompts. Chaque lentille a lu le même patch figé (`scratchpad/diff-6-1.patch`, produit par `git diff f3d937d^..HEAD`). La blind-hunter avait pour consigne de ne lire aucun autre fichier.
- **Diff stats.** 45 fichiers, +1332 / −438, 3211 lignes de diff. Au-dessus du seuil de 3000 lignes, passage en un seul lot confirmé par l'utilisateur.
- **Vérification au triage.**
  - Code lu : `Kape22Persister.cs`, `AscoLsiFichierJournal.cs`, `Kape22FichierProcessor.cs` (Journal), `InboxScanner.cs:212-230` et `:308-325`, l'ancien `OkLogRowExists` (baseline), `ReadProductionRows` et `ReadProductionOperatorChanges`.
  - Hors diff (SVN) : `GpaoImportP60/Client.cs` et `ConvertP89.Tests/RunTickCoreTests.cs`.
- **Tests non relancés** pendant cette revue (aucun finding n'en dépend).
