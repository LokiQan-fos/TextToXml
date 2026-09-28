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
- Worker: registered in `WorkerRegistry` and `workers.json`; any unconfigured `P89:*Path` key blocks startup with a message naming the key.
- Cross-cutting: strict TDD (CC-1), English comments (CC-2), alphabetical ordering (CC-4), CC-7 for DB-touching stories; tests tagged `[Trait("AC", "FR2x-…")]`.

## Technical Decisions

- P89 Fichiers are UTF-8; the library stays frozen on Windows-1252, so the format transcodes before the library call.
- Real layout shifts every Position from `AnomaliePitsFour1` onward by +5 versus the supplied template (`Reserve9` Size 6); corrected template and XSD live in `Templates/`.
- Journal = interface (`src/FichierJournal`, zero references/packages); `src/AscoLsiJournal` (`AscoLsiFichierJournal`) references only `FichierJournal` + EF Core SqlServer, with a DbContext limited to `L_D_LOG_COMMANDE`. Its D8/D15 rules are a deliberate copy from `Kape22Persister` until P60 migrates.
- `src/P89Converter`: `net10.0` library, descriptor + XSD embedded, `TimeProvider` (local time) for the suffix; tests use temp folders and a fake journal (`Category=Unit`), with an XSD↔descriptor test.
- Worker `GPAO/ConvertP89` (MicroServices.sln): `Client : Publisher` on the `GpaoImportP60` model, references `P89Converter` + `AscoLsiJournal`; one `WorkerRegistry` line, one `workers.json` entry, one `ProjectReference` in `Launcher.csproj`. Config `GpaoConvertP89.json`: `ConnectionStrings:AscoLSI`, section `P89` (`SourcePath`, `XmlPath`, `DonePath`, `ErrorPath`, `PollingInterval`), `AscoLsiJournal:InitiatingServer`. Tests in `GPAO/ConvertP89.Tests` + `Launcher.Tests`. SVN commit is done by the user.
- Items deferred to the worker story: validate options at startup (empty paths, overlapping folders, full key name `AscoLsiJournal:InitiatingServer` in the error); guard `RunTick` (unexpected exception ⇒ log + next tick, missing/unreachable `SourcePath`); set the `PollingInterval` default (README says 30 s, library default is zero) or move it into worker options; decide how repeated Deferred outcomes surface.

## Cross-Story Dependencies

- Strict order 5.0 → 5.1 → 5.2; 5.0 and 5.1 are done, 5.2 is next.
- The iteration-1 `GPAO/ConvertP89` worker (SVN-added, never committed) no longer compiles (still uses `Kape22Importer.Persistence` + EF), which breaks the MicroServices.sln build and the P60 worker E2E integration test until Story 5.2 rewires it onto `AscoLsiFichierJournal`.
- Out of Epic 5: migrating P60 onto `IFichierJournal` (to be planned via correct-course); Publisher timer re-entrancy and the file-stability gate, both shared with P60 and to be fixed once.
