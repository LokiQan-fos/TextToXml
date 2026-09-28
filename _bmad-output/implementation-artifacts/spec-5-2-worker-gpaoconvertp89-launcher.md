---
title: 'Story 5.2 — Worker GpaoConvertP89 under the Launcher'
type: 'feature'
created: '2026-09-28'
status: 'done'
baseline_commit: 'f6b18b5f04cb5ffa8fb8601674b6167a5df67487'
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/implementation-artifacts/epic-5-context.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The iteration-1 worker `GpaoConvertP89` (MicroServices, SVN-added, uncommitted) still builds its journal from `Kape22Importer.Persistence` + EF, so `MicroServices.sln` no longer compiles, the P60 worker E2E test fails, and P89 does not run under the `Launcher`.

**Approach:** Rewire the `Client` onto `P89FolderConverter(P89Options, IFichierJournal, TimeProvider)` with an `AscoLsiFichierJournal`, validate the whole configuration at construction (AC-FR22-8, plus the 5.x startup defers), keep the contained tick, and redo its tests test-first.

## Boundaries & Constraints

**Always:** CC-1 (tests red before `Client.cs` changes), CC-2/CC-3, CC-4, CC-7 (no literal path or connection string). Model `GpaoImportP60`. Worker references `MicroService` + `P89Converter` + `AscoLsiJournal` only. A config error throws `InvalidOperationException` naming the full key (`P89:SourcePath`, `ConnectionStrings:AscoLSI`, `AscoLsiJournal:InitiatingServer`). `Actions` never throws. Unit tests: temp folders + a recording fake `IFichierJournal`, no database.

**Ask First:** any change under `src/` of TextToXml (`P89Converter`, `AscoLsiJournal`, `TextToXml`, `Kape22Importer`); any new NuGet package; any SVN commit (the user commits SVN).

**Never:** Publisher timer re-entrancy fix, file-stability gate, attempt counter / quarantine for repeated Deferred Fichiers, P60 migration onto `IFichierJournal` (all out of Epic 5 or shared with P60). Docker. Touching `P60/tmp/`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Complete config | 4 paths, interval 45 s, AscoLSI, InitiatingServer | options bound, journal built, `Frequency` 45 | N/A |
| Blank folder | `P89:<Key>` blank / absent | no worker | throws naming `P89:<Key>` |
| Two folders equal | e.g. `DonePath` == `SourcePath` (case / trailing separator ignored) | no worker (done files `LP89_*_<ts>` would be rescanned) | throws naming both keys |
| Blank AscoLSI | `ConnectionStrings:AscoLSI` absent | no worker | throws naming the key |
| InitiatingServer too long | > `L_D_LOG_COMMANDE.User` length | no worker | throws naming `AscoLsiJournal:InitiatingServer` |
| InitiatingServer blank | empty | machine name used | N/A |
| Interval absent / < 1 s | `P89:PollingInterval` missing | `Frequency` 30 | N/A |
| Missing source folder | `SourcePath` does not exist at tick | nothing processed | onError → `LogError`, next tick retries |
| Fichier in source | one invalid-UTF-8 `LP89_*` | one Rejected outcome relayed, one journal entry | N/A |
| Deferred Fichier | journal throws | outcome Deferred | `LogWarning` each tick (no counter, see Never) |

</frozen-after-approval>

## Code Map

MicroServices = `C:\Users\Administrateur\Documents\MicroServices` (SVN).

