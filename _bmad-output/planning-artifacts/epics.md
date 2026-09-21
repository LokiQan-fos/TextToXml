---
stepsCompleted: [step-01-validate-prerequisites, step-02-design-epics, step-03-create-stories, step-04-final-validation]
inputDocuments:
  - _bmad-output/planning-artifacts/PRD.md
  - _bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md
---

# TextToXml - Epic Breakdown

> Langue du document : **français**, en cohérence avec le PRD et le glossaire §3
> (le PRD impose l'usage à l'identique du vocabulaire français dans les FR, UJ,
> tests et code). `document_output_language` du `config.yaml` (English) est
> délibérément écarté pour cette raison — même choix que pour le PRD.

## Overview

Ce document décompose le PRD `TextToXml` en 3 épics et 22 stories implémentables.
Tous les tests d'intégration base de données tournent contre une **instance SQL
Server locale** (Developer Edition) avec un **schéma minimal versionné**
(`scripts/schema/`, généré depuis la base réelle) — voir **AR-12**.
Aucune architecture ni UX design séparés (v1 = bibliothèque .NET pure +
microservice worker, sans UI). Les décisions d'architecture sont portées par le
PRD lui‑même (§0, §0bis D1–D26, §4). Cible technique : **.NET 10.0**, **EF Core
10.0.x**, xUnit.

**Contraintes d'exécution transverses (imposées par le donneur d'ordre) :**

1. **TDD strict** — pour toute story de développement, les tests xUnit dérivés
   des `AC-FRx-y` (et des `CTR-x` de contrat) sont écrits **en premier** (voir `CC-1`).
2. **Standards de codage du PRD §4.5** intégrés comme critères d'acceptation
   transverses de chaque story de développement (`CC-2`..`CC-5`).

**Note de numérotation PRD :** le PRD numérote **deux** `AC-FR5-12`. Ils sont
distingués ici : **`AC-FR5-12a`** (round‑trip vers un `record` générique, Story
1.6) et **`AC-FR5-12b`** (round‑trip vers `Kape22File` généré du XSD, Story 2.3).

## Requirements Inventory

### Functional Requirements

| FR | Intitulé | Livrable |
|---|---|---|
| FR-1 | Chargement & validation du Descripteur (`P60.xml`, pas de méta‑schéma) | `TextToXml` |
| FR-2 | Décodage `Windows-1252` (strict) & découpage en Lignes | `TextToXml` |
| FR-3 | Affectation des Blocs + contrôle du nombre de Lignes + contrôle `Segment` (Warning) | `TextToXml` |
| FR-4 | Contrôle de longueur des Lignes (`format="Fixed"`) | `TextToXml` |
| FR-5 | Extraction, typage (descripteur directeur) & sérialisation du XML normalisé | `TextToXml` |
| FR-6 | Contrat `ConversionResult` : `Success`/`Errors`/`Warnings`/`Xml`, pureté, déterminisme, thread‑safety | `TextToXml` |
| FR-7 | Désérialisation XML normalisé → DTO `Kape22File` → entité `L_D_KAPE22` (mapping Annexe B) | `Kape22Importer` |
| FR-8 | Contrôle de compatibilité descripteur ↔ table **au démarrage** du worker | `Kape22Importer` |
| FR-9 | Champs dérivés & combinés (`Date` jour‑de‑l'année, `DateReception`, `Indice`, `NumeroFichier` roulette) | `Kape22Importer` |
| FR-10 | Contrôles de cohérence non bloquants (`Footer.Records`, inter‑blocs `File`, nom de fichier) → `Warnings` | `Kape22Importer` |
| FR-11 | Persistance transactionnelle (`L_D_KAPE22` + `L_D_LOG_COMMANDE`) & garde‑fou anti‑doublon | `Kape22Importer` |
| FR-12 | Scrutation du dossier de réception via `IFileSource`, cycle de vie `processing`/`archive`/`error`, purge de rétention | `Kape22Importer` |
| FR-13 | Orchestration par Fichier (ordre strict lecture → convert → archive → map → insert) | `Kape22Importer` |
| FR-14 | Double journalisation (`MQTTnetServices.Logs` + `L_D_LOG_COMMANDE`) & intégration Launcher | `Kape22Importer` |
| FR-15 | Robustesse de la boucle worker (exception isolée, sources injoignables, recycle) | `Kape22Importer` |
| FR-16 | Anatomie d'un microservice de format : `TextToXml` reste 100 % générique | `TextToXml` + `Kape22Importer` |
| FR-17 | Extraction & documentation du mapping legacy `L_D_KAPE22` → tables aval (annexe colonne-par-colonne, dérivée du `MappingTemplate` XML) | `Kape22Importer` |
| FR-18 | Mapping explicite (sans réflexion) `L_D_KAPE22` → `L_D_ORDRE_FABRICATION` & `L_D_COULEE` | `Kape22Importer` |
| FR-19 | Mapping explicite (sans réflexion) `L_D_KAPE22` → `L_D_CONSIGNES` & les 7 `L_D_SECTIONCHARGE_*` (Chutage, Lingot, Découpe, Pits, PoidsMétrique, Refroidissoirs, SVT), règle d'applicabilité par OF | `Kape22Importer` |
| FR-20 | Contrôles métier bloquants avant persistance (existence coulée froide, format coulée chaude, cohérence répartition lingots/fours) | `Kape22Importer` |
| FR-21 | Persistance transactionnelle étendue (`Kape22ImportBundle` + `Kape22Persister` remplacé) : un seul commit `L_D_KAPE22` + 9 tables aval, cause d'échec journalisée via le circuit existant (FR-14) | `Kape22Importer` |

### Contract Requirements (hors `AC-FRx-y`, issues de §0 / §4.1 / Annexe A.4)

| CTR | Exigence | Story |
|---|---|---|
| CTR-1 | `TextToXml` normalise `datatype="decimal"` (`decimalSeparator`) : valeur valide → valeur canonique dans le XML ; invalide → `Error {Code:InvalidDecimal, FieldId, RawValue}` (Étape 1) | 1.8 |
| CTR-2 | `TextToXml` normalise `datatype="datetime"` avec `convert` : valeur valide → ISO‑8601 dans le XML ; invalide → `Error {Code:InvalidDate, FieldId, RawValue}` (Étape 1) | 1.8 |
| CTR-3 | Round‑trip d'un XML normalisé à types mixtes (`int`/`decimal`/`datetime`/`string`) vers un DTO `record` : valeurs conservées sans convertisseur custom (`fixtures/generic/roundtrip.xml`) | 1.8 |

### NonFunctional Requirements

| NFR | Exigence | Source PRD |
|---|---|---|
| NFR-1 | Performance : un Fichier (~700 o, 3 Lignes) traité de bout en bout en **< 200 ms** hors latence FTP/SQL | §4.4 |
| NFR-2 | Performance : un tick de **500 Fichiers en < 30 s** | §4.4 |
| NFR-3 | `TextToXml` : **sans état, thread‑safe** — 100 `Convert` concurrents ⇒ résultats identiques au séquentiel | §4.4 |
| NFR-4 | `TextToXml` : **zéro dépendance runtime hors BCL** (`System.Xml`, `System.Text.Encoding.CodePages`) | §4.4 |
| NFR-5 | Sécurité : compte SQL `sa` existant, chaînes de connexion **lues de la configuration, jamais en dur** ; secrets via User Secrets (dev) / env ou appsettings protégé (prod) ; aucun secret dans le dépôt | §4.4, D21 |
| NFR-6 | Observabilité : chaque `ImportResult` traçable du nom de fichier à l'`Id` inséré ou à la liste d'erreurs (`Logs` + `L_D_LOG_COMMANDE` + `*.errors.json`) | §4.4 |
| NFR-7 | Reprise : rejouer un fichier = le redéposer corrigé dans le dossier de réception ; **aucune** action en base | §4.4 |
| NFR-8 | Cibles runtime : `net10.0`, `Microsoft.EntityFrameworkCore` 10.0.x, `Serilog` + `Serilog.Sinks.MSSqlServer` (aligné workers existants) | §4.4 |
| NFR-9 | Arrêt propre du worker sur `Stop` Launcher **< 5 s** ; jamais de fichier à moitié inséré | AC-FR14-6 |

### Additional Requirements

Dérivées des décisions d'architecture du PRD (§0, §0bis) — **pas de document
Architecture séparé, pas de starter template**.

- **AR-1** — Par format : une **bibliothèque** .NET (`Kape22Importer` : descripteur,
  XSD, DTO, entité, mapper, persister, `InboxScanner`, `Kape22FichierProcessor`)
  **plus** une classe **`Client : Publisher`** mince enregistrée dans le `Launcher`
  (`MicroServices.sln`). `TextToXml` est le **seul** code partagé côté Étape 1.
  `Kape22Importer` + son `Client` sont le gabarit des formats suivants (§0, D24, FR-16).
- **AR-2** — **`TextToXml.sln`** (ce dépôt) : `TextToXml` (lib) + `Kape22Importer`
  (**lib** format P60) + projets de tests ; référence `PortalSharedLibrary` (D20).
  Le `Client` exécutable vit dans **`MicroServices.sln`** (`ProjectReference`
  cross‑dépôt vers la lib `Kape22Importer` + `MicroService.csproj`). **Prérequis** :
  `MicroServices.sln` aligné sur `net10.0` + EF Core 10.0.x avant l'implémentation
  de la Story 3.4.
- **AR-3** — **1 XSD statique écrit à la main par format** (`P60.xsd`), versionné,
  décrivant le **XML normalisé**. **Pas** de méta‑schéma des descripteurs
  (`commande.xsd`) (D10).
- **AR-4** — Le **DTO C# `Kape22File` est généré** depuis `P60.xsd` (`xsd.exe /classes`).
  Le XML normalisé est **validé contre le XSD avant désérialisation** (D10, FR-7).
- **AR-5** — `P60.xml` (dans `Templates/`) est **embarqué comme ressource** dans
  `Kape22Importer` ; unique évolution v1 : **ajout d'un `datatype` par `<value>`**
  (dérivé du type de colonne `L_D_KAPE22`, D6) + attribut `expectedMessageCount="1"`.
  Positions 526‑636 **ignorées** (D5).
- **AR-6** — Accès **système de fichiers / partage** (`D:\Site-FTP\Reception\GPAO`
  sur `AFS017`), pas de protocole FTP. Chemin en configuration (`Import:InboxPath`).
  Cible d'évolution : MQTT, sans toucher `TextToXml` (D1).
- **AR-7** — Worker = classe **`Client : Publisher, IPublisher, IService`** pilotée
  par le timer de `Publisher` (`Frequency` = intervalle de polling, `Execute` = un
  tick appelant `InboxScanner.RunTick` + `PurgeRetention`), modèle
  `Laminoir/OrdresFabricationSync/Client.cs`. Abstraction **`IFileSource`**
  (`DirectoryFileSource` en prod, impl. mémoire en test) **inchangée** (§4.3, FR-12).
- **AR-8** — Base **database‑first** : `AscoLsiDbContext` fige les tables
  `L_D_KAPE22` (92 colonnes) + `L_D_LOG_COMMANDE` d'après Annexe C. **Aucune
  migration**, `Id` identity (D8, D9, Annexe C).
- **AR-9** — Enregistrement = **une ligne** dans `Launcher/WorkerRegistry.cs`
  (`Factories`, enveloppée dans `WorkerAdapter<Client>`) + une entrée
  `Launcher/workers.json`. Le Launcher fournit : l'hôte HTTP unique, `WorkerAdapter`
  (qui **est** le `WorkerStatus` : `IsRunning = Client.IsConnected`, `IsActive`,
  `LastStartedAt`, `LastError`), la table `WorkerSettings` (**Launcher‑owned**,
  auto‑créée), la pause `IsActive`, l'auth `X-Launcher-Api-Key`. Le worker
  **ne touche pas** `WorkerSettings` et **n'expose aucun HTTP** (D9, FR-14).
- **AR-10** — Encodage d'entrée **`Windows-1252` figé** ; décodeur **strict** ;
  la lib enregistre elle‑même `CodePagesEncodingProvider` (D2, D19, FR-2).
- **AR-11** — Jeu de fixtures : les 10 fichiers `P60/` + variantes fautives
  dérivées + **1 descripteur synthétique non‑P60** `fixtures/generic/` (Annexe A.4).
- **AR-12** — **Tests d'intégration base de données : instance SQL Server locale
  + schéma versionné.** Tous les tests qui touchent SQL Server (EF Core, scripts
  SQL, transactions, garde‑fou anti‑doublon, E2E) s'exécutent contre une
  **instance SQL Server** (édition **Developer**, gratuite dev/test), base(s) de
  test **`AscoLSI_Test`** + **`MQTTnetServices_Test`** (ou schémas équivalents sur
  la même instance). **Jamais la base de production.** Les tests unitaires
  (`TextToXml`, `Kape22Mapper` hors persistance) et les tests `IFileSource`
  mémoire **ne** requièrent **pas** SQL Server.
  - **Schéma** — scripts SQL **idempotents versionnés** dans `scripts/schema/`
    (`01-ascolsi-tables.sql`, `02-mqtt-logs.sql`…) créant **uniquement** les
    tables nécessaires aux tests : `L_D_KAPE22`, `L_D_LOG_COMMANDE`,
    `MQTTnetServices.dbo.Logs`, `dbo.WorkerSettings` — **rien d'autre** de la base
    réelle.
  - **Provenance (R-3)** — ces scripts sont **générés depuis la base réelle
    `AFV004-LSI`** (`sqlcmd` sur `sys.columns` / `GENERATE SCRIPTS`), en‑tête
    portant **serveur, base et date d'extraction**. Jamais rédigés de mémoire.
    Un test « modèle `AscoLsiDbContext` ⟺ `scripts/schema/` » verrouille la
    dérive.
  - **Isolation entre tests — deux régimes selon ce qui est testé :**
    - **`TransactionScope` + rollback** (défaut) pour les tests qui lisent/écrivent
      sans dépendre du commit : rapide, aucun nettoyage. En async ⇒
      `TransactionScopeAsyncFlowOption.Enabled` **obligatoire**.
    - **Commit réel + reset de données** (`Respawn` ou `TRUNCATE` ciblé) pour les
      tests qui **dépendent d'un état persisté entre deux actions** ou qui
      **testent les frontières de transaction** — un `TransactionScope` ambiant
      les fausserait : garde‑fou anti‑doublon (`AC-FR11-6/11-7`, D22 — la 2ᵉ
      tentative doit voir la ligne `… — OK` **commitée** par la 1ʳᵉ), rollback
      sur échec (`AC-FR11-3/11-5`), E2E 10 fichiers (Story 3.6).
  - **Configuration** — chaîne(s) de connexion de test dans `appsettings.Test.json`
    (ou User Secrets / variables d'environnement en CI), pointant vers l'instance
    de test. **Jamais en dur.** Aucun secret au dépôt.
  - **Catégorisation** — ces tests portent `[Trait("Category","Integration")]`.
    `dotnet test --filter Category=Unit` ne les exécute pas ; `Category=Integration`
    requiert **une instance SQL Server joignable**. À défaut, ils sont **ignorés
    avec un message clair** (pas en échec) — détection par tentative de connexion
    brève dans la fixture niveau assembly.
  - **CI** — le workflow exécute `Category=Unit` sur chaque push (inchangé).
    `Category=Integration` tourne quand un runner fournit SQL Server : conteneur
    Linux `mcr.microsoft.com/mssql/server` **si** le runner est Linux, **ou**
    instance SQL Server native sur runner Windows. Les mêmes `scripts/schema/`
    servent dans les deux cas. **Aucune dépendance Docker en développement.**

- **AR-13** — **Frontière cross‑solution** (correction de cap 2026‑09‑09). La lib
  `Kape22Importer` ne référence que `TextToXml` + `PortalSharedLibrary` (garde
  `AC-FR16-1` inchangée). Seule la classe `Client` référence `MicroService.csproj`
  (broker, `AbstractService`/`Publisher`, `SharedLogger`) — c'est le **7ᵉ point de
  variation** d'un format (voir `AC-FR16-2` amendé). `Client` est hors périmètre de
  `TextToXml.Tests` ; ses tests vivent dans `MicroServices.sln`
  (`OrdresFabricationSync.Tests` comme modèle : provider EF InMemory, pas de broker
  réel). Le bump `MicroServices.sln` → `net10.0` / EF Core 10.0.x est un prérequis
  d'infra suivi hors de ce sprint (passe archi + tests de non‑régression sur les
  workers en prod : `ImportFiles`, `CopyDataToDb`, `ConvertAndSave`,
  `OrdresFabricationSync`).

- **AR-14** — **Transaction unique étendue** (spine `architecture-kape22-dispatch-2026-09-14`,
  AD-1). `L_D_KAPE22`, `L_D_LOG_COMMANDE` et les 9 tables aval (`L_D_ORDRE_FABRICATION`,
  `L_D_COULEE`, `L_D_CONSIGNES`, 7×`L_D_SECTIONCHARGE_*`) sont ajoutées au **même**
  `DbContext` et committées par **un seul** `SaveChanges()` — aucune écriture partielle,
  préserve NFR-7 et le garde-fou anti-doublon `AC-FR11-6/7` (FR-21).
- **AR-15** — **Mapping explicite, sans réflexion** (AD-2). Un mapper par table cible
  (`OrdreFabricationMapper`, `CouleeMapper`, `ConsignesMapper`,
  `SectionCharge{Chutage,Lingot,Decoupe,Pits,PoidsMetrique,Refroidissoirs,Svt}Mapper`),
  propriétés lues/écrites par leur nom explicite. Aucun `System.Reflection`,
  `Activator.CreateInstance` ni table de mapping interprétée à l'exécution (FR-18, FR-19).
- **AR-16** — **Mur d'étanchéité avec l'application legacy** (AD-3). Le code
  `Ascometal.LSI.DAL`/`BLL` (dépôt legacy, hors `TextToXml`) est une référence de lecture
  seule pour comprendre l'intention métier ; il n'est **jamais modifié**, **jamais référencé
  en assembly**, **jamais appelé à l'exécution** par `Kape22Importer`. Les nouvelles
  entités EF sont redéfinies dans `Kape22Importer.Persistence`, indépendamment des classes
  `EntityObject` legacy.
- **AR-17** — **Cause d'échec journalisée via le circuit existant** (AD-4). Tout échec du
  dispatch (SQL ou métier : coulée introuvable, répartition lingots/fours incohérente,
  section de charge manquante) devient un `ConversionError{Block:File, Code}` distinct et
  rejoint `L_D_LOG_COMMANDE` (`"<NumeroFichier> — REJETÉ : <cause>"`, AC-FR11-4) +
  `MQTTnetServices.Logs` (FR-14) + déplacement en `error/` (FR-12). Pas de nouveau canal.
- **AR-18** — **Entités EF database-first, sans migration** (AD-5). Les 10 nouvelles
  entités suivent la convention `L_D_KAPE22.cs`/AR-8 : un fichier par entité sous
  `Persistence/`, schéma dérivé d'`AFV004-LSI` (`sys.columns`, jamais rédigé de mémoire,
  R-3), `scripts/schema/` étendu pour que les tests AR-12 les couvrent.
