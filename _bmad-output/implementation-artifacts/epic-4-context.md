# Epic 4 Context: `Kape22Importer` — Dispatch transactionnel vers les tables aval

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Complete the P60 pipeline after `L_D_KAPE22` insertion (Epic 2) by dispatching
explicitly — without reflection — to `L_D_ORDRE_FABRICATION`, `L_D_COULEE`,
`L_D_CONSIGNES` and the 7 `L_D_SECTIONCHARGE_*` tables (Chutage, Lingot,
Decoupe, Pits, PoidsMetrique, Refroidissoirs, Svt), replacing the legacy
`MappingTemplate`/reflection procedure (`Ascometal.LSI.DAL`, legacy repo,
never modified, never called at runtime). A valid Fichier must write **one
single transaction** covering all 10 tables; any business or SQL failure
blocks the whole set, logs the precise cause through the existing circuit, and
moves the Fichier to `error/`, never leaving an orphan `L_D_KAPE22` row.

## Stories

- Story 4.1: EF entities (database-first) + extend SQL test harness for the 10 downstream tables
- Story 4.2: Extract & document the legacy mapping (verifiable column-by-column annex)
- Story 4.3: OF + Coulee mapper (pure structural mapping)
- Story 4.4: Consignes + the 7 SectionCharge mappers (pure structural mapping, per-OF applicability rule)
- Story 4.5: `Kape22ImportBundleMapper` (orchestrator) + pure business checks
- Story 4.6: `Kape22Persister` replaced (bundle, single transaction, Coulee existence check) + `Kape22FichierProcessor` updated
- Story 4.7: Extend the E2E suite to the 10 downstream tables
- Story 4.2-bis: Extend the mapping annex with a `scale` field
- Story 4.3-bis: Decimal scaling fix (5 mappers)
- Story 4.9: Hardening (A-2..A-5: downstream DbSet assertions, OF/Coulee trim-at-source, shared hot/cold Coulee marker, Consignes natural-key collision pre-check)
- Story 4.10: Hardening (B-1..B-5: mapper/annex scale-drift guard, `DecimalScale.Apply` bypass guard, production parity round-trip for scaled columns, e2e-worker script exit-code guard, out-of-range decimal guard)

## Requirements & Constraints

- FR-17: extract/document the legacy column-by-column mapping annex, with a
  mechanical completeness test against the EF model (no undocumented column;
  `à_clarifier` entries require a `deferred-work.md` note).
- FR-18/FR-19: explicit, reflection-free mappers per target table; a
  `SectionCharge_*` mapper returns `null` when its table does not apply to the
  current OF (no default row created); `L_D_CONSIGNES` carries its owning
  section-of-charge key as an explicit scalar FK.
- FR-20: pure business checks with no DB read (lingots/fours distribution
  consistency, hot-coulee number format, missing Pits enfournement consigne),
  plus one DB-reading check (cold-Coulee existence) that stays in the
  Persister because it needs the open context.
- FR-21: `Kape22ImportBundle` replaces `MapResult<L_D_KAPE22>` at the
  Persister boundary (same metadata, plus the 9 nullable downstream entities);
  a single `Persist` surface, no second method kept in parallel.
- Preserves NFR-7 (replay = redrop the corrected Fichier, zero DB action) and
  the anti-duplicate guard `AC-FR11-6/7`, both unchanged by this epic.
- AR-12 test harness (local SQL Server, `scripts/schema/` generated from
  `AFV004-LSI`) is extended, not replaced, to cover the 10 new tables.
- CC-2 comment-language exemption extends, from this epic on, to the domain
  nouns `Ordre de Fabrication`/`OF`, `Coulee`, `Chutage`, `Decoupe`, `Lingot`,
  `PoidsMetrique`, `Refroidissoirs`.
- CC-4 (alphabetical property order) applies to the new EF entities; CC-7 (no
  hard-coded secrets) applies to this epic's `Kape22Importer` code.

