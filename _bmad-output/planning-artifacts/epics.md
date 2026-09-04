---
stepsCompleted: [step-01-validate-prerequisites, step-02-design-epics, step-03-create-stories, step-04-final-validation]
inputDocuments:
  - _bmad-output/planning-artifacts/PRD.md
---

# TextToXml - Epic Breakdown

> Langue du document : **français**, en cohérence avec le PRD et le glossaire §3
> (le PRD impose l'usage à l'identique du vocabulaire français dans les FR, UJ,
> tests et code). `document_output_language` du `config.yaml` (English) est
> délibérément écarté pour cette raison — même choix que pour le PRD.

## Overview

Ce document décompose le PRD `TextToXml` en 3 épics et 22 stories implémentables.
Tous les tests d'intégration base de données sont **dockerisés** (Testcontainers /
`docker-compose` de test, schéma minimal) — voir **AR-12**.
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

- **AR-1** — Un **microservice / projet .NET par format**. `TextToXml` est le
  **seul** code partagé. `Kape22Importer` est le gabarit des suivants (§0, D24, FR-16).
- **AR-2** — Solution **`TextToXml.sln` autonome** dans ce dépôt : projet `TextToXml`
  (lib) + `Kape22Importer` (worker) + projets de tests. Référence
  `PortalSharedLibrary` pour l'identité/log (D20).
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
- **AR-7** — Worker sur pattern **`BackgroundService` + `PeriodicTimer`** déjà en
  place dans `FactoryScope` (`FileImportBackgroundService`). Abstraction
  **`IFileSource`** (`DirectoryFileSource` en prod, impl. mémoire en test) (§4.3, FR-12).
- **AR-8** — Base **database‑first** : `AscoLsiDbContext` fige les tables
  `L_D_KAPE22` (92 colonnes) + `L_D_LOG_COMMANDE` d'après Annexe C. **Aucune
  migration**, `Id` identity (D8, D9, Annexe C).
- **AR-9** — Enregistrement Launcher via `MQTTnetServices.dbo.WorkerSettings`
  (`WorkerName`, `IsActive`) ; contrat `WorkerStatus` identique à
  `ServicesMicroScope.LauncherApiClient` (D9, FR-14).
- **AR-10** — Encodage d'entrée **`Windows-1252` figé** ; décodeur **strict** ;
  la lib enregistre elle‑même `CodePagesEncodingProvider` (D2, D19, FR-2).
- **AR-11** — Jeu de fixtures : les 10 fichiers `P60/` + variantes fautives
  dérivées + **1 descripteur synthétique non‑P60** `fixtures/generic/` (Annexe A.4).
- **AR-12** — **Tous** les tests d'intégration qui touchent SQL Server (EF Core,
  scripts SQL, transactions, garde‑fou anti‑doublon, E2E) s'exécutent et sont
  orchestrés via **conteneurs Docker** : **Testcontainers for .NET**
  (`Testcontainers.MsSql`) de préférence, ou un `docker-compose` de test. La base
  de test **n'est pas** la base complète : un script d'init crée **uniquement les
  tables nécessaires aux tests** (`L_D_KAPE22`, `L_D_LOG_COMMANDE`, et pour les
  logs `MQTTnetServices.dbo.Logs` / `dbo.WorkerSettings`). Les tests unitaires
  (`TextToXml`, `Kape22Mapper` hors persistance) et les tests `IFileSource`
  mémoire **ne** requièrent **pas** Docker.
  - **Un seul conteneur** est démarré pour **tout l'assembly d'intégration**
    (fixture xUnit niveau assembly) ; l'isolation entre tests se fait par **reset
    des données** (`Respawn` ou `TRUNCATE`), pas par un nouveau conteneur —
    limite le coût (image ~1,5 Go, démarrage 10‑30 s).
  - Jamais de dépendance à un SQL local (LocalDB, instance de poste).
  - **Prérequis runner CI (documenté `README`) :** le runner de build doit savoir
    exécuter des **conteneurs Linux** (image `mcr.microsoft.com/mssql/server`,
    édition Developer, gratuite pour dev/test). Sur l'infra Windows Server 2019
    du portail : WSL2 / Hyper‑V requis. À défaut, la catégorie `Integration` ne
    tourne qu'en local — **voir Risque R-1**.
  - Image épinglée par tag exact (pas `latest`).

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
    (harnais Docker Story 2.1). Ces cas restent test‑first au sens : le test /
    l'assertion existe et échoue **avant** que le code de prod le satisfasse.
