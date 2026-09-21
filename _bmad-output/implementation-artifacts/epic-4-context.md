# Epic 4 Context: `Kape22Importer` — Dispatch transactionnel vers les tables aval

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Complete the P60 pipeline after the `L_D_KAPE22` insert (Epic 2) by dispatching
explicitly — without reflection — into `L_D_ORDRE_FABRICATION`, `L_D_COULEE`,
`L_D_CONSIGNES` and the 7 `L_D_SECTIONCHARGE_*` tables (Chutage, Lingot,
Decoupe, Pits, PoidsMetrique, Refroidissoirs, Svt), replacing the legacy
reflection-based `MappingTemplate` procedure (read-only reference, never
modified or called at runtime). A valid Fichier must write exactly one
transaction covering all 10 tables; any business or SQL failure blocks the
whole batch, logs the precise cause through the existing circuit, and moves
the Fichier to `error/`, never leaving an orphaned `L_D_KAPE22` row. The epic
went through three retrospectives that surfaced hardening gaps closed by
follow-up stories (4.2-bis/4.3-bis/4.9/4.10/4.11); the latest (4.11) closes the
remaining coverage/documentation items and records two business notes as
one-line code comments rather than new behavior.

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
- Story 4.9: Hardening (A-2..A-5: DbSet assertions, OF/Coulee trim-at-source, shared hot/cold marker, Consignes collision pre-check)
- Story 4.10: Hardening (B-1..B-5: scale/annex conformance guard, `DecimalScale.Apply` bypass guard, production parity extended, script exit-code guard, magnitude overflow guard)
- Story 4.11: Hardening (C-1..C-10) + business notes Q-3/Q-5

## Requirements & Constraints

- Extract and document, column-by-column, how every downstream column derives
  from `L_D_KAPE22` or a business rule, with a test that mechanically fails on
  any undocumented column (no rule may be coded from memory).
- Map `L_D_KAPE22` → OF/Coulee and → Consignes/7×SectionCharge explicitly, one
  mapper per target table, respecting the per-OF applicability rule for
  SectionCharge tables.
- Blocking business checks must run before persistence: cold Coulee must
  exist, hot Coulee format must be valid, ingot/furnace distribution must be
  consistent (missing Pits, malformed hot-Coulee, etc.).
- Extended transactional persistence: one commit for `L_D_KAPE22` + the 9 (now
  10, per the bundle) downstream tables; any failure cause is logged via the
  existing dual-logging circuit (`L_D_LOG_COMMANDE` + `MQTTnetServices.Logs`)
  and the file is routed to `error/`. Must preserve NFR-7 (replay = redeposit,
  no DB action) and the anti-duplicate guard keyed on `(NumeroFichier, OF)`.
- The SQL Server integration test harness (local instance, versioned
  `scripts/schema/`, dual isolation regime) is extended to cover the 10 new
  entities; unit tests for mapping logic stay DB-free.
- Transverse coding standards apply to every development story: TDD
  (test-first, one xUnit test per `AC-FRx-y`), English-only comments (own line,
  above the code, sentence case), alphabetical property ordering on classes/
  entities/records (not on local tuples/deconstructions), PRD glossary
  vocabulary in identifiers, no secrets/connection strings hard-coded.
- Every `decimal` column sourced from a KAPE22 `int`/`int?` must have an
  explicit, annex-documented scale, applied only through `DecimalScale.Apply`;
  a completeness test must fail if a mapper's literal scale diverges from the
  annex, or if such a column is assigned without going through `DecimalScale.Apply`.
- A pre-persistence magnitude guard must catch a scaled value that still
  overflows its target column's precision and turn it into a diagnosed
  `ConversionError` rather than a raw SQL overflow.
- The Consignes natural-key collision pre-check `(OF, CodeOperation,
  TypeConsigne, ConsigneGPAO)` must compare `CodeOperation` case-insensitively,
  matching the real primary key's SQL Server collation.

## Technical Decisions

- **Single extended transaction**: `L_D_KAPE22`, `L_D_LOG_COMMANDE` and the 9
  downstream tables live in the same `DbContext` and commit via one
  `SaveChanges()` — no partial write.
- **Explicit mapping, no reflection**: one mapper class per target table
  (`OrdreFabricationMapper`, `CouleeMapper`, `ConsignesMapper`,
  `SectionCharge{Chutage,Lingot,Decoupe,Pits,PoidsMetrique,Refroidissoirs,Svt}Mapper`),
  properties read/written by explicit name — no `System.Reflection`,
  `Activator.CreateInstance`, or runtime-interpreted mapping table.
- **Legacy wall**: `Ascometal.LSI.DAL`/`BLL` (legacy repo) is read-only
  reference for business intent only — never modified, referenced as an
  assembly, or called at runtime. New EF entities are redefined in
  `Kape22Importer.Persistence`, independent of legacy `EntityObject` classes.
- **EF database-first, no migrations**: the 10 new entities follow the
  `L_D_KAPE22.cs` convention, one file per entity under `Persistence/`, schema
  derived exclusively from `AFV004-LSI` (`sys.columns`), never from the
  mapping annex (which documents derivation rules, not table shape).
- **`Kape22ImportBundle` replaces `MapResult<L_D_KAPE22>`** at the Persister
  boundary: same metadata (`Success`, `Errors`, `Warnings`, `NumeroFichier`,
  `OF`) plus the 9 nullable downstream entities. `Kape22Persister.Persist` is
  replaced, not overloaded.
- **FK by explicit scalar column**: each bundle entity carries its business
  key as a scalar column (e.g. `OF`); no EF navigation properties
  (`ICollection<T>`) link bundle entities.
- **Failure cause reuses the existing circuit**: any dispatch failure (SQL or
  business) becomes a distinct `ConversionError{Block:File, Code}` feeding
  `L_D_LOG_COMMANDE` (`"<NumeroFichier> — REJETÉ : <cause>"`) and
  `MQTTnetServices.Logs` — no new channel.
- **Hot-Coulee reuse semantics** (documented, not changed): a Coulee is
  created by the first OF that references it, then shared as-is by later OFs
  of the same Coulee; a later OF must not modify it. An OF cannot be
  resubmitted today — an OF in error is reissued under a new OF number rather
  than replayed identically.

## Cross-Story Dependencies

- Internal sequencing: 4.1 → {4.3, 4.4} → 4.5 → 4.6 → 4.7; Story 4.2 is a
  transverse prerequisite for 4.3/4.4 (mapping annex must exist before mappers
  are coded against it).
- Hardening chain: 4.2-bis → 4.3-bis → 4.9 → 4.10 → 4.11, each retrospective
  building on the fixes and tests of the previous one; 4.11 is independent
  internally (C-1..C-10 have no ordering) but depends on all prior Epic 4
  stories being delivered.
- Depends on Epic 2 (`L_D_KAPE22` insert, FR-11) as the upstream write this
  epic extends, and on Epic 3's dual-logging/error-routing circuit (FR-14,
  FR-12) which this epic's failure handling reuses rather than replacing.