## Technical Decisions

Pipeline: pure per-table mappers → `Kape22ImportBundleMapper` (composes
`Kape22Mapper.Map` + table mappers + DB-free business checks) →
`Kape22ImportBundle` → `Kape22Persister` (sole I/O point: DB-reading checks,
`context.Add` for every non-null entity, then **one** `SaveChanges()`).
Atomicity comes from that single `SaveChanges()`, not an explicit
`TransactionScope`.

- AD-1 — one `SaveChanges()` commits `L_D_KAPE22` + `L_D_LOG_COMMANDE` + all
  applicable downstream entities together; never partial writes.
- AD-2 — no `System.Reflection`/`Activator.CreateInstance`/interpreted mapping
  table; one mapper class per target table, explicit named properties.
- AD-3 — legacy `Ascometal.LSI.*` code is read-only reference only: never
  modified, never assembly-referenced, never called at runtime.
- AD-4 — every dispatch failure (SQL or business) becomes a distinct
  `ConversionError{Block:File, Code}` and reuses the existing circuit
  (`L_D_LOG_COMMANDE` "REJETÉ", `MQTTnetServices.Logs`, move to `error/`) — no
  new logging channel.
- AD-5 — the 10 new entities are database-first from `AFV004-LSI`
  (`sys.columns`), one file per entity under `Persistence/`, no EF migration.
- AD-6 — `Kape22ImportBundle` carries the same metadata as
  `MapResult<L_D_KAPE22>` plus the 9 nullable downstream entities;
  `Kape22Persister.Persist` is replaced (not overloaded).
- AD-7 — inter-table links are explicit scalar FK columns (e.g. `OF`, owning
  section-of-charge key); no EF navigation properties tie bundle entities
  together.
- Naming: `<TableNameNoPrefix>Mapper` (mirrors `Kape22Mapper`); entities named
  after their table. No new dependency: built with what `Kape22Importer`
  already references (`TextToXml`, `PortalSharedLibrary`, EF Core).
- Decimal scaling (4.2-bis/4.3-bis/4.10): columns of CLR type `decimal`
  sourced from a KAPE22 `int`/`int?` carry an explicit `scale` field in the
  mapping annex (`MappingAnnexEntry.Scale`) and must be written through the
  single sanctioned path `DecimalScale.Apply(rawValue, scale)` — never a raw
  integer write. `L_D_SECTIONCHARGE_REFROIDISSOIRS`, `_POIDSMETRIQUE` and
  `_SVT` have no `decimal` columns and are out of scope.
- The cold/hot Coulee marker (`ColdConsignePits = "1"`) must live in one
  shared location referenced by both `Kape22Persister` and
  `Kape22ImportBundleMapper` — not duplicated as separate literals.

## Cross-Story Dependencies

- Internal sequencing: 4.1 → {4.3, 4.4} → 4.5 → 4.6 → 4.7; Story 4.2 (mapping
  annex) is a cross-cutting prerequisite for 4.3 and 4.4 — no mapper may code
  a rule absent from the annex.
- Post-retro #1, strict series (no parallelization): 4.2-bis → 4.3-bis → 4.9
  (4.9 depends on 4.3-bis because A-3 changes the value source 4.3-bis tests).
- Post-retro #2: Story 4.10 (B-1..B-5) has no prerequisite and no internal
  order — independent of 4.2-bis/4.3-bis/4.9 and of each other, grouped into
  one story purely for a single review cycle.
- Story 4.6 explicitly must NOT change `IFichierProcessor.Process` signature
  or `FichierProcessingResult` shape, to avoid triggering a `MicroServices.sln`
  (SVN) commit; verified before closing the story.
- Story 4.7 (E2E) reuses Story 3.6's 10-file `P60/` fixture suite; adds
  dedicated faulty fixtures per new failure cause.
- No UX/design artifacts exist or apply to this epic (v1 ships no UI).