- **CC-2 — Commentaires : langue & syntaxe.** Tous les commentaires (C#, XML, SQL,
  YAML, `.csproj`, scripts) sont en **anglais**. Chaque phrase de commentaire
  commence par une **majuscule** et se termine par un **point**. Les listes
  numérotées dans les commentaires sont **interdites**.
- **CC-3 — Commentaires : position & préservation.** **Aucun** commentaire en fin
  de ligne (trailing). Chaque commentaire est sur sa **propre ligne**,
  immédiatement **au‑dessus** du bloc qu'il décrit. Les commentaires existants
  sont **conservés à l'identique** sauf si le code sous‑jacent est modifié.
- **CC-4 — Tri alphabétique.** Les propriétés des classes et des objets (records
  DTO, entités EF, classes de configuration, `ConversionError`, `ImportResult`…)
  sont déclarées par **ordre alphabétique**.
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
| NFR-8 | 1.1, 2.1 | inspection des versions de packages (dont `Testcontainers.MsSql`) |
| NFR-9 | 3.4 (`AC-FR14-6`) | test arrêt propre < 5 s, jamais de demi‑insertion |

**AR-12 (tests d'intégration dockerisés)** — harnais central : **Story 2.1**
(`Testcontainers.MsSql` + script d'init tables minimales + fallback
`docker-compose`) ; réutilisé par **2.8** (persistance transactionnelle),
**3.3** (écritures de logs), **3.5** (`AC-FR15-3/4`), **3.6** (E2E 10 fichiers).
Tests unitaires (`TextToXml`, `Kape22Mapper` hors persistance) sans Docker.

### Risques & points à trancher en début d'épic

| # | Risque / point | Impact | Action | Statut |
|---|---|---|---|---|
| R-1 | Runner CI (Windows Server 2019) : capacité conteneurs Linux (`mssql/server`) | catégorie `Integration` non exécutée en CI | **Tranché (utilisateur) :** l'environnement cible gère les conteneurs ; à défaut les tests s'appuient sur Testcontainers / instance locale selon les besoins. Story 2.1 : garde le skip propre si pas de démon Docker. | ✅ résolu |
| R-2 | `PortalSharedLibrary` : mode de référencement (D20) | bloque le build de la solution (Story 1.1) | **Tranché (utilisateur) :** `ProjectReference` pointant vers l'emplacement de `PortalSharedLibrary` dans l'arborescence (chemin standard de la solution / dépôt `PortalFosMarcegaglia`). Story 1.1 : documenter le chemin exact dans le `README`. | ✅ résolu |
| R-3 | `test-schema.sql` rédigé à la main → dérive vs `L_D_KAPE22` réelle | tests d'intégration verts contre un faux schéma (contre‑métrique SM‑3) | **Confirmé (utilisateur) :** base `AFV004-LSI` accessible sur le serveur cible pour lire les 92 colonnes. Story 2.1 : script généré depuis `AFV004-LSI` (provenance datée), + test « modèle EF ⟺ `test-schema.sql` ». | ✅ accès confirmé |
| R-4 | Ordre des enfants du XML normalisé vs `<xs:sequence>` de `P60.xsd` | validation `AC-FR5-14` / `AC-FR7-1` casse si divergence | Décision : XML émis dans l'**ordre du Descripteur**, `P60.xsd` en `<xs:sequence>` **même ordre** (Stories 1.6 & 2.3). | ✅ résolu |
| R-5 | `xsd.exe` génère `Kape22File` dans l'ordre du schéma, pas alphabétique — conflit `CC-4` | friction inutile / post‑traitement fragile | `CC-4` **ne s'applique pas** aux fichiers générés ; membres ajoutés à la main → classe partielle triée (Story 2.3). | ✅ résolu |
| R-6 | Source de `max_length` pour `AC-FR8-2` non spécifiée | dépendance schéma au runtime, ou constantes qui dérivent | Story 2.5 : constantes issues d'Annexe C dans la config d'entité, pas de requête `sys.columns` live. | ✅ résolu |

> **C2 (utilisateur)** : le fichier `Templates/P60.xml` est présent et à jour dans
> l'espace de travail — les Stories 2.2 / 2.3 en dérivent la liste complète des
> `<value>`.

### Setup / socle (sans `AC-FRx-y` direct mais nécessaires)

| Besoin | Story |
|---|---|
| Solution `TextToXml.sln`, projets, xUnit, `PortalSharedLibrary`, arbo fixtures + 10 fichiers valides, séparation catégories `Unit` / `Integration` | 1.1 |
| `AscoLsiDbContext` database‑first (`L_D_KAPE22`, `L_D_LOG_COMMANDE`) + **harnais de test Docker** (Testcontainers `MsSql` + script d'init tables minimales, AR-12) | 2.1 |
| `P60.xml` enrichi (`datatype`, `expectedMessageCount`) + ressource embarquée | 2.2 |
| `P60.xsd` + génération DTO `Kape22File` + validation avant désérialisation | 2.3 |

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
  tourne **sans Docker**, `Category=Integration` requiert un démon Docker (AR-12)

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
  (AC-FR16-4) ; les seuls `ProjectReference` de `Kape22Importer` vers du code
  partagé sont `TextToXml` + `PortalSharedLibrary` (AC-FR16-1) ; les points de
  variation d'un format sont **exactement** `<format>.xml`, `<format>.xsd`, DTO,
  entité + `DbContext`, table de mapping, `appsettings` (AC-FR16-2)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR1-9`, `AC-FR5-13`,
`AC-FR16-1`, `AC-FR16-2`, `AC-FR16-3`, `AC-FR16-4`, `CTR-1`, `CTR-2`, `CTR-3`
(tests d'architecture + fixtures `generic/`).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-6.

---

## Épic 2 : `Kape22Importer` — contrat de format, mapping & persistance

Étape 2 pour P60 : du XML normalisé à la ligne insérée dans `AscoLSI`, **tout ou
rien**, avec contrôle de compatibilité au démarrage et garde‑fou anti‑doublon.

### Story 2.1 : Entités EF (database‑first) & harnais de test Docker

As a `Kape22Importer`,
I want un `AscoLsiDbContext` figeant les tables cibles d'après Annexe C (sans
migration) **et** un harnais de tests d'intégration dockerisé (Testcontainers
SQL Server, schéma minimal),
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
**Then** elle lance **un** conteneur **SQL Server** via `Testcontainers.MsSql`
  (image épinglée par tag exact) pour tout l'assembly d'intégration, applique
  `test-schema.sql` (idempotent) créant **uniquement** `AscoLSI.dbo.L_D_KAPE22`,
  `AscoLSI.dbo.L_D_LOG_COMMANDE`, `MQTTnetServices.dbo.Logs`,
  `MQTTnetServices.dbo.WorkerSettings` — et **rien d'autre** de la base réelle
**And** `test-schema.sql` est **scripté depuis la base réelle `AFV004-LSI`**
  (`GENERATE SCRIPTS` / `sys.columns`), avec en en‑tête sa **provenance et sa
  date** ; il n'est **jamais** rédigé de mémoire (R-3)
**And** l'isolation entre tests se fait par **reset des données** (`Respawn` /
  `TRUNCATE`), pas par un nouveau conteneur ; le conteneur est **détruit** en fin
  d'assembly
**And** la chaîne de connexion du conteneur est fournie au `DbContext` par la
  fixture (jamais en dur, jamais un SQL local)
**And** un fallback `docker-compose` de test équivalent est fourni et documenté
  pour l'exécution locale et la CI
**And** ces tests portent `[Trait("Category","Integration")]` ; ils sont
  **ignorés avec un message clair** (pas en échec) si aucun démon Docker n'est
  disponible

**Tests xUnit (TDD — écrits en premier, CC-1) :** test « ensemble des colonnes
NOT NULL == Annexe C.1 » ; test de forme d'entité (types CLR) ; test « pas de
migration / `Id` identity » ; test « connexions lues de `IConfiguration` » ;
**test « modèle `AscoLsiDbContext` ⟺ colonnes/types de `test-schema.sql` »** (R-3) ;
**test d'intégration fumée** : la fixture Docker démarre, `test-schema.sql` passe,
un `INSERT`/`SELECT` round‑trip sur `L_D_KAPE22` réussit dans le conteneur.

**Critères transverses :** CC-1, CC-2, CC-3, **CC-4 (propriétés d'entité par
ordre alphabétique)**, CC-5, CC-7. **AR-12** : harnais de test dockerisé.

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
  conforme → `{Block:File, Code:PersistenceError}` citant l'erreur de schéma
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
  vérifie que ces constantes == `test-schema.sql`
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

*(Tests d'intégration EF — via le **harnais Docker de la Story 2.1**
(`Testcontainers.MsSql`, tables minimales `L_D_KAPE22` + `L_D_LOG_COMMANDE`),
`[Trait("Category","Integration")]`. Aucun SQL local. AR-12.)*

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
(catégorie `Integration`, harnais Docker Story 2.1).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7. **AR-12** : tests
d'intégration dockerisés.

---

## Épic 3 : `Kape22Importer` — worker, orchestration & exploitation

Le worker `BackgroundService` + `PeriodicTimer` qui orchestre dossier → `TextToXml`
→ archive → `Kape22Mapper` → EF → déplacement → double log, supervisé par le
Launcher.

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

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR14-1`, `AC-FR14-2`,
`AC-FR14-3`, `AC-FR14-4`, `AC-FR14-7`, `AC-FR14-8` — les cas écrivant en base
(`L_D_LOG_COMMANDE`, `Logs`) sont en catégorie `Integration` sur le harnais
Docker de la Story 2.1 (AR-12).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7. **AR-12** : tests
d'intégration dockerisés.

---

### Story 3.4 : Intégration Launcher & arrêt propre

As a exploitation LSI,
I want que le worker s'enregistre auprès du Launcher, expose son `WorkerStatus`
et s'arrête proprement en moins de 5 s sans jamais laisser d'insertion à moitié
faite,
So that il est supervisable et recyclable comme les autres workers du portail.

**Acceptance Criteria:**

**Given** le Launcher
**When** il interroge le worker
**Then** le worker expose un `WorkerStatus` (`IsRunning`, `IsActive`,
  `LastStartedAt`, `LastError`) — même contrat que
  `ServicesMicroScope.LauncherApiClient` — et s'enregistre dans
  `MQTTnetServices.dbo.WorkerSettings` (`WorkerName`, `IsActive`) (AC-FR14-5, AR-9)

**Given** un `Stop` du Launcher pendant un tick
**When** le worker s'arrête
**Then** le Fichier en cours finit ou reste dans `processing/` (jamais à moitié
  inséré) ; arrêt propre **< 5 s** (AC-FR14-6, NFR-9)

**Tests xUnit (TDD — écrits en premier, CC-1) :** `AC-FR14-5`, `AC-FR14-6`.

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7.

---

### Story 3.5 : Robustesse de la boucle worker

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
Docker de la Story 2.1 — conteneur arrêté / redémarré en cours de test (AR-12).

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
**When** le test d'intégration de bout en bout s'exécute sur le **harnais Docker**
  de la Story 2.1 (`Testcontainers.MsSql`, tables minimales ; ou `docker-compose`
  de test) — `[Trait("Category","Integration")]`, aucun SQL local (AR-12)
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
harnais Docker Story 2.1) ; test `*.errors.json` lisible (SM‑3) ; tests de perf
(NFR‑1/2 — la mesure < 200 ms exclut le temps de démarrage du conteneur).

**Critères transverses :** CC-1, CC-2, CC-3, CC-4, CC-5, CC-7. **AR-12** : E2E
dockerisé.
