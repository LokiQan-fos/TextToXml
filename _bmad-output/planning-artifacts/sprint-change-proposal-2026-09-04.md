---
type: sprint-change-proposal
date: 2026-09-04
trigger: gate de readiness Épic 2 (R-1)
scope: Moderate
status: approuvé (2026-09-04) — éditions §4.1–4.11 appliquées à epics.md + PRD.md FR-11 ; scripts/schema/ générés + validés depuis AFV004-LSI
---

# Sprint Change Proposal — Abandon du harnais Docker (AR-12)

> Langue : **français**, en cohérence avec `PRD.md` / `epics.md` (override
> assumé de `document_output_language`).

## Section 1 — Résumé du problème

**Déclencheur.** Le gate de readiness de l'Épic 2 (2026-09-04, avant démarrage
de la Story 2.1) a cherché à vérifier le risque **R-1** — capacité de la cible à
exécuter des conteneurs Linux pour `Testcontainers.MsSql` (AR-12).

**Constat.** La machine cible (Windows Server 2019, celle que l'AR-12 nomme
explicitement) ne peut pas exécuter l'image `mcr.microsoft.com/mssql/server` :

| Vérification | Résultat |
|---|---|
| `docker info` | OSType = **windows**, isolation = **process**, Engine Community windows/amd64 |
| WSL | `wsl.exe` introuvable — pas de WSL2 |
| Docker Desktop / bascule conteneurs Linux | absent |
| `docker compose` v2 | absent |
| contexte Docker Linux | aucun |

`mcr.microsoft.com/mssql/server` est Linux-only ; `Testcontainers.MsSql` exige un
démon Linux. R-1 était marqué « ✅ résolu » sans preuve — le constat le
contredit.

**Catégorie.** Limitation technique découverte + simplification d'architecture
décidée par le donneur d'ordre (« je ne suis pas un inconditionnel de Docker,
c'était une idée initiale »).

**Piège complémentaire identifié.** L'AC de la Story 2.1 prévoit un *skip
propre* « si aucun démon Docker ». Or un démon Docker **Windows répond** : le
ping Testcontainers réussit, puis la création du conteneur échoue sur
incompatibilité de plateforme — **échec rouge dur, pas un skip**. La garde telle
que spécifiée ne protège pas.

## Section 2 — Analyse d'impact

### Impact épic

| Épic | Impact |
|---|---|
| Épic 1 | **Aucun.** Terminé, aucun test d'intégration exécuté (1 placeholder `Skipped` dans `Kape22Importer.Tests`). |
| Épic 2 | **Direct.** AR-12 (énoncé transverse), Story 2.1 (harnais), Story 2.8 (référence harnais). |
| Épic 3 | **Indirect.** Stories 3.3, 3.5, 3.6 référencent « le harnais Docker de la Story 2.1 ». La Story 3.5 (`AC-FR15-3` base injoignable / `AC-FR15-4` recycle) suppose « conteneur arrêté / redémarré en cours de test » — mécanisme à remplacer. |

Aucun épic rendu obsolète, aucun épic nouveau, ordre inchangé.

### Conflits d'artefacts

| Artefact | Sections | Nature |
|---|---|---|
| `epics.md` | AR-12 (L115-134) | **Réécriture complète** |
| `epics.md` | Overview (L16-18) | « dockerisés » → SQL local |
| `epics.md` | NFR-8 map (L223), note AR-12 (L226-230) | retrait `Testcontainers.MsSql` |
| `epics.md` | CC-1 (L162) | « harnais Docker » → « harnais de base de test » |
| `epics.md` | Table risques R-1 (L236), R-3 (L238) | mise à jour résolution |
| `epics.md` | Table Setup (L252) | « harnais de test Docker » → SQL local |
| `epics.md` | Story 1.1 AC (L335) | mention « démon Docker » — story **done**, suivi mineur |
| `epics.md` | **Story 2.1** (titre + ACs harnais + tests) | **Réécriture ciblée** |
| `epics.md` | Story 2.8 (L991-993, L1025-1026) | mise à jour parenthèse harnais |
| `epics.md` | Stories 3.3 (L1168-1172), 3.5 (L1233-1234), 3.6 (L1258-1289) | « harnais Docker » → SQL local ; mécanisme injoignable/recycle 3.5 |
| `PRD.md` | FR-11 note (L648) | déjà permissif (« LocalDB / conteneur SQL ») — **clarification optionnelle**, pas bloquant |

