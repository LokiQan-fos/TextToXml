---
title: 'Story 3.0 — Kape22Importer devient une bibliothèque'
type: 'refactor'
created: '2026-09-09'
status: 'done'
review_loop_iteration: 0
baseline_commit: '48aa13c0bb0f1e38b3cf7c66940b4fceee9bca6e'
context:
  - _bmad-output/implementation-artifacts/epic-3-context.md
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** La correction de cap du 2026‑09‑09 aligne `Kape22Importer` sur le
`Launcher` du portail : le worker exécutable devient une classe `Client : Publisher`
dans `MicroServices.sln` (différé — objectif B, `deferred-work.md`). `src/Kape22Importer`
est aujourd'hui un projet `Microsoft.NET.Sdk.Worker` avec un `Program.cs` scaffold,
un `Worker` stub et une composition DI d'hôte (`AddKape22Startup`) qui n'ont plus
de raison d'être.

**Approach:** Convertir `src/Kape22Importer` en **bibliothèque** pure
(`Microsoft.NET.Sdk`) : supprimer le scaffold d'hôte et la glue DI, garder tous
les composants d'Épic 2/3 (déjà `public`) tels quels, ajuster les tests qui
testaient la glue supprimée. La vérification FR‑8 est déjà exposée en méthode
statique (`StartupCompatibilityCheck.Verify`) — rien à faire de ce côté.

## Boundaries & Constraints

**Always:**
- La lib ne référence en `ProjectReference` que `TextToXml` + `PortalSharedLibrary`
  (`AC-FR16-1`). Les `PackageReference` restent limités à `Microsoft.EntityFrameworkCore.SqlServer`
  + les abstractions `Microsoft.Extensions.*` réellement utilisées par le code
  (`Configuration.Abstractions` pour `IConfiguration`, `Logging.Abstractions` pour
  `ILogger<T>`).
- CC‑1 test‑first : chaque test supprimé dont le `AC` reste pertinent doit rester
  couvert par un test existant ; toute nouvelle assertion est écrite avant le code.
- CC‑2/CC‑3 (commentaires anglais, une phrase, au‑dessus du code), CC‑4 (ordre
  alphabétique), CC‑5 (vocabulaire glossaire).
- `dotnet build TextToXml.sln -warnaserror` reste propre ; toute la suite
  `Category=Unit` reste verte.
- `src/Kape22Importer/appsettings.json` **reste sur le disque** : `ImportOptionsTests`
  le lit par chemin (`RepoLayout`). Il n'est plus copié à la sortie du build.

**Ask First:**
- Toucher `Kape22Persister` / `Kape22FichierProcessor` (leurs ctors, leurs seams
  `IConfiguration`) — hors périmètre de cette story, garder `IConfiguration`
  (le `Client` le passera, modèle `OrdresFabricationSync`).
- Créer le projet `Client` ou toucher `MicroServices.sln` — c'est l'objectif B,
  différé.

**Never:**
- Ne pas recréer les fichiers 3.4 supprimés (`WorkerStatusProvider`,
  `WorkerControl`, `MqttNetServicesDbContext`, `WorkerSettingsRegistrationService`,
  et leurs tests).
- Ne pas introduire de nouvel abstraction de journalisation (`IImportJournal`) —
  YAGNI tant que le `Client` n'existe pas.
- Ne pas changer le comportement d'un composant d'Épic 2/3 : c'est un refactor de
  structure, zéro changement fonctionnel.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Build de la solution | `TextToXml.sln` après conversion | `Kape22Importer` compile en `Microsoft.NET.Sdk` ; 0 warning `-warnaserror` | build casse si un `using` implicite du SDK Worker manquait |
| `SolutionStructureTests` | csproj `Kape22Importer` | SDK == `Microsoft.NET.Sdk` ; `PackageReference` ⊆ liste autorisée mise à jour | test rouge sinon |
| `Category=Unit` complète | après suppression de `AddKape22Startup` / `AddAscoLsiPersistence` | verte ; `AC-FR8-1..4` toujours couverts par `StartupCompatibilityTests` ; `AC-FR11-8` / CC‑7 par `PersisterConfigurationTests` ; `AC-FR12-8` par `ImportOptionsTests` | — |
| `AcTraitCoverage` gate | assembly de tests | aucun `_AcFrX_Y` orphelin de `[Trait]` | build casse sinon |

</frozen-after-approval>

## Code Map

- `src/Kape22Importer/Kape22Importer.csproj` — `Sdk="Microsoft.NET.Sdk.Worker"` →
  `"Microsoft.NET.Sdk"`. Retirer `<PackageReference Include="Microsoft.Extensions.Hosting" />`
  et `<UserSecretsId>`. Ajouter `Microsoft.Extensions.Configuration.Abstractions`
  + `Microsoft.Extensions.Logging.Abstractions`. Garder EF SqlServer, les 2
  `EmbeddedResource` (`P60.xml`/`P60.xsd`), `InternalsVisibleTo`, les 2 `ProjectReference`.
