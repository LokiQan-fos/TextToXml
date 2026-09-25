# Epic 4 Context: `Kape22Importer` — Dispatch transactionnel vers les tables aval

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Complete the P60 pipeline after the `L_D_KAPE22` insert (Epic 2) by dispatching
explicitly, without reflection, into `L_D_ORDRE_FABRICATION`, `L_D_COULEE`,
`L_D_CONSIGNES` and the 7 `L_D_SECTIONCHARGE_*` tables (Chutage, Lingot,
Decoupe, Pits, PoidsMetrique, Refroidissoirs, Svt). This replaces the legacy
`MappingTemplate`/reflection procedure, which stays a read-only reference and
is never modified or called at runtime. A valid Fichier writes exactly one
transaction covering all 10 tables. Any business or SQL failure blocks the whole
batch, logs the precise cause through the existing circuit and moves the Fichier
to `error/`, so no orphaned `L_D_KAPE22` row is ever left behind. Retrospectives
and production-parity test sessions have reopened the epic several times. The
latest correction, Story 4.13, makes `L_D_CONSIGNES` match production's 2 × N
rows: each consigne is written twice, as a `ConsigneGPAO=1` row and as a
`ConsigneGPAO=0` working-copy row that carries the composite labels.

## Stories

- Story 4.1: EF database-first entities + SQL test harness extended for the 10 downstream tables
- Story 4.2: Extraction & documentation of the legacy mapping (verifiable column-by-column annex)
- Story 4.3: OF + Coulee mapper (pure structural mapping)
- Story 4.4: Consignes + 7 SectionCharge mappers (pure structural mapping, per-OF applicability rule)
- Story 4.5: `Kape22ImportBundleMapper` (orchestrator) + pure business checks
- Story 4.6: `Kape22Persister` replaced (bundle, single transaction, Coulee check) + `Kape22FichierProcessor` updated
- Story 4.7: E2E suite extended to the 10 downstream tables
- Story 4.2-bis: Mapping annex extended with a `scale` field
- Story 4.3-bis: Decimal scaling fix (5 mappers)
- Story 4.9: Hardening (A-2..A-5)
- Story 4.10: Hardening (B-1..B-5)
- Story 4.11: Hardening (C-1..C-10) + business notes Q-3/Q-5
- Story 4.4-bis: Decompose `L_D_CONSIGNES` into per-section decoded sub-fields
- Story 4.12: Populate `L_D_CONSIGNES.LibelleConsigne` (port of `GetLibelle`)
- Story 4.13: `ConsigneGPAO=0` working-copy row + composite labels (port of `BuildLibelleConsigne`)
- Story 4.14: Unit-tier schema parity lock for `DownstreamColumnPrecisions`

## Requirements & Constraints

- Every downstream column is documented in the mapping annex, which says how it
  derives from `L_D_KAPE22` or from a business rule. A completeness test fails
  on any undocumented column. No rule may be coded from memory: offsets and
  branches are read directly from the legacy source (`Desktop/kape22/`,
  `Lsi.Net/Ascometal.LSI.DAL/`). A rule that cannot be found is marked
  `à_clarifier` / `assumed, unverified`, never guessed (SVT is the recurring
  case).
- There is one pure mapper per target table. SectionCharge tables follow the
  per-OF applicability rule: a table that does not apply gets no default row.
- Blocking business checks run before persistence: the cold Coulee must exist,
  the ingot/furnace distribution must be consistent, and the enfournement
  instruction (Pits) must be present. There is no Coulee number format check:
  AC-FR20-3 was withdrawn on 2026-09-22 (`547bf2a`, process-owner decision)
  because hot/cold and Coulee origin are independent.
- Every `decimal` column sourced from a KAPE22 `int` has an annex-documented
  scale, applied only through `DecimalScale.Apply`. A pre-persistence magnitude
  guard turns an overflow into a diagnosed `ConversionError`.
- The Consignes natural key is `(OF, CodeOperation, TypeConsigne,
  ConsigneGPAO)`. The collision pre-check compares `CodeOperation`
  case-insensitively.
- **`L_D_CONSIGNES` content:**
  - Each decodable section (all except SVT) produces a full-code row
    (`TypeConsigne=13`) plus one row per positional sub-field.
  - XP1 has a second, optional size-18 code (type 24) with its own sub-fields
    (types 25-29). These rows exist only when that code is non-empty.
  - `LibelleConsigne` comes from the pure `LibelleConsigneResolver`, a port of
    `GetLibelle`. `Kape22FichierProcessor` loads a SELECT-only snapshot of the
    13 `L_P_CONSIGNES_*` tables once per Fichier. With an empty snapshot,
    table-sourced labels are `?`.
