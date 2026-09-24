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
has been reopened four times after retrospectives/production-parity testing
surfaced gaps, each closed by a follow-up story
(4.2-bis/4.3-bis/4.9/4.10/4.11/4.4-bis). The latest, 4.4-bis, corrects
`ConsignesMapper`: production-parity testing on a real OF found the mapper
producing only 1/12th of the real `L_D_CONSIGNES` row count, because it never
decoded the per-section sub-fields legacy derives from the raw consigne code,
and tagged its rows with the wrong `ConsigneGPAO` value.

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
- Story 4.4-bis: Decompose `L_D_CONSIGNES` into per-section decoded sub-fields + correct `ConsigneGPAO` semantics

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
- `L_D_CONSIGNES` decomposition (4.4-bis): each of the 6 decodable sections
  (every section but SVT) decodes its raw consigne code into several positional sub-fields (`Substring` offsets),
  each a distinct `L_D_CONSIGNES` row with its own `TypeConsigne`, in addition
  to the already-produced "full code" row. Offsets must be read directly from
  the legacy source per section rather than reconstructed from memory (same
  no-guessing discipline as the mapping annex). `Decoupe` (XP1) has a second,
  independent size-18 consigne code, `LibelleConsigneDecoupe` (Position 287),
  with its own sub-fields, present only if non-empty (code review D-1,
  2026-09-23).
  `SVT` may legitimately have no discoverable decomposition rule — document as
  `à_clarifier` rather than invent one; this does not block the story.
- `ConsigneGPAO` semantics (confirmed by the business owner, not previously
  known): `0` = the value initially planned by the OF; `1` = the value as
  possibly adjusted by an operator for a temporary production constraint —
  these are two distinct, both-needed values, not a mirror/duplicate. Every
  row this pipeline produces (P60 dispatch) is a `1`-value; producing or
  assuming the existence of a matching `0`-value row is out of scope for this
  mapper and for downstream code (no cross-mapper existence check).
- Production-parity testing (`Kape22ProductionDataParityTests`) did not
  previously cover `L_D_CONSIGNES` or `L_D_COULEE` column-by-column against
  real data — only `L_D_KAPE22` and the `decimal` columns of
  `L_D_ORDRE_FABRICATION`/`L_D_SECTIONCHARGE_*` were checked, which is why the
  `L_D_CONSIGNES` under-population went undetected. Closing this coverage gap
  for `L_D_CONSIGNES` (`ConsigneGPAO=1` rows only) is part of this epic.

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
  (`ICollection<T>`) link bundle entities. `ConsignesMapper`/`Kape22Persister`
  must not add a cross-mapper existence check for the (out-of-scope)
  `ConsigneGPAO=0` counterpart rows — consistent with this no-navigation rule.
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
- Story 4.4-bis has no prerequisite and is independent of
  4.2-bis/4.3-bis/4.9/4.10/4.11 (already delivered); it solely corrects
  `ConsignesMapper` (Story 4.4) and extends the mapping annex + production
  parity suite it depends on (Stories 4.2/4.2-bis).
- Depends on Epic 2 (`L_D_KAPE22` insert, FR-11) as the upstream write this
  epic extends, and on Epic 3's dual-logging/error-routing circuit (FR-14,
  FR-12) which this epic's failure handling reuses rather than replacing.