- `Directory.Packages.props:15` — `<PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.11" />`
  devient mort → le retirer. Ajouter les 2 `PackageVersion` `Microsoft.Extensions.*.Abstractions`
  à une version 10.0.x cohérente avec EF Core 10.
- `src/Kape22Importer/Program.cs` — SUPPRIMER (scaffold hôte).
- `src/Kape22Importer/Worker.cs` — SUPPRIMER (`BackgroundService` stub `Task.Delay`).
- `src/Kape22Importer/StartupServiceCollectionExtensions.cs` — SUPPRIMER (`AddKape22Startup`).
- `src/Kape22Importer/StartupCompatibilityHostedService.cs` — SUPPRIMER (wrapper `IHostedService`
  autour de `StartupCompatibilityCheck.Verify`, déjà statique).
- `src/Kape22Importer/Persistence/PersistenceServiceCollectionExtensions.cs` — SUPPRIMER
  (`AddAscoLsiPersistence` ; le consommateur construit `new AscoLsiDbContext(new DbContextOptionsBuilder<…>().UseSqlServer(cs).Options)`).
- `src/Kape22Importer/Properties/launchSettings.json` (+ dossier `Properties/`) — SUPPRIMER.
- `src/Kape22Importer/appsettings.Development.json` — SUPPRIMER. `appsettings.json` RESTE.
- `src/Kape22Importer/StartupCompatibilityCheck.cs` — INCHANGÉ (`public static Verify(IModel, string)` +
  `StartupCompatibilityException`).
- `src/Kape22Importer/*.cs` + `Persistence/*.cs` restants — INCHANGÉS : tous les
  seams sont déjà `public` (`InboxScanner`, `IFichierProcessor`, `Kape22FichierProcessor`,
  `IFileSource`, `DirectoryFileSource`, `FichierEntry`, `FichierProcessingResult`,
  `ImportOptions`, `ImportResult`, `AscoLsiDbContext`, `Kape22Persister`, `Kape22Mapper`,
  `EmbeddedDescriptor`, …). `ParisTime` reste `internal` (via `InternalsVisibleTo`).
  Vérifier qu'aucun ne dépendait d'un `using` implicite du SDK Worker (peu probable :
  usings explicites constatés).
- `tests/TextToXml.Tests/SolutionStructureTests.cs:35-37` — `Kape22Importer_UsesTheWorkerSdk`
  → attendre `Microsoft.NET.Sdk` (renommer `Kape22Importer_UsesTheClassLibrarySdk`).
  `AllowedImporterPackages` (ligne 25) : retirer `Microsoft.Extensions.Hosting`,
  ajouter `Microsoft.Extensions.Configuration.Abstractions` + `Microsoft.Extensions.Logging.Abstractions`.
- `tests/Kape22Importer.Tests/StartupWiringTests.cs` — SUPPRIMER : son sujet
  (`AddKape22Startup`, ordre des `IHostedService`) n'existe plus ; `AC-FR8-4` reste
  couvert par `StartupCompatibilityTests.Verify_NominalDescriptorAndTable_DoesNotThrow_AcFr8_4`.
- `tests/Kape22Importer.Tests/AscoLsiConnectionConfigTests.cs` — SUPPRIMER : les
  tests `AddAscoLsiPersistence_*` sont caducs ; le scan CC‑7 « pas de chaîne en dur »
  est déjà dans `PersisterConfigurationTests.ImporterSource_HasNoHardCodedConnectionString_AcFr11_8`.
- `tests/Kape22Importer.Tests/Kape22Importer.Tests.csproj` — **modifié** (déviation) :
  la lib ne tire plus toute la pile `Microsoft.Extensions.Configuration.*` par
  transitivité (elle passait par `Microsoft.Extensions.Hosting`). Le projet de
  tests, qui construit `IConfiguration` (`AddJsonFile`, `AddInMemoryCollection`,
  `AddEnvironmentVariables`, `Get<ImportOptions>()`), déclare donc directement
  `Microsoft.Extensions.Configuration.Json` + `.EnvironmentVariables` + `.Binder`.
- `.github/workflows/ci.yml` — INCHANGÉ (`build` + `test`, pas de `dotnet run`).
- `src/Kape22Importer/Persistence/AscoLsiDbContext.cs` + `tests/Kape22Importer.Tests/PersisterConfigurationTests.cs`
  — commentaires d'en‑tête corrigés : ils citaient `AddAscoLsiPersistence` /
  `AscoLsiConnectionConfigTests`, supprimés (CC‑3 : un commentaire devenu faux).

## Tasks & Acceptance

