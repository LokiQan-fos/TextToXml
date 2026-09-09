---
type: sprint-change-proposal
date: 2026-09-09
trigger: revue adverse Story 3.4 — infra Launcher dupliquée
scope: Major
status: approuvé (2026-09-09) — P1–P15 appliqués (PRD, epics.md, scripts/schema, sprint-status.yaml, deferred-work.md, README) ; rollback arbre 3.4 fait ; impl restante = Story 3.0 → 3.6 via bmad-build + bump MicroServices.sln (AR-13)
---

# Sprint Change Proposal — Alignement `Kape22Importer` sur le `Launcher` du portail

> Langue : **français**, en cohérence avec `PRD.md` / `epics.md` (override
> assumé de `document_output_language`).

## Section 1 — Résumé du problème

**Déclencheur.** Story 3.4 « Intégration Launcher & arrêt propre » (FR‑14‑5/6,
NFR‑9), implémentée le 2026‑09‑08/09, **rejetée en revue adverse** le 2026‑09‑09
(`/bmad-code-review`, fan‑out 4 couches).

**Constat.** L'implémentation 3.4 réécrit **en autonome et déconnectée** toute
l'infrastructure de supervision que le vrai `Launcher` du portail
(`C:\Users\Administrateur\Documents\MicroServices\MicroServices.sln`) fournit
déjà :

