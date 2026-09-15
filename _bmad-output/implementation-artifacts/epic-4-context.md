# Epic 4 Context: `Kape22Importer` — Dispatch transactionnel vers les tables aval

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Complete the P60 pipeline after the `L_D_KAPE22` insert (Epic 2, FR-11) by dispatching — explicitly, with no reflection — into `L_D_ORDRE_FABRICATION` (Ordre de Fabrication / OF), `L_D_COULEE` (Coulée), `L_D_CONSIGNES`, and the seven `L_D_SECTIONCHARGE_*` tables (Chutage, Lingot, Decoupe, Pits, PoidsMetrique, Refroidissoirs, SVT), replacing the legacy `MappingTemplate`/reflection-based procedure (`Ascometal.LSI.DAL`, legacy repo — never modified, never called at runtime). A valid File writes exactly one transaction covering all 10 tables; any business or SQL failure blocks the whole set, logs the precise cause through the existing circuit (`L_D_LOG_COMMANDE` + `MQTTnetServices.Logs`), and moves the File to `error/` — never leaving an orphaned `L_D_KAPE22` row (preserves NFR-7 and the anti-duplicate guard `AC-FR11-6/7`).

## Stories

- Story 4.1: EF database-first entities + extend the SQL harness to the 10 downstream tables
- Story 4.2: Extract & document the legacy mapping (column-by-column annex, mechanically verifiable)
- Story 4.3: OF + Coulée mapper (pure structural mapping)
- Story 4.4: Consignes + the 7 SectionCharge mappers (pure structural mapping, OF applicability rule)
- Story 4.5: `Kape22ImportBundleMapper` (orchestrator) + pure business controls
- Story 4.6: `Kape22Persister` replaced (bundle, single transaction, Coulée control) + `Kape22FichierProcessor` updated
- Story 4.7: Extend the E2E suite to the 10 downstream tables

**Sequencing:** 4.1 → {4.3, 4.4} → 4.5 → 4.6 → 4.7; 4.2 is a transverse prerequisite for 4.3/4.4.

## Requirements & Constraints

- FR-17: extraction/documentation of the legacy column-by-column mapping annex, with a status per column (`sourcée` / `règle` / `à_clarifier`); an `à_clarifier` entry must be backed by a `deferred-work.md` note or the completeness test fails.
- FR-18/FR-19: explicit mapping (no reflection) from `L_D_KAPE22` to OF/Coulée, and to Consignes + the 7 SectionCharge tables, including the per-OF applicability rule (a non-applicable SectionCharge mapper returns `null`, no default row).
- FR-20: blocking business controls before persistence — cold Coulée must exist in DB, hot Coulée number format (`'0'` prefix), ingot/furnace distribution consistency vs the OF's half-product count, presence of an enfournement instruction (Pits).
- FR-21: extended transactional persistence — one `Kape22ImportBundle`, one commit for `L_D_KAPE22` + the 9 downstream tables; failure cause logged via the existing FR-14 circuit.
- The legacy annex at `_bmad-output/implementation-artifacts/annexe-mapping-dispatch-epic4.md` is the single source of truth for every mapper in 4.3/4.4/4.5 — no mapper may encode a rule absent from it.
- Downstream entity schemas come exclusively from `sys.columns` on `AFV004-LSI` (never written from memory), same discipline as the existing `L_D_KAPE22`/Annexe C provenance rule (R-3).
- Cross-epic reuse, extended not replaced: the AR-12 local SQL Server harness (Story 2.1), the double-journal circuit (Story 3.3/FR-14), the anti-duplicate guard (`AC-FR11-6/7`, Story 2.8).
- Story 4.6 stays confined to `Kape22Importer` (Git repo), no `MicroServices.sln` (SVN) commit, conditional on `IFichierProcessor.Process`/`FichierProcessingResult` keeping an unchanged signature — verified before closing 4.6.

## Technical Decisions

Governed by `_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md` (AD-1..AD-7):

- **AD-1** — Single extended transaction: `L_D_KAPE22`, `L_D_LOG_COMMANDE`, and the 9 downstream entities go into the same `DbContext`, one `SaveChanges()`. No explicit `TransactionScope`; atomicity is the existing EF Core single-`SaveChanges()` behavior (unchanged since Story 2.8).
- **AD-2** — One mapper per target table, properties read/written by explicit name. No `System.Reflection`, no `Activator.CreateInstance`, no runtime-interpreted mapping table.
- **AD-3** — Legacy wall: `Ascometal.LSI.DAL`/`BLL` is read-only reference material — never modified, never assembly-referenced, never called at runtime. New EF entities are redefined independently in `Kape22Importer.Persistence`.
- **AD-4** — Every dispatch failure (SQL or business) becomes a distinct `ConversionError{Block:File, Code}` feeding the existing circuit (`L_D_LOG_COMMANDE`, `MQTTnetServices.Logs`, move to `error/`). No new logging channel.
- **AD-5** — Database-first, no EF migrations; entities mirror `L_D_KAPE22.cs`, one file per entity; `scripts/schema/` extended so AR-12 tests cover the 10 new tables.
- **AD-6** — `Kape22ImportBundle` carries `MapResult<L_D_KAPE22>`'s metadata (`Success`, `Errors`, `Warnings`, `NumeroFichier`, `OF`) plus the 9 nullable downstream entities. `Kape22Persister.Persist` is replaced, not overloaded — no second persistence surface.
- **AD-7** — Inter-table links via explicit scalar FK columns, assigned directly by mappers — no EF navigation properties linking bundle entities.
- Mapper naming: `<TableNameNoPrefix>Mapper` (e.g. `OrdreFabricationMapper`, `SectionChargeChutageMapper`).
- CC-1..CC-5 and CC-7 apply as in prior epics (TDD-first, English comments, alphabetical property order, PRD glossary vocabulary, config-only secrets); CC-6 (zero runtime deps) does not apply here — this is `Kape22Importer`, not `TextToXml`.

## Cross-Story Dependencies

- Story 4.1 (entities + schema) is a hard prerequisite for 4.2's completeness test (which reflects over the EF model) and for 4.3/4.4 mappers; it does **not** depend on 4.2 — schema comes from `sys.columns`, not from the mapping annex.
- Story 4.2's annex is the single reference cited by 4.3, 4.4, and 4.5 — no mapper in those stories may invent a rule the annex doesn't document.
- Story 4.5 composes `Kape22Mapper.Map` (Epic 2) with the 4.3/4.4 mappers into `Kape22ImportBundle`, and owns the pure (no-DB-read) business controls (ingot/furnace distribution, hot Coulée format, missing Pits instruction).
- Story 4.6 consumes the 4.5 bundle, adds the one DB-read control (cold Coulée existence), and replaces `Kape22Persister`/updates `Kape22FichierProcessor` (Story 3.2) in the same story — no dual persistence surface at any point.
- Story 4.7 replays the Story 3.6 E2E harness (10 `P60/` reference files) against the new tables; it adds coverage on top of 4.6's own integration proof, it does not replace it.