**Execution:**
- [x] `tests/TextToXml.Tests/SolutionStructureTests.cs` — assertion SDK
  (`Microsoft.NET.Sdk`) et `AllowedImporterPackages` mises à jour **avant** le csproj
  (rouge constaté : 2 tests échouent, puis verts).
- [x] `tests/Kape22Importer.Tests/StartupWiringTests.cs` — supprimé.
- [x] `tests/Kape22Importer.Tests/AscoLsiConnectionConfigTests.cs` — supprimé.
- [x] `src/Kape22Importer/Kape22Importer.csproj` — SDK `Microsoft.NET.Sdk` ;
  `Microsoft.Extensions.Hosting` + `UserSecretsId` retirés ; abstractions
  `Configuration` + `Logging` ajoutées.
- [x] `Directory.Packages.props` — `Microsoft.Extensions.Hosting` retiré ; ajoutés
  `Configuration.Abstractions`, `Logging.Abstractions` (10.0.9, aligné EF Core 10),
  plus `Configuration.Json` / `.EnvironmentVariables` / `.Binder` (test‑only).
- [x] `src/Kape22Importer/` — `Program.cs`, `Worker.cs`,
  `StartupServiceCollectionExtensions.cs`, `StartupCompatibilityHostedService.cs`,
  `Persistence/PersistenceServiceCollectionExtensions.cs`, `Properties/launchSettings.json`,
  `appsettings.Development.json` supprimés (`git rm`). `appsettings.json` conservé.
- [x] Build vert : aucun `using` implicite manquant dans un fichier survivant
  (usings explicites partout, comme anticipé).
- [x] `tests/Kape22Importer.Tests/Kape22Importer.Tests.csproj` — 3 `PackageReference`
  `Microsoft.Extensions.Configuration.*` ajoutées (déviation documentée ci‑dessus).

**Acceptance Criteria:**
- Given la solution convertie, when `dotnet build TextToXml.sln -warnaserror`, then
  build réussi, 0 warning, `Kape22Importer` produit une DLL de bibliothèque (pas
  d'`.exe`).
- Given la lib, when j'inspecte son `.csproj`, then SDK = `Microsoft.NET.Sdk`,
  aucune référence à `Microsoft.Extensions.Hosting` ni à un package ASP.NET Core,
  `ProjectReference` = { `TextToXml`, `PortalSharedLibrary` }.
- Given la suite de tests, when `dotnet test TextToXml.sln --filter Category=Unit`,
  then verte, et `AC-FR8-1..4`, `AC-FR11-8`, `AC-FR12-8`, CC‑7 ont chacun au moins
  un test vert portant leur `[Trait]`.
- Given `AcTraitCoverageTests`, when il s'exécute, then aucun offender (aucun test
  `_AcFrX_Y` sans `[Trait]`).
- Given `git grep -n "AddKape22Startup\|AddAscoLsiPersistence\|StartupCompatibilityHostedService"`
  sur `src/` et `tests/`, when exécuté, then zéro résultat.

## Verification

**Commands:**
- `dotnet build TextToXml.sln -warnaserror` — expected: `Build succeeded`, 0 warning.
- `dotnet test TextToXml.sln --filter Category=Unit` — expected: toutes vertes,
  0 failed (≈ 480 tests, moins celles des 2 fichiers supprimés).