- **`ConsigneGPAO` semantics** (corrected 2026-09-24, replacing the inverted
  2026-09-23 version):
  - `1` is the value received from the GPAO and is never edited.
  - `0` is a working copy that the import itself creates with identical codes;
    operators edit it later through the MCC command. No other process writes
    the `0` rows.
- Each `1` row therefore has a `0` twin with the same `OF`, `CodeOperation`,
  `TypeConsigne`, `CodeConsigne`, `SizeCodeConsigne` and label, with one
  exception: the composite rows (type 13 of each decoded section, type 24 of
  XP1). On those rows the `0` row carries the composite label built by an exact,
  pure port of `BuildLibelleConsigne`, while the `1` row keeps `?`. The port
  keeps the sub-type order per section, the `"\t\t"` separators (trailing one
  included), `"°C"` on PC1 types 10/11, `"\n"` + the PC1 type 6 label, and
  copies `?` sub-labels as-is.
- `CodeConsigne` trailing spaces are trimmed (unlike legacy). This is an
  accepted deviation and must be documented in the annex.
- Production parity (`Kape22ProductionDataParityTests`, real `P60/` files)
  covers `L_D_CONSIGNES`:
  - For `1` rows, it compares every column, including `LibelleConsigne`.
  - For `0` rows, it checks that a production row with the same natural key
    exists. Codes are compared against production's `1` row. The label is
    compared only for sections whose production `0` codes are all equal to
    their `1` codes. A section edited through MCC is reported as skipped,
    not failed.
- `scripts/e2e-worker-import.ps1` first reloads the reference tables from
  production. It fails on a `NULL` label, checks that the number of `0` rows
  equals the number of `1` rows, and checks that no type 13 label on a `0` row
  is `?`.
- The production DB is read-only (SELECT only).
- Transverse standards apply:
  - TDD, with one xUnit test per AC.
  - English comments.
  - Case-insensitive alphabetical property ordering.
  - PRD glossary vocabulary.
  - No hard-coded secrets.

## Technical Decisions

- **Single transaction:** `L_D_KAPE22`, `L_D_LOG_COMMANDE` and the downstream
  tables share one `DbContext` and are written by one `SaveChanges()`.
  `Kape22ImportBundle` replaces `MapResult<L_D_KAPE22>` at the Persister
  boundary.
- **Explicit mapping, pure functions:** mappers read and write properties by
  name, with no `System.Reflection`, `Activator` or runtime mapping table.
  Mappers have no DB access and no cross-mapper knowledge. The label resolver
  and the composite-label port are also pure.
- **Legacy wall:** `Ascometal.LSI.DAL`/`BLL` are reference material for
  business intent only. They are never modified, referenced as assemblies or
  called at runtime.
- **EF database-first, no migrations:** there is one file per entity under
  `Persistence/`. The schema comes from `AFV004-LSI` `sys.columns`, never from
  the annex.
- **Foreign keys by scalar column:** bundle entities are linked only by scalar
  columns such as `OF`, never by navigation properties. Adding the `0` rows
  needs no schema or Persister change because `ConsigneGPAO` already
  distinguishes the pair in the key.
- **Failure cause reuses the existing circuit:** a failure becomes a
  `ConversionError{Block:File, Code}` that feeds both `L_D_LOG_COMMANDE`
  (`"<NumeroFichier> — REJETÉ : <cause>"`) and `MQTTnetServices.Logs`. No new
  channel is added.
- **Hot-Coulee reuse:** the first OF that references a Coulee creates it. Later
  OFs share it and must not modify it. An OF in error is reissued under a new
  number and never replayed.

## Cross-Story Dependencies

- Core sequence: 4.1 → 4.2 → {4.3, 4.4} → 4.5 → 4.6 → 4.7.
- Hardening chain: 4.2-bis → 4.3-bis → 4.9 → 4.10 → 4.11.
- Consignes chain: 4.4-bis (sub-fields) → 4.12 (`LibelleConsigneResolver`) →
  4.13. Story 4.13 reuses the resolver for `0` rows that are not composite, and
  the annex, `ConsignesMapper.cs` comments, parity test comments and the epics
  section header must all be updated to the corrected `ConsigneGPAO` semantics.
- Upstream dependencies: Epic 2 (the `L_D_KAPE22` insert and the
  `(NumeroFichier, OF)` anti-duplicate guard) and Epic 3 (dual logging and
  `error/` routing). Replaying a Fichier means redepositing it, with no DB action.
- Story 4.14 closes retro #4 item D-2 (the `(precision, scale)` map added by
  manual-session commit `1ab5ea7` had no Unit-tier parity test, unlike
  `DownstreamColumnMagnitudes`). Its closure also re-closes `PROJECT-CLOSED.md`
  (item D-3).