**PRD §4.4 / NFR :** aucune mention de `Testcontainers` — **aucune modif PRD
obligatoire**. `Testcontainers.MsSql` n'existait que dans `epics.md`.

### Impact technique

- **Prérequis supprimé** : WSL2 / Hyper-V / bascule démon Docker sur Windows
  Server 2019. Plus aucune dépendance Docker en développement.
- **Prérequis ajouté** : une instance SQL Server (Developer Edition, gratuite)
  joignable là où la catégorie `Integration` doit tourner (poste dev + runner CI
  qui l'exécute).
- **Package retiré** : `Testcontainers.MsSql` (jamais ajouté — Épic 2 pas
  démarrée). Éventuel ajout `Respawn` pour le reset ciblé.
- **Nouveau versionné** : `scripts/schema/*.sql` (idempotents), généré depuis
  `AFV004-LSI` via `sqlcmd` (R-3).

## Section 3 — Voie recommandée

**Option retenue : ajustement direct (Option 1)** — modifier AR-12 + les ACs des
stories concernées, dans l'Épic 2 non démarrée.

| Option | Verdict | Raison |
|---|---|---|
| 1 — Ajustement direct | **Retenue** | Épic 2 pas commencée ; aucun code à jeter ; effort **faible** ; risque **faible**. |
| 2 — Rollback | N/A | Rien de livré à annuler. |
| 3 — Revue MVP | N/A | Périmètre MVP inchangé — c'est un choix d'outillage de test, pas de fonctionnalité. |

**Effort** : faible (édition de doc + le harnais réécrit en Story 2.1, de
complexité comparable voire moindre). **Risque** : faible. **Impact planning** :
nul, voire négatif (pas d'install WSL2/Hyper-V).

## Section 4 — Propositions de modification détaillées

### 4.1 — `epics.md` · AR-12 (réécriture complète)

**OLD** (L115-134) :

```
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
```

**NEW** :

```
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
```

### 4.2 — `epics.md` · Overview (L16-18)

**OLD** :
```
Tous les tests d'intégration base de données sont **dockerisés** (Testcontainers /
`docker-compose` de test, schéma minimal) — voir **AR-12**.
```
**NEW** :
```
Tous les tests d'intégration base de données tournent contre une **instance SQL
Server locale** (Developer Edition) avec un **schéma minimal versionné**
(`scripts/schema/`, généré depuis la base réelle) — voir **AR-12**.
```

### 4.3 — `epics.md` · NFR-8 map (L223) et note AR-12 (L226-230)

**OLD (L223)** :
```
| NFR-8 | 1.1, 2.1 | inspection des versions de packages (dont `Testcontainers.MsSql`) |
```
**NEW** :
```
| NFR-8 | 1.1, 2.1 | inspection des versions de packages (`Microsoft.EntityFrameworkCore` 10.0.x, `Serilog.Sinks.MSSqlServer`) |
```

**OLD (L226-230)** :
```
**AR-12 (tests d'intégration dockerisés)** — harnais central : **Story 2.1**
(`Testcontainers.MsSql` + script d'init tables minimales + fallback
`docker-compose`) ; réutilisé par **2.8** (persistance transactionnelle),
**3.3** (écritures de logs), **3.5** (`AC-FR15-3/4`), **3.6** (E2E 10 fichiers).
Tests unitaires (`TextToXml`, `Kape22Mapper` hors persistance) sans Docker.
```
**NEW** :
```
**AR-12 (tests d'intégration base de données locale)** — harnais central :
**Story 2.1** (fixture assembly SQL Server + `scripts/schema/` idempotents généré
depuis `AFV004-LSI` + double régime d'isolation `TransactionScope` / reset) ;
réutilisé par **2.8** (persistance transactionnelle), **3.3** (écritures de
logs), **3.5** (`AC-FR15-3/4`), **3.6** (E2E 10 fichiers). Tests unitaires
(`TextToXml`, `Kape22Mapper` hors persistance) sans SQL Server.
```

### 4.4 — `epics.md` · CC-1 (L162)

**OLD** : `… et l'infrastructure de test elle‑même (harnais Docker Story 2.1).`
**NEW** : `… et l'infrastructure de test elle‑même (harnais de base de test Story 2.1).`

### 4.5 — `epics.md` · Table des risques

**OLD (R-1, L236)** :
```
| R-1 | Runner CI (Windows Server 2019) : capacité conteneurs Linux (`mssql/server`) | catégorie `Integration` non exécutée en CI | **Tranché (utilisateur) :** l'environnement cible gère les conteneurs ; à défaut les tests s'appuient sur Testcontainers / instance locale selon les besoins. Story 2.1 : garde le skip propre si pas de démon Docker. | ✅ résolu |
```
**NEW** :
```
| R-1 | Exécution des tests `Integration` sur cible Windows Server 2019 sans conteneurs Linux (pas de WSL2/Hyper-V) | catégorie `Integration` non exécutable | **Tranché (utilisateur, 2026-09-04) :** abandon de Docker/Testcontainers. Tests contre une **instance SQL Server locale** (Developer Edition) + `scripts/schema/` versionné ; skip propre si aucune instance joignable (détection connexion). CI : conteneur Linux `mssql/server` sur runner Linux, ou SQL natif sur runner Windows. Voir Sprint Change Proposal 2026-09-04. | ✅ résolu |
```

**OLD (R-3, L238)** :
```
| R-3 | `test-schema.sql` rédigé à la main → dérive vs `L_D_KAPE22` réelle | tests d'intégration verts contre un faux schéma (contre‑métrique SM‑3) | **Confirmé (utilisateur) :** base `AFV004-LSI` accessible sur le serveur cible pour lire les 92 colonnes. Story 2.1 : script généré depuis `AFV004-LSI` (provenance datée), + test « modèle EF ⟺ `test-schema.sql` ». | ✅ accès confirmé |
```
**NEW** :
```
| R-3 | `scripts/schema/*.sql` dérive vs schéma réel de `AscoLSI` | tests d'intégration verts contre un faux schéma (contre‑métrique SM‑3) | **Confirmé (utilisateur) :** base `AFV004-LSI` accessible via `sqlcmd` (ODBC 17) depuis le poste. Story 2.1 : `scripts/schema/` **générés** depuis `AFV004-LSI` (`sys.columns`, en‑tête serveur/base/date), + test « modèle `AscoLsiDbContext` ⟺ `scripts/schema/` ». Connexion (hôte, instance, base, compte) à fournir avant démarrage 2.1. | ⚠️ accès confirmé, connexion à câbler |
```

### 4.6 — `epics.md` · Table Setup (L252)

**OLD** :
```
| `AscoLsiDbContext` database‑first (`L_D_KAPE22`, `L_D_LOG_COMMANDE`) + **harnais de test Docker** (Testcontainers `MsSql` + script d'init tables minimales, AR-12) | 2.1 |
```
**NEW** :
```
| `AscoLsiDbContext` database‑first (`L_D_KAPE22`, `L_D_LOG_COMMANDE`) + **harnais de test SQL Server local** (`scripts/schema/` générés depuis `AFV004-LSI` + double régime d'isolation, AR-12) | 2.1 |
```

### 4.7 — `epics.md` · Story 2.1 (titre + ACs harnais + tests)

**Titre OLD** : `### Story 2.1 : Entités EF (database‑first) & harnais de test Docker`
**Titre NEW** : `### Story 2.1 : Entités EF (database‑first) & harnais de test SQL Server local`

**« I want » OLD** :
```
I want un `AscoLsiDbContext` figeant les tables cibles d'après Annexe C (sans
migration) **et** un harnais de tests d'intégration dockerisé (Testcontainers
SQL Server, schéma minimal),
```
**NEW** :
```
I want un `AscoLsiDbContext` figeant les tables cibles d'après Annexe C (sans
migration) **et** un harnais de tests d'intégration contre une instance SQL
Server locale (schéma minimal versionné, généré depuis la base réelle),
```

**Bloc harnais — OLD** (L703-722) :
```
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
```
**NEW** :
```
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
```

**Bloc « Tests xUnit » — OLD** (L724-729) :
```
**Tests xUnit (TDD — écrits en premier, CC-1) :** test « ensemble des colonnes
NOT NULL == Annexe C.1 » ; test de forme d'entité (types CLR) ; test « pas de
migration / `Id` identity » ; test « connexions lues de `IConfiguration` » ;
**test « modèle `AscoLsiDbContext` ⟺ colonnes/types de `test-schema.sql` »** (R-3) ;
**test d'intégration fumée** : la fixture Docker démarre, `test-schema.sql` passe,
un `INSERT`/`SELECT` round‑trip sur `L_D_KAPE22` réussit dans le conteneur.
```
**NEW** :
```
**Tests xUnit (TDD — écrits en premier, CC-1) :** test « ensemble des colonnes
NOT NULL == Annexe C.1 » ; test de forme d'entité (types CLR) ; test « pas de
migration / `Id` identity » ; test « connexions lues de `IConfiguration` » ;
**test « modèle `AscoLsiDbContext` ⟺ colonnes/types de `scripts/schema/` »** (R-3) ;
**test d'intégration fumée** : la fixture se connecte, `scripts/schema/` s'applique
sans erreur, un `INSERT`/`SELECT` round‑trip sur `L_D_KAPE22` réussit — sous
`TransactionScope` (rollback), la base reste vide après le test.
```

**Critères transverses — OLD** : `… CC-5, CC-7. **AR-12** : harnais de test dockerisé.`
**NEW** : `… CC-5, CC-7. **AR-12** : harnais de test SQL Server local.`

### 4.8 — `epics.md` · Story 2.8 (L991-993, L1025-1026)

**OLD (L991-993)** :
```
*(Tests d'intégration EF — via le **harnais Docker de la Story 2.1**
(`Testcontainers.MsSql`, tables minimales `L_D_KAPE22` + `L_D_LOG_COMMANDE`),
`[Trait("Category","Integration")]`. Aucun SQL local. AR-12.)*
```
**NEW** :
```
*(Tests d'intégration EF — via le **harnais de la Story 2.1** (instance SQL
Server de test, tables minimales `L_D_KAPE22` + `L_D_LOG_COMMANDE`),
`[Trait("Category","Integration")]`. `AC-FR11-3`/`AC-FR11-5` (frontières de
transaction) et `AC-FR11-6`/`AC-FR11-7` (garde‑fou anti‑doublon) tournent en
**commit réel + reset de données**, pas sous `TransactionScope` ambiant. AR-12.)*
```

**OLD (L1025-1026)** : `… CC-5, CC-7. **AR-12** : tests d'intégration dockerisés.`
**NEW** : `… CC-5, CC-7. **AR-12** : tests d'intégration sur SQL Server local.`

### 4.9 — `epics.md` · Épic 3 (Stories 3.3, 3.5, 3.6)

Remplacer partout « harnais Docker de la Story 2.1 » / « Testcontainers.MsSql » /
« tests d'intégration dockerisés » / « aucun SQL local » par la formulation SQL
Server local d'AR-12. Points spécifiques :

- **Story 3.3 (L1168-1172)** : « … sont en catégorie `Integration` sur le
  **harnais SQL Server local** de la Story 2.1 (AR-12). » / « **AR-12** : tests
  d'intégration sur SQL Server local. »
- **Story 3.5 (L1233-1234)** — **OLD** :
  ```
  `AC-FR15-3` (base injoignable) et `AC-FR15-4` (recycle) s'appuient sur le harnais
  Docker de la Story 2.1 — conteneur arrêté / redémarré en cours de test (AR-12).
  ```
  **NEW** :
  ```
  `AC-FR15-3` (base injoignable) et `AC-FR15-4` (recycle) s'appuient sur le
  harnais SQL Server local de la Story 2.1 — la panne est simulée par une chaîne
  de connexion pointant un hôte/port mort (ou un toggle de la fixture), pas par
  l'arrêt d'un conteneur (AR-12).
  ```
- **Story 3.6 (L1258-1289)** : E2E `[Trait("Category","Integration")]` sur
  l'instance SQL Server de test ; « aucun SQL local » retiré ; la note perf
  NFR-1/2 « exclut le temps de démarrage du conteneur » devient « exclut le
  temps de connexion / d'application du schéma ».

### 4.10 — `PRD.md` · FR-11 note (L648) — **optionnel, non bloquant**

**OLD** : `*(intégration EF — LocalDB / conteneur SQL, schéma miroir …)*`
**NEW** : `*(intégration EF — instance SQL Server de test, schéma miroir …)*`

### 4.11 — Suivi mineur (hors édition immédiate)

- `tests/Kape22Importer.Tests/IntegrationHarnessTests.cs` : le message `Skip`
  (« Docker-backed SQL Server harness … ») est réécrit **dans la Story 2.1**.
- `epics.md` Story 1.1 AC (L335) « `Category=Integration` requiert un démon
  Docker » : story **done** — corriger au fil de l'eau (« requiert une instance
  SQL Server ») lors du prochain passage, non bloquant.
- `README.md` : la section « Prérequis » (bloc Docker) est mise à jour **dans la
  Story 2.1** (l'AC socle « documenter dans le README » s'applique).

## Section 5 — Handoff

**Classe de changement : Moderate** — réorganisation de contrat de stories, Épic
2 non démarrée, aucun code impacté.

| Rôle | Responsabilité |
|---|---|
| **PM + Lead Architecte** | Valider le nouvel énoncé AR-12 (§4.1) et le double régime d'isolation. Appliquer les éditions §4.1-4.10 à `epics.md` (+ PRD optionnel). |
| **Scrum Master** | Confirmer que `sprint-status.yaml` reste inchangé (mêmes stories, mêmes clés — voir ci-dessous). |
| **Utilisateur (donneur d'ordre)** | Fournir la connexion `AFV004-LSI` (hôte/instance, base(s), compte) pour que le dev génère `scripts/schema/` en Story 2.1. Décider où tourne `Category=Integration` en CI (runner Linux avec conteneur `mssql/server`, ou runner Windows avec SQL natif). |
| **Dev (Story 2.1)** | Générer `scripts/schema/*.sql` depuis `AFV004-LSI` via `sqlcmd` ; bâtir la fixture assembly + les deux régimes d'isolation ; mettre à jour README + message `Skip`. |

**`sprint-status.yaml` :** aucune modification — les 8 stories de l'Épic 2
gardent leurs identifiants et leur statut `backlog`. (La clé
`2-1-entités-ef-database-first-harnais-de-test-docker` contient « docker » dans
son slug ; le renommer casserait le suivi pour un gain nul — **laissé tel
quel**, le titre affiché sera corrigé, pas la clé.)

**Critères de succès :**
1. `epics.md` ne référence plus Docker/Testcontainers (hors historique/note de
   décision).
2. Story 2.1 démarrable : connexion `AFV004-LSI` câblée, cible CI `Integration`
   décidée.
3. `dotnet test --filter Category=Unit` reste vert et sans dépendance SQL
   (inchangé, 187 tests Épic 1).

## Section 6 — Points restés ouverts

- ~~**R-3 connexion**~~ **Résolu (2026-09-04)** : connexion `sa` fournie,
  `AFV004-LSI` joignable via `sqlcmd` (ODBC 17) depuis le poste. `AscoLSI` **et**
  `MQTTnetServices` sont sur **la même instance** (SQL Server 2012 Enterprise).
  `scripts/schema/01-ascolsi-tables.sql` + `02-mqtt-tables.sql` **générés,
  validés** (exécution idempotente dans une base jetable : 92 / 8 / 7 / 2
  colonnes, NOT NULL conformes, round-trip identity OK). Secrets **non commités**
  — la chaîne de test ira dans `appsettings.Test.json` (gitignore) / User
  Secrets / env, câblée en Story 2.1.
- **Cible CI `Integration`** : runner Linux (conteneur `mssql/server`) vs runner
  Windows (SQL natif). Décision d'infra, sans impact sur le code des tests
  (mêmes `scripts/schema/`).
- ~~**Bug spec — PRD Annexe C.1 / C.2 : longueurs `nchar`/`nvarchar` doublées**~~
  **Corrigé (2026-09-04).** L'Annexe C listait le nombre d'**octets** (24, 12, 2,
  14, 26, 100, 100, 200) au lieu de caractères. Réel : `OF nchar(12)`,
  `Coulee nvarchar(6)`, `Type nchar(1)`, `Nuance nvarchar(7)`,
  `Client nvarchar(13)`, `Commande/User nvarchar(50)`, `WorkerName nvarchar(100)`.
  Vérifié : le `Size` de **chaque** `<value>` string de `P60.xml` est **égal** à
  la longueur réelle de la colonne homonyme (aucun débordement, 0/57).
  `PRD.md` Annexe A.2 + C.1 + C.2 + C.3 corrigées, note explicative ajoutée.
  Les types `int` de l'Annexe C sont corrects (seules les longueurs texte
  l'étaient). **Story 2.5** : `HasMaxLength` depuis `scripts/schema/`.