- `dotnet build src/Kape22Importer/Kape22Importer.csproj -getProperty:OutputType` —
  expected: `Library` (ou absence d'`Exe`).
- `git grep -nE "AddKape22Startup|AddAscoLsiPersistence|StartupCompatibilityHostedService|Microsoft.Extensions.Hosting" -- src tests Directory.Packages.props`
  — expected: aucun résultat.

## Suggested Review Order

**Le pivot : projet worker → bibliothèque**

- Point d'entrée : le SDK passe de `Microsoft.NET.Sdk.Worker` à la lib pure ; `Hosting` disparaît, les abstractions `Configuration`/`Logging` que le code utilise en paramètre deviennent explicites.
  [`Kape22Importer.csproj:1`](../../src/Kape22Importer/Kape22Importer.csproj#L1)
- Le graphe de composition d'hôte (`AddKape22Startup` → `AddAscoLsiPersistence` + `AddHostedService`) est entièrement supprimé ; le consommateur (le futur `Client`) construira le contexte à la main.
  [`AscoLsiDbContext.cs:10`](../../src/Kape22Importer/Persistence/AscoLsiDbContext.cs#L10)

**Conséquence sur les dépendances**

- La lib ne tire plus la pile `Microsoft.Extensions.Configuration.*` par transitivité (via `Hosting`) ; le projet de tests, qui construit `IConfiguration`, la déclare directement.
  [`Kape22Importer.Tests.csproj:15`](../../tests/Kape22Importer.Tests/Kape22Importer.Tests.csproj#L15)
- Versions centralisées : abstractions à `10.0.9` (plancher imposé par EF Core 10) ; les 3 paquets Configuration test-only groupés dans le même commentaire.
  [`Directory.Packages.props:16`](../../Directory.Packages.props#L16)

**Garde-fous de structure (tests d'architecture)**

- L'assertion SDK bascule sur `Microsoft.NET.Sdk` (rouge d'abord, CC-1) ; `AllowedImporterPackages` remplace `Hosting` par les 2 abstractions.
  [`SolutionStructureTests.cs:42`](../../tests/TextToXml.Tests/SolutionStructureTests.cs#L42)
- Nouveau garde : `Kape22Importer` ne déclare aucun `<FrameworkReference>` (couvre l'AC « aucun package ASP.NET Core », que l'allowlist de `PackageReference` ne voyait pas).
  [`SolutionStructureTests.cs:108`](../../tests/TextToXml.Tests/SolutionStructureTests.cs#L108)

**Périphérie**

- Suppressions : `Program.cs`, `Worker.cs`, `StartupServiceCollectionExtensions.cs`, `StartupCompatibilityHostedService.cs`, `Persistence/PersistenceServiceCollectionExtensions.cs`, `Properties/launchSettings.json`, `appsettings.Development.json`, et les tests `StartupWiringTests` / `AscoLsiConnectionConfigTests` (sujets caducs).
- `PersisterConfigurationTests.cs:14` — commentaire d'en-tête corrigé (citait des symboles supprimés).

## Review Findings

Code review 2026-09-09 (adversarial Tech Lead pass, 4 layers). Build `-warnaserror`
vert (0 warning), `Category=Unit` verte (480/480), `git grep` des symboles supprimés
propre, CC-2/CC-3/CC-4 sans violation dure (ordre alphabétique intact, aucun
commentaire trailing, anglais, pas de liste numérotée). Revue refusée sur les 6
écarts ci-dessous ; **tous corrigés et revérifiés le 2026-09-09** (build + 480 tests
verts).

- [x] [Review][Patch] Item `<None Include="appsettings.json" />` hors périmètre, redondant avec le glob `None` par défaut du SDK, commentaire fragmentaire (« The default Import section values. ») — 3 lignes supprimées, comportement build identique (vérifié : pas d'`.exe`, fichier non copié). Le Code Map du spec liste exhaustivement les modifs csproj sans cet item. [src/Kape22Importer/Kape22Importer.csproj]
- [x] [Review][Patch] Bloc `Logging:LogLevel` mort dans `appsettings.json` (dont `Microsoft.Hosting.Lifetime`) — `appsettings.Development.json` a été supprimé pour cette raison exacte ; nettoyage incohérent. Bloc retiré, `Import` conservé. [src/Kape22Importer/appsettings.json]
- [x] [Review][Patch] Commentaire imprécis : « appsettings.Test.json » alors que le test unitaire AC-FR12-8 `ImportOptions_BindEveryValueFromShippedAppsettings` lit `src/Kape22Importer/appsettings.json`. Commentaire réécrit (rôle de chaque paquet). [tests/Kape22Importer.Tests/Kape22Importer.Tests.csproj:14]
- [x] [Review][Patch] README : « Consommée par le worker exécutable. » affirmé comme fait présent alors que la note suivante dit le worker différé/inexistant ; cible EF → `AscoLSI` + double journalisation restaurées, `Kape22Persister` nommé. [README.md:8]
- [x] [Review][Patch] deferred-work.md : entrées « `Kape22Importer.Tests` has no executing test » et « Worker template dead code — `Program.cs` / `Worker.cs` » barrées `~~…~~ **RÉSOLU 2026-09-09 (Story 3.0)**`. [_bmad-output/implementation-artifacts/deferred-work.md]
- [x] [Review][Patch] deferred-work.md : bullet symétrique ajouté pour le portage du gate FR-8 (`StartupCompatibilityCheck.Verify`, plus aucun appelant de prod ici) vers le `Client`, renvoyant à spec-3-4. [_bmad-output/implementation-artifacts/deferred-work.md]
- [x] [Review][Defer] Garde de non-régression sur les paquets *résolus* de `Kape22Importer` (pas seulement l'allowlist du `.csproj` littéral) — une réintroduction transitive de `Microsoft.Extensions.Hosting` via `PortalSharedLibrary` passerait tous les tests. Pré-existant (l'allowlist csproj-only précède cette story). [tests/TextToXml.Tests/SolutionStructureTests.cs]
- [x] [Review][Defer] Assertion automatisée « sortie = bibliothèque, pas d'`.exe` » (`OutputType`) absente ; `Kape22Importer_UsesTheClassLibrarySdk` ne teste que l'attribut SDK. [tests/TextToXml.Tests/SolutionStructureTests.cs:108]
