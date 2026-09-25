# Epic 5 Context: P89 — conversion Fichier → XML normalisé + XSD (Étape 1)

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Deliver Step 1 of the P89 format: raw P89 Fichiers (`LP89_682_617_<nnn>`, `nnn` 001..999 then wrapping back to 001) are converted into normalized XML validated by `P89.xsd`, without touching the generic `TextToXml` library. At the end of the epic a Launcher-supervised worker `GpaoConvertP89` (MicroServices.sln, over the `P89Converter` library) empties the configured source folder into the XML folder (timestamped output) and the done / error folders, writing one `L_D_LOG_COMMANDE` row per Fichier. Business mapping and persistence of P89 data are out of scope, deferred to a later, unplanned epic.

## Stories

- Story 5.1: `P89Converter` — raw P89 folder → timestamped normalized XML

## Requirements & Constraints

- Every `LP89_*` Fichier in the source folder is processed: strict UTF-8 → Windows-1252 transcode, `Converter.Convert` with the P89 descriptor, XSD validation, XML written as `<name>_<yyyyMMddHHmmss>.xml`, then the source moved to the done folder under the same timestamp suffix.
- Reference Fichiers convert without Error and are XSD-valid; the message Ligne is exactly 3442 characters (last Champ `LG24`, Position 3438, Size 4).
- The XSD must stay in sync with the descriptor; P60 XSD generation must remain unchanged.
- Encoding failures (invalid UTF-8 byte, character absent from Windows-1252) fail the Fichier with an encoding reason; no replacement character is ever emitted.
- Two conversions of the same name at different instants never overwrite each other (index rotation).
- Failure (conversion Error, XSD, encoding) ⇒ no XML written, Fichier moved to the error folder, each reason logged to `MQTTnetServices.Logs`, a REJETÉ `L_D_LOG_COMMANDE` row when the OF is readable; remaining Fichiers still processed (no fail-fast). Unreachable database ⇒ Fichier stays in source, retried next tick.
- The `TextToXml` library is not modified (zero library line for a new format's Step 1).
- Strict TDD (CC-1), English comments (CC-2), alphabetical ordering (CC-4), glossary vocabulary reused verbatim (CC-5).

## Technical Decisions

- P89 Fichiers are UTF-8 (measured on 248 real Fichiers); the library stays frozen on Windows-1252, so the P89 format transcodes before the library call.
- The supplied template was wrong: all Positions from `AnomaliePitsFour1` to `LG24` shift by +5 (`Reserve9` Size 6). Corrected template and generated XSD already exist in `Templates/` (uncommitted manual-session work, carried into Story 5.1 as its starting point together with `scripts/gen.ps1 -Format`).
- `src/P89Converter`: library `net10.0`, references `TextToXml` + `Kape22Importer` (L_D_LOG_COMMANDE entity); descriptor/XSD embedded. Folders and interval from the `P89` section of `GpaoConvertP89.json`. Clock via `TimeProvider` (local time). Worker `GpaoConvertP89` follows the `GpaoImportP60` model (Story 5.1 checkpoint correction, 2026-09-25).
- Move to done happens after the XML write, so a crash between the two leaves the Fichier in raw to be reconverted next run.
- The prototype `scripts/p89-to-xml.cs` is replaced and deleted; both new projects join `TextToXml.sln`; `README.md` gains a P89 section.
- Tests: `tests/P89Converter.Tests`, `Category=Unit`, temporary folders, no database; at least 3 real fixtures (one with accents) + faulty variants (invalid UTF-8, non-Windows-1252 char, truncated Ligne); an XSD↔descriptor test in the style of `P60XsdTests`.

## Cross-Story Dependencies

- None inside the epic. Epics 1–4 are done and untouched; the project is reopened for this epic and re-closes at Story 5.1 closure.