| Ajouté par la 3.4 (à supprimer) | Déjà fourni par le `Launcher` |
|---|---|
| `Program.cs` → `WebApplication` + endpoints `/status` et `/active` + `AddSerilog(...WriteTo.MSSqlServer)` | Hôte HTTP unique (`WebApplication` + `UseWindowsService`), endpoints `/workers` + `/workers/{name}/start\|stop\|active`, auth `X-Launcher-Api-Key` |
| `FrameworkReference Microsoft.AspNetCore.App` dans `Kape22Importer.csproj` | — (le worker n'a aucune dépendance ASP.NET Core) |
| `WorkerStatus`, `WorkerStatusProvider`, `WorkerControl`, `WorkerSettingsRegistrationService` | `Launcher/Adapters/WorkerAdapter<TClient>` : **est** le `WorkerStatus` (`IsRunning = Client.IsConnected`, `IsActive`, `LastStartedAt`, `LastError`) |
| `Persistence/MqttNetServicesDbContext`, `Persistence/WorkerSetting`, `Persistence/MqttNetServicesServiceCollectionExtensions` | `Launcher/WorkerSettingsRepository` sur `SharedLogger.GetDefaultConnectionString()` ; table `WorkerSettings` **auto‑créée** par le Launcher, lue au démarrage, écrite sur bascule |
| moitié `WorkerSettings` de `scripts/schema/02-mqtt-tables.sql` | idem — table Launcher‑owned |
| sink Serilog `MQTTnetServices.Logs` câblé main | `MicroService/Logging/SharedLogger` (cache un `Logger` par (chaîne, table)), via `AbstractService.LogInformation/LogWarning/LogError` |
| `Worker : BackgroundService` + `PeriodicTimer` | modèle réel : `class Client : Publisher, IPublisher, IService`, boucle sur `Publisher._timer` (`Frequency` / `Execute`) |

**Cause racine.** `AC-FR14-5` (« même contrat que
`ServicesMicroScope.LauncherApiClient ») et `AR‑9` ont été lus comme
« réimplémenter le contrat de statut » au lieu de « être un worker in‑process que
le `Launcher` existant supervise ». Le modèle réel est établi par
`Laminoir/OrdresFabricationSync` : un worker = **une classe `Client`** + **une
ligne** dans `Launcher/WorkerRegistry.cs` (`Factories`, enveloppée dans
`WorkerAdapter<Client>`) + **une entrée** `Launcher/workers.json`.

**Preuves.** Revue 2026‑09‑09 ; implémentation de référence `MicroServices.sln`
(`Laminoir/OrdresFabricationSync/Client.cs`, `Launcher/WorkerRegistry.cs` +
`workers.json` + `Adapters/WorkerAdapter.cs` + `Program.cs` +
`WorkerSettingsRepository.cs`, `MicroService/Service/AbstractService.cs`,
`MicroService/Publish/Publisher.cs`, `MicroService/Logging/SharedLogger.cs`) ;
mémoires `portal-launcher-model`, `story-3-4-launcher-contract`.

**État du dépôt.** Rien n'est commité pour la 3.4. L'arbre de travail contient
les fichiers 3.4 indexés + des correctifs de revue non commités.
`sprint-status.yaml` est encore à `review` (branche `master`). Stories 3.1 / 3.2
livrées et saines ; Story 3.3 commitée (`14a42cc`, `841d299`), fond correct.

**Décision utilisateur (2026‑09‑09) — Option A.** `Kape22Importer` s'aligne sur
`MicroServices.sln` : la lib de format est réutilisée, le worker devient une
classe `Client : Publisher` enregistrée dans le `Launcher`.

---

## Section 2 — Impact épic & artefacts

### 2.1 Impact épic

| Story | État | Décision |
|---|---|---|
| 3.1 Scrutation / cycle de vie | `done` | **Réutilisée telle quelle** (seams `IFileSource` / `IFichierProcessor`). |
| 3.2 Orchestration par Fichier | `done` | **Réutilisée telle quelle** (seam `Func<AscoLsiDbContext>`). |
| 3.3 Double journalisation | `done` (commitée) | **Revisitée** : le seam `ILogger` câblé par un `Program.cs` `AddSerilog` → routé vers le `Logger` Serilog partagé d'`AbstractService` / `SharedLogger`. Le fond (`L_D_LOG_COMMANDE`, discriminant skip‑doublon, tri `AC-FR6-4` sur `ImportResult`) est conservé. |
| **3.0** *(nouvelle)* | — | Repositionnement structurel : `Kape22Importer` → **bibliothèque** ; `Client : Publisher` mince dans `MicroServices.sln` ; ligne `WorkerRegistry` + entrée `workers.json`. |
| 3.4 Intégration Launcher & arrêt propre | `review` → `backlog` | **Réécrite** : boucle `Client.Actions` + annulation coopérative + arrêt < 5 s + FR‑8 au démarrage. |
| 3.5 Robustesse de la boucle | `backlog` | **Réécrite** (réancrage `Client.Actions`, `AC-FR15-x` inchangés). |
| 3.6 E2E & couverture | `backlog` | **Réécrite** (E2E au niveau bibliothèque ; fumée `Client` remplace `WebApplicationFactory<Program>`). |

Aucun épic supprimé ni ajouté. Épics 1 & 2 `done`, non impactés côté logique
(`TextToXml` reste une lib pure ; les artefacts d'Épic 2 suivent `Kape22Importer`
dans son changement de **type de projet**, sans changement de code).

### 2.2 Prérequis d'infra (hors sprint)

`MicroServices.sln` (aujourd'hui `net9.0;net10.0` + EF Core 9.0.9) doit être
aligné sur **`net10.0` + EF Core 10.0.x** avant l'implémentation de la Story 3.4
réécrite. Décision utilisateur : bump `MicroServices.sln`. Suivi par **AR‑13**
(passe archi + non‑régression sur les workers en prod : `ImportFiles`,
`CopyDataToDb`, `ConvertAndSave`, `OrdresFabricationSync`).

### 2.3 Décisions utilisateur (AskUserQuestion 2026‑09‑09)

| # | Question | Réponse |
|---|---|---|
| D‑A | Où vit le code Kape22 ? | **Lib dans `TextToXml.sln` + `Client` dans `MicroServices.sln`.** |
| D‑B | Réconciliation framework / EF | **Bump `MicroServices.sln` → net10.0 + EF Core 10.0.x.** |
| D‑C | Dépendance broker MQTT dès v1 | **Oui** — aligner sur le modèle ; broker requis comme les autres workers. |
| D‑D | Mode workflow | Incrémental. |

### 2.4 Artefacts à modifier

| Artefact | Nature |
|---|---|
| `PRD.md` §0bis D1, D9, D20 ; §4.3 ; FR‑14 `AC-FR14-5` / `AC-FR14-6` ; §4.4 « Cibles » | reformulations (Section 4, P1–P5) |
| `epics.md` AR‑1, AR‑2, AR‑7, AR‑9 ; nouvel **AR‑13** ; note `AC-FR16-2` ; intro Épic 3 ; ledger « AddDbContextFactory » | reformulations + ajout (P6–P7) |
| `epics.md` **Story 3.0** (nouvelle), addendum **Story 3.3**, **Stories 3.4 / 3.5 / 3.6** réécrites | ajout + réécritures (P8–P12) |
| `scripts/schema/02-mqtt-tables.sql` | retrait du bloc `dbo.WorkerSettings` (P13) |
| `sprint-status.yaml` | +`3-0`, `3-4`→`backlog`, `action_items` (P14) |
| `deferred-work.md` | section « Resolved by: Story 3.4 » → « Correction of course » (P15) |
| `README` | layout solution, emplacement du `Client`, prérequis `MicroServices.sln` |
| UX | **aucun impact.** |

---

## Section 3 — Chemin retenu

**Hybride : Rollback (Option 2) + Ajustement dirigé (Option 1) + amendements
PRD / architecture.**

1. **Rollback** de l'arbre de travail 3.4 non commité — ne garder que la
   suppression de l'entrée CPM morte `Microsoft.Extensions.Hosting` dans
   `Directory.Packages.props`. (Les correctifs de revue CC‑4 sur
   `WorkerControl.cs` / `WorkerSettingsRegistrationService.cs` / `Program.cs` sont
   caducs — ces fichiers sont supprimés. Idem garde d'annulation avant purge dans
   `Worker.RunTick`, test `Worker_WhilePaused`, trait `[Trait("AC","FR14-6")]` sur
   le test NFR‑9 : `Worker.cs` disparaît.)
2. **Amendements ciblés** `PRD.md` + `epics.md` (Section 4).
3. **Passe `bmad-architecture`** couvrant : la frontière `TextToXml.sln` ↔
   `MicroServices.sln`, le modèle `Client : Publisher`, le prérequis net10/EF10.
4. **Re‑plan Épic 3** : Story 3.0 → revisite 3.3 → 3.4 → 3.5 → 3.6, via
   `bmad-build`, test‑first (CC‑1).

**Pourquoi pas Option 1 seule.** Les décisions d'architecture changent (AR‑1, 2,
7, 9 + nouvel AR‑13 ; PRD §4.3, D1, D9, D20) → classification **Majeure**,
implication PM / Architecte requise avant implémentation.

**Pourquoi pas un revert dur de 3.3.** Son livrable (`L_D_LOG_COMMANDE`,
`ImportResult`, tri `AC-FR6-4`, tests d'intégration round‑trip) est correct et
indépendant du modèle d'hébergement ; seul le seam `ILogger` est retouché.

**MVP.** Inchangé — mêmes FR, mêmes `AC-FRx-y` en intention. Seul le *comment* de
FR‑14‑5/6 change.

**Effort / risque.** Rollback : L. Amendements doc : L‑M. Story 3.0 : M.
Réécritures 3.4/3.5/3.6 : M (le pipeline est déjà livré en 3.1/3.2 ; le `Client`
est mince, calqué sur `OrdresFabricationSync`). Risque principal : le prérequis
bump `MicroServices.sln` (workers en prod) — porté hors de ce sprint.

---

## Section 4 — Propositions de modification détaillées

### PRD.md

#### P1 — §0bis D1 (broker requis dès v1)

**Actuel**
> D1 | Fichiers pris dans **`D:\Site-FTP\Reception\GPAO`** (serveur `AFS017`), chemin en **configuration** (`Import:InboxPath`). Accès système de fichiers / partage, pas de protocole FTP. Cible d'évolution : MQTT — sans toucher `TextToXml`. | utilisateur

**Proposé**
> D1 | Fichiers pris dans **`D:\Site-FTP\Reception\GPAO`** (serveur `AFS017`), chemin en **configuration** (`Import:InboxPath`). Accès système de fichiers / partage, pas de protocole FTP pour l'**ingestion**. Le worker se connecte néanmoins au **broker MQTT dès la v1** pour son cycle de vie (`AbstractService`/`Publisher` : `ConnectWithRetryAsync` au démarrage, publication d'un message de statut), comme tous les workers du portail — le broker fait partie de l'infra requise. Cible d'évolution : MQTT aussi pour l'**ingestion** des Fichiers, sans toucher `TextToXml`. | utilisateur (rév. correction‑de‑cap 2026‑09‑09)

#### P2 — §0bis D9 (Launcher owns WorkerSettings)

**Actuel**
> D9 | Enregistrement Launcher via `MQTTnetServices.dbo.WorkerSettings` (`WorkerName`, `IsActive`), comme les autres workers. | schéma réel

**Proposé**
> D9 | Le worker s'intègre au **`Launcher` existant** (`MicroServices.sln`) comme classe in‑process `Client : Publisher` : **une ligne** dans `Launcher/WorkerRegistry.cs` (`Factories`, enveloppée dans `WorkerAdapter<Client>`) + une entrée `Launcher/workers.json` (`{Name, Type, ConfigPath}`). La table `MQTTnetServices.dbo.WorkerSettings` (`WorkerName`, `IsActive`) est **détenue par le Launcher** (il l'auto‑crée, lit `IsActive` au démarrage, l'écrit sur bascule) ; **le worker n'y touche jamais**. Le `WorkerStatus` vu du dashboard est fourni par `WorkerAdapter` (`IsRunning = Client.IsConnected`), pas par le worker. | schéma réel + modèle `Launcher` (correction‑de‑cap 2026‑09‑09)

#### P3 — §0bis D20 (frontière solution)

**Actuel**
> D20 | Solution **`TextToXml.sln` autonome** dans ce dépôt (`TextToXml` lib + `Kape22Importer` worker + tests), référence `PortalSharedLibrary` pour l'identité/log. | utilisateur (Q18a)

**Proposé**
> D20 | **`TextToXml.sln`** (ce dépôt) = `TextToXml` (lib pure, Épic 1) + **`Kape22Importer` (bibliothèque** de format P60 : descripteur, `P60.xsd`, DTO `Kape22File`, entité + `AscoLsiDbContext`, `Kape22Mapper`, `Kape22Persister`, `InboxScanner`, `Kape22FichierProcessor`) + projets de tests. **Le worker exécutable** est une classe **`Client : Publisher, IPublisher, IService`** mince ajoutée à **`MicroServices.sln`** (à côté de `Laminoir/OrdresFabricationSync`), en `ProjectReference` cross‑dépôt vers la lib `Kape22Importer` + `MicroService.csproj`, enregistrée dans `Launcher`. `PortalSharedLibrary` référencé pour l'identité/log. **Prérequis bloquant** : `MicroServices.sln` (aujourd'hui `net9.0;net10.0` + EF Core 9.0.9) aligné sur **`net10.0` + EF Core 10.0.x**. | utilisateur (Q18a ; rév. correction‑de‑cap 2026‑09‑09)

#### P4 — §4.3 description worker

**Actuel**
> **Description :** worker .NET (`net10.0`) hébergé et supervisé par le **Launcher** existant, sur le pattern `BackgroundService` + `PeriodicTimer` déjà en place dans `FactoryScope` (`FileImportBackgroundService`). Il orchestre uniquement : dossier de réception → `TextToXml` → archive XML → `Kape22Mapper` → EF → déplacement fichier → double log.

**Proposé**
> **Description :** classe **`Client : Publisher, IPublisher, IService`** in‑process au **Launcher** existant (`MicroServices.sln`), sur le modèle `Laminoir/OrdresFabricationSync/Client.cs` : ctor `Client(IConfiguration)`, `CreateAsync()` qui se connecte au broker puis démarre la boucle timer de `Publisher` (`Frequency` = intervalle de polling, `Execute` = un tick). Elle réutilise la bibliothèque `Kape22Importer` (Épic 2 + seams 3.1/3.2) et orchestre uniquement : dossier de réception → `TextToXml` → archive XML → `Kape22Mapper` → EF → déplacement fichier → double log. Le Launcher fournit l'hôte HTTP, le `WorkerStatus` (via `WorkerAdapter`), la table `WorkerSettings`, la pause `IsActive`, l'auth `X-Launcher-Api-Key` et le logging `MQTTnetServices.Logs` (`AbstractService.LogInformation` → `SharedLogger`).

#### P5 — FR‑14 `AC-FR14-5` / `AC-FR14-6` + §4.4 « Cibles »

**Actuel `AC-FR14-5`**
> `AC-FR14-5` : le worker expose au Launcher un `WorkerStatus` (`IsRunning`, `IsActive`, `LastStartedAt`, `LastError`) — même contrat que `ServicesMicroScope.LauncherApiClient` ; s'enregistre dans `WorkerSettings`.

**Proposé `AC-FR14-5`**
> `AC-FR14-5` : le worker est **enregistré dans le `Launcher`** (`WorkerRegistry.Factories` + `workers.json`) comme `Client : Publisher`. Le Launcher le supervise via `WorkerAdapter<Client>` — `IsRunning` (= `Client.IsConnected`), `IsActive`, `LastStartedAt`, `LastError` exposés par `GET /workers` du Launcher — et gère seul la table `WorkerSettings`. Le worker **n'implémente ni `WorkerStatus` ni endpoint HTTP** ; il n'a aucune dépendance ASP.NET Core.

**Actuel `AC-FR14-6`**
> `AC-FR14-6` : `Stop` du Launcher pendant un tick → le Fichier en cours finit ou reste dans `processing/` (jamais à moitié inséré) ; arrêt propre < 5 s.

**Proposé `AC-FR14-6`**
> `AC-FR14-6` : `WorkerAdapter.StopAsync` (→ `Client.Stop()` + `Client.Disconnect()`) pendant un tick → via une **annulation coopérative** dans `Client.Actions()` (jeton vérifié entre Fichiers, jamais mi‑Fichier), le Fichier en cours finit ou reste dans `processing/` (jamais à moitié inséré) ; arrêt propre < 5 s.

**§4.4 « Cibles » — ajout**
> Le worker est supervisé par le `Launcher` (`MicroServices.sln`, `net10.0`). `Serilog` + `Serilog.Sinks.MSSqlServer` proviennent de `AbstractService` / `SharedLogger` (cache un `Logger` par (chaîne, table)) — **jamais câblés par le worker** ni par un `Program.cs`.

---

### epics.md

#### P6 — AR‑1, AR‑2, AR‑7, AR‑9 + nouvel AR‑13 + note `AC-FR16-2`

**AR‑1**
> _actuel_ : Un **microservice / projet .NET par format**. `TextToXml` est le **seul** code partagé. `Kape22Importer` est le gabarit des suivants (§0, D24, FR-16).
>
> _proposé_ : Par format : une **bibliothèque** .NET (`Kape22Importer` : descripteur, XSD, DTO, entité, mapper, persister, `InboxScanner`, `Kape22FichierProcessor`) **plus** une classe **`Client : Publisher`** mince enregistrée dans le `Launcher` (`MicroServices.sln`). `TextToXml` est le **seul** code partagé côté Étape 1. `Kape22Importer` + son `Client` sont le gabarit des formats suivants (§0, D24, FR‑16).

**AR‑2**
> _actuel_ : Solution **`TextToXml.sln` autonome** dans ce dépôt : projet `TextToXml` (lib) + `Kape22Importer` (worker) + projets de tests. Référence `PortalSharedLibrary` pour l'identité/log (D20).
>
> _proposé_ : **`TextToXml.sln`** (ce dépôt) : `TextToXml` (lib) + `Kape22Importer` (**lib** format P60) + projets de tests ; référence `PortalSharedLibrary` (D20). Le `Client` exécutable vit dans **`MicroServices.sln`** (`ProjectReference` cross‑dépôt vers la lib `Kape22Importer` + `MicroService.csproj`). **Prérequis** : `MicroServices.sln` aligné sur `net10.0` + EF Core 10.0.x avant l'implémentation de la Story 3.4.

**AR‑7**
> _actuel_ : Worker sur pattern **`BackgroundService` + `PeriodicTimer`** déjà en place dans `FactoryScope` (`FileImportBackgroundService`). Abstraction **`IFileSource`** (`DirectoryFileSource` en prod, impl. mémoire en test) (§4.3, FR-12).
>
> _proposé_ : Worker = classe **`Client : Publisher, IPublisher, IService`** pilotée par le timer de `Publisher` (`Frequency` = intervalle de polling, `Execute` = un tick appelant `InboxScanner.RunTick` + `PurgeRetention`), modèle `Laminoir/OrdresFabricationSync/Client.cs`. Abstraction **`IFileSource`** (`DirectoryFileSource` en prod, impl. mémoire en test) **inchangée** (§4.3, FR‑12).

**AR‑9**
> _actuel_ : Enregistrement Launcher via `MQTTnetServices.dbo.WorkerSettings` (`WorkerName`, `IsActive`) ; contrat `WorkerStatus` identique à `ServicesMicroScope.LauncherApiClient` (D9, FR-14).
>
> _proposé_ : Enregistrement = **une ligne** dans `Launcher/WorkerRegistry.cs` (`Factories`, enveloppée dans `WorkerAdapter<Client>`) + une entrée `Launcher/workers.json`. Le Launcher fournit : l'hôte HTTP unique, `WorkerAdapter` (qui **est** le `WorkerStatus` : `IsRunning = Client.IsConnected`, `IsActive`, `LastStartedAt`, `LastError`), la table `WorkerSettings` (**Launcher‑owned**, auto‑créée), la pause `IsActive`, l'auth `X-Launcher-Api-Key`. Le worker **ne touche pas** `WorkerSettings` et **n'expose aucun HTTP** (D9, FR‑14).

**Nouvel AR‑13**
> **AR-13** — **Frontière cross‑solution.** La lib `Kape22Importer` ne référence que `TextToXml` + `PortalSharedLibrary` (garde `AC-FR16-1` inchangée). Seule la classe `Client` référence `MicroService.csproj` (broker, `AbstractService`/`Publisher`, `SharedLogger`) — c'est le **7ᵉ point de variation** d'un format (voir `AC-FR16-2` amendé). `Client` est hors périmètre de `TextToXml.Tests` ; ses tests vivent dans `MicroServices.sln` (`OrdresFabricationSync.Tests` comme modèle : provider EF InMemory, pas de broker réel). Le bump `MicroServices.sln` → `net10.0` / EF Core 10.0.x est un prérequis d'infra suivi hors de ce sprint (passe archi + tests de non‑régression sur les workers en prod).

**Note `AC-FR16-2` (Story 1.8, déjà `done`)**
> Ajouter `Client : Publisher` à la liste des points de variation d'un format : `<format>.xml`, `<format>.xsd`, DTO, entité + `DbContext`, table de mapping, `appsettings`/`<worker>.json`, **classe `Client`**. Le test `FormatIsolationTests` reste vert (il inspecte la lib, pas le `Client`).

#### P7 — Intro Épic 3 + ledger « AddDbContextFactory »

**Intro Épic 3 — ajout en tête**
> **Correction de cap (2026‑09‑09).** L'intégration Launcher est réalisée en s'alignant sur le modèle réel du portail (`MicroServices.sln`) : `Kape22Importer` devient une **bibliothèque** et le worker est une classe **`Client : Publisher`** enregistrée dans le `Launcher`. Les Stories 3.1 / 3.2 (livrées) sont réutilisées telles quelles. Une **Story 3.0** porte le repositionnement structurel ; 3.3 (livrée) est **revisitée** pour le seam de journalisation ; **3.4 / 3.5 / 3.6 sont réécrites** contre le modèle `WorkerAdapter`/`Client`. Voir `sprint-change-proposal-2026-09-09.md`.

**Ledger « AddDbContext vs AddDbContextFactory »**
> _actuel_ : …tranché pour la **Story 3.4** (composition DI / worker) → `AddDbContextFactory<AscoLsiDbContext>` (fabrique singleton)…
>
> _proposé, ajout_ : **Requalifié (correction de cap) :** il n'y a plus de conteneur DI ni d'hôte pour le worker. Le `Client` construit ses contextes **à la main par tick** (`new AscoLsiDbContext(new DbContextOptionsBuilder<…>().UseSqlServer(cs).Options)`), comme `OrdresFabricationSync.Client`. `Kape22FichierProcessor` garde son seam `Func<AscoLsiDbContext> newContext` (Story 3.2) ; `Kape22Persister` est construit par Fichier dans l'orchestrateur. Le point « fabrique singleton » est **abandonné** avec `AddKape22Startup`.

#### P8 — Nouvelle Story 3.0 : Repositionnement structurel (lib + Client Launcher)

> **As a** mainteneur du portail,
> **I want** `Kape22Importer` transformé en bibliothèque et un `Client : Publisher` mince enregistré dans le `Launcher`,
> **So that** le worker P60 est supervisé exactement comme les autres workers du portail, sans infra dupliquée.
>
> **Prérequis :** `MicroServices.sln` aligné sur `net10.0` + EF Core 10.0.x (suivi AR‑13).
>
> **Acceptance Criteria :**
>
> **Given** le projet `src/Kape22Importer`
> **When** je le convertis
> **Then** son SDK passe de `Microsoft.NET.Sdk.Worker` à `Microsoft.NET.Sdk` (bibliothèque) ; `Program.cs` est **supprimé** ; aucune référence `Microsoft.AspNetCore.App` ni `Microsoft.Extensions.Hosting` ; il expose `InboxScanner`, `IFileSource`/`DirectoryFileSource`, `IFichierProcessor`, `Kape22FichierProcessor`, `Kape22Mapper`, `Kape22Persister`, `AscoLsiDbContext`, `ImportOptions` en API publique (ou `InternalsVisibleTo` le `Client`)
> **And** `AddKape22Startup` / `StartupCompatibilityHostedService` / `*ServiceCollectionExtensions` (host) sont **supprimés** ; la vérification FR‑8 est exposée en méthode statique appelable (`StartupCompatibilityCheck.Verify(...)`)
> **And** la lib ne référence que `TextToXml` + `PortalSharedLibrary` (`AC-FR16-1` reste vert)
>
> **Given** `MicroServices.sln`
> **When** j'ajoute le worker P60
> **Then** un projet `Kape22/ImportP60` (nom à confirmer) contient `Client : Publisher, IPublisher, IService` : ctor `Client(IConfiguration)` (lit `Import:*`), `CreateAsync()` (`ConnectWithRetryAsync` puis `Start()`), `Frequency` = `Import:PollingInterval`, `Execute` = un tick (`FR-8` au premier tick / dans `CreateAsync`, puis `InboxScanner.RunTick(ct)` + `PurgeRetention()`)
> **And** il référence la lib `Kape22Importer` (`ProjectReference` cross‑dépôt) + `MicroService.csproj`
> **And** `Launcher/WorkerRegistry.cs` gagne **une** entrée `Factories["Kape22ImportP60"]` (enveloppe `WorkerAdapter<Client>`) et `Launcher/workers.json` **une** entrée `{ "Name": "Kape22ImportP60", "Type": "Kape22ImportP60", "ConfigPath": "Kape22ImportP60.json" }`
> **And** `Kape22ImportP60.json` porte `Import:*` (chemins, `PollingInterval`, `InitiatingServer`, `RetentionDays`) + les chaînes `AscoLSI` / `MQTTnetServices` — **jamais en dur** (CC‑7)
>
> **Given** le harnais de tests
> **When** je build `TextToXml.sln`
> **Then** `Kape22Importer.Tests` compile contre la lib (plus de `Program`/host) ; les tests supprimés en 3.4 (`WorkerStatusProviderTests`, `WorkerShutdownTests`, `WorkerCompositionTests`, `WorkerControlTests`, `WorkerSettingsRegistrationTests`, `MqttSchemaModelParityTests`) ne sont **pas** recréés
>
> **Tests xUnit (TDD, CC‑1) :** test « `Kape22Importer` est une lib sans dépendance host/ASP.NET » ; test « `AC-FR16-1` ProjectReferences lib == {TextToXml, PortalSharedLibrary} » (existant, reste vert) ; côté `MicroServices.sln` : `ClientCompositionTests` (le `Client` construit son graphe depuis une `IConfiguration` en mémoire, sans broker).
>
> **Critères transverses :** CC‑2, CC‑3, CC‑4, CC‑5, CC‑7. *(CC‑1 partiel : story surtout structurelle ; la logique `Client.Actions` est test‑first.)*

#### P9 — Addendum Story 3.3 (revisite)

> **Revisite (correction de cap 2026‑09‑09).** Le fond livré (`L_D_LOG_COMMANDE`, discriminant skip‑doublon, tri `AC-FR6-4` sur `ImportResult`) est **conservé**. Change : `Kape22FichierProcessor` ne prend plus `ILogger<Kape22FichierProcessor>` câblé par un `Program.cs` `AddSerilog(...WriteTo.MSSqlServer)`. Le `Client` route le journal vers le `Logger` Serilog partagé d'`AbstractService` (`SharedLogger.GetDefault()`), soit via un pont `Serilog.Extensions.Logging` (`new SerilogLoggerFactory(Logger).CreateLogger<…>()`), soit via un petit seam `IImportJournal` implémenté par le `Client` sur `AbstractService.LogInformation/LogWarning/LogError`. Décision de forme dans la Story 3.0 / 3.4. `DoubleJournalIntegrationTests` (round‑trip `Serilog.Sinks.MSSqlServer`) **reste**. La note deferred‑work « Serilog host wiring » est retirée (le host disparaît).

#### P10 — Story 3.4 réécrite : Boucle worker `Client` & arrêt propre

> **As a** exploitation LSI,
> **I want** que la classe `Client` exécute le pipeline d'import à chaque tick et s'arrête proprement en < 5 s sans laisser d'insertion à moitié faite,
> **So that** le worker P60 est supervisable et recyclable comme les autres workers du portail.
>
> **Acceptance Criteria :**
>
> **Given** `Client.CreateAsync()`
> **When** le worker démarre
> **Then** `ConnectWithRetryAsync` (broker) puis, **avant** `Start()`, `StartupCompatibilityCheck.Verify(descripteur embarqué, AscoLsiDbContext)` (FR‑8) ; un échec de compatibilité → `LogError` + le worker **ne démarre pas sa boucle** (défaut de déploiement, `AC-FR8-1..4`) ; compatible → `Start()` arme le timer de `Publisher` (`Frequency` = `Import:PollingInterval`)
>
> **Given** un tick (`Execute` = `Client.Actions`)
> **When** il s'exécute
> **Then** il construit par tick : `Func<AscoLsiDbContext>` (`new DbContextOptionsBuilder<…>().UseSqlServer(cs)`), `DirectoryFileSource(Import:InboxPath)`, `Kape22FichierProcessor`, `InboxScanner` ; appelle `scanner.RunTick(cancellationToken)` puis `scanner.PurgeRetention()` ; publie un message de statut MQTT (comme `OrdresFabricationSync`)
> **And** une exception **par Fichier** est capturée dans `Actions` / `InboxScanner` (log `ERROR`, Fichier en `error/` ou laissé en `processing/` selon la panne — détail Story 3.5), la boucle **ne s'arrête pas** (contourne le `catch → Stop()` du timer de `Publisher`)
>
> **Given** un `WorkerAdapter.StopAsync` du Launcher (→ `Client.Stop()` + `Client.Disconnect()`) pendant un tick
> **When** le worker s'arrête
> **Then** le `Client` détient un `CancellationTokenSource` annulé par `Stop()` ; `Actions` vérifie le jeton **entre Fichiers** (jamais mi‑Fichier) → le Fichier en cours finit ou reste dans `processing/`, jamais à moitié inséré ; le timer est disposé ; arrêt propre **< 5 s** (`AC-FR14-6`, NFR‑9)
>
> **Given** le Launcher `GET /workers`
> **When** il interroge la flotte
> **Then** `WorkerAdapter<Client>` remonte `IsRunning` (= `Client.IsConnected`), `IsActive`, `LastStartedAt`, `LastError` — aucun code de statut côté worker (`AC-FR14-5`, réalisé par la Story 3.0 + vérifié ici)
>
> **Tests xUnit (TDD, CC‑1)** — dans `MicroServices.sln` (`Kape22ImportP60.Tests`, modèle `OrdresFabricationSync.Tests`) :
> `AC-FR14-6` (un `Stop()` pendant un tick long simulé rend la main dans le budget, Fichier en cours terminé ou en `processing/`) ; `AC-FR14-5` (via `WorkerAdapter<Client>` avec un client stubé : `IsRunning`/`LastError` reflètent l'état) ; FR‑8 (`CreateAsync` sur un `AscoLsiDbContext` incompatible ne démarre pas la boucle). Pipeline lui‑même : couvert par 3.1/3.2 (lib).
>
> **Critères transverses :** CC‑1, CC‑2, CC‑3, CC‑4, CC‑5, CC‑7.
>
> _(Le `WorkerStatusProvider` / `WorkerControl` / « skip tick si pausé » de l'ex‑3.4 disparaissent : une désactivation Launcher **arrête** le `Client` via `WorkerAdapter.SetActiveAsync`, il n'y a rien à ignorer côté worker.)_

#### P11 — Story 3.5 réécrite : Robustesse de la boucle `Client`

> Périmètre et AC **inchangés sur le fond** (`AC-FR15-1..4`, NFR‑7). Réancrage :
>
> - La « boucle » est `Client.Actions` armée par le timer de `Publisher`. Le callback du timer de `Publisher` appelle `Stop()` sur exception non capturée → `Actions` **doit** capturer ses propres exceptions par Fichier (modèle `OrdresFabricationSync.Client.Actions` : `try/catch` autour de chaque unité de travail, `LogError`, on continue).
> - `AC-FR15-1` (exception par Fichier → `error/`, la boucle continue) : `try/catch` dans `InboxScanner`/`Actions`.
> - `AC-FR15-2` (dossier de réception injoignable) : `DirectoryFileSource` lève → capturé dans `Actions`, `LogWarning`, aucun Fichier perdu, retry au tick suivant.
> - `AC-FR15-3` (base `AscoLSI` injoignable) : `Kape22Persister` traduit `DbException` → `PersistenceError`, Fichiers **laissés en `processing/`** (pas `error/`), `LogWarning`, retry.
> - `AC-FR15-4` (recycle) : reprise via `processing/` (Story 3.1) + garde‑fou D22 ; le `Client` est reconstruit à chaque `CreateAsync` du `WorkerAdapter`.
>
> **Tests :** `AC-FR15-1/2` en `Category=Unit` sur `InboxScanner` (lib) + un test `Actions` (`MicroServices.sln`) ; `AC-FR15-3/4` sur le harnais SQL Server local de la Story 2.1 (panne = chaîne de connexion vers un hôte mort). Pas de conteneur (AR‑12).
>
> **Critères transverses :** CC‑1, CC‑2, CC‑3, CC‑4, CC‑5, CC‑7.

#### P12 — Story 3.6 réécrite : Validation de bout en bout & harnais de couverture (SM‑1/2/3)

> AC **inchangés** (`SM-1/2/3`, contre‑métriques, NFR‑1/2). Réancrage :
>
> - Le test agrégateur `AC → [Trait]` (`AcTraitCoverageTests`, SM‑1) est **inchangé** (déjà en place, retro Épic 1).
> - **SM‑2 (E2E 10 fichiers)** : piloté au niveau **bibliothèque** — `InboxScanner` (in‑memory `IFileSource` ou `DirectoryFileSource` sur dossier temp) + `Kape22FichierProcessor` réel + SQL Server local (commit réel + reset), `[Trait("Category","Integration")]`. Insère 10 lignes `L_D_KAPE22` + 10 `L_D_LOG_COMMANDE` ` — OK`.
> - **SM‑3** : `*.errors.json` lisible — inchangé.
> - **NFR‑1/2** : perf mesurée sur le pipeline lib (hors broker/HTTP).
> - Un test « fumée » niveau `Client` (`MicroServices.sln`) : `CreateAsync` (broker fake/embarqué) → un tick → une ligne insérée. Remplace le `WebApplicationFactory<Program>` de l'ex‑3.4 (abandonné, plus de `Program`).
>
> **Critères transverses :** CC‑1, CC‑2, CC‑3, CC‑4, CC‑5, CC‑7. **AR‑12**.

---

### Autres artefacts

#### P13 — `scripts/schema/02-mqtt-tables.sql`

Retirer le bloc `dbo.WorkerSettings` (lignes 35‑45) : la table est **détenue et
auto‑créée par le `Launcher`** ; le harnais d'intégration de `Kape22Importer`
(devenu lib) ne la touche plus (`MqttNetServicesDbContext` /
`MqttSchemaModelParityTests` supprimés). `dbo.Logs` reste (round‑trip
`DoubleJournalIntegrationTests`, Story 3.3). Ajuster l'en‑tête
(`Scope: only dbo.Logs`).

#### P14 — `sprint-status.yaml`

```yaml
  epic-3: in-progress
  3-0-repositionnement-structurel-lib-client-launcher: backlog
  3-1-scrutation-du-dossier-de-réception-cycle-de-vie-du-fichier: done
  3-2-orchestration-par-fichier: done
  3-3-double-journalisation-mqttnetservices-logs-l_d_log_commande: done
  3-4-intégration-launcher-arrêt-propre: backlog
  3-5-robustesse-de-la-boucle-worker: backlog
  3-6-validation-de-bout-en-bout-harnais-de-couverture-sm-1-2-3: backlog
  epic-3-retrospective: optional
```

+ `action_items` :

```yaml
  - id: "epic-3-correct-course-2026-09-09-launcher-alignment"
    epic: 3
    action: "Story 3.4 rejetée en revue (infra Launcher dupliquée). Option A : Kape22Importer
      devient une lib, worker = Client:Publisher dans MicroServices.sln/Launcher. Rollback
      arbre 3.4 non commité ; amendements PRD (D1/D9/D20, §4.3, FR-14) + AR-1/2/7/9/13 ;
      re-plan Épic 3 : Story 3.0 nouvelle, 3.3 revisitée, 3.4/3.5/3.6 réécrites. Prérequis :
      bump MicroServices.sln net10.0/EF10. Passe bmad-architecture avant impl."
    owner: "PM + Architecte"
    status: open
    ref: "_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-09.md"
```

#### P15 — `deferred-work.md`

- Remplacer la section **« ## Resolved by: Story 3.4 (2026-09-09) »** (caduque)
  par **« ## Correction of course: Story 3.4 rejected — Launcher alignment
  (2026-09-09) »** : rollback de l'arbre 3.4 non commité (ne garder que la
  suppression de l'entrée CPM `Microsoft.Extensions.Hosting` dans
  `Directory.Packages.props`) ; `AddDbContextFactory` / composition‑root
  abandonnés (plus d'hôte) ; seam journalisation (ex‑`ILogger` câblé) traité en
  3.0 / 3.4 ; renvoi vers `sprint-change-proposal-2026-09-09.md`.
- Retirer les notes « Serilog host wiring done » et « No `WebApplicationFactory<Program>`
  smoke » (host supprimé).
- Conserver : `IConfiguration`+`ImportOptions` overlap dans `Kape22Persister`
  (→ hygiène Story 3.0) ; per‑tick failure handling coarse (→ Story 3.5).

---

## Section 5 — Handoff & implémentation

**Classification : Majeure.**

| Rôle | Responsabilité | Livrables |
|---|---|---|
| **PM** | Valider les amendements `PRD.md` (P1–P5) ; confirmer le nom du projet `Client` (`Kape22ImportP60` proposé). | PRD.md édité. |
| **Architecte** (`bmad-architecture`) | Passe archi : frontière `TextToXml.sln` ↔ `MicroServices.sln`, modèle `Client : Publisher`, seam journalisation, prérequis bump net10/EF10. Éditer `epics.md` (P6–P12). | epics.md édité + spine archi. |
| **Infra / Architecte** | Bump `MicroServices.sln` → net10.0 + EF Core 10.0.x + non‑régression workers en prod. **Bloquant pour Story 3.4.** | `MicroServices.sln` aligné (suivi AR‑13). |
| **Dev** (`bmad-build`, test‑first CC‑1) | Rollback arbre 3.4 ; `scripts/schema` (P13) ; `sprint-status.yaml` (P14) ; `deferred-work.md` (P15) ; puis Story 3.0 → revisite 3.3 → 3.4 → 3.5 → 3.6. | Code + tests verts, attestation CC‑1 par PR. |

**Critères de succès.**
- `TextToXml.sln` : `Kape22Importer` est une bibliothèque, 0 dépendance host /
  ASP.NET ; toute la suite `Category=Unit` verte ; `AC-FR16-1` vert.
- `MicroServices.sln` : `Kape22ImportP60` enregistré (`WorkerRegistry` +
  `workers.json`), visible dans `GET /workers` du Launcher.
- `AC-FR14-5` / `AC-FR14-6` verts contre le modèle `WorkerAdapter`/`Client`.
- E2E 10 fichiers (SM‑2) vert sur SQL Server local.
- Aucune régression sur les workers en prod du portail (prérequis net10/EF10).

**Séquencement.** Rollback + doc → amendements PRD/epics → passe archi + bump
`MicroServices.sln` (parallélisable) → Story 3.0 → revisite 3.3 → 3.4 → 3.5 →
3.6.