- `GPAO/ConvertP89/Client.cs` -- rewire: drop `Kape22Importer.Persistence`/EF usings and the `Func<AscoLsiDbContext>`; `ReadConfig` returns `WorkerConfiguration(IFichierJournal Journal, P89Options Options)`, builds `new AscoLsiFichierJournal(() => new AscoLsiJournalDbContext(UseSqlServer(cs)), configuration["AscoLsiJournal:InitiatingServer"])`, rethrows its `ArgumentException` as `InvalidOperationException` naming the full key (F-2 of 5.0). Adds the pairwise-distinct folder check (`Path.GetFullPath` + `TrimEndingDirectorySeparator`, `OrdinalIgnoreCase`). Keep `Frequency` fallback 30 s (settles 5.1 iter-3 F-1 at the worker), `RunTickCore` catch-all (settles 5.1 iter-2 RunTick guard + missing `SourcePath`), `Stop`/`Dispose`, `LogOutcome`.
- `GPAO/ConvertP89/GpaoConvertP89.csproj` -- add `ProjectReference ..\..\..\TextToXml\src\AscoLsiJournal\AscoLsiJournal.csproj` (CPM resolves via TextToXml `Directory.Packages.props`, as `Kape22Importer` does for P60).
- `GPAO/ConvertP89/GpaoConvertP89.json` -- remove `P89:InitiatingServer`; add `"AscoLsiJournal": { "InitiatingServer": "" }`; keep `MQTTnetServices` (Logs sink, as P60).
- `GPAO/ConvertP89.Tests/` -- `ClientConfigurationTests.cs` (new cases above), `RunTickCoreTests.cs` (fake journal instead of InMemory DbContext), csproj drops `Microsoft.EntityFrameworkCore.InMemory`.
- `Launcher/{WorkerRegistry.cs,workers.json,Launcher.csproj}`, `Launcher.Tests/WorkerRegistryTests.cs`, `MicroServices.sln` -- already modified (SVN `M`), correct; keep. Factory is lazy (`() => new Client(config)`), so the registry test with empty config stays valid.
- SVN hygiene -- `GPAO/ConvertP89/bin`, `obj` (and Tests' if added) are SVN-`A`: `svn revert -R` them (unversions only, files stay).
- Read-only: `src/P89Converter/P89FolderConverter.cs:31` (ctor), `:56-60` (`GetFiles` throws on missing source); `src/AscoLsiJournal/AscoLsiFichierJournal.cs:19,62-72` (ctor, `ResolveUser` throws `ArgumentException`).
- TextToXml docs: `README.md:84-87` (worker validates, distinct folders), `PRD.md` AC-FR22-8 (+ distinct folders), `deferred-work.md` (5.0 F-2, 5.1 iter-2 RunTick guard, iter-3 F-1/F-2, closure worker-compile → resolved; repeated-Deferred → kept, Warning per tick).

## Tasks & Acceptance

**Execution:**
- [x] `GPAO/ConvertP89.Tests/*` -- red first: `RecordingJournal : IFichierJournal`; tests per matrix row (config rows on `ReadConfig`, tick rows on `RunTickCore`); interval rows via a new `internal static int FrequencyFor(TimeSpan)`; drop EF InMemory -- CC-1, AC-FR22-8.
- [x] `GPAO/ConvertP89/{Client.cs,GpaoConvertP89.csproj,GpaoConvertP89.json}` -- green as in Code Map.
- [x] SVN -- revert the `bin`/`obj` adds.
- [x] `README.md`, `PRD.md`, `deferred-work.md` -- CC-5 sync.

**Acceptance Criteria:**
- AC-FR22-8: given `workers.json` and `WorkerRegistry`, then `GpaoConvertP89` is registered as `WorkerAdapter<GpaoConvertP89.Client>` with config `GpaoConvertP89.json`; given any config-error row of the matrix, when the `Client` is built, then it throws naming the key(s).
- Given a Launcher tick, when the source folder is missing or a Fichier is Deferred, then `Actions` does not throw and the next tick runs.
- Given the rewire, when `MicroServices.sln` builds, then 0 warnings and no reference to `Kape22Importer` from `GpaoConvertP89`.

## Design Notes

Validation stays in the worker, not in `P89FolderConverter` (library untouched): the keys and file name are worker concerns. Equal folders are rejected; nesting is not (`GetFiles` is top-level, pattern `LP89_*`).

## Verification

**Commands:**
- `dotnet build C:\Users\Administrateur\Documents\MicroServices\MicroServices.sln -warnaserror` -- 0 warnings.
- `dotnet test` on `GPAO/ConvertP89.Tests` and `Launcher.Tests` -- green.
- `dotnet test TextToXml.sln --filter Category=Unit` -- green; `--filter "Category=Integration" -m:1` -- `GpaoImportP60WorkerEndToEndTests` green again.
- `git diff f6b18b5 -- src` -- empty.

## Suggested Review Order

**Journal rewire (the fix that restores the build)**

- Converter now takes an `IFichierJournal`; no `Kape22Importer`, no EF context of its own.
  [`Client.cs:36`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L36)

- Journal built from AscoLSI + `AscoLsiJournal:InitiatingServer`; its ArgumentException renamed to the full key.
  [`Client.cs:97`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L97)

- Catch narrowed to the `initiatingServer` parameter (review P4).
  [`Client.cs:101`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L101)

**Startup validation (AC-FR22-8)**

- Entry point: every config error throws naming the key(s).
  [`Client.cs:56`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L56)

- Blank, relative and malformed folders refused in one helper (review P5).
  [`Client.cs:114`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L114)

- Pairwise-distinct folders: done files `LP89_*_<ts>` would otherwise be rescanned.
  [`Client.cs:88`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L88)

- Interval fallback to 30 s, settles the 5.1 PollingInterval defer at the worker.
  [`Client.cs:50`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L50)

**Contained tick and logging**

- Unchanged tick loop: nothing reaches the Publisher timer.
  [`Client.cs:156`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L156)

- Static seams: RunTickCore forwards the token; LogOutcome maps status to level (review P2/P3).
  [`Client.cs:220`](../../../MicroServices/GPAO/ConvertP89/Client.cs#L220)

**Project and config**

- References AscoLsiJournal, MicroService, P89Converter, alphabetical (review P1).
  [`GpaoConvertP89.csproj:25`](../../../MicroServices/GPAO/ConvertP89/GpaoConvertP89.csproj#L25)

- `InitiatingServer` moved from `P89` to its own `AscoLsiJournal` section.
  [`GpaoConvertP89.json:2`](../../../MicroServices/GPAO/ConvertP89/GpaoConvertP89.json#L2)

**Tests**

- One test per config row of the matrix.
  [`ClientConfigurationTests.cs:28`](../../../MicroServices/GPAO/ConvertP89.Tests/ClientConfigurationTests.cs#L28)

- Cancelled token, missing source, rejected and deferred ticks with a recording journal.
  [`RunTickCoreTests.cs:24`](../../../MicroServices/GPAO/ConvertP89.Tests/RunTickCoreTests.cs#L24)

- Log level and reasons per outcome status.
  [`LogOutcomeTests.cs:27`](../../../MicroServices/GPAO/ConvertP89.Tests/LogOutcomeTests.cs#L27)

**Docs (CC-5)**

- AC-FR22-8 extended: absolute, distinct folders; AscoLSI; InitiatingServer length.
  [`PRD.md:1053`](../planning-artifacts/PRD.md#L1053)

- P89 configuration paragraph.
  [`README.md:86`](../../README.md#L86)

- Two new defers (Connect outside try, shutdown budget overrun), both shared with P60.
  [`deferred-work.md:1302`](deferred-work.md#L1302)
