# Review report — Story 6.8

Range : 9f9a8e5^..HEAD (git TextToXml) + `GpaoClientIntegrationTests.cs` (MicroServices, SVN, lu hors diff)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-6-8-flush-sink-logs-assertions-harnais-e2e.md
Date : 2026-10-02
Verdict : REFUSÉ (patches P-1, P-2 appliqués le 2026-10-02, voir §7)
Findings : D=0 P=2 F=0 R=22  (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**REFUSÉ** : deux Patch de gravité medium restent ouverts, tous deux dans `scripts/e2e-worker-import.ps1` :

- l'assertion « tick rows » est satisfaite par les lignes de démarrage du worker, donc elle ne prouve pas ce qu'elle annonce ;
- sur les chemins d'échec, les polls enchaînés peuvent dépasser `--blame-hang-timeout 2m`. Le run est alors tué, `finally` ne s'exécute pas, et le Launcher reste orphelin avec la config patchée.

Le frozen block, les boundaries (Ask First, Never), AD-3, CC-2 et CC-7 sont respectés. Aucune modification de code de production : `svn status` sur MicroServices ne montre aucun fichier modifié. Le test de routage MicroServices (AC-3) existe et porte `[Trait("AC","FR25-7")]`.

| Gravité | Nombre | IDs |
|---|---|---|
| high | 0 | — |
| medium | 2 | P-1, P-2 |
| low | 0 | — |

Note : la spec contient déjà une section « Review Findings » (D-1, P-1..P-6, W-1). Elle vient d'une revue faite pendant le build, avant le commit `9f9a8e5`. Le présent rapport porte sur l'état commité. W-1 est déjà au ledger et n'est pas recompté ici.

## 2. Decision (à trancher par l'humain)

Aucune.

## 3. Patch

### P-1 — L'assertion « GpaoImportP60 tick row » passe sur les lignes de démarrage (medium)

- **Localisation :** `scripts/e2e-worker-import.ps1:260`
- **Sources :** verification-gap, edge-case-hunter
- **Constat :** le filtre est `LEFT(Message, 15) = '[GpaoImportP60 ' AND CHARINDEX('WorkerLifecycle', Message) = 0`. Au démarrage, le worker écrit deux lignes `GeneralInformation` : `Connecting …` et `Starting …` (`MicroServices/GPAO/ImportP60/Client.cs`, `CreateAsync`). Ces lignes sont écrites après `$logsIdBefore`, puisque la baseline est lue avant le lancement du Launcher. Elles portent le préfixe `[GpaoImportP60 ` et ne contiennent pas `WorkerLifecycle`. L'assertion est donc remplie avant tout tick. Supprimer le `LogInformation(…, "Executing import tick.")` du tick laisse le script vert. Le commentaire du script (« written by the worker during its tick ») et l'entrée P-1 de la spec (« Tick rows only ») décrivent une garantie qui n'existe pas. Aucune des mutations de D-1 ne visait ce filtre.
- **Atténuation :** le contrôle `processed: "archived"` par Fichier détecte toujours un worker qui ne journalise plus du tout.
- **Action corrective :** remplacer le filtre par `LEFT(Message, 15) = '[GpaoImportP60 ' AND CHARINDEX('Executing import tick.', Message) > 0`. Faire un run de mutation (retirer la ligne `Executing import tick.` de `Client.cs`, constater l'échec, revenir en arrière) et le consigner dans la spec. Ajuster le commentaire.

### P-2 — Les polls post-marqueur additionnent leurs timeouts et peuvent dépasser la limite du hang-timeout (medium)

- **Localisation :** `scripts/e2e-worker-import.ps1:104-116` (`Wait-LogsRow`), `:259-268`
- **Sources :** edge-case-hunter, blind-hunter
- **Constat :** chaque appel à `Wait-LogsRow` repart avec un délai neuf de `$PollTimeoutSeconds` (30 s). Il y a un appel pour le marqueur, un pour les ticks, puis un par Fichier, auxquels s'ajoutent le stop (`-TimeoutSec 30`) et l'étape 4 (30 s). Sur un chemin d'échec, par exemple FICHIER_UNLOGGED avec deux Fichiers, cela fait au moins 60 s de polls inutiles en plus des builds et de l'étape 4. Le total peut dépasser `--blame-hang-timeout 2m`. Le test est alors tué au lieu d'échouer avec son message. `finally` ne s'exécute pas, ce qui orphelinise le Launcher (port 5050) et laisse `GpaoImportP60.json` patché (voir la mémoire integration-run-orphans). Ces polls sont aussi inutiles par construction : selon les Design Notes de la spec, une fois le marqueur `Worker stopped.` arrivé, toutes les lignes antérieures du worker sont déjà écrites.
- **Action corrective :** ne poller que le marqueur. Les contrôles suivants (ticks, `processed` par Fichier) font une seule lecture, via un paramètre `-Once` de `Wait-LogsRow` ou une petite fonction de comptage. Le commentaire doit citer la garantie FIFO. Les mutations de D-1 sur le filtre `processed` doivent toujours échouer avec leur message nommé.

## 4. Defer

Aucun nouveau. W-1 (nettoyage autonome de `L_D_CONSIGNES`) est déjà au ledger depuis la revue du build.

## 5. Rejetés (bruit)

| # | Source | Constat | Justification du rejet |
|---|---|---|---|
| R-1 | blind-hunter | Les exports restants feraient échouer un 2e run | Faux : l'étape 1 supprime `$exportPath` avant le seed (`e2e-worker-import.ps1:143`). |
| R-2 | blind-hunter | L'exclusion `WorkerLifecycle` ne marcherait pas si l'id est numérique | Faux : `AppLogEvents.WorkerLifecycle = new(5000, "WorkerLifecycle")`, et `EventId.ToString()` rend le nom (confirmé par l'acceptance-auditor). |
| R-3 | blind-hunter | L'espace final de `'[GpaoImportP60 '` est ignoré par `=` | Sans effet : `LEFT(…, 15)` prend le 15e caractère, donc un nom plus long (`[GpaoImportP60B`) ne matche pas. |
| R-4 | blind-hunter, acceptance-auditor | Spec `status: 'done'` alors que sprint-status indique `review` | Cycle normal : `/commit-review` passe la story à `done` dans sprint-status à la clôture. |
| R-5 | blind-hunter | Le test MicroServices est absent du diff | Il vit sous SVN. Fichier lu par l'auditor (AC-3 couvert, trait présent), `svn status` propre. |
| R-6 | blind-hunter | Section Verification (« red after task 1 ») et Change Log non mis à jour | La résolution D-1 de la spec documente l'écart et la stratégie de mutation. Rien n'est perdu. |
| R-7 | blind-hunter | W-1 devrait être corrigé dans cette story | Déjà tranché (defer) lors de la revue du build. Pré-existant depuis Epic 4. |
| R-8 | blind-hunter | « Exactly one processed » suppose un seul sink partagé | Vérifié : `SerilogLoggerFactory(Logger)` et `_client.LogInformation` passent par le même `AbstractService.Logger`. |
| R-9 | blind-hunter | Le match `processed` dépend du rendu Serilog de `{Outcome}` | Couplage voulu (P-2 du build). Épinglé par le run vert et la mutation `archived`. |
| R-10 | blind-hunter | Un seul `$PollTimeoutSeconds` pour trois attentes ; écart spec/code | La valeur par défaut est 30 (`:36`), conforme à la spec. Le volet budget est traité par P-2. |
| R-11 | blind-hunter, edge-case-hunter | Résultat du stop non vérifié ; 200 sans log si `_client` est null | Le run échoue quand même (timeout du marqueur, message nommé). Cas hors du chemin nominal. |
| R-12 | edge-case-hunter | Un ancien marqueur `Worker stopped.` (redémarrage pendant le run) satisferait le poll | Improbable sur un run de 30 s sans restart. Un restart laisserait aussi les lignes `processed` déjà écrites. |
| R-13 | blind-hunter | Dossier d'export absent : le message dit « found 0 » | Le message nomme `$exportPath`. Diagnostic suffisant. |
| R-14 | blind-hunter | `MSBUILDDISABLENODEREUSE` non restauré dans `finally` | Variable de processus du `pwsh` enfant. Même régime que `MICROSERVICE_LOG_CONNECTION_STRING`. |
| R-15 | blind-hunter | Échappement SQL partiel des noms de Fichier | Noms de fixtures contrôlés (`P60_847_682_*`). Pas de frontière de confiance. |
| R-16 | blind-hunter | `Wait-LogsRow` dépend de variables de script | Style. Appelée uniquement après la baseline, dans le `try`. |
| R-17 | blind-hunter | L'AC-2 ne couvre pas le contrôle `processed` par Fichier | Texte du frozen block. Le script va au-delà via la matrice FICHIER_UNLOGGED. |
| R-18 | blind-hunter, acceptance-auditor | `epic-6-context.md` : exigences 6.1 condensées, la note « Risk to confirm » est retirée | Exigences de stories terminées (6.1). La source de vérité reste PRD + spec-6-1. La note de risque était périmée (tranchée en 6.1). |
| R-19 | blind-hunter | Numéros de ligne de la Code Map périmés | La Code Map est écrite avant l'implémentation. La Suggested Review Order porte les lignes à jour. |
| R-20 | edge-case-hunter | `-PollTimeoutSeconds 0` donne `-TimeoutSec 0` (infini) ; `-Fichiers` en doublon | Paramètres d'appel manuel. Le test passe les valeurs par défaut. |
| R-21 | acceptance-auditor | CC-4 : `Wait-LogsRow` inséré hors ordre alphabétique des fonctions | Hors périmètre de CC-4, qui gouverne les déclarations et initialiseurs de propriétés (précédent, mémoire). Les scripts ne suivent pas cet ordre (`gen.ps1` : Get, Build, Build). |
| R-22 | acceptance-auditor (hors mandat) | Erreur d'export « extra » sans nom de Fichier ; assertion du resolver tautologique ; un seul autre DB vérifié | Un fichier étranger n'a pas de Fichier à nommer (les fichiers sont nommés, P-5 du build). Le resolver prouve la priorité env > settings. Un seul autre DB, conformément au périmètre de la tâche. |

## 6. Auto-vérifications

- **Lentilles lancées : 4/4.**
  - blind-hunter : 19 constats.
  - edge-case-hunter : 6 constats.
  - verification-gap : 1 constat principal et 1 annexe.
  - acceptance-auditor : 1 finding et 5 hors mandat.
- Aucune lentille en échec (`failed_layers` vide).
- **Diff stats :** 5 fichiers, +203 / −23 (332 lignes de diff). Seul fichier de code : `scripts/e2e-worker-import.ps1` (+74/−23 env.). Le reste est documentaire : spec, `deferred-work.md`, `epic-6-context.md`, `sprint-status.yaml`.
- **Vérifications dans le code avant notation :**
  - `Client.cs` (lignes `Connecting`, `Starting`, `Executing import tick.`) ;
  - `AbstractService.LogInformation` (format du préfixe) ;
  - `AppLogEvents` (EventIds nommés) ;
  - `e2e-worker-import.ps1:36` (`$PollTimeoutSeconds = 30`) et `:141-143` (purge des exports) ;
  - ordre des fonctions dans `scripts/*.ps1`.
- Aucun test n'a été relancé pendant cette revue (revue statique).

## 7. Application des patches (2026-10-02, choix utilisateur : « appliquer tous les patches »)

- **P-1 — appliqué.**
  - `scripts/e2e-worker-import.ps1:262` : le filtre est devenu `CHARINDEX('Executing import tick.', Message) > 0`, et le commentaire est corrigé (les lignes de démarrage ne suffisent plus à satisfaire le contrôle).
  - Mutation : la ligne `Executing import tick.` est retirée de `MicroServices/GPAO/ImportP60/Client.cs`. Le test échoue avec « No GpaoImportP60 tick row was written to MQTTnetServices_Test after the flush marker. ». Le fichier a ensuite été restauré et `svn status` reste inchangé.
  - Piège rencontré : `Copy-Item` conserve la date de modification de la sauvegarde. Le build incrémental du Launcher a donc réutilisé le binaire muté, et le run suivant a échoué à tort. Corrigé en mettant à jour la date du fichier.
- **P-2 — appliqué.**
  - `Wait-LogsRow` gagne un switch `-Once`, qui fait une seule lecture et produit le message « after the flush marker ».
  - Seul le marqueur `Worker stopped.` est encore pollé. Les contrôles des ticks et des lignes `processed` par Fichier lisent une seule fois.
  - Mutation `'archived'` → `'archivedX'` : le test échoue en 19 s avec « No 'P60_847_682_081 processed: archived' row … after the flush marker. ». Le script a ensuite été restauré.
- **Vérification :** `dotnet test tests/Kape22Importer.Tests --filter "FullyQualifiedName~GpaoImportP60WorkerEndToEndTests" --blame-hang-timeout 2m` passe (vert, 33 s) après restauration. Aucun Launcher orphelin sur le port 5050.
- **Suite complète (2026-10-02) :** `dotnet build TextToXml.sln -warnaserror` donne 0 erreur. `dotnet test TextToXml.sln --filter Category=Integration -m:1 --blame-hang-timeout 2m` : Kape22Importer.Tests 1396 réussis, 0 échec, 20 ignorés ; AscoLsiJournal.Tests 2/2. Les tests ignorés sont des cas de parité production sans ligne prod comparable (Fichiers 059, 060, 220, 267-269, 286, 338, 339, 428), ignorés par conception. Le run inclut les nouveaux Fichiers prod `P60_847_682_409..461`.

## Clôture (2026-10-02)

- P-1 → appliqué (filtre `Executing import tick.`, mutation rouge consignée dans la spec).
- P-2 → appliqué (seul le marqueur `Worker stopped.` est pollé, contrôles suivants en `-Once`).
- Aucune Decision, aucun nouveau Defer (W-1 déjà au ledger).
- Vérification finale : `dotnet build TextToXml.sln -warnaserror` 0 avertissement, 0 erreur ; Unit 1253 réussis, 0 échec (TextToXml 192, Kape22Importer 984, P89Converter 56, AscoLsiJournal 21) ; Integration `-m:1` 1398 réussis, 0 échec, 20 ignorés (Kape22Importer 1396/20, AscoLsiJournal 2).
- Statut : story 6-8 → done ; epic-6 → done (rétrospective en attente) ; commit de clôture = celui qui ajoute cette section.