- **AR-19** — **`Kape22ImportBundle` remplace `MapResult<L_D_KAPE22>` au niveau du
  Persister** (AD-6). Le bundle porte les mêmes métadonnées (`Success`, `Errors`,
  `Warnings`, `NumeroFichier`, `OF`) plus les 9 entités avales nullables.
  `Kape22Persister.Persist` est **remplacé** (pas surchargé) pour accepter le bundle ;
  `Kape22FichierProcessor` (Story 3.2) est mis à jour dans la même story — aucune double
  surface de persistance.
- **AR-20** — **Liaisons inter-tables par FK explicite** (AD-7). Chaque entité du bundle
  porte sa clé métier en colonne scalaire (`OF`, clé de la section de charge propriétaire
  pour ses `L_D_CONSIGNES`) ; les mappers assignent ces colonnes directement, **aucune**
  propriété de navigation EF (`ICollection<T>`, `AddRef` façon legacy) n'est utilisée pour
  relier les entités du bundle entre elles.

### UX Design Requirements

**Aucune.** v1 ne livre **ni UI ni écran dédié** (PRD §5, D12). Supervision via
ServicesMicroScope / Launcher existants + XML conservé + `Message` de
`L_D_LOG_COMMANDE` + fichiers `*.errors.json`. Les règles UI du PRD §4.5.2
(Bootstrap, séparation C#/JS, CSS trié) sont notées pour les **futurs**
microservices à UI et ne s'appliquent à aucune story v1.

### Critères d'acceptation transverses (CC)

**Appliqués à CHAQUE story de développement** (rappelés en fin de chaque story).

- **CC-1 — TDD strict.** Approche test‑first, vérifiée par **résultat +
  attestation** :
  - **Résultat (obligatoire, vérifiable) :** chaque `AC-FRx-y` / `CTR-x` de la
    story a **au moins un test xUnit nommé** portant son `[Trait("AC","FRx-y")]`
    (test agrégateur Story 3.6). Un `AC` sans test vert = story **non terminée**
    (SM‑1). Aucune logique de production sans le test qui la motive.
  - **Attestation (obligatoire) :** la description de PR liste, pour chaque `AC`,
    le test correspondant, et atteste que le test a été écrit **avant** le code —
    nécessaire là où le merge écrase l'historique (squash).
  - **Cérémonie rouge→vert :** attendue pour les tests de comportement (unitaires
    `TextToXml` / `Kape22Mapper`, intégration EF). **Exemptés** du « rouge »
    propre : les **tests‑barrière‑à‑la‑compilation** (`AC-FR7-3`, `AC-FR16-1..4`,
    `AC-FR9-6` — tests d'architecture / de complétude par réflexion) dont la forme
    d'échec est un **build qui casse**, et l'infrastructure de test elle‑même
    (harnais de base de test Story 2.1). Ces cas restent test‑first au sens : le test /
    l'assertion existe et échoue **avant** que le code de prod le satisfasse.
- **CC-2 — Commentaires : langue & syntaxe.** Tous les commentaires (C#, XML, SQL,
  YAML, `.csproj`, scripts) sont en **anglais**. Chaque phrase de commentaire
  commence par une **majuscule** et se termine par un **point**. Les listes
  numérotées dans les commentaires sont **interdites**. **Dérogation (2026-09-14) :**
  restent non traduits, comme des termes propres, les noms de domaine suivants —
  `Champ`, `Ligne`, `Bloc`, `Fichier`, `Descripteur`, `Segment` (Épics 1‑3) et,
  depuis l'Épic 4, `Ordre de Fabrication`/`OF`, `Coulee`, `Chutage`, `Decoupe`,
  `Lingot`, `PoidsMetrique`, `Refroidissoirs` — ainsi que la citation `Annexe
  A/B/C` pour renvoyer à une annexe PRD précise. Toute autre expression
  française, notamment multi‑mots, doit être traduite.
- **CC-3 — Commentaires : position & préservation.** **Aucun** commentaire en fin
  de ligne (trailing). Chaque commentaire est sur sa **propre ligne**,
  immédiatement **au‑dessus** du bloc qu'il décrit. Les commentaires existants
  sont **conservés à l'identique** sauf si le code sous‑jacent est modifié.
- **CC-4 — Tri alphabétique.** Les propriétés des classes et des objets (records
  DTO, entités EF, classes de configuration, `ConversionError`, `ImportResult`…)
  sont déclarées par **ordre alphabétique** ; les initialiseurs d'objet suivent
  le même ordre que la déclaration du type. **Dérogation (2026-09-07) :** ne sont
  **pas** visés les tuples nommés locaux (`(string FieldId, string Segment, …)`)
  ni les déconstructions, dont l'ordre des éléments est positionnel et porteur de
  sens à la lecture ; ils restent libres de suivre l'ordre métier. Voir R-7.
- **CC-5 — Vocabulaire.** Les identifiants de code et de test réutilisent **à
  l'identique** le vocabulaire du glossaire PRD §3 (`Fichier`, `Ligne`, `Bloc`,
  `Champ`, `Descripteur`, `Valeur brute`, `Valeur normalisée`, `XML normalisé`,
  `Entité cible`, `Mapping`, `ConversionError`, `ConversionResult`, `ImportResult`,
  `ErrorCode`…). Les noms de test citent le `AC-FRx-y` / `CTR-x` couvert.
- **CC-6 — Pureté / dépendances (`TextToXml` uniquement, Épic 1).** Le projet
  `TextToXml` n'a **aucune** dépendance runtime hors du framework partagé, à la
  seule exception de `System.Text.Encoding.CodePages` (les API `System.Xml` sont
  dans le framework) — liste autorisée figée par le PRD §4.4. Aucune I/O disque,
  réseau, EF, ni état statique mutable.
- **CC-7 — Secrets (`Kape22Importer` uniquement, Épics 2 & 3).** Aucune chaîne de
  connexion ni secret en dur ; tout provient de `IConfiguration`. Aucun secret
  ajouté au dépôt.

### FR Coverage Map

| FR | Épic — Story(ies) | `AC-FRx-y` couverts |
|---|---|---|
| FR-1 | Épic 1 — 1.2 (cœur), 1.8 (`AC-FR1-9`) ; Épic 2 — 2.3 (`AC-FR1-13`) | AC-FR1-1 … AC-FR1-13 |
| FR-2 | Épic 1 — 1.3 | AC-FR2-1 … AC-FR2-7 |
| FR-3 | Épic 1 — 1.4 | AC-FR3-1 … AC-FR3-8 |
| FR-4 | Épic 1 — 1.5 | AC-FR4-1 … AC-FR4-6 |
| FR-5 | Épic 1 — 1.6 ; Épic 2 — 2.3 (`AC-FR5-12b`, `AC-FR5-14`) | AC-FR5-1 … AC-FR5-14 |
| FR-6 | Épic 1 — 1.7 | AC-FR6-1 … AC-FR6-7 |
| FR-7 | Épic 2 — 2.4 ; Épic 2 — 2.3 (`AC-FR7-1`) | AC-FR7-1 … AC-FR7-6 |
| FR-8 | Épic 2 — 2.5 | AC-FR8-1 … AC-FR8-6 |
| FR-9 | Épic 2 — 2.6 | AC-FR9-1 … AC-FR9-6 |
| FR-10 | Épic 2 — 2.7 | AC-FR10-1 … AC-FR10-7 |
| FR-11 | Épic 2 — 2.8 | AC-FR11-1 … AC-FR11-8 |
| FR-12 | Épic 3 — 3.1 | AC-FR12-1 … AC-FR12-9 |
| FR-13 | Épic 3 — 3.2 | AC-FR13-1 … AC-FR13-5 |
| FR-14 | Épic 3 — 3.3 (`AC-FR14-1..4, 14-7, 14-8`), 3.4 (`AC-FR14-5, 14-6`) | AC-FR14-1 … AC-FR14-8 |
| FR-15 | Épic 3 — 3.5 | AC-FR15-1 … AC-FR15-4 |
| FR-16 | Épic 1 — 1.8 | AC-FR16-1 … AC-FR16-4 |
| FR-17 | Épic 4 — 4.2 | AC-FR17-1 … AC-FR17-5 |
| FR-18 | Épic 4 — 4.3 | AC-FR18-1 … AC-FR18-4 |
| FR-19 | Épic 4 — 4.4 | AC-FR19-1 … AC-FR19-4 |
| FR-20 | Épic 4 — 4.5 (contrôles purs), 4.6 (`AC-FR20-5`, existence coulée) | AC-FR20-1 … AC-FR20-5 |
| FR-21 | Épic 4 — 4.6 (persister), 4.7 (E2E) | AC-FR21-1 … AC-FR21-5 |
| CTR-1/2/3 | Épic 1 — 1.8 | contrat `decimal`/`datetime`/`convert` + round‑trip typé |
| SM-1/2/3 | Épic 3 — 3.6 | Couverture 100 % + E2E 10 fichiers + `*.errors.json` lisible |

### NFR Coverage Map

| NFR | Story(ies) porteuse(s) | Vérification |
|---|---|---|
| NFR-1 | 1.7 (`Convert` seul), 3.6 (bout en bout) | test de perf < 200 ms / Fichier |
| NFR-2 | 3.6 | test de perf tick 500 Fichiers < 30 s |
| NFR-3 | 1.7 | test 100 `Convert` concurrents == séquentiel |
| NFR-4 | 1.1 (`.csproj`), garde `CC-6` sur toutes les stories Épic 1 | inspection `PackageReference` + test d'architecture |
| NFR-5 | 2.1 (`DbContext`/connexions), 2.8 (`AC-FR11-8`), garde `CC-7` Épics 2‑3 | test « connexions lues de `IConfiguration` », revue secrets |
| NFR-6 | 3.3 (double log), 3.6 (traçabilité) | E2E : nom fichier → `InsertedId` / `Errors` via `Logs` + `L_D_LOG_COMMANDE` + `*.errors.json` |
| NFR-7 | 3.1 (`AC-FR12-6`), 3.5 (`AC-FR15-4`) | test reprise sans action en base |
| NFR-8 | 1.1, 2.1 | inspection des versions de packages (`Microsoft.EntityFrameworkCore` 10.0.x, `Serilog.Sinks.MSSqlServer`) |
| NFR-9 | 3.4 (`AC-FR14-6`) | test arrêt propre < 5 s, jamais de demi‑insertion |

**AR-12 (tests d'intégration base de données locale)** — harnais central :
**Story 2.1** (fixture assembly SQL Server + `scripts/schema/` idempotents généré
depuis `AFV004-LSI` + double régime d'isolation `TransactionScope` / reset) ;
réutilisé par **2.8** (persistance transactionnelle), **3.3** (écritures de
logs), **3.5** (`AC-FR15-3/4`), **3.6** (E2E 10 fichiers). Tests unitaires
(`TextToXml`, `Kape22Mapper` hors persistance) sans SQL Server.

### Risques & points à trancher en début d'épic

| # | Risque / point | Impact | Action | Statut |
|---|---|---|---|---|
| R-1 | Exécution des tests `Integration` sur cible Windows Server 2019 sans conteneurs Linux (pas de WSL2/Hyper-V) | catégorie `Integration` non exécutable | **Tranché (utilisateur, 2026-09-04) :** abandon de Docker/Testcontainers. Tests contre une **instance SQL Server locale** (Developer Edition) + `scripts/schema/` versionné ; skip propre si aucune instance joignable (détection connexion). CI : conteneur Linux `mssql/server` sur runner Linux, ou SQL natif sur runner Windows. Voir `sprint-change-proposal-2026-09-04.md`. | ✅ résolu |
| R-2 | `PortalSharedLibrary` : mode de référencement (D20) | bloque le build de la solution (Story 1.1) | **Tranché (utilisateur) :** `ProjectReference` pointant vers l'emplacement de `PortalSharedLibrary` dans l'arborescence (chemin standard de la solution / dépôt `PortalFosMarcegaglia`). Story 1.1 : documenter le chemin exact dans le `README`. | ✅ résolu |
| R-3 | `scripts/schema/*.sql` dérive vs schéma réel de `AscoLSI` | tests d'intégration verts contre un faux schéma (contre‑métrique SM‑3) | **Résolu (2026-09-04) :** `scripts/schema/01-ascolsi-tables.sql` + `02-mqtt-tables.sql` **générés depuis `AFV004-LSI`** (SQL Server 2012, `AscoLSI` + `MQTTnetServices` **même instance**), en‑tête de provenance daté, **validés** (exécution idempotente en base jetable). Story 2.1 : câbler la chaîne de test (User Secrets / `appsettings.Test.json` gitignoré) + test « `AscoLsiDbContext` ⟺ `scripts/schema/` ». **NB : PRD Annexe A.2 / C.1 / C.2 corrigées (2026-09-04) — les longueurs texte étaient en octets (×2), désormais en caractères, alignées sur `scripts/schema/`.** | ✅ résolu |
| R-4 | Ordre des enfants du XML normalisé vs `<xs:sequence>` de `P60.xsd` | validation `AC-FR5-14` / `AC-FR7-1` casse si divergence | Décision : XML émis dans l'**ordre du Descripteur**, `P60.xsd` en `<xs:sequence>` **même ordre** (Stories 1.6 & 2.3). | ✅ résolu |
| R-5 | `xsd.exe` génère `Kape22File` dans l'ordre du schéma, pas alphabétique — conflit `CC-4` | friction inutile / post‑traitement fragile | `CC-4` **ne s'applique pas** aux fichiers générés ; membres ajoutés à la main → classe partielle triée (Story 2.3). | ✅ résolu |
| R-6 | Source de `max_length` pour `AC-FR8-2` non spécifiée | dépendance schéma au runtime, ou constantes qui dérivent | Story 2.5 : constantes issues d'Annexe C dans la config d'entité, pas de requête `sys.columns` live. | ✅ résolu |
| R-7 | Portée de `CC-4` sur les tuples nommés locaux et les déconstructions (Story 2.7, `Kape22Mapper.CheckFileName`) | friction de revue / réordonnancement qui casse la lisibilité positionnelle | **Tranché (utilisateur, 2026-09-07) :** `CC-4` **ne s'applique pas** aux tuples nommés locaux ni aux déconstructions ; leur ordre d'éléments reste métier/positionnel. `CC-4` continue de viser les classes, records, entités, classes de configuration et initialiseurs d'objet. Texte `CC-4` mis à jour. | ✅ résolu |

> **C2 (utilisateur)** : le fichier `Templates/P60.xml` est présent et à jour dans
> l'espace de travail — les Stories 2.2 / 2.3 en dérivent la liste complète des
> `<value>`.

### Setup / socle (sans `AC-FRx-y` direct mais nécessaires)

| Besoin | Story |
|---|---|
| Solution `TextToXml.sln`, projets, xUnit, `PortalSharedLibrary`, arbo fixtures + 10 fichiers valides, séparation catégories `Unit` / `Integration` | 1.1 |
| `AscoLsiDbContext` database‑first (`L_D_KAPE22`, `L_D_LOG_COMMANDE`) + **harnais de test SQL Server local** (`scripts/schema/` générés depuis `AFV004-LSI` + double régime d'isolation, AR-12) | 2.1 |
| `P60.xml` enrichi (`datatype`, `expectedMessageCount`) + ressource embarquée | 2.2 |
| `P60.xsd` + génération DTO `Kape22File` + validation avant désérialisation | 2.3 |
| `AscoLsiDbContext` étendu (10 tables aval : OF, Coulée, Consignes, 7×SectionCharge) + extension du harnais SQL Server local (AR-12, AR-18) | 4.1 |

> **Fixtures fautives (Annexe A.4)** : chaque story crée **les fixtures fautives
> dont elle a besoin** (`two_lines.txt`, `segment_mismatch.txt`,
> `non_numeric_diametre.txt`, `empty_required.txt`…). La Story 1.1 ne fournit que
> l'arborescence `fixtures/{valid,generic}/` et les 10 fichiers `P60/` valides.

## Epic List

### Épic 1 : Bibliothèque générique `TextToXml` — Fichier → XML normalisé
Livrer la bibliothèque .NET **pure et générique** qui convertit un fichier plat
largeur fixe en **XML normalisé désérialisable**, pilotée par le seul
Descripteur. À l'issue de l'épic : `Converter.Convert(bytes, descriptor)` est
utilisable par n'importe quel microservice de format, avec un contrat
`ConversionResult` stable, un jeu de tests xUnit vert couvrant `AC-FR1..6` +
`AC-FR16` + `CTR-1..3`, et une preuve de généricité (descripteur synthétique
non‑P60).
**FRs couverts :** FR-1, FR-2, FR-3, FR-4, FR-5, FR-6, FR-16 (+ CTR-1/2/3).

### Épic 2 : `Kape22Importer` — contrat de format, mapping & persistance
Livrer la chaîne **Étape 2** pour P60 : entité EF `L_D_KAPE22` (database‑first),
descripteur `P60.xml` enrichi, `P60.xsd` + DTO généré, désérialisation → mapping
→ règles dérivées → contrôles de cohérence (Warnings) → **insertion
transactionnelle** avec garde‑fou anti‑doublon, et le **contrôle de compatibilité
descripteur ↔ table au démarrage**. À l'issue de l'épic :
`Kape22Mapper.Map(xml, fileName)` produit une entité insérable, la persistance
est atomique, `AC-FR7..11` + `AC-FR1-13` + `AC-FR5-12b/14` sont verts.
**FRs couverts :** FR-7, FR-8, FR-9, FR-10, FR-11.

### Épic 3 : `Kape22Importer` — worker, orchestration & exploitation
Livrer le **worker** supervisé par le Launcher : scrutation du dossier de
réception via `IFileSource`, cycle de vie `processing`/`archive`/`error`, purge
de rétention, orchestration stricte par Fichier, **double journalisation**
(`MQTTnetServices.Logs` + `L_D_LOG_COMMANDE`), intégration Launcher, robustesse
de boucle, et validation de bout en bout (SM‑1/2/3). À l'issue de l'épic : le
microservice tourne, chaque rejet produit une raison lisible par l'exploitant,
`AC-FR12..15` sont verts et les 10 fichiers `P60/` insèrent 10 lignes cohérentes.
**FRs couverts :** FR-12, FR-13, FR-14, FR-15 (+ SM-1/2/3).

### Épic 4 : `Kape22Importer` — Dispatch transactionnel vers les tables aval
Compléter le pipeline P60 après l'insertion `L_D_KAPE22` (Épic 2, FR-11) par le
dispatch explicite — **sans réflexion** — vers `L_D_ORDRE_FABRICATION`,
`L_D_COULEE`, `L_D_CONSIGNES` et les 7 `L_D_SECTIONCHARGE_*` (Chutage, Lingot,
Découpe, Pits, PoidsMétrique, Refroidissoirs, SVT), en remplacement de la
procédure legacy à base de `MappingTemplate`/réflexion (`Ascometal.LSI.DAL`,
dépôt legacy, **jamais modifié, jamais appelé** — AD-3 du spine d'architecture).
À l'issue de l'épic : un Fichier valide écrit **une seule transaction** couvrant
les 10 tables ; toute violation métier ou échec SQL bloque l'ensemble, journalise
la cause précise via le circuit existant (`L_D_LOG_COMMANDE` +
`MQTTnetServices.Logs`) et déplace le Fichier en `error/`, sans jamais laisser de
`L_D_KAPE22` orpheline (préserve NFR-7 et le garde-fou anti-doublon
`AC-FR11-6/7`).
**FRs couverts :** FR-17, FR-18, FR-19, FR-20, FR-21.
**Architecture :** `_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md`.
**Séquencement (7 stories) :** 4.1 → 4.2 → {4.3, 4.4} → 4.5 → 4.6 → 4.7.

---

## Épic 1 : Bibliothèque générique `TextToXml` — Fichier → XML normalisé

Bibliothèque .NET 10.0 **pure** (aucune I/O, aucun état, aucun EF) qui convertit
des octets `Windows-1252` + un Descripteur XML en `ConversionResult` porteur d'un
**XML normalisé** désérialisable. Générique : aucun littéral propre à P60.

### Story 1.1 : Mise en place de la solution et du socle de tests

As a développeur du dépôt `TextToXml`,
I want une solution `TextToXml.sln` autonome avec les projets `TextToXml`,
`Kape22Importer`, `TextToXml.Tests`, `Kape22Importer.Tests` et l'arborescence de
fixtures,
So that toute story suivante démarre sur un socle compilable et testable en TDD.

**Acceptance Criteria:**

**Given** un poste .NET 10.0 SDK
**When** je clone le dépôt et lance `dotnet build TextToXml.sln`
**Then** la solution compile avec les projets `TextToXml` (`net10.0`, `Nullable=enable`,
  `LangVersion=latest`), `Kape22Importer` (`Microsoft.NET.Sdk.Worker`, `net10.0`),
  `TextToXml.Tests` et `Kape22Importer.Tests` (xUnit)
**And** `TextToXml` n'a **aucune** `PackageReference` hors BCL (`System.Xml`,
  `System.Text.Encoding.CodePages` uniquement) (NFR-4)
**And** `Kape22Importer` référence `TextToXml` et `PortalSharedLibrary` (identité/log)
  et **rien d'autre de propre à P60** dans les `ProjectReference` (AR-1, AR-2, AC-FR16-1)

**Given** la solution
**When** j'inspecte l'arborescence de test
**Then** `TextToXml.Tests/fixtures/` contient les sous‑dossiers `valid/` et `generic/`
**And** les 10 fichiers `P60/P60_847_682_001..010` sont copiés dans
  `fixtures/valid/` (contenu binaire identique aux échantillons)
**And** les fixtures **fautives** (Annexe A.4) ne sont **pas** créées ici — chaque
  story les ajoute au fil de l'eau

**Given** la solution
**When** je lance `dotnet test TextToXml.sln`
**Then** la commande s'exécute (0 test ou tests squelette) et sert de porte SM‑1
  pour toutes les stories suivantes
**And** les tests sont séparés en deux catégories via `[Trait("Category","Unit")]`
  et `[Trait("Category","Integration")]` ; `dotnet test --filter Category=Unit`
  tourne **sans base de données**, `Category=Integration` requiert une instance
  SQL Server joignable (AR-12)

**Note (risque à lever tôt) :** `PortalSharedLibrary` est référencé par D20 sans
préciser NuGet interne vs `ProjectReference` externe (dépôt
`PortalFosMarcegaglia` visible dans l'espace de travail). La story doit trancher
le mode de référencement et le documenter dans le `README`.

**Critères transverses :** CC-2, CC-3, CC-4 (fichiers `.csproj`, `Directory.Build.props`),
CC-6. *(CC-1 sans objet : story de scaffolding, pas de logique.)*

---

### Story 1.2 : Chargement & validation du Descripteur

As a développeur d'un microservice de format,
I want que `TextToXml` lise et valide le Descripteur XML directement (sans
méta‑schéma) et signale toute anomalie de layout par une `Error` `LayoutInvalid`
sans lever d'exception,
So that une erreur de configuration du format est diagnostiquée immédiatement et
proprement.

**Acceptance Criteria:**

**Given** un Descripteur (racine `<commande type format [expectedMessageCount]
  [segmentField] [headerMarker] [messageMarker] [footerMarker]>`, sections
  `<header>` opt. / `<message>` req. / `<footer>` opt., `<value Id Position Size
  datatype [convert] [Description]>`)
**When** j'appelle `Converter.Convert(input, descriptor)` avec un descripteur bien formé et valide
**Then** le descripteur est accepté et la conversion se poursuit
**And** un descripteur sans `<header>` ni `<footer>` est accepté, toutes les Lignes
  devenant des Détails (AC-FR1-6)
**And** deux Champs aux tranches qui se chevauchent (ex. `Segment` et
  `NumeroFichier` du message, `Position=9`) sont acceptés **sans erreur** (AC-FR1-7, D23)
**And** un descripteur sans `segmentField`/`*Marker` est accepté et ne produit
  **aucun** `SegmentMismatch` (AC-FR1-10)

**Given** un Descripteur invalide
**When** j'appelle `Convert`
**Then** le résultat est `Success=false` avec **une** `Error`
  `{Block:File, LineNumber:0, Code:LayoutInvalid}` et **aucune exception**, pour chacun des cas :
  XML non bien formé (AC-FR1-1) ; section `<message>` absente (AC-FR1-2) ; deux
  `<value>` de même `Id` dans le même Bloc (AC-FR1-3) ; `Position`/`Size` absent,
  négatif ou non entier — le `Message` cite l'`Id` (AC-FR1-4) ; `datatype` non
  reconnu (∉ `string|int|decimal|datetime`) (AC-FR1-5) ; `segmentField` désignant
  un `Id` absent d'un Bloc (AC-FR1-11) ; `format="Semicolon"` — `Message` « non
  supporté en v1 » (AC-FR1-12, D24)

**Given** `descriptor == null`
**When** j'appelle `Convert`
**Then** une `ArgumentNullException` est levée (**seul** cas d'exception autorisé) (AC-FR1-8)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR1-1`, `AC-FR1-2`,
`AC-FR1-3`, `AC-FR1-4`, `AC-FR1-5`, `AC-FR1-6`, `AC-FR1-7`, `AC-FR1-8`,
`AC-FR1-10`, `AC-FR1-11`, `AC-FR1-12`. *(`AC-FR1-9` → Story 1.8 ; `AC-FR1-13` →
Story 2.3.)*

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-6.

---

### Story 1.3 : Décodage `Windows-1252` strict & découpage en Lignes

As a `TextToXml`,
I want décoder les octets d'entrée en `Windows-1252` avec un décodeur **strict**
et découper le Fichier en Lignes en tolérant `LF` et `CR LF`,
So that l'analyse travaille sur des Lignes fiables et tout octet illisible est
rejeté explicitement plutôt que corrompu.

**Acceptance Criteria:**

**Given** un Fichier de 0 octet, ou composé uniquement d'espaces / sauts de ligne
**When** j'appelle `Convert`
**Then** le résultat porte **une** `Error` `{Block:File, LineNumber:0, Code:EmptyFile}` (AC-FR2-1, AC-FR2-2)

**Given** un Fichier contenant l'octet `0xE9` dans un Champ texte
**When** j'appelle `Convert`
**Then** la Valeur normalisée contient `"é"` (ni `"?"`, ni exception) — la lib
  enregistre elle‑même `CodePagesEncodingProvider` (AC-FR2-3, AR-10)

**Given** un Fichier contenant un octet non décodable en `Windows-1252`
**When** j'appelle `Convert`
**Then** le résultat porte `{Block:File, LineNumber:0, Code:UndecodableInput}` et
  **jamais** d'exception (AC-FR2-4, D19)

**Given** des fins de ligne variées
**When** j'appelle `Convert`
**Then** la dernière Ligne est prise en compte même sans `LF` final (AC-FR2-5) ;
  les fins `LF` et `CR LF` mixtes sont correctement détectées, le `CR` résiduel
  retiré avant analyse (AC-FR2-6) ; un `LF` final n'ajoute pas de Ligne vide (AC-FR2-7)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR2-1` … `AC-FR2-7`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-6.

---

### Story 1.4 : Affectation des Blocs, contrôle du nombre de Lignes & contrôle `Segment`

As a `TextToXml`,
I want affecter chaque Ligne à un Bloc (`Header`/`Detail`/`Footer`) selon les
sections déclarées et `expectedMessageCount`, vérifier le compte de Lignes, et
contrôler la valeur `Segment` de façon **non bloquante**,
So that la structure du Fichier est validée sans figer de règle propre à P60.

**Acceptance Criteria:**

**Given** un Descripteur `<header>`+`<footer>`, `expectedMessageCount="1"` (profil KAPE22)
**When** le Fichier n'a **pas** exactement 3 Lignes non vides
**Then** `{Block:File, LineNumber:0, Code:WrongBlockCount}` citant attendu vs
  trouvé, **aucune** analyse de Champ (AC-FR3-1, AC-FR3-8)
**And** avec 3 Lignes : Ligne 1 = `Header`, 2 = `Detail`, 3 = `Footer` (AC-FR3-2)

**Given** un Descripteur **sans** `<header>` ni `<footer>`, `expectedMessageCount` absent
**When** le Fichier a 5 Lignes
**Then** 5 Blocs `Detail`, aucune erreur (AC-FR3-3)

**Given** un Descripteur avec `<header>` seul (pas de `<footer>`)
**When** le Fichier a 4 Lignes
**Then** Ligne 1 = `Header`, Lignes 2‑4 = `Detail` (AC-FR3-4)

**Given** `expectedMessageCount="1"`
**When** le Fichier porte 2 Lignes de Détail
**Then** `WrongBlockCount` (AC-FR3-5)

**Given** un Bloc dont le Champ `Segment` ≠ son marqueur attendu (ex. Détail lu
  `"000"`, attendu `"EOF"`)
**When** j'appelle `Convert`
**Then** un **`Warning`** `{Block:Detail, LineNumber:2, FieldId:"Segment",
  Code:SegmentMismatch, RawValue:"000"}` ; `Success` **inchangé**, le Fichier est
  traité ; chaque écart = un `Warning` distinct (AC-FR3-6, D16)

**Given** des lignes vides
**When** elles sont en **fin** de Fichier (ou `LF` final)
**Then** elles sont ignorées avant le décompte ; une ligne vide **au milieu**
  compte comme une Ligne → `WrongBlockCount` (AC-FR3-7)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR3-1` … `AC-FR3-8`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-6.

---

### Story 1.5 : Contrôle de longueur des Lignes (`format="Fixed"`)

As a `TextToXml`,
I want vérifier que chaque Ligne couvre la `Position` de départ de chacun de ses
Champs (dernier Champ tronqué toléré),
So that une Ligne trop courte est signalée une seule fois, sans confondre
troncature finale et champ manquant.

**Acceptance Criteria:**

**Given** une Ligne qui couvre la `Position` de tous ses Champs
**When** j'appelle `Convert`
**Then** aucune `LineTooShort`, même si le dernier Champ (`Filler`, `Reserve…`)
  est tronqué ou absent en fin (AC-FR4-1) ; Entête réelle de 19 caractères,
  Champ `Filler` @18 → valide (AC-FR4-3) ; Pied de 17 caractères, `Records` @12 → valide (AC-FR4-4)

**Given** une Ligne trop courte pour atteindre la `Position` d'un Champ
**When** j'appelle `Convert`
**Then** **une seule** `Error` `{Block, LineNumber, Code:LineTooShort}` citant la
  `Position` manquante vs la longueur réelle (AC-FR4-2, AC-FR4-6) ; Pied de 12
  caractères, `Records` @12 absent → `LineTooShort` (AC-FR4-4)

**Given** une Ligne Détail plus **longue** que le dernier Champ déclaré (637 > 526)
**When** j'appelle `Convert`
**Then** **pas d'erreur** ; le surplus est ignoré (AC-FR4-5, D5)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR4-1` … `AC-FR4-6`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-6.

---

### Story 1.6 : Extraction, typage & sérialisation du XML normalisé

As a `TextToXml`,
I want extraire chaque Champ, le normaliser selon son `datatype` (descripteur
directeur) et — **si et seulement si `Errors` est vide** — produire le XML
normalisé `<file>` déterministe et désérialisable,
So that l'Étape 2 dispose d'un document stable qu'un `XmlSerializer` relit sans
convertisseur custom.

**Acceptance Criteria:**

**Given** le Fichier de référence (Annexe A) et un Descripteur `<header>/<message>/<footer>`
**When** j'appelle `Convert`
**Then** `Xml` = `<file><header>…</header><message>…</message><footer>…</footer></file>`,
  chaque section avec **un enfant par Champ**, nom d'élément = `Id`, **tous** les
  `<value>` émis (AC-FR5-1)
**And** un Descripteur sans `<header>`/`<footer>` et N Lignes → `<file>` avec N
  `<message>` et aucun `<header>`/`<footer>` (AC-FR5-2)

**Given** des Valeurs brutes typées
**When** `Convert` normalise
**Then** `string` `"APERAM ALLOYS"` + padding → `<Client>APERAM ALLOYS</Client>`
  (`TrimEnd`, espaces internes conservés) (AC-FR5-3) ; `int` `"0005900"` →
  `5900`, `"0000000"` → `0`, **`""` → élément omis** (Champ typé vide, PRD §0bis
  D27 — révision décidée à la rétro Épic 1) (AC-FR5-4) ; `int` `"11A0"` ou
  `"-12"` → `Error {Code:InvalidInteger, FieldId, RawValue}` (non signé) (AC-FR5-5, D17) ;
  `string` (ou sans `datatype`) vide/espaces → élément **vide** `<Id></Id>` (AC-FR5-6) ;
  Champ sans `datatype` → `string`/`TrimEnd` (AC-FR5-7)

> **Révision post‑rétro (D27, 2026‑09‑04) :** `AC-FR5-4` / `AC-FR5-6` ont changé —
> un Champ **typé** (`int`/`decimal`/`datetime`) à valeur vide **omet** son élément
> (au lieu de `<Id></Id>`), pour que `P60.xsd` puisse le typer fort en
> `minOccurs="0"` et que `Kape22File` reçoive `int?`/`decimal?`/`DateTime?`. Modif
> `NormalizedXmlBuilder` + tests **livrés** (action rétro `epic-1-retro-item-7`).

**Given** le XML normalisé produit
**When** je l'inspecte / le recharge
**Then** `&`, `<`, `>` échappés, rechargeable via `XDocument.Parse` (AC-FR5-8) ;
  deux Champs qui se chevauchent (`Segment`/`NumeroFichier` @9) → les deux
  éléments émis avec leur valeur (AC-FR5-9) ; **sans BOM**, déclaration
  `<?xml version="1.0" encoding="utf-8"?>` (AC-FR5-10) ; deux appels →
  sortie **octet pour octet identique** (déterminisme) (AC-FR5-11)
**And** les enfants de chaque Bloc sont émis dans l'**ordre de déclaration des
  `<value>` du Descripteur** (ordre stable et prévisible pour le XSD de chaque
  format — voir Story 2.3, R-4) (AC-FR5-11)

**Given** un Fichier valide converti
**When** je désérialise le XML normalisé en un DTO `record` avec `[XmlElement]`
**Then** round‑trip valeur→XML→DTO sans perte : `int`/`decimal`/`DateTime`
  conservés (**`AC-FR5-12a`**, variante `record` générique)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR5-1` … `AC-FR5-11`,
`AC-FR5-12a`. *(`AC-FR5-12b` variante `Kape22File` et `AC-FR5-14` → Story 2.3 ;
`AC-FR5-13` généricité → Story 1.8.)*

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-6.

---

### Story 1.7 : Contrat `ConversionResult`, pureté & thread‑safety

As a consommateur de `TextToXml`,
I want un contrat `ConversionResult` stable et des garanties de pureté,
déterminisme et thread‑safety,
So that je peux logger, sérialiser et paralléliser les conversions en confiance.

**Acceptance Criteria:**

**Given** une conversion réussie
**When** j'inspecte le `ConversionResult`
**Then** `Success == true` ⇒ `Errors.Count == 0` **et** `Xml != null` **et** `Xml`
  bien formé (des `Warnings` possibles) (AC-FR6-1) ; un `SegmentMismatch` **seul**
  → `Success == true`, `Xml != null`, `Warnings.Count == 1` (AC-FR6-3)

**Given** une conversion en échec
**When** j'inspecte le résultat
**Then** `Success == false` ⇒ `Errors.Count >= 1` **et** `Xml == null` (AC-FR6-2)

**Given** un résultat porteur d'`Errors` et/ou `Warnings`
**When** je les parcours
**Then** ils sont triés par `LineNumber` croissant (`0` en tête) (AC-FR6-4) ;
  chaque `Message` est non nul, en **français**, sans stack trace ni nom de type
  .NET (AC-FR6-5) ; `ConversionError` est sérialisable en JSON par
  `System.Text.Json` sans configuration (AC-FR6-7)

**Given** 20 entrées corrompues générées (fuzz : octets aléatoires, tailles 0..2000)
**When** j'appelle `Convert` sur chacune
**Then** **aucune** exception n'est levée (AC-FR6-6)

**Given** 100 appels `Convert` concurrents sur des entrées variées
**When** je compare aux résultats séquentiels
**Then** ils sont **identiques** — `TextToXml` est sans état et thread‑safe (NFR-3)

**Given** un Fichier ~700 octets / 3 Lignes
**When** je mesure `Convert` seul
**Then** le temps reste très en deçà du budget de bout en bout (contribue NFR-1)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR6-1` … `AC-FR6-7`, +
test de concurrence (NFR-3), + test de non‑régression perf indicatif (NFR-1).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-6.

---

### Story 1.8 : Généricité, isolation de format & types étendus (`decimal` / `datetime` / `convert`)

As a intégrateur d'un futur format,
I want la preuve que `TextToXml` ne contient **aucun** littéral propre à P60,
fonctionne sur un Descripteur synthétique différent, et gère `decimal` /
`datetime` / `convert`,
So that ajouter un format = 0 ligne de code dans `TextToXml`.

**Acceptance Criteria:**

**Given** un **Descripteur synthétique** `fixtures/generic/message-only.xml` — `Id`,
  positions, tailles, marqueurs, présence header/footer **différents** de P60
**When** j'appelle `Convert` avec ses fichiers d'entrée
**Then** le XML produit est cohérent, noms d'éléments = `Id` de **ce**
  descripteur, **aucune** balise P60 en dur, **sans modification** de `TextToXml`
  (AC-FR1-9, AC-FR5-13)

**Given** un Descripteur `fixtures/generic/typed-values.xml` avec des Champs
  `datatype="decimal"` (`decimalSeparator`) et `datatype="datetime"` (`convert`)
**When** j'appelle `Convert` avec des valeurs valides puis invalides
**Then** valeurs valides → valeur canonique dans le XML (`decimal` normalisé,
  `datetime` en ISO‑8601) (**CTR-1**, **CTR-2**)
**And** valeur `decimal` non conforme → `Error {Code:InvalidDecimal, FieldId, RawValue}`
  (Étape 1) ; valeur `datetime`/`convert` non conforme → `Error {Code:InvalidDate,
  FieldId, RawValue}` (Étape 1) ; **0 XML** produit (**CTR-1**, **CTR-2**)

**Given** `fixtures/generic/roundtrip.xml` (types mixtes)
**When** je désérialise le XML normalisé en un DTO `record`
**Then** `int`/`decimal`/`DateTime`/`string` conservés sans convertisseur custom (**CTR-3**)

**Given** la suite de tests de `TextToXml`
**When** je l'exécute
**Then** elle n'importe **aucun** projet `*Importer` (AC-FR16-3) et inclut le
  descripteur synthétique non‑P60

**Given** le code source de `TextToXml`
**When** je le passe en revue + test d'architecture
**Then** il ne contient **aucune** constante littérale propre à P60 (`"EOF"`,
  `"Segment"`, position `9`, longueurs de Champs…) ; seul `Windows-1252` est figé
  (AC-FR16-4) ; les seuls `ProjectReference` de la **lib** `Kape22Importer` vers du
  code partagé sont `TextToXml` + `PortalSharedLibrary` (AC-FR16-1) ; les points de
  variation d'un format sont **exactement** `<format>.xml`, `<format>.xsd`, DTO,
  entité + `DbContext`, table de mapping, `appsettings`/`<worker>.json`, **classe
  `Client`** (AC-FR16-2 ; classe `Client` ajoutée à la correction de cap
  2026‑09‑09 — voir AR-13)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR1-9`, `AC-FR5-13`,
`AC-FR16-1`, `AC-FR16-2`, `AC-FR16-3`, `AC-FR16-4`, `CTR-1`, `CTR-2`, `CTR-3`
(tests d'architecture + fixtures `generic/`).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-6.

---

## Épic 2 : `Kape22Importer` — contrat de format, mapping & persistance

Étape 2 pour P60 : du XML normalisé à la ligne insérée dans `AscoLSI`, **tout ou
rien**, avec contrôle de compatibilité au démarrage et garde‑fou anti‑doublon.

### Story 2.1 : Entités EF (database‑first) & harnais de test SQL Server local

As a `Kape22Importer`,
I want un `AscoLsiDbContext` figeant les tables cibles d'après Annexe C (sans
migration) **et** un harnais de tests d'intégration contre une instance SQL
Server locale (schéma minimal versionné, généré depuis la base réelle),
So that le descripteur, le mapping et la persistance disposent d'entités fidèles
au schéma réel et d'une base de test jetable dès la première story de l'épic.

**Acceptance Criteria:**

**Given** Annexe C.1
**When** je définis l'entité `L_D_KAPE22`
**Then** elle a 92 colonnes + PK `Id` `int` identity ; les colonnes NOT NULL
  (hors `Id`) sont exactement `NumeroFichier`, `OF`, `Indice`, `Type`, `Coulee`,
  `Nuance`, `Client`, `DateReception` ; toutes les autres nullables ; types CLR
  cohérents avec Annexe C (`int?`, `string`, `DateTime?`)
**And** les colonnes `datetime` `DateEnfournementFour1/2` sont mappées `DateTime?`
  et restent `NULL` (D14)
**And** la liste exacte des ~30 colonnes `int` est dérivée de `sys.columns`
  (Annexe C) et exposée pour la Story 2.2 (dérivation des `datatype`)

**Given** Annexe C.2
**When** je définis l'entité `L_D_LOG_COMMANDE`
**Then** colonnes `Id` (identity), `Commande`, `Message`, `OF`, `User`, `Date`,
  `NumLingot`, `Trace` avec nullabilité de l'Annexe C.2

**Given** `AscoLsiDbContext`
**When** je build les tests
**Then** aucune migration EF n'est générée ; `Id` est `ValueGeneratedOnAdd` ;
  chaînes de connexion `AscoLSI` et `MQTTnetServices` lues de `IConfiguration`
  (AR-8, NFR-5, CC-7)

**Given** le harnais de test d'intégration (AR-12)
**When** la fixture xUnit **niveau assembly** démarre
**Then** elle se connecte à l'instance SQL Server de test (chaîne d'
  `appsettings.Test.json` / variable d'environnement) et applique les scripts
  **idempotents** de `scripts/schema/` créant **uniquement** `L_D_KAPE22`,
  `L_D_LOG_COMMANDE`, `MQTTnetServices.dbo.Logs`, `MQTTnetServices.dbo.WorkerSettings`
  — et **rien d'autre** de la base réelle
**And** les scripts de `scripts/schema/` sont **générés depuis la base réelle
  `AFV004-LSI`** (`sqlcmd` / `sys.columns`), avec en en‑tête **serveur, base et
  date d'extraction** ; ils ne sont **jamais** rédigés de mémoire (R-3)
**And** l'isolation par défaut se fait par **`TransactionScope` + rollback**
  (async ⇒ `TransactionScopeAsyncFlowOption.Enabled`) ; les tests qui dépendent
  d'un état commité entre deux actions, ou qui testent les frontières de
  transaction, utilisent **commit réel + reset de données** (`Respawn` / `TRUNCATE`)
**And** la chaîne de connexion est fournie au `DbContext` par la fixture (jamais
  en dur ; base de **test** dédiée, jamais la production)
**And** ces tests portent `[Trait("Category","Integration")]` ; ils sont
  **ignorés avec un message clair** (pas en échec) si aucune instance SQL Server
  n'est joignable (tentative de connexion brève dans la fixture)

**Tests xUnit (TDD — écrits en premier, CC-1) :** test « ensemble des colonnes
NOT NULL == Annexe C.1 » ; test de forme d'entité (types CLR) ; test « pas de
migration / `Id` identity » ; test « connexions lues de `IConfiguration` » ;
**test « modèle `AscoLsiDbContext` ⟺ colonnes/types de `scripts/schema/` »** (R-3) ;
**test d'intégration fumée** : la fixture se connecte, `scripts/schema/` s'applique
sans erreur, un `INSERT`/`SELECT` round‑trip sur `L_D_KAPE22` réussit — sous
`TransactionScope` (rollback), la base reste vide après le test.

**Critères transverses :** CC-1, CC-2, CC-3, **CC-4 (propriétés d'entité par
ordre alphabétique)**, CC-5, CC-7. **AR-12** : harnais de test SQL Server local.

---

### Story 2.2 : Descripteur `P60.xml` enrichi & embarqué en ressource

As a mainteneur du format P60,
I want le Descripteur `P60.xml` complété d'un `datatype` par `<value>` (dérivé du
type de colonne `L_D_KAPE22`) et de `expectedMessageCount="1"`, embarqué comme
ressource dans `Kape22Importer`,
So that `TextToXml` type correctement les Champs sans connaître P60.

**Acceptance Criteria:**

**Given** le `P60.xml` actuel de `Templates/` et la liste des colonnes `int`
  exposée par la Story 2.1
**When** j'applique l'unique évolution v1
**Then** chaque `<value>` porte un `datatype` : `"int"` pour les ~30 Champs dont
  la colonne cible `L_D_KAPE22` est `int` (`Indice`, `DiametreProduit`, toutes
  les `Tolerance*`, `Epaisseur*`, `H2Coulee`, `NumeroFour1/2`, `SectionLaminage`,
  `ChutageTete/Pied`, `LongueurMoyenne`, `MatriculeClient`, `NombreLingotsFour1/2`,
  `PriseDeFer`…), `"string"` sinon (D6, Annexe A, Annexe C.1)
**And** aucun Champ `datetime` ni `decimal` (D6) ; aucun `convert`
**And** la racine porte `expectedMessageCount="1"` (D3)
**And** les Champs des positions 526‑636 ne sont **pas** déclarés (ignorés, D5) ;
  `P60.xml` décrit 526 caractères

**Given** `Kape22Importer`
**When** je build
**Then** `P60.xml` est embarqué comme ressource (`EmbeddedResource`) et lisible à
  l'exécution sans accès disque (AR-5)

**Given** `P60.xml` enrichi
**When** `TextToXml.Convert` le charge
**Then** il passe la validation FR-1 (aucun `LayoutInvalid`) et le contrôle
  `Segment` est actif (`segmentField`=`Segment`, marqueurs `000`/`EOF`/`999`)

**Tests xUnit (TDD — écrits en premier, CC-1) :** test de chargement `P60.xml` via
`Converter.Convert` sans erreur ; test « chaque `<value Id>` a un `datatype`
∈ `{string,int}` » ; test « `datatype` == `int` ⟺ colonne `L_D_KAPE22` homonyme
est `int` » (croisé avec `AscoLsiDbContext` de la Story 2.1).

**Critères transverses :** CC-1, CC-2 (commentaires XML du descripteur en
anglais), CC-3, CC-5, CC-7.

---

### Story 2.3 : `P60.xsd` statique + génération du DTO `Kape22File` + validation avant désérialisation

As a `Kape22Importer`,
I want un `P60.xsd` écrit à la main décrivant le XML normalisé, un DTO
`Kape22File` généré depuis ce XSD, et la validation du XML normalisé contre le
XSD **avant** désérialisation,
So that toute dérive entre descripteur, XML et DTO est attrapée tôt.

**Acceptance Criteria:**

**Given** `P60.xsd` versionné dans le dépôt
**When** je le compare au Descripteur `P60.xml`
**Then** chaque `<value Id>` non ignoré a un `<xs:element name="Id">` du bon type
  (`xs:int` / `xs:string`), présence header/footer alignée (AC-FR1-13)
**And** les éléments des Champs **typés** (`xs:int`, plus tard `xs:decimal` /
  `xs:dateTime`) sont `minOccurs="0"` — un Champ typé vide est omis du XML
  normalisé (PRD §0bis **D27**, livré côté `TextToXml` par l'action rétro
  `epic-1-retro-item-7`) ; le DTO `Kape22File` reçoit `int?` / `decimal?` /
  `DateTime?`.

**Given** `P60.xsd`
**When** je le structure
**Then** chaque section utilise `<xs:sequence>` dans l'**ordre exact des `<value>`
  de `P60.xml`** — le XML normalisé (Story 1.6) émet les enfants dans ce même
  ordre, donc la validation `AC-FR5-14` / `AC-FR7-1` passe (R-4)

**Given** `P60.xsd`
**When** je génère le DTO (`xsd.exe /classes` ou équivalent)
**Then** `Kape22File { Header, Message, Footer }` est produit, propriétés typées
  d'après le XSD ; le fichier généré est committé (AR-4)
**And** `CC-4` (tri alphabétique) **ne s'applique pas** au fichier généré —
  l'ordre du générateur fait foi ; toute propriété ajoutée à la main l'est dans
  une **classe partielle**, elle‑même triée alphabétiquement (R-5)

**Given** le XML normalisé d'un Fichier valide
**When** `Kape22Importer` le traite
**Then** il est **validé contre `P60.xsd`** (`XmlReader` + schéma) — un XML non
  conforme → `{Block:File, Code:SchemaInvalid}` citant l'erreur de schéma
  (filet — ne doit pas arriver si Étape 1 a réussi) (AC-FR7-1, AC-FR5-14)
**And** un XML conforme se **désérialise** en `Kape22File` (`XmlSerializer`) sans
  perte : round‑trip `int?`/`string` conservé, un Champ typé omis → propriété
  `null` (**AC-FR5-12b**, D27)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR1-13`, `AC-FR5-14`,
`AC-FR7-1`, `AC-FR5-12b`, + test « ordre `<xs:sequence>` de `P60.xsd` == ordre
des `<value>` de `P60.xml` » (R-4).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4 (**sauf fichier `Kape22File`
généré**, R-5), CC-5, CC-7.

---

### Story 2.4 : Désérialisation & mapping DTO `Kape22File` → entité `L_D_KAPE22`

As a `Kape22Importer`,
I want `Kape22Mapper.Map(normalizedXml, sourceFileName)` qui désérialise le XML
normalisé et mappe le DTO vers l'entité (par nom, avec les exceptions d'Annexe B),
en collectant **toutes** les erreurs sans rien écrire,
So that le mapping est complet, vérifiable et « tout ou rien ».

**Acceptance Criteria:**

**Given** un XML normalisé valide
**When** `Map` s'exécute
**Then** `Kape22File` est désérialisé puis l'entité `L_D_KAPE22` reçoit, pour
  chaque propriété homonyme (insensible à la casse), la valeur typée du DTO (AC-FR7-2)

**Given** la table de mapping
**When** je build les **tests**
**Then** toute propriété du DTO **non mappée et absente de la liste « ignorés »**
  (Annexe B) fait **échouer le build des tests** (complétude) (AC-FR7-3) ;
  test paramétré sur chaque entrée d'Annexe B : la propriété source existe dans
  `Kape22File`, la cible existe dans `L_D_KAPE22` (AC-FR7-6)

**Given** les exceptions d'Annexe B
**When** `Map` s'exécute
**Then** `OFOriginInterne` (DTO) → `OForiginInterne` (entité) (AC-FR7-4) ;
  `Filler`, `Reserve*`, `libre`, `Element`, `KAP`, `Segment`, `Date` (Détail) sont
  présents dans le DTO, **non copiés**, **aucune** erreur (AC-FR7-5)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR7-2` … `AC-FR7-6`.
*(`AC-FR7-1` → Story 2.3.)*

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 2.5 : Contrôle de compatibilité descripteur ↔ table (au démarrage)

As a exploitant,
I want que le worker **refuse de démarrer** si le Descripteur décrit quelque
chose que `L_D_KAPE22` ne peut pas accepter,
So that une incompatibilité de configuration est un défaut de déploiement, jamais
un rejet par fichier.

**Acceptance Criteria:**

**Given** le `DbContext` + le Descripteur embarqués
**When** le worker démarre
**Then** pour chaque Champ mappé, `datatype` compatible avec le type CLR de la
  propriété (`int`→`int/int?`, `decimal`→`decimal/decimal?`,
  `datetime`→`DateTime/DateTime?`, `string`→`string`) — sinon **exception de
  démarrage** listant les couples fautifs (AC-FR8-1)
**And** pour chaque Champ `string` mappé, `Size` ≤ `max_length` de la colonne (en
  caractères) — sinon exception de démarrage `{Champ, Size, colonne, max_length}` (AC-FR8-2)
**And** `max_length` provient de **constantes issues d'Annexe C** portées par la
  configuration d'entité EF (`HasMaxLength`), **pas** d'une requête `sys.columns`
  au runtime — aucune dépendance au schéma live au démarrage (R-6) ; un test
  vérifie que ces constantes == `scripts/schema/`
**And** toute colonne **NOT NULL** (Annexe C) a une source (Champ mappé ou règle
  dérivée FR‑9) — sinon exception de démarrage (AC-FR8-3)
**And** descripteur & table compatibles (cas nominal KAPE22) → le worker démarre (AC-FR8-4)

**Given** un Fichier en cours de traitement (worker démarré)
**When** une valeur `null`/vide sort de l'Étape 1 pour une colonne **NOT NULL**
**Then** `{Block, Line, FieldId, Column, Code:RequiredFieldMissing}` — contrôle
  **par fichier** (AC-FR8-5) ; plusieurs colonnes NOT NULL vides → une
  `RequiredFieldMissing` par colonne, `Errors` trié par ordre des Champs du
  descripteur (AC-FR8-6)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR8-1` … `AC-FR8-6`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 2.6 : Champs dérivés & combinés

As a `Kape22Importer`,
I want appliquer les règles dérivées propres à P60 (dans le microservice, pas
dans `TextToXml`) : `Date` = jour‑de‑l'année, `DateReception`, `Indice`,
`NumeroFichier` roulette, Champs `DateEnfournement*` ignorés,
So that l'entité est complète et conforme aux décisions D4/D5/D14.

**Acceptance Criteria:**

**Given** le Champ `Date` de l'Entête
**When** `Map` le convertit
**Then** interprété comme **numéro du jour dans l'année courante, heure de Paris**
  via `TimeProvider` injectable : `"245"` en 2026 → `2026-09-02` ; `"000"` ou
  hors `1..366` → `InvalidDate` sur `Date` (AC-FR9-1, D4)

**Given** l'entité en construction
**When** `Map` renseigne les dérivés
**Then** `NumeroFichier` (entité) = valeur du Bloc **Entête** (roulette), = 4ᵉ
  segment du nom (AC-FR9-2) ; `DateReception` (NOT NULL, hors descripteur) =
  horodatage de traitement **heure de Paris** via `TimeProvider` (AC-FR9-3) ;
  `Indice` (int NOT NULL) = Champ Détail `Indice`, vide → `RequiredFieldMissing` (AC-FR9-4) ;
  `DateEnfournementFour1/2_Date`/`_Heure` **ignorés**, colonnes
  `DateEnfournementFour1/2` laissées `NULL` (AC-FR9-5, D14)

**Given** l'architecture
**When** test d'architecture
**Then** `TextToXml` n'a **aucune** notion de `DateReception` / jour‑de‑l'année ;
  les règles dérivées vivent **dans le microservice** (AC-FR9-6)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR9-1` … `AC-FR9-6` (dont
test d'architecture pour `AC-FR9-6`).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 2.7 : Contrôles de cohérence (Warnings, non bloquants)

As a exploitant,
I want que `Kape22Mapper.Map` reçoive le nom du Fichier et signale les écarts de
cohérence (`Footer.Records`, inter‑blocs `File`, nom ↔ Entête) comme
**`Warnings`** sans bloquer l'import,
So that j'ai des signaux « pour contrôle » sans perdre un fichier dont les
données sont bonnes.

**Acceptance Criteria:**

**Given** un `Footer.Records`
**When** `Map` s'exécute
**Then** `≠ 3` → `Warning {Block:Footer, FieldId:"Records", Code:InterBlockMismatch}` (AC-FR10-1, D18) ;
  `== 3` → aucun `Warning` (AC-FR10-2)

**Given** le Champ `File` (Position 0, Size 3) des trois Blocs
**When** ils diffèrent
**Then** `Warning {Code:InterBlockMismatch, FieldId:"File"}` (AC-FR10-3)

**Given** le nom `P60_847_682_001`
**When** `Map` le décompose en `File`/`Emet`/`Recepteur`/`NumeroFichier`
**Then** un segment ≠ Champ homonyme de l'Entête (zéros de tête ignorés pour
  `NumeroFichier`) → `Warning {Block:File, FieldId:"<champ>", Code:FileNameMismatch,
  RawValue:"<segment du nom>"}` (AC-FR10-4) ; nom hors motif `A_B_C_D` (3 `_`) →
  `Warning {Block:File, Code:FileNameMismatch}` citant le nom, extension `.txt`
  ignorée (AC-FR10-5) ; nom et Entête concordants → aucun `Warning` (AC-FR10-6)

**Given** un Fichier avec **uniquement** des `Warnings` de cohérence + données valides
**When** la chaîne complète s'exécute
**Then** il est **inséré** dans `L_D_KAPE22`, `L_D_LOG_COMMANDE` statut `OK`, les
  `Warnings` figurent dans `MQTTnetServices.Logs` (AC-FR10-7)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR10-1` … `AC-FR10-7`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 2.8 : Persistance transactionnelle & garde‑fou anti‑doublon

As a exploitant,
I want que l'insertion de `L_D_KAPE22` et de la ligne `L_D_LOG_COMMANDE` (`OK`)
soit **atomique**, précédée d'un garde‑fou anti‑doublon sur crash post‑commit,
So that un retraitement ne crée jamais de doublon et un échec ne laisse jamais de
demi‑écriture.

**Acceptance Criteria:**

*(Tests d'intégration EF — via le **harnais de la Story 2.1** (instance SQL
Server de test, tables minimales `L_D_KAPE22` + `L_D_LOG_COMMANDE`),
`[Trait("Category","Integration")]`. `AC-FR11-3`/`AC-FR11-5` (frontières de
transaction) et `AC-FR11-6`/`AC-FR11-7` (garde‑fou anti‑doublon) tournent en
**commit réel + reset de données**, pas sous `TransactionScope` ambiant. AR-12.)*

**Given** `MapResult.Success == true`
**When** la persistance s'exécute
**Then** **1** ligne `L_D_KAPE22`, `ImportResult.InsertedId` = `Id` identity
  généré (AC-FR11-1) ; insert `L_D_KAPE22` + insert `L_D_LOG_COMMANDE` (` — OK`)
  dans **une même transaction**, échec de l'un ⇒ rollback des deux (AC-FR11-3)

**Given** `MapResult.Success == false` avec `OF` lisible
**When** la persistance s'exécute
**Then** **0** ligne `L_D_KAPE22`, `InsertedId == null` (AC-FR11-2) ; **1** ligne
  `L_D_LOG_COMMANDE` (` — REJETÉ : <résumé>`) en transaction dédiée (AC-FR11-4)

**Given** un échec SQL
**When** la persistance s'exécute
**Then** `{Block:File, Code:PersistenceError}`, **pas** d'exception qui remonte,
  rollback vérifié (AC-FR11-5)

**Given** le garde‑fou (D22)
**When** un `L_D_LOG_COMMANDE … — OK` existe déjà pour ce `NumeroFichier` + `OF`
**Then** **0 insert**, Fichier déplacé en `archive/`, log `Warning` « déjà
  importé, ignoré » (AC-FR11-6) ; sinon (jamais importé avec succès) → import
  normal (AC-FR11-7)

**Given** la configuration
**When** le worker lit ses chaînes de connexion
**Then** `AscoLSI` et `MQTTnetServices` viennent de la configuration, **jamais en
  dur**, compte `sa` existant (AC-FR11-8, D21, CC-7)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR11-1` … `AC-FR11-8`
(catégorie `Integration`, harnais SQL Server local Story 2.1).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7. **AR-12** : tests
d'intégration sur SQL Server local.

---

## Épic 3 : `Kape22Importer` — worker, orchestration & exploitation

Le worker qui orchestre dossier → `TextToXml` → archive → `Kape22Mapper` → EF →
déplacement → double log, supervisé par le Launcher.

**Correction de cap (2026‑09‑09).** L'intégration Launcher est réalisée en
s'alignant sur le modèle réel du portail (`MicroServices.sln`) : `Kape22Importer`
devient une **bibliothèque** et le worker est une classe **`Client : Publisher`**
enregistrée dans le `Launcher` (pilotée par le timer de `Publisher`, pas
`BackgroundService`). Les Stories 3.1 / 3.2 (livrées) sont réutilisées telles
quelles. Une **Story 3.0** porte le repositionnement structurel ; 3.3 (livrée) est
**revisitée** pour le seam de journalisation ; **3.4 / 3.5 / 3.6 sont réécrites**
contre le modèle `WorkerAdapter`/`Client`. Voir
`sprint-change-proposal-2026-09-09.md`.

### AC reportés d'Épic 2 — réconciliation (retro Épic 2 action A-3 / item-10, 2026-09-08)

Quatre `AC` nommés ont franchi la frontière Épic 2 → Épic 3 via des notes
`deferred-work.md` (F-3 de la retro). Requalification formelle, chacun rattaché à
sa story porteuse :

| `AC` reporté | Périmètre exact pour l'Épic 3 | Story porteuse | État |
|---|---|---|---|
| **AC-FR6-4** (tri par `LineNumber`) | Fait et testé au niveau `ConversionResult` (Story 1.7). **Étendu à `ImportResult`** : `Errors` **et** `Warnings` chacun trié par `LineNumber` croissant (`0` en tête), **listes indépendantes** — il n'y a **pas** de liste fusionnée `Errors`+`Warnings` (le mot « fusionnée » des notes de dette 2.7/2.8 est abandonné). Aujourd'hui `Kape22FichierProcessor.Import` concatène `conversion.Warnings` + `map.Warnings` sans re‑trier. | **Story 3.3** | à faire |
| **Volet journalisation de AC-FR10-7 / AC-FR11** (`L_D_LOG_COMMANDE` « — OK », `MQTTnetServices.Logs`, `Warnings` de cohérence dans `Logs`) | Le volet `Kape22Mapper` / persistance est vert (stories 2.7 / 2.8). Le volet « ça atterrit dans les logs » **est** `AC-FR14-1` / `AC-FR14-8`. | **Story 3.3** (déjà couvert par `AC-FR14-1`, `AC-FR14-8`) | attaché |
| **AC-FR12-3** (`ImportResult.XmlArchivePath`) | Champ posé et rempli par `Kape22FichierProcessor.Import` ; le XML physique est écrit par `InboxScanner.Archive`. | **Story 3.2** | **fait** — résiduel : `InboxScanner` ne consomme pas encore le champ (décision seam `IFichierProcessor` → Story 3.3) |
| **Signal explicite de skip‑doublon** (D22 / `AC-FR11-6`) | Aujourd'hui structurel (`Success == true && InsertedId == null && Errors.Count == 0`). La journalisation « déjà importé, ignoré » en a besoin. | **Story 3.3** : ajouter un discriminant explicite sur `ImportResult` (drapeau `AlreadyImported` ou enum `Outcome`) plutôt que la détection structurelle. | à faire |

**AddDbContext vs AddDbContextFactory + durée de vie du persister (F-11) :**
**requalifié (correction de cap 2026‑09‑09).** Il n'y a plus de conteneur DI ni
d'hôte pour le worker. Le `Client` construit ses contextes **à la main par tick**
(`new AscoLsiDbContext(new DbContextOptionsBuilder<…>().UseSqlServer(cs).Options)`),
comme `OrdresFabricationSync.Client`. `Kape22FichierProcessor` garde son seam
`Func<AscoLsiDbContext> newContext` (Story 3.2) ; `Kape22Persister` est construit
par Fichier dans l'orchestrateur. Le point « fabrique singleton »
(`AddDbContextFactory` / `AddKape22Startup`) est **abandonné**.

### Story 3.0 : Repositionnement structurel (lib + `Client` Launcher)

*(Ajoutée à la correction de cap 2026‑09‑09.)*

As a mainteneur du portail,
I want `Kape22Importer` transformé en bibliothèque et un `Client : Publisher`
mince enregistré dans le `Launcher`,
So that le worker P60 est supervisé exactement comme les autres workers du
portail, sans infra dupliquée.

**Prérequis :** `MicroServices.sln` aligné sur `net10.0` + EF Core 10.0.x
(suivi AR-13).

**Acceptance Criteria:**

**Given** le projet `src/Kape22Importer`
**When** je le convertis
**Then** son SDK passe de `Microsoft.NET.Sdk.Worker` à `Microsoft.NET.Sdk`
  (bibliothèque) ; `Program.cs` est **supprimé** ; aucune référence
  `Microsoft.AspNetCore.App` ni `Microsoft.Extensions.Hosting` ; il expose
  `InboxScanner`, `IFileSource`/`DirectoryFileSource`, `IFichierProcessor`,
  `Kape22FichierProcessor`, `Kape22Mapper`, `Kape22Persister`, `AscoLsiDbContext`,
  `ImportOptions` en API publique (ou `InternalsVisibleTo` le `Client`)
**And** `AddKape22Startup` / `StartupCompatibilityHostedService` /
  `*ServiceCollectionExtensions` (host) sont **supprimés** ; la vérification FR-8
  est exposée en méthode statique appelable (`StartupCompatibilityCheck.Verify(...)`)
**And** la lib ne référence que `TextToXml` + `PortalSharedLibrary` (`AC-FR16-1`
  reste vert)

**Given** `MicroServices.sln`
**When** j'ajoute le worker P60
**Then** un projet `GPAO/ImportP60` (`GpaoImportP60`) contient
  `Client : Publisher, IPublisher, IService` : ctor `Client(IConfiguration)` (lit
  `Import:*`), `CreateAsync()` (`ConnectWithRetryAsync` puis `Start()`),
  `Frequency` = `Import:PollingInterval`, `Execute` = un tick (FR-8 au premier tick
  / dans `CreateAsync`, puis `InboxScanner.RunTick(ct)` + `PurgeRetention()`)
**And** il référence la lib `Kape22Importer` (`ProjectReference` cross‑dépôt) +
  `MicroService.csproj`
**And** `Launcher/WorkerRegistry.cs` gagne **une** entrée
  `Factories["GpaoImportP60"]` (enveloppe `WorkerAdapter<GpaoImportP60.Client>`) et
  `Launcher/workers.json` **une** entrée
  `{ "Name": "GpaoImportP60", "Type": "GpaoImportP60", "ConfigPath": "GpaoImportP60.json" }`
**And** `GpaoImportP60.json` porte `Import:*` (chemins, `PollingInterval`,
  `InitiatingServer`, `RetentionDays`) + les chaînes `AscoLSI` / `MQTTnetServices`
  — **jamais en dur** (CC-7)

**Given** le harnais de tests
**When** je build `TextToXml.sln`
**Then** `Kape22Importer.Tests` compile contre la lib (plus de `Program`/host) ;
  les tests supprimés en 3.4 (`WorkerStatusProviderTests`, `WorkerShutdownTests`,
  `WorkerCompositionTests`, `WorkerControlTests`, `WorkerSettingsRegistrationTests`,
  `MqttSchemaModelParityTests`) ne sont **pas** recréés

**Tests xUnit (TDD, CC-1) :** test « `Kape22Importer` est une lib sans dépendance
host/ASP.NET » ; test « `AC-FR16-1` ProjectReferences lib == {TextToXml,
PortalSharedLibrary} » (existant, reste vert) ; côté `MicroServices.sln` :
`ClientCompositionTests` (le `Client` construit son graphe depuis une
`IConfiguration` en mémoire, sans broker).

**Critères transverses :** CC-2, CC-3, CC-4, CC-5, CC-7. *(CC-1 partiel : story
surtout structurelle ; la logique `Client.Actions` est test‑first.)*

---

### Story 3.1 : Scrutation du dossier de réception & cycle de vie du Fichier

As a exploitant,
I want un worker qui scrute `Import:InboxPath` via `IFileSource`, déplace chaque
Fichier dans `processing/` avant traitement, range succès/rejets dans
`archive/`/`error/` et purge selon `Import:RetentionDays`,
So that le traitement est sûr en reprise et l'espace disque maîtrisé.

**Acceptance Criteria:**

**Given** une inbox avec plusieurs Fichiers
**When** un tick s'exécute
**Then** tous sont traités du plus **ancien au plus récent** (ordre déterministe)
  (AC-FR12-1) ; chaque Fichier est **d'abord déplacé dans `processing/`** et
  c'est cette copie qui est lue (AC-FR12-2) ; inbox vide → tick sans effet ni
  erreur (AC-FR12-7)

**Given** un traitement
**When** il réussit
**Then** Fichier → `archive/<yyyy>/<MM>/<nom>` **et** XML normalisé écrit à côté
  `<nom>.xml` (AC-FR12-3, D11)
**When** il est rejeté
**Then** Fichier → `error/<nom>` + `<nom>.errors.json` (tableau `Errors`) à côté (AC-FR12-4)

**Given** un Fichier encore en cours d'écriture (taille instable entre deux lectures)
**When** le tick le rencontre
**Then** il est laissé dans l'inbox, retenté au tick suivant, **aucune** erreur loggée (AC-FR12-5)

**Given** un worker tué pendant le traitement
**When** il redémarre
**Then** un Fichier resté dans `processing/` est **repris** ; le garde‑fou
  anti‑doublon (Story 2.8) empêche une 2ᵉ insertion si le commit avait eu lieu (AC-FR12-6)

**Given** la configuration
**When** le worker s'initialise
**Then** tous les chemins + l'intervalle de polling + `Import:InitiatingServer` +
  `Import:RetentionDays` viennent de la configuration, **aucune** valeur en dur (AC-FR12-8, CC-7)

**Given** `archive/` et `error/`
**When** la purge s'exécute (à chaque tick ou 1×/jour)
**Then** les fichiers dont l'âge > `Import:RetentionDays` sont **supprimés** ;
  `RetentionDays ≤ 0` → purge désactivée (AC-FR12-9, D13)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR12-1` … `AC-FR12-9`
(via `IFileSource` mémoire).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 3.2 : Orchestration par Fichier

As a `Kape22Importer`,
I want un ordre de traitement **strict** par Fichier avec isolation totale entre
Fichiers d'un même tick,
So that une erreur sur un Fichier n'en impacte jamais un autre et l'`ImportResult`
reflète exactement l'étape atteinte.

**Acceptance Criteria:**

**Given** un Fichier
**When** l'orchestrateur le traite
**Then** ordre strict : lecture octets → `Converter.Convert` → (si succès)
  archive XML → `Kape22Mapper.Map` → (si succès) insertion → déplacement archive (AC-FR13-1)

**Given** un échec `Converter`
**When** l'orchestrateur réagit
**Then** pas d'archive XML, pas de mapping, Fichier en `error/`,
  `ImportResult.Errors` = erreurs Étape 1 (AC-FR13-2)

**Given** `Converter` réussit mais `Mapper` échoue
**When** l'orchestrateur réagit
**Then** le Fichier **et** son XML normalisé (`<nom>.xml`) vont dans `error/` avec
  `<nom>.errors.json` ; `Errors` = erreurs Étape 2 (AC-FR13-3)

**Given** plusieurs Fichiers dans un tick
**When** l'un échoue
**Then** les autres sont traités normalement — isolation des erreurs, de la
  transaction et du scope EF **par fichier** (AC-FR13-4)

**Given** un succès
**When** j'inspecte l'`ImportResult`
**Then** `Success=true`, `InsertedId` non nul, `Errors` vide (`Warnings`
  possibles), `XmlArchivePath` non nul (AC-FR13-5)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR13-1` … `AC-FR13-5`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 3.3 : Double journalisation (`MQTTnetServices.Logs` + `L_D_LOG_COMMANDE`)

As a exploitant,
I want que chaque Fichier produise une ligne `MQTTnetServices.Logs` (toujours) et,
si l'`OF` est lisible, une ligne `L_D_LOG_COMMANDE`,
So that tout traitement — succès comme rejet — laisse une trace lisible.

**Acceptance Criteria:**

**Given** un succès
**When** la journalisation s'exécute
**Then** `Logs` `Information` (nom fichier, `NumeroFichier`, `OF`, `InsertedId`,
  nb Lignes, durée ms) **et** `L_D_LOG_COMMANDE.Message` finissant par ` — OK` (AC-FR14-1)

**Given** un rejet avec `OF` lisible
**When** la journalisation s'exécute
**Then** `Logs` `Error` listant **toutes** les `Errors` **et**
  `L_D_LOG_COMMANDE.Message` = `"<NumeroFichier> — REJETÉ : "` + résumé (nb
  erreurs + libellés) (AC-FR14-2)

**Given** un rejet **structurel**, `OF` non lisible
**When** la journalisation s'exécute
**Then** **seul** `Logs` est écrit (`Error`) ; **aucune** ligne `L_D_LOG_COMMANDE` (AC-FR14-3, D15)

**Given** un Fichier importé **avec** des `Warnings` de cohérence
**When** la journalisation s'exécute
**Then** `Logs` `Warning` listant les `Warnings` (`SegmentMismatch`,
  `InterBlockMismatch`, `FileNameMismatch`) en plus de la ligne `Information` (AC-FR14-8)

**Given** une ligne `L_D_LOG_COMMANDE`
**When** elle est écrite
**Then** `User` = `Import:InitiatingServer` (config), `OF` = valeur brute trimée
  du bloc message, `Commande="P60"`, `NumLingot=0`, `Trace=1` (AC-FR14-4, D8, D25)

**Given** `Logs` indisponible
**When** un import réussit
**Then** l'insertion `L_D_KAPE22` n'est pas empêchée (log best‑effort) ;
  `L_D_LOG_COMMANDE` reste dans la transaction de succès (AC-FR14-7, NFR-6)

**Given** un `ImportResult` porteur d'`Errors` et/ou de `Warnings`
**When** la journalisation les parcourt
**Then** `Errors` et `Warnings` sont **chacun** triés par `LineNumber` croissant
  (`0` en tête), listes indépendantes — `AC-FR6-4` étendu à `ImportResult`
  (réconciliation A-3). `Kape22FichierProcessor.Import` retrie les `Warnings`
  concaténées.

**Given** un Fichier déjà importé (garde‑fou D22, `AC-FR11-6`)
**When** l'orchestrateur le retraite
**Then** `ImportResult` porte un discriminant explicite « déjà importé »
  (drapeau/enum), pas seulement la forme structurelle ; la journalisation loggue
  `Warning` « déjà importé, ignoré » (réconciliation A-3).

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR14-1`, `AC-FR14-2`,
`AC-FR14-3`, `AC-FR14-4`, `AC-FR14-7`, `AC-FR14-8`, plus `AC-FR6-4` (tri
`ImportResult`) et le skip‑doublon explicite (réconciliation A-3) — les cas
écrivant en base (`L_D_LOG_COMMANDE`, `Logs`) sont en catégorie `Integration` sur
le harnais SQL Server local de la Story 2.1 (AR-12).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7. **AR-12** : tests
d'intégration sur SQL Server local.

**Revisite (correction de cap 2026‑09‑09).** Le fond livré (`L_D_LOG_COMMANDE`,
discriminant skip‑doublon, tri `AC-FR6-4` sur `ImportResult`) est **conservé**.
Change : `Kape22FichierProcessor` ne prend plus `ILogger<Kape22FichierProcessor>`
câblé par un `Program.cs` `AddSerilog(...WriteTo.MSSqlServer)`. Le `Client` route
le journal vers le `Logger` Serilog partagé d'`AbstractService`
(`SharedLogger.GetDefault()`), soit via un pont `Serilog.Extensions.Logging`
(`new SerilogLoggerFactory(Logger).CreateLogger<…>()`), soit via un petit seam
`IImportJournal` implémenté par le `Client` sur
`AbstractService.LogInformation/LogWarning/LogError`. Décision de forme dans la
Story 3.0 / 3.4. `DoubleJournalIntegrationTests` (round‑trip
`Serilog.Sinks.MSSqlServer`) **reste**.

---

### Story 3.4 : Boucle worker `Client` & arrêt propre

*(Réécrite à la correction de cap 2026‑09‑09 — l'ex‑3.4 « expose un `WorkerStatus`
+ endpoints + `AddDbContextFactory` » est abandonnée : le `WorkerStatusProvider` /
`WorkerControl` / « skip tick si pausé » disparaissent — une désactivation
Launcher **arrête** le `Client` via `WorkerAdapter.SetActiveAsync`.)*

As a exploitation LSI,
I want que la classe `Client` exécute le pipeline d'import à chaque tick et
s'arrête proprement en < 5 s sans laisser d'insertion à moitié faite,
So that le worker P60 est supervisable et recyclable comme les autres workers du
portail.

**Acceptance Criteria:**

**Given** `Client.CreateAsync()`
**When** le worker démarre
**Then** `ConnectWithRetryAsync` (broker) puis, **avant** `Start()`,
  `StartupCompatibilityCheck.Verify(descripteur embarqué, AscoLsiDbContext)`
  (FR-8) ; un échec de compatibilité → `LogError` + le worker **ne démarre pas sa
  boucle** (défaut de déploiement, `AC-FR8-1..4`) ; compatible → `Start()` arme le
  timer de `Publisher` (`Frequency` = `Import:PollingInterval`)

**Given** un tick (`Execute` = `Client.Actions`)
**When** il s'exécute
**Then** il construit par tick : `Func<AscoLsiDbContext>`
  (`new DbContextOptionsBuilder<…>().UseSqlServer(cs)`),
  `DirectoryFileSource(Import:InboxPath)`, `Kape22FichierProcessor`, `InboxScanner` ;
  appelle `scanner.RunTick(cancellationToken)` puis `scanner.PurgeRetention()` ;
  publie un message de statut MQTT (comme `OrdresFabricationSync`)
**And** une exception **par Fichier** est capturée dans `Actions` / `InboxScanner`
  (log `ERROR`, Fichier en `error/` ou laissé en `processing/` selon la panne —
  détail Story 3.5), la boucle **ne s'arrête pas** (contourne le
  `catch → Stop()` du timer de `Publisher`)

**Given** un `WorkerAdapter.StopAsync` du Launcher (→ `Client.Stop()` +
  `Client.Disconnect()`) pendant un tick
**When** le worker s'arrête
**Then** le `Client` détient un `CancellationTokenSource` annulé par `Stop()` ;
  `Actions` vérifie le jeton **entre Fichiers** (jamais mi‑Fichier) → le Fichier
  en cours finit ou reste dans `processing/`, jamais à moitié inséré ; le timer
  est disposé ; arrêt propre **< 5 s** (`AC-FR14-6`, NFR-9)

**Given** le Launcher `GET /workers`
**When** il interroge la flotte
**Then** `WorkerAdapter<Client>` remonte `IsRunning` (= `Client.IsConnected`),
  `IsActive`, `LastStartedAt`, `LastError` — aucun code de statut côté worker
  (`AC-FR14-5`, réalisé par la Story 3.0 + vérifié ici)

**Tests xUnit (TDD — écrits en premier, CC-1)** — dans `MicroServices.sln`
(`GpaoImportP60.Tests`, modèle `OrdresFabricationSync.Tests`) : `RunTickCore`
(seam statique) traite les Fichiers d'un `InMemoryFileSource` ; `ReadConfig`
(guard `ConnectionStrings:AscoLSI` vide + liaison de la section `Import`) ; FR-8
(`StartupCompatibilityCheck.Verify` sur le descripteur embarqué ⟺ modèle
`AscoLsiDbContext` — wiring cross-dépôt). L'arrêt coopératif entre Fichiers
(`InboxScanner.RunTick(CancellationToken)` + `Client.override Stop()` qui annule
un `CancellationTokenSource`) est livré (`AC-FR14-6`). Le pipeline lui‑même
est couvert par 3.1/3.2 (lib).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 3.5 : Robustesse de la boucle `Client`

As a exploitant,
I want que la boucle survive à une exception imprévue, à une source de fichiers
injoignable et à une base injoignable, sans perdre ni dupliquer de Fichier,
So that le service tourne sans surveillance permanente.

**Acceptance Criteria:**

**Given** une exception non prévue sur un Fichier
**When** la boucle la rencontre
**Then** elle est capturée, loggée `ERROR`, le Fichier va en `error/`, la boucle
  continue avec le Fichier suivant (AC-FR15-1)

**Given** le dossier de réception injoignable un tick
**When** la boucle s'exécute
**Then** log `Warning`, **aucun** Fichier perdu, retry au tick suivant (AC-FR15-2)

**Given** la base `AscoLSI` injoignable
**When** la boucle s'exécute
**Then** Fichiers laissés dans `processing/`, log `Warning`, retry ultérieur
  (**pas** de passage en `error/`) (AC-FR15-3)

**Given** un redémarrage du worker (recycle) au milieu d'un lot
**When** il reprend
**Then** reprise complète au tick suivant, **sans perte ni doublon** (garde‑fou
  Story 2.8) (AC-FR15-4, NFR-7)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR15-1` … `AC-FR15-4`.
`AC-FR15-3` (base injoignable) et `AC-FR15-4` (recycle) s'appuient sur le harnais
SQL Server local de la Story 2.1 — la panne est simulée par une chaîne de
connexion pointant un hôte/port mort (ou un toggle de la fixture), pas par
l'arrêt d'un conteneur (AR-12).

**Réancrage (correction de cap 2026‑09‑09).** La « boucle » est `Client.Actions`
armée par le timer de `Publisher`. Le callback du timer de `Publisher` appelle
`Stop()` sur exception non capturée → `Actions` **doit** capturer ses propres
exceptions par Fichier (modèle `OrdresFabricationSync.Client.Actions` : `try/catch`
autour de chaque unité de travail, `LogError`, on continue). `AC-FR15-1` :
`try/catch` dans `InboxScanner`/`Actions` ; l'exception imprévue devient un
résultat `{Block:File, Code:UnexpectedFailure}` → `error/`. `AC-FR15-2` :
`DirectoryFileSource` lève → capturé, `LogWarning`, retry. `AC-FR15-3` :
`Kape22Persister` traduit `DbException` → `PersistenceError` (seul émetteur de ce
code — une non‑conformité schéma est `SchemaInvalid`, une exception imprévue
`UnexpectedFailure`), Fichiers **laissés en `processing/`**, jamais `error/`. `AC-FR15-4` :
reprise via `processing/` + garde‑fou D22 ; le `Client` est reconstruit à chaque
`CreateAsync` du `WorkerAdapter`. Tests `AC-FR15-1/2` en `Category=Unit` sur
`InboxScanner` (lib) + un test `Actions` (`MicroServices.sln`).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 3.6 : Validation de bout en bout & harnais de couverture (SM-1/2/3)

As a PM et exploitation LSI,
I want une preuve mesurable que la chaîne fonctionne : 100 % des `AC-FRx-y`
couverts par un test vert, les 10 fichiers `P60/` insérés, un fichier corrompu
produisant un `*.errors.json` compréhensible,
So that la v1 est acceptable pour la mise en production.

**Acceptance Criteria:**

**Given** la convention de test « chaque test porte `[Trait("AC", "FRx-y")]` (ou
  `[Trait("AC", "CTR-x")]`) »
**When** le test agrégateur de couverture s'exécute
**Then** il **échoue** si un `AC-FRx-y` du PRD (FR‑1..FR‑16) ou un `CTR-x` n'a
  **aucun** test porteur du trait correspondant ; il produit un rapport de
  traçabilité `AC → test(s)` sans lacune (SM‑1)

**Given** les 10 fichiers `P60/P60_847_682_001..010`
**When** le test d'intégration de bout en bout s'exécute sur le **harnais SQL
  Server local** de la Story 2.1 (instance de test, tables minimales de
  `scripts/schema/`) — `[Trait("Category","Integration")]`, en **commit réel +
  reset de données** (AR-12)
**Then** `Kape22Importer` insère **10 lignes** `L_D_KAPE22` cohérentes avec les
  données visibles (`OF`, `Coulee`, `Client`, `Nuance`) + 10 lignes
  `L_D_LOG_COMMANDE` ` — OK` (SM‑2)

**Given** un fichier volontairement corrompu (Annexe A.4 : `non_numeric_diametre`
  + `empty_required`)
**When** il est traité
**Then** il produit un `*.errors.json` listant **toutes** les causes distinctes
  (bloc, ligne, champ, colonne, code, message, valeur fautive), revu comme
  « compréhensible sans aide » avec l'exploitation (SM‑3)

**Given** les contre‑métriques
**When** je revois la suite
**Then** aucune réduction du nombre d'`Errors` « pour faire propre » (SM‑C1) ;
  aucun état/cache ajouté à `TextToXml` (SM‑C2) ; aucune « réparation »
  silencieuse d'une valeur SAP douteuse (SM‑C3)

**Given** NFR-1 / NFR-2
**When** un test de performance s'exécute
**Then** un Fichier de bout en bout < 200 ms hors latence FTP/SQL ; un tick de
  500 Fichiers < 30 s

**Tests xUnit (TDD — écrits en premier, CC-1) :** test agrégateur de traçabilité
(SM‑1, catégorie `Unit`) ; E2E 10 fichiers (SM‑2, catégorie `Integration`,
harnais SQL Server local Story 2.1) ; test `*.errors.json` lisible (SM‑3) ;
tests de perf (NFR‑1/2 — la mesure < 200 ms exclut le temps de connexion /
d'application du schéma).

**Réancrage (correction de cap 2026‑09‑09).** L'agrégateur `AC → [Trait]`
(`AcTraitCoverageTests`, SM‑1) est **inchangé** (déjà en place, retro Épic 1).
**SM‑2 (E2E 10 fichiers)** est piloté au niveau **bibliothèque** : `InboxScanner`
(in‑memory `IFileSource` ou `DirectoryFileSource` sur dossier temp) +
`Kape22FichierProcessor` réel + SQL Server local (commit réel + reset). **NFR‑1/2**
mesurés sur le pipeline lib (hors broker/HTTP). Un test « fumée » niveau `Client`
(`MicroServices.sln`) — `CreateAsync` (broker fake/embarqué) → un tick → une ligne
insérée — remplace le `WebApplicationFactory<Program>` de l'ex‑3.4 (abandonné,
plus de `Program`).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7. **AR-12** : E2E
sur SQL Server local.

---

## Épic 4 : `Kape22Importer` — Dispatch transactionnel vers les tables aval

Compléter le pipeline P60 après l'insertion `L_D_KAPE22` (Épic 2, FR-11) par le
dispatch explicite — **sans réflexion** — vers `L_D_ORDRE_FABRICATION`,
`L_D_COULEE`, `L_D_CONSIGNES` et les 7 `L_D_SECTIONCHARGE_*` (Chutage, Lingot,
Découpe, Pits, PoidsMétrique, Refroidissoirs, SVT), en remplacement de la
procédure legacy à base de `MappingTemplate`/réflexion (`Ascometal.LSI.DAL`,
dépôt legacy — **jamais modifié, jamais appelé au runtime**, AD-3). Cadré par le
spine `_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md`
(AD-1 à AD-7). Toutes les tables aval commitent avec `L_D_KAPE22` dans **une
seule transaction** (AD-1) ; tout échec — métier ou SQL — journalise sa cause
précise via le circuit existant (FR-14) sans laisser aucune trace partielle,
préservant NFR-7 et le garde-fou anti-doublon (`AC-FR11-6/7`).

**FRs couverts :** FR-17, FR-18, FR-19, FR-20, FR-21.

**Séquencement des stories (dépendance interne à l'épic, pas de dépendance vers un
épic futur) :** 4.1 → {4.3, 4.4} → 4.5 → 4.6 ; 4.2 (infra) est un prérequis
transverse de 4.3/4.4.

### Story 4.1 : Entités EF database-first + extension du harnais SQL pour les 10 tables aval

As a `Kape22Importer`,
I want les 10 nouvelles entités (`L_D_ORDRE_FABRICATION`, `L_D_COULEE`,
`L_D_CONSIGNES`, 7×`L_D_SECTIONCHARGE_*`) ajoutées à `AscoLsiDbContext` d'après
le schéma réel `AFV004-LSI`, et les scripts `scripts/schema/` étendus en
conséquence,
So that les mappers et le Persister des stories suivantes disposent d'entités
fidèles et d'un harnais de test SQL Server local qui les couvre (AR-12), et que
la story suivante (4.2, extraction du mapping) ait un schéma réel contre lequel
vérifier la complétude de son annexe.

**Prérequis (levé) :** confirmer par une exécution réelle du workflow `ci.yml`
(pas seulement en local) que `Category=Integration` passe avec le service
container SQL Server — **confirmé** : run GitHub Actions
[`34836094341`](https://github.com/LokiQan-fos/TextToXml/actions/runs/34836094341)
(2026-09-14, commit `chore(e2e): add real-worker end-to-end regression
harness`), étapes `Initialize containers` / `Test (Category=Integration)` /
`Check commit body` toutes vertes sur runner réel. Cette story peut démarrer.

**Acceptance Criteria:**

**Given** cette story
**When** je choisis la source du schéma des 10 entités
**Then** elle vient **exclusivement** de `sys.columns` d'`AFV004-LSI` (R-3) —
  **jamais** de l'annexe de mapping de la Story 4.2, qui n'existe pas encore et
  documente des *règles de dérivation*, pas la *forme* des tables ; les deux
  sources sont indépendantes, cette story ne dépend pas de la 4.2

**Given** `sys.columns` d'`AFV004-LSI` pour les 10 tables cibles
**When** je définis les entités
**Then** chaque entité porte les colonnes réelles avec leur nullabilité et leur
  type CLR fidèles (`int?`, `string`, `DateTime?`…), un fichier par entité sous
  `Persistence/` (miroir de `L_D_KAPE22.cs`)
**And** aucune migration EF n'est générée ; `Id` est `ValueGeneratedOnAdd` quand
  la table en a un

**Given** `AscoLsiDbContext`
**When** je l'étends
**Then** il expose un `DbSet` par nouvelle entité

**Given** le harnais AR-12
**When** la fixture d'assembly s'exécute
**Then** `scripts/schema/01-ascolsi-tables.sql` (ou un script dédié) crée aussi
  les 10 nouvelles tables, **généré depuis `AFV004-LSI`** avec l'en-tête de
  provenance (serveur, base, date), jamais rédigé de mémoire (R-3)
**And** un test « modèle `AscoLsiDbContext` ⟺ `scripts/schema/` » couvre aussi
  ces 10 tables (extension du test existant de la Story 2.1)

**Tests xUnit (TDD — écrits en premier, CC-1) :** test de forme d'entité par
table (types CLR, nullabilité) ; test « pas de migration / `Id` identity » ;
extension du test « modèle `AscoLsiDbContext` ⟺ `scripts/schema/` » (Story 2.1)
aux 10 tables ; test d'intégration fumée : `scripts/schema/` s'applique sans
erreur pour les 10 tables sous `TransactionScope` (rollback).

**Critères transverses :** CC-1, CC-2, CC-3, **CC-4 (propriétés d'entité par
ordre alphabétique)**, CC-7. *(CC-6 sans objet : worker, pas `TextToXml`.)*

---

### Story 4.2 : Extraction & documentation du mapping legacy (annexe colonne-par-colonne, vérifiable)

As a développeur de `Kape22Importer`,
I want une annexe documentée, dérivée du `MappingTemplate` XML legacy et des
entités EF legacy (`OrdreFabrication`, `Consignes*`, `Couleee`), qui associe à
chaque colonne de `L_D_ORDRE_FABRICATION`, `L_D_COULEE`, `L_D_CONSIGNES` et des 7
`L_D_SECTIONCHARGE_*` (le schéma réel de la Story 4.1) son champ ou sa règle de
dérivation source dans `L_D_KAPE22`, **et un test qui vérifie mécaniquement
cette complétude**,
So that les stories 4.3/4.4/4.5 codent des mappers explicites sans jamais
deviner une règle de mémoire (même discipline que R-3 pour `L_D_KAPE22`), et
qu'une colonne oubliée dans l'annexe casse la build plutôt que de passer
inaperçue.

**Prérequis (à obtenir avant de commencer, lecture seule — AD-3) :** le code
source de `MappingTemplate` + sa configuration XML, et
`OrdreDeFabricationManager.CompleteConsignes2`
(`Desktop/kape22/OrdreDeFabricationManager.cs`, non encore lu) et
`CouleeManager.cs` (déjà fourni, non encore lu en détail) — ces trois éléments
manquent encore pour couvrir exhaustivement la règle d'applicabilité par OF des
`L_D_SECTIONCHARGE_*`.

**Format et emplacement imposés :** `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md`,
un tableau Markdown par table cible, colonnes `| Colonne | Statut | Source / Règle |`,
`Statut ∈ {sourcée, règle, à_clarifier}` (même trois catégories que la parité
production Épic 2, `spec-parity-kape22-legacy-fill-rules.md`).

**Acceptance Criteria:**

**Given** le `MappingTemplate` XML legacy et les entités EF legacy citées
  ci-dessus
**When** l'annexe est produite
**Then** elle liste, pour **chaque** colonne de `L_D_ORDRE_FABRICATION`,
  `L_D_COULEE`, `L_D_CONSIGNES` et des 7 `L_D_SECTIONCHARGE_*`, un `Statut`
  parmi `sourcée` (champ `L_D_KAPE22` exact cité), `règle` (dérivation exacte
  décrite — ex. contrôle coulée froide/chaude, répartition lingots/fours), ou
  `à_clarifier` (AC-FR17-1)
**And** une colonne `à_clarifier` **ne bloque pas** la story : elle est notée
  telle quelle dans l'annexe et une entrée est ajoutée à
  `_bmad-output/implementation-artifacts/deferred-work.md` (pattern
  `assumed, unverified` de la parité Épic 2) — la story avance sur les colonnes
  sourcées/à règle (AC-FR17-2)

**Given** les 7 tables `L_D_SECTIONCHARGE_*`
**When** l'annexe documente leur applicabilité
**Then** elle énonce explicitement la règle métier qui décide quelle(s)
  table(s) concernent un OF donné (le legacy ne semble pas toujours peupler les
  7) (AC-FR17-3)

**Given** l'annexe terminée
**When** je la publie
**Then** elle est versionnée au chemin imposé ci-dessus et devient la
  **référence unique** citée par les stories 4.3, 4.4 et 4.5 — aucun mapper de
  ces stories ne code une règle absente de l'annexe (AC-FR17-4)

**Given** le modèle EF des 10 tables (Story 4.1) et l'annexe produite
**When** un test dédié parse l'annexe et la confronte au modèle EF
**Then** il **échoue** si une colonne du modèle EF n'a **aucune** ligne dans
  l'annexe (vrai trou — sourcée, règle ou à_clarifier, peu importe, mais
  **présente**) ; il **échoue** aussi si une ligne de l'annexe référence une
  table/colonne absente du modèle EF (entrée orpheline — table renommée,
  faute de frappe) ; il **échoue** si le `Statut` d'une ligne n'est pas l'une
  des trois valeurs autorisées (AC-FR17-5)
**And** une colonne présente avec `Statut = à_clarifier` **passe** le test de
  complétude (elle n'est pas un trou : elle est documentée comme dette) **à
  condition** qu'une entrée `deferred-work.md` référence explicitement cette
  colonne — son absence fait échouer le test sur cette ligne précise, ce qui
  mécanise le pattern `assumed, unverified` au lieu de le laisser à la
  discipline humaine (AC-FR17-5, réponse à V2)
**And** une colonne `sourcée` ou `règle` passe sans condition supplémentaire

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR17-5`
(`Category=Unit`, test de complétude par réflexion sur le modèle EF de la
Story 4.1 — même famille que `SchemaModelParityTests`/`Kape22ColumnLengthsParityTests`,
exempté du rouge→vert propre comme les tests-barrière-à-la-compilation, CC-1) ;
un cas par branche : colonne manquante (échec), entrée orpheline (échec),
statut invalide (échec), `à_clarifier` sans note `deferred-work.md` (échec),
`à_clarifier` avec note (succès). Le contenu métier de l'annexe (quelle règle
exacte pour quelle colonne, AC-FR17-1..4) reste une revue humaine, non
automatisable.

**Critères transverses :** CC-1 (le test de complétude), CC-2, CC-5.
*(CC-3/CC-4/CC-6/CC-7 sans objet : pas de code de production.)*

---

### Story 4.3 : Mapper OF + Coulée (mapping structurel pur)

As a `Kape22Importer`,
I want `OrdreFabricationMapper.Map(L_D_KAPE22) -> L_D_ORDRE_FABRICATION` et
`CouleeMapper.Map(L_D_KAPE22) -> L_D_COULEE` codés explicitement d'après
l'annexe de la Story 4.2, sans aucun contrôle métier ni accès base,
So that le bundle dispose de ces deux entités prêtes à être validées et
persistées par les stories suivantes.

**Acceptance Criteria:**

**Given** un `L_D_KAPE22` mappé (Story 2.4/2.6) et l'annexe de la Story 4.2
**When** j'appelle `OrdreFabricationMapper.Map`
**Then** chaque colonne `sourcée`/`règle` de `L_D_ORDRE_FABRICATION` dans
  l'annexe est renseignée par son champ/règle source explicite ; aucune
  colonne n'est devinée hors de l'annexe (AC-FR18-1)
**And** aucune dépendance à `System.Reflection`, aucune lecture base de données
  — fonction pure (AC-FR18-2)

**Given** le même `L_D_KAPE22`
**When** j'appelle `CouleeMapper.Map`
**Then** `L_D_COULEE` est renseignée selon l'annexe, fonction pure également
  (AC-FR18-3)

**Given** une colonne marquée `à_clarifier` dans l'annexe de la Story 4.2
**When** le mapper correspondant est codé
**Then** le mapper porte un commentaire `assumed, unverified` citant l'entrée
  `deferred-work.md` correspondante plutôt que d'inventer une règle — il ne
  bloque **pas** le reste de la story (AC-FR18-4, pattern Épic 2)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR18-1` … `AC-FR18-4`, un
test par colonne mappée d'après l'annexe (fixtures `P60/` existantes).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5. *(CC-6/CC-7 sans objet :
pas d'accès base dans ce mapper.)*

---

### Story 4.4 : Mapper Consignes + les 7 SectionCharge (mapping structurel pur, règle d'applicabilité)

As a `Kape22Importer`,
I want un mapper explicite par table `L_D_SECTIONCHARGE_*` (Chutage, Lingot,
Découpe, Pits, PoidsMétrique, Refroidissoirs, SVT) et un `ConsignesMapper` qui
produit les `L_D_CONSIGNES` rattachées à leur section de charge propriétaire
par FK explicite, le tout d'après l'annexe de la Story 4.2,
So that le bundle porte, pour chaque OF, exactement les sections de charge et
consignes qui le concernent — pas plus, pas moins.

**Acceptance Criteria:**

**Given** un `L_D_KAPE22` mappé et l'annexe de la Story 4.2
**When** j'appelle chacun des 7 mappers `SectionCharge*Mapper.Map`
**Then** chaque colonne de la table correspondante est renseignée selon la
  règle documentée dans l'annexe, fonctions pures, aucune réflexion (AC-FR19-1)

**Given** la règle d'applicabilité par OF documentée dans l'annexe (Story 4.2)
**When** une table `L_D_SECTIONCHARGE_*` ne concerne pas l'OF en cours
**Then** le mapper correspondant renvoie `null` — le bundle laisse cet
  emplacement vide, aucune ligne par défaut n'est créée (AC-FR19-2)

**Given** un `L_D_KAPE22` mappé et les `L_D_SECTIONCHARGE_*` déjà mappées
**When** j'appelle `ConsignesMapper.Map`
**Then** chaque `L_D_CONSIGNES` produite porte en colonne scalaire la clé de
  sa section de charge propriétaire (FK explicite, AD-7 — jamais de propriété
  de navigation EF) et son `TypeConsigne`/`CodeConsigne` selon l'annexe
  (AC-FR19-3)

**Given** une colonne ou une règle d'applicabilité marquée `à_clarifier` dans
  l'annexe de la Story 4.2
**When** le mapper correspondant est codé
**Then** il porte un commentaire `assumed, unverified` citant l'entrée
  `deferred-work.md` correspondante — pas de blocage total de la story
  (AC-FR19-4, pattern Épic 2)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR19-1` … `AC-FR19-4`,
un test par table `SectionCharge*` (mappée + non applicable → `null`), un test
`ConsignesMapper` par section de charge propriétaire, fixtures `P60/`
existantes.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5. *(CC-6/CC-7 sans objet :
pas d'accès base dans ces mappers.)*

---

### Story 4.5 : `Kape22ImportBundleMapper` (orchestrateur) + contrôles métier purs

As a `Kape22Importer`,
I want un `Kape22ImportBundleMapper` qui compose `Kape22Mapper.Map` avec les
mappers des stories 4.3/4.4 pour produire un `Kape22ImportBundle` complet, et
qui applique les contrôles métier **ne nécessitant aucune lecture base**
(cohérence répartition lingots/fours, format de la coulée chaude),
So that le bundle transmis au Persister porte déjà toutes les entités
structurelles et les rejets purement calculables, sans dupliquer cette logique
côté Persister.

**Acceptance Criteria:**

**Given** un XML normalisé valide
**When** j'appelle `Kape22ImportBundleMapper.Map`
**Then** le bundle porte `L_D_KAPE22`, `L_D_ORDRE_FABRICATION`, `L_D_COULEE`,
  chaque `L_D_SECTIONCHARGE_*` applicable (ou `null`) et les `L_D_CONSIGNES`
  associées, plus les métadonnées `Success`/`Errors`/`Warnings`/`NumeroFichier`/`OF`
  héritées de `Kape22Mapper` (AC-FR20-1, AD-6)

**Given** une `L_D_SECTIONCHARGE_REFROIDISSOIRS` mappée dont
  `NombreLingotsFour1 + NombreLingotsFour2 ≠ NombreDemiProduit` de l'OF
**When** le bundle est construit
**Then** `Success = false`, une `ConversionError{Block:File, Code}` dédiée cite
  l'OF et les deux valeurs en écart — aucune entité n'est ajoutée au contexte
  plus tard (AC-FR20-2)

**Given** une coulée « chaude » (`ConsignesEnfournementPits.CodeConsigne ≠ "1"`)
  dont le numéro `Coulee` ne commence pas par `'0'`
**When** le bundle est construit
**Then** `Success = false`, une `ConversionError` dédiée cite l'OF et le numéro
  de coulée (AC-FR20-3)

**Given** un OF sans `L_D_SECTIONCHARGE_PITS` applicable (donc sans consigne
  d'enfournement)
**When** le bundle est construit
**Then** `Success = false`, une `ConversionError` dédiée signale l'absence de
  consigne d'enfournement — reproduit le rejet legacy correspondant
  (AC-FR20-4)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR20-1` … `AC-FR20-4`,
fixtures `P60/` valides + variantes fautives dédiées (répartition incohérente,
coulée chaude mal formée, PITS absent).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5. *(CC-6/CC-7 sans objet :
pas d'accès base dans cet orchestrateur.)*

---

### Story 4.6 : `Kape22Persister` remplacé (bundle, transaction unique, contrôle coulée) + `Kape22FichierProcessor` mis à jour

As a `Kape22Importer`,
I want `Kape22Persister.Persist` remplacé par une surface acceptant
`Kape22ImportBundle`, qui exécute le contrôle d'existence de la coulée froide
(lecture base) puis empile toutes les entités non nulles du bundle avant un
**unique** `SaveChanges()`, et `Kape22FichierProcessor` mis à jour en
conséquence,
So that un Fichier réussi écrit ses 10 tables de façon atomique, et qu'un échec
— SQL ou métier — ne laisse **aucune** trace partielle, `L_D_KAPE22` incluse.

**Note de périmètre (`epic-3-retro-item-4`, attestation commit SVN) :** cette
story ne modifie que `Kape22Importer` (`TextToXml.sln`, Git) et ne produit
**aucun** commit `MicroServices.sln` (SVN) **si et seulement si** la signature
`IFichierProcessor.Process(string, byte[]) -> FichierProcessingResult` et la
forme de `FichierProcessingResult` restent **strictement identiques** en sortie
de cette story — c'est une condition vérifiable, pas juste une hypothèse : la
story de revue de code confirme cette égalité de signature avant de clore 4.6.
Si l'une des deux doit changer pour porter les nouvelles causes d'échec, le
premier commit `MicroServices.sln` qui en découle applique immédiatement la
convention `AC → test` de l'item-4 (encore `open`).

**Note de couverture (réponse à V3) :** cette story **garde sa propre preuve
d'atomicité**, au niveau intégration (`AC-FR20-5` : rejet métier → rien
d'ajouté ; `AC-FR21-2` : échec SQL → rien de committé), construite sur
`TransactionalPersistenceTests` sans les 10 fichiers `P60/` réels. La Story
4.7 ajoute la **même** preuve rejouée de bout en bout sur les fixtures
réelles (`AC-FR21-5`) — une couverture supplémentaire, pas un déplacement :
4.6 ne perd rien.

**Acceptance Criteria:**

**Given** un `Kape22ImportBundle` avec `Success = true`
**When** `Kape22Persister.Persist(bundle)` est appelé
**Then** le garde-fou anti-doublon (`AC-FR11-6/7`, inchangé) est vérifié en
  premier ; s'il détecte un import déjà commité, rien n'est ajouté,
  `AlreadyImported = true`

**Given** un bundle qui franchit le garde-fou et dont la coulée est « froide »
  (`ConsignesEnfournementPits.CodeConsigne == "1"`)
**When** `Persist` s'exécute
**Then** il lit `L_D_COULEE` via le contexte déjà ouvert ; si la coulée
  n'existe pas, **aucune** entité du bundle n'est ajoutée, une ligne
  `L_D_LOG_COMMANDE` « REJETÉ » est écrite (même mécanisme que
  `PersistRejected` existant) et la cause cite explicitement la coulée
  manquante (AC-FR20-5 — contrôle métier FR-20, exécuté ici pour la lecture
  base qu'il requiert, AD-1)

**Given** un bundle qui franchit tous les contrôles (garde-fou + coulée)
**When** `Persist` s'exécute
**Then** `L_D_KAPE22`, la ligne `L_D_LOG_COMMANDE` « OK », `L_D_ORDRE_FABRICATION`,
  `L_D_COULEE`, chaque `L_D_SECTIONCHARGE_*` non nulle et ses `L_D_CONSIGNES`
  associées sont ajoutées au même contexte, puis **un seul** `SaveChanges()`
  les commit ensemble (AC-FR21-1, AD-1)

**Given** ce même `SaveChanges()` unique qui échoue
  (`DbUpdateException`/`DbException`)
**When** `Persist` intercepte l'exception
**Then** la transaction est déjà annulée par EF Core ; **aucune** des 11 lignes
  potentielles n'est committée ; le résultat porte une
  `ConversionError{Code:PersistenceError}` citant la cause SQL (extension
  d'`AC-FR11-5` aux nouvelles tables) (AC-FR21-2)

**Given** l'ancienne surface `Persist(MapResult<L_D_KAPE22>)`
**When** je relis le code après cette story
**Then** elle a disparu — une seule méthode `Persist` existe, prenant
  `Kape22ImportBundle` (AC-FR21-3, AD-6) ; `Kape22FichierProcessor` (Story 3.2)
  appelle `Kape22ImportBundleMapper.Map` puis cette unique surface

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR20-5`, `AC-FR21-1` …
`AC-FR21-3`, extension des tests `TransactionalPersistenceTests`/
`DoubleJournalIntegrationTests` existants (Story 2.8/3.3) aux nouvelles
tables.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 4.7 : Extension de la suite E2E aux 10 tables aval

As a `Kape22Importer`,
I want la suite E2E (Story 3.6, 10 fichiers `P60/` de référence) étendue pour
vérifier les 10 tables aval, succès comme échec,
So that l'atomicité de bout en bout (AD-1) et l'absence de trace partielle sont
prouvées sur le pipeline réel, pas seulement sur les tests unitaires du
Persister.

**Acceptance Criteria:**

**Given** les 10 fichiers `P60/` de référence
**When** je rejoue la suite E2E
**Then** chaque Fichier valide produit des lignes cohérentes dans les 10
  tables aval, cohérentes avec les données visibles du Fichier (AC-FR21-4)

**Given** au moins une fixture fautive dédiée par cause (coulée absente,
  répartition lingots/fours incohérente, échec SQL simulé)
**When** je rejoue la suite E2E sur ces fixtures
**Then** **aucune** des 10 tables ne reçoit de ligne — pas même `L_D_KAPE22` —
  et la cause est lisible dans `L_D_LOG_COMMANDE` + `*.errors.json`
  (AC-FR21-5)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR21-4`, `AC-FR21-5`,
extension de la suite E2E (Story 3.6) — fixtures fautives dédiées créées par
cette story (Annexe A.4).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

## Corrections post-rétrospective Épic 4

> Issues de la rétrospective Épic 4 (`epic-4-retro-2026-09-17.md`, verdict
> accepted-with-open-items) et du Sprint Change Proposal
> `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-17.md`
> (approuvé). Périmètre FR-17..FR-21 inchangé — ce sont des corrections de
> conformité aux `AC-FRx-y` déjà déclarés, pas de nouveaux FR. **Séquencement
> imposé, série stricte, pas de parallélisation :** 4.2-bis → 4.3-bis → 4.9.

### Story 4.2-bis : Extension de l'annexe de mapping (champ `scale`)

As a développeur de `Kape22Importer`,
I want que l'annexe `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md`
enregistre, pour toute colonne cible `decimal` sourcée d'un champ KAPE22
`int`/`int?`, son échelle sous un champ nommé `scale` (valeur = entier, nombre
de décimales à appliquer, ex. `1` pour `DECIMAL(2,1)`) — et qu'un test de
complétude échoue si ce champ manque, que la colonne soit déjà connue du défaut
ou nouvellement découverte,
So that aucun mapper, présent ou futur, ne puisse reproduire le défaut de mise
à l'échelle découvert indépendamment dans 5 mappers
(`story-4-6-decimal-scale-defect-story-4-3-bis`).

**Acceptance Criteria:**

**Given** une colonne `sourcée` de type cible `decimal` dont la source KAPE22
  est `int`/`int?`
**When** l'annexe est étendue
**Then** elle porte un champ `scale` (entier, nombre de décimales à appliquer)
  explicite pour cette colonne — format figé : nom de champ `scale`, valeur
  entière, pas d'unité ni de facteur multiplicatif texte libre (AC-FR17-1 étendu)

**Given** le modèle EF des 10 tables aval (Story 4.1) et l'annexe étendue
**When** `MappingAnnexCompletenessTests` (famille `AC-FR17-5`) s'exécute
**Then** il échoue si une colonne de type CLR `decimal` sourcée d'un `int`/`int?`
  KAPE22 n'a pas de champ `scale` renseigné dans l'annexe — sans distinction
  entre une colonne déjà identifiée par la rétro (les 5 mappers connus) et une
  colonne nouvellement détectée par le test lui-même (fusion des deux AC
  précédemment distinctes en une seule assertion de complétude)
**And** une colonne `decimal` dont la source n'est pas `int`/`int?` (ex. déjà
  `decimal` côté KAPE22) n'est pas soumise à cette règle

**Given** l'annexe étendue
**When** elle sert de référence à la Story 4.3-bis
**Then** elle couvre explicitement, avec leur `scale`, les 12 colonnes déjà
  connues (`OrdreFabricationMapper` : `DiametreProduit`, 6×`Tolerance*`,
  `LongueurCD`, `PoidsDemiProduitUnitaire`, `PoidsPrevuDemiProduit` ;
  `SectionChargeLingotMapper` : `SectionLaminage`, `EpaisseurEnLaminage`,
  4×`Tolerance*1` ; `SectionChargeChutageMapper` : `ChutageTete`,
  `ChutagePied` ; `SectionChargeDecoupeMapper` : `LongueurMoyenne` ;
  `SectionChargePitsMapper` : `H2Coulee`)
**And** elle note explicitement, par une ligne dédiée par table, que
  `L_D_SECTIONCHARGE_REFROIDISSOIRS`, `L_D_SECTIONCHARGE_POIDSMETRIQUE` et
  `L_D_SECTIONCHARGE_SVT` ne portent **aucune** colonne `decimal` (vérifié par
  lecture directe des 3 entités EF) — hors périmètre de 4.3-bis, pas un trou
  de l'annexe

**Tests xUnit (TDD — écrits en premier, CC-1) :** extension de
`MappingAnnexCompletenessTests.cs` (`Category=Unit`, même famille
qu'`AC-FR17-5`, exempté du rouge→vert propre comme test-barrière-à-la-compilation) :
un cas colonne `decimal←int` avec `scale` présent (succès), un cas sans
`scale` (échec), un cas colonne `decimal` non sourcée d'un `int` (pas de
règle, succès).

**Critères transverses :** CC-1 (test de complétude), CC-2, CC-5.
*(CC-3/CC-4/CC-6/CC-7 sans objet : pas de code de production.)*

---

### Story 4.3-bis : Correctif de mise à l'échelle décimale (5 mappers)

As a `Kape22Importer`,
I want que `OrdreFabricationMapper`, `SectionChargeLingotMapper`,
`SectionChargeChutageMapper`, `SectionChargeDecoupeMapper` et
`SectionChargePitsMapper` appliquent la mise à l'échelle documentée par le
champ `scale` de l'annexe étendue (Story 4.2-bis) au lieu d'écrire l'entier
KAPE22 brut,
So that tout Fichier P60 réel dont les sections `SectionCharge` applicables
portent une valeur dans l'échelle attendue s'insère sans dépassement SQL.

**Prérequis :** Story 4.2-bis livrée — l'annexe étendue et son test de
complétude sont la référence unique du facteur d'échelle par colonne.

**Acceptance Criteria:**

**Given** l'annexe étendue (Story 4.2-bis) portant le `scale` par colonne
**When** chacun des 5 mappers ci-dessus est corrigé
**Then** la valeur écrite dans la colonne `DECIMAL` cible respecte l'échelle
  documentée (ex. `DECIMAL(2,1)` max 9.9 ne déborde plus pour un entier KAPE22
  dans la plage réelle observée sur les fixtures `P60/`)

**Given** `SectionChargeRefroidissoirsMapper`, `SectionChargePoidsMetriqueMapper`
  et `SectionChargeSvtMapper`
**When** le périmètre de cette story est vérifié
**Then** ils sont explicitement **hors périmètre** — confirmé par 4.2-bis
  (aucune colonne `decimal` dans leurs 3 tables cibles) — pas de mise à
  l'échelle à coder

**Given** les suites d'intégration qui contournaient le défaut
  (`Kape22FichierProcessorIntegrationTests`, `DoubleJournalIntegrationTests`,
  `EndToEndImportIntegrationTests`, `WorkerLoopRobustnessIntegrationTests` —
  toutes patchées Story 4.6 via `TestSupport.ZeroOutOfScaleDimensions`/`InsertableFichier`)
**When** le correctif est en place
**Then** le contournement `TestSupport` est **retiré**, ces suites repassent
  sur les fixtures réelles `P60/` non mutées, et restent vertes

**Given** `GpaoImportP60WorkerEndToEndTests` (actuellement skip documenté,
  fixtures partagées byte-for-byte avec `Kape22ProductionDataParityTests`)
**When** le correctif est en place
**Then** ce test cesse d'être skip et passe vert sur les fixtures réelles
  `P60_847_682_081/082`

**Tests xUnit (TDD — écrits en premier, CC-1) :** un test par colonne mise à
l'échelle (fixtures `P60/` existantes, valeur brute connue → valeur decimal
attendue), + les 4 suites d'intégration ci-dessus repassées sans
contournement, + `GpaoImportP60WorkerEndToEndTests`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5.

---

### Story 4.9 : Hardening Épic 4 (A-2, A-3, A-4, A-5)

As a `Kape22Importer`,
I want fermer les 4 gaps de robustesse identifiés par la rétro Épic 4 sur le
chemin `FichierProcessor → BundleMapper → Persister`,
So that l'épic ne laisse aucune régression silencieuse ni cause d'échec non
journalisée derrière lui.

**Prérequis :** Story 4.3-bis livrée (séquence linéaire imposée — pas de
parallélisation avec A-3, qui modifie la source des valeurs que 4.3-bis teste).

**Acceptance Criteria:**

**Given** `Import_CleanFichier_RunsThroughToTheInsert_AcFr13_1` et
  `Import_Success_ResultShape_AcFr13_5` (`Category=Unit`, in-memory
  `AscoLsiDbContext`)
**When** un import réussit
**Then** les 10 `DbSet` aval sont assertés en plus de
  `Kape22Rows`/`LogCommandeRows` : `OrdreFabricationRows`, `CouleeRows`,
  `ConsignesRows`, `SectionChargeChutageRows`, `SectionChargeDecoupeRows`,
  `SectionChargeLingotRows`, `SectionChargePitsRows`,
  `SectionChargePoidsMetriqueRows`, `SectionChargeRefroidissoirsRows`,
  `SectionChargeSvtRows` (liste exhaustive, `AscoLsiDbContext.cs`) (A-2)

**Given** `Kape22Mapper.Map` et sa boucle réflective par Champ
**When** elle copie `OF` et `Coulee` sur `entity`
**Then** `entity.OF` et `entity.Coulee` sont trim **une seule fois, à la
  source**, dans cette boucle (ou immédiatement après) — chaque consommateur
  aval (9 mappers + `Kape22Persister`) cesse de trim/ne-pas-trim à son propre
  site de lecture ; `CouleeMapper.IdCoulee` n'a plus besoin de sa propre garde,
  le risque de désynchronisation avec `Kape22Persister.couleeAlreadyExists`
  disparaît structurellement (A-3)

**Given** `Kape22Persister.cs` qui possède déjà `private const string
  ColdConsignePits = "1";` et `Kape22ImportBundleMapper.cs` qui porte le
  littéral inline `"1"` pour la même règle métier
**When** le marqueur hot/cold Coulee est consulté par les deux collaborateurs
**Then** `ColdConsignePits` est **extrait vers une source partagée** que les
  deux référencent (ex. `Kape22ImportBundle` ou un petit type déjà référencé
  par les deux) — il ne s'agit pas de créer une nouvelle constante, mais de
  faire cesser la duplication de celle qui existe déjà côté `Kape22Persister`
  (A-4)

**Given** le risque déjà documenté (`ConsignesMapper.cs`) qu'une collision sur
  la clé naturelle `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` de
  `L_D_CONSIGNES` survienne au sein d'un même bundle
**When** `Kape22Persister.PersistMapped` prépare
  `context.ConsignesRows.AddRange(bundle.Consignes)`
**Then** une **pré-vérification de la clé naturelle** s'exécute avant
  l'`AddRange` : toute collision détectée produit un `ConversionError` + une
  ligne `L_D_LOG_COMMANDE` REJETÉ via le circuit AD-4 existant, **sans jamais
  appeler** `AddRange` sur les entrées en collision
**And** le filtre `catch (... when (exception is DbUpdateException or
  DbException))` de `Kape22Persister` **n'est pas élargi** — élargir le filtre
  à `InvalidOperationException` risquerait d'avaler des
  `InvalidOperationException` sans rapport avec cette collision (violation de
  la frontière `UnexpectedFailure` du contrat `ErrorCode`, Story 3.5) ; la
  pré-vérification est la seule implémentation retenue (A-5)

**Tests xUnit (TDD — écrits en premier, CC-1) :** un test par AC (A-2 :
assertions étendues des 2 tests existants ; A-3 : `Kape22MapperTests` sur un
Champ `OF`/`Coulee` paddé ; A-4 : test de non-régression que les deux
collaborateurs lisent la même source ; A-5 : un test d'intégration simulant
une collision `CodeOperation` → `ConversionError` + ligne REJETÉ, zéro
exception non catchée).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5.

---

## Corrections post-rétrospective Épic 4 (rétro #2)

> Issues de la rétrospective Épic 4 #2 (`epic-4-retro-2026-09-18.md`, verdict
> accepted-with-open-items) et du Sprint Change Proposal
> `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-18.md`
> (approuvé). Périmètre FR-17..FR-21 inchangé — B-1..B-4 sont une correction
> de routage (déjà tracés depuis la revue de Story 4.3-bis, à tort rattachés
> à Story 4.9) ; B-5 est une découverte nouvelle. Aucun ordre imposé entre
> B-1..B-5 — regroupés en une seule story pour un seul cycle de revue.

### Story 4.10 : Hardening Épic 4 (B-1..B-5)

As a `Kape22Importer`,
I want fermer les 5 items de hardening restants identifiés par la rétro Épic 4
#2 autour de `DecimalScale.Apply` et de la couverture de parité production,
So that la mise à l'échelle décimale (Story 4.3-bis) reste garantie dans le
temps par des garde-fous automatiques plutôt que par vigilance manuelle.

**Prérequis :** aucun — B-1..B-5 sont indépendants entre eux et des stories
4.2-bis/4.3-bis/4.9 déjà livrées ; regroupés en une seule story pour un seul
cycle de revue de code (même rationale que Story 4.9 pour A-2/A-4/A-5).

**Acceptance Criteria:**

**Given** les 21 appels `DecimalScale.Apply(source.X, N)` codés en dur dans
  les 5 mappers (`OrdreFabricationMapper.cs`, `SectionChargeChutageMapper.cs`,
  `SectionChargeDecoupeMapper.cs`, `SectionChargeLingotMapper.cs`,
  `SectionChargePitsMapper.cs`) et la colonne `Scale` de l'annexe de mapping
  (`MappingAnnexEntry.Scale`, Story 4.2-bis)
**When** un nouveau contrôle de complétude s'exécute (extension ou sibling de
  `MappingAnnexCompleteness.Check`, `MappingAnnexSchema.cs`)
**Then** il échoue si le littéral `N` passé par un mapper à
  `DecimalScale.Apply` diverge de la valeur `Scale` de l'annexe pour la même
  colonne — l'existant `MappingAnnexCompletenessTests` (famille AC-FR17-5) ne
  vérifie aujourd'hui que la présence d'un `Scale` dans l'annexe, jamais sa
  conformité au code mappeur réellement livré (B-1)

**Given** `DecimalScale.Apply(int rawValue, int scale)` /
  `Apply(int? rawValue, int scale)` (`DecimalScale.cs`), seul chemin
  sanctionné d'un `int`/`int?` KAPE22 brut vers une colonne `decimal` EF
**When** un test de complétude réflectif (ou dérivé de l'annexe) s'exécute
**Then** il échoue si une propriété EF `decimal` cible référencée par
  l'annexe et sourcée d'un `int`/`int?` est assignée sans passer par
  `DecimalScale.Apply` — garde-fou contre un futur 6e mapper reproduisant le
  défaut fixé par Story 4.3-bis (B-2)

**Given** `Kape22ProductionDataParityTests` (`RoundTripThroughTestDatabase`),
  qui ne round-trip aujourd'hui que `L_D_KAPE22`
**When** la suite est étendue
**Then** elle round-trip également `L_D_ORDRE_FABRICATION` et les tables
  `L_D_SECTIONCHARGE_*` mises à l'échelle par `DecimalScale`, chaque colonne
  scaled persistée étant comparée à la valeur de production réelle lue via
  `ReadProductionRows` (connexion `AscoLSI_Production`) — pas de fixture
  statique de substitution (B-3)

**Given** `scripts/e2e-worker-import.ps1`, dont l'étape `dotnet build`
  (lignes 105-106) vérifie déjà `$LASTEXITCODE` et lève si non nul
**When** la même exécution atteint l'invocation finale
  `dotnet ... Kape22ProductionDataParityTests` (boucle
  `$SkipProductionCompare`, ligne 188)
**Then** cette invocation est gardée de la même façon — un `$LASTEXITCODE`
  non nul fait échouer le script englobant au lieu de continuer
  silencieusement (B-4)

**Given** `DecimalScale.Apply`, qui corrige le placement décimal (`scale`)
  mais ne connaît la précision totale `p` d'aucune colonne cible
  `DECIMAL(p,s)` — `p` n'est aujourd'hui capturé nulle part, ni dans l'annexe
  (`MappingAnnexEntry` ne porte que `Scale`) ni ailleurs
**When** un entier KAPE22 brut hors gabarit, une fois mis à l'échelle,
  dépasse encore la magnitude de sa colonne cible
**Then** un garde-fou pré-persistance détecte le dépassement et produit un
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
