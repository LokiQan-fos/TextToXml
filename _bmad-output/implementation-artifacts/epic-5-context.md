# Epic 5 Context: P89 — conversion Fichier → XML normalisé + XSD (Étape 1)

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Deliver Step 1 (Étape 1) of the P89 format: raw P89 Fichiers (`LP89_682_617_<nnn>`, index rotating 999 → 001) are converted into normalized XML validated by `P89.xsd`, with the generic `TextToXml` library left untouched. At the end of the epic, the Launcher-supervised worker `GpaoConvertP89` (MicroServices.sln, over the `P89Converter` library) empties the source folder into the XML folder (timestamped output) and the Done / error folders, recording one `IFichierJournal` entry per Fichier (LSI implementation: one `L_D_LOG_COMMANDE` row). P89 business mapping and persistence are out of scope (later, unplanned epic).

## Stories

- Story 5.0: Fichier journal — `IFichierJournal` + LSI implementation
- Story 5.1: `P89Converter` — raw P89 folder → timestamped normalized XML
- Story 5.2: Worker `GpaoConvertP89` under the `Launcher`

## Requirements & Constraints

- Per Fichier, in order: strict UTF-8 → Windows-1252 transcode, `Converter.Convert` with the embedded P89 descriptor, validation against the embedded `P89.xsd`, XML written as `<name>_<yyyyMMddHHmmss>.xml`, journal entry, then move of the source under the same suffix to Done (success) or error (failure).
- Reference Fichiers convert without Error and are XSD-valid; the message Ligne ends at 3442 characters (last Champ `LG24`, Position 3438, Size 4).
- `P89.xsd` must stay in sync with `P89.xml` (`gen.ps1 -Check -Format P89`); P60 XSD check must stay clean.
- Invalid UTF-8 byte or character absent from Windows-1252 ⇒ Fichier fails with an encoding reason; no replacement character, ever.
- Two conversions of the same name at different instants never overwrite each other (clock injected).
- Failure (conversion Error, XSD, encoding) ⇒ no XML, Fichier to error, journal entry with reasons (and OF if readable), each reason logged to `MQTTnetServices.Logs`; other Fichiers still processed (no fail-fast). Journal write fails ⇒ written XML deleted, Fichier left in source, retried next tick.
- `TextToXml` not modified; `P89Converter` references only `TextToXml` + `FichierJournal`.
- Journal contract: entry = `Commande`, `FichierName`, UTC `Instant`, optional `NumeroFichier` / `OF`, `Reasons` (none = success). LSI row: `"<NumeroFichier> — OK"` or `"<NumeroFichier> — REJETÉ : <reasons>"`, Paris-time `Date`, `NumLingot = 0`, `Trace = 1`, `User` = configured initiating server (machine name if empty, too long ⇒ fails at construction). No OF ⇒ no row; OF but no `NumeroFichier` ⇒ `FichierName` heads the message. Each `Record` is an autonomous write outside any business transaction; unreachable DB ⇒ throws, nothing written.
- Worker (AC-FR22-8): registered in `WorkerRegistry` and `workers.json`; startup is blocked, with a message naming the full key(s), by a `P89:*Path` folder that is unconfigured, relative or malformed, two `P89:*Path` keys naming the same folder, a blank `ConnectionStrings:AscoLSI`, or an `AscoLsiJournal:InitiatingServer` longer than `L_D_LOG_COMMANDE.User`. A missing or sub-second `P89:PollingInterval` falls back to 30 s.
- Cross-cutting: strict TDD (CC-1), English comments (CC-2), alphabetical ordering (CC-4), CC-7 for DB-touching stories; tests tagged `[Trait("AC", "FR2x-…")]`.

## Technical Decisions

- The move to Done happens after the XML write, so a crash between the two leaves the Fichier in the source folder, reconverted at the next tick.
- P89 Fichiers are UTF-8; the library stays frozen on Windows-1252, so the format transcodes before the library call.
- Real layout shifts every Position from `AnomaliePitsFour1` onward by +5 versus the supplied template (`Reserve9` Size 6); corrected template and XSD live in `Templates/`.
- Journal = interface (`src/FichierJournal`, zero references/packages); `src/AscoLsiJournal` (`AscoLsiFichierJournal`) references only `FichierJournal` + EF Core SqlServer, with a DbContext limited to `L_D_LOG_COMMANDE`. Its D8/D15 rules are a deliberate copy from `Kape22Persister` until P60 migrates.
- `src/P89Converter`: `net10.0` library, descriptor + XSD embedded, `TimeProvider` (local time) for the suffix; tests use temp folders and a fake journal (`Category=Unit`), at least 3 real fixtures (one with accents) plus faulty variants (invalid UTF-8, character absent from Windows-1252, truncated Ligne), and an XSD↔descriptor test.
- Worker `GPAO/ConvertP89` (MicroServices.sln): `Client : Publisher` on the `GpaoImportP60` model, references `P89Converter` + `AscoLsiJournal`; one `WorkerRegistry` line, one `workers.json` entry, one `ProjectReference` in `Launcher.csproj`. Config `GpaoConvertP89.json`: `ConnectionStrings:AscoLSI`, section `P89` (`SourcePath`, `XmlPath`, `DonePath`, `ErrorPath`, `PollingInterval`), `AscoLsiJournal:InitiatingServer`. Tests in `GPAO/ConvertP89.Tests` + `Launcher.Tests`. SVN commit is done by the user.
- Items deferred to the worker story, settled by Story 5.2: startup validation of the options (AC-FR22-8 above) — resolved; `RunTick` guarded by `RunTickCore` (any exception, including a missing `SourcePath`, is logged and the next tick runs) — resolved; `PollingInterval` fallback to 30 s in the worker — resolved; repeated Deferred outcomes — kept as a Warning on every tick, no attempt counter (`deferred-work.md`).

## Cross-Story Dependencies

- Strict order 5.0 → 5.1 → 5.2; 5.0, 5.1 and 5.2 are done.
- Story 5.2 rewired `GPAO/ConvertP89` onto `AscoLsiFichierJournal` (SVN r534): MicroServices.sln builds again and no longer references `Kape22Importer` from the P89 worker.
- Epics 1–4 are done and untouched; the project is reopened for this epic and re-closes at the Story 5.2 closure.
- Out of Epic 5: migrating P60 onto `IFichierJournal` (to be planned via correct-course); Publisher timer re-entrancy and the file-stability gate, both shared with P60 and to be fixed once.
