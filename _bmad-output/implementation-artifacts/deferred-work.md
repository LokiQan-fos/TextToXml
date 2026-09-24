# Deferred Work

## Deferred from: code review of story-4.9 (2026-09-18)

- source_spec: `spec-4-9-hardening-epic-4.md` / `src/Kape22Importer/Persistence/Kape22Persister.cs`
  summary: `PersistMapped`'s two rejection checks (the pre-existing missing-cold-Coulee guard and the new
  A-5 consignes-natural-key-collision guard) are sequential early-returns, not an accumulate-then-report
  pass — unlike the sibling FR-20 controls in `Kape22ImportBundleMapper`, which run regardless of an
  earlier one's outcome so multiple violations surface together. A Fichier that has both a missing Coulee
  and a consignes collision only ever reports the missing-Coulee error; the collision stays hidden until
  that is fixed and the Fichier is resubmitted.
  evidence: Raised by the Blind Hunter layer at Story 4.9's code review. Neither epics.md's A-5 AC nor the
  frozen spec requires combining these two Persister-level checks into one pass, and doing so now would
  mean restructuring the pre-existing missing-Coulee check, which is outside this story's stated boundary.
  Real, but not blocking; a future hardening pass could accumulate both.

- source_spec: `spec-4-9-hardening-epic-4.md` / `src/Kape22Importer/Persistence/Kape22Persister.cs`
  summary: the new A-5 pre-check reports only the first colliding `CodeOperation` group
  (`.FirstOrDefault(group => group.Count() > 1)`). If a bundle had two independent collisions, only one
  would ever surface in the `ConversionError`/REJETÉ log; the second stays undiscovered until the first is
  fixed and the Fichier resubmitted.
  evidence: Raised by the Blind Hunter layer at Story 4.9's code review. epics.md's A-5 AC only describes
  "a collision" (singular); with `ConsignesMapper` producing at most 7 rows per bundle and
  `TypeConsigne`/`ConsigneGPAO` always constant, two independent simultaneous collisions in one bundle is
  an unlikely combination. Not blocking; a future hardening pass could report every colliding group.

- source_spec: `spec-4-9-hardening-epic-4.md` / `src/Kape22Importer/Kape22Mapper.cs`
  summary: no test exercises a blank/whitespace-only `OF` or `Coulee` Champ through the new unconditional
  `entity.OF.Trim()`/`entity.Coulee.Trim()` (`Kape22Mapper.cs:112`) — only the padded-Champ trim case is
  covered. The generic `RequiredFieldCheck` blank-field pattern is tested for other columns but not
  specifically routed through this new trim site for OF/Coulee.
  evidence: Raised by the formal `/run-review` pass on story-4.9 (2026-09-18). Verified by inspection that
  `Kape22FileMessage.OF`/`.Coulee` default to `string.Empty`, never null, so `.Trim()` cannot throw; the
  gap is coverage-only, not a correctness risk. Not blocking.

- source_spec: `spec-4-9-hardening-epic-4.md` / `src/Kape22Importer/Persistence/Kape22Persister.cs`
  summary: the A-5 REJETÉ message (`Kape22Persister.cs:124`) cites only the raw colliding `CodeOperation`
  value, not which two sections collided (e.g. Chutage vs Decoupe). An operator reading the
  `L_D_LOG_COMMANDE` row has to cross-reference the source Fichier by hand to identify the sections.
  evidence: Raised by the formal `/run-review` pass on story-4.9 (2026-09-18). Diagnosability nice-to-have;
  the frozen spec only requires the message to name the OF and cite the collision, which it does. Not
  blocking.

## Deferred from: code review of story-4.7 (2026-09-17)

- source_spec: `spec-4-7-e2e-suite-downstream-tables.md` / `tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs`
  summary: the three AC-FR21-5 rejection tests only assert `Assert.Contains("REJETÉ", log.Message)` (or, for
  the SQL-failure case, a generic `[Kape22Importer][ImportRejected]` marker plus `ErrorCode.PersistenceError`)
  rather than asserting the log text actually names the specific cause (the missing Coulée, the
  ingot/furnace mismatch, the persistence failure detail). A generic-but-wrong REJETÉ message would still
  pass.
  evidence: Raised by the Blind Hunter layer at Story 4.7's code review. AC-FR21-5 only requires the cause
  be "lisible" (readable), which the current assertions satisfy structurally; asserting the exact wording
  would tie the test to message-format details not otherwise pinned by any AC. Not blocking; a future
  hardening pass could assert on cause-specific substrings once the exact rejection message wording is
  considered a contract worth pinning.

- source_spec: `spec-4-7-e2e-suite-downstream-tables.md` / `tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs`
  summary: only `ErrorCode.BusinessRuleViolation` (x2) and `ErrorCode.PersistenceError` are exercised for
  AD-1 atomicity on the rejection path. Other `ErrorCode` values established since Story 3.5 (e.g.
  `SchemaInvalid`, `UnexpectedFailure`) are not proven to roll back all 11 AscoLSI tables the same way at
  the E2E level.
  evidence: Raised by the Blind Hunter layer at Story 4.7's code review. AC-FR21-5 only requires "au moins
  une fixture fautive dédiée par cause" across the three named causes (coulée absente, répartition
  lingots/fours incohérente, échec SQL simulé) — already satisfied. Extending to every `ErrorCode` is a
  reasonable hardening step, not a gap in this story's own scope.

- source_spec: `_bmad-output/project-profile.md` (AD-1) / `_bmad-output/planning-artifacts/epics.md:189-190`
  summary: AD-1's wording says "L_D_KAPE22, L_D_LOG_COMMANDE et les 9 tables aval" while enumerating 10
  tables (`L_D_ORDRE_FABRICATION`, `L_D_COULEE`, `L_D_CONSIGNES`, 7×`L_D_SECTIONCHARGE_*`) — a pre-existing
  off-by-one in the architecture documentation, inherited from Story 4.1/4.6, outside this test-only
  story's scope.
  evidence: Raised by the /run-review aggregated review of Story 4.7 (Acceptance Auditor + Blind Hunter
  layers). Confirmed against `AscoLsiDbContext.cs` (12 `DbSet`s minus `Kape22Rows`/`LogCommandeRows` = 10
  downstream) and `epics.md:1630` ("les 10 nouvelles entités"). The code is correct; only the AD-1/epics.md
  prose needs a wording fix, in a future documentation pass.

- source_spec: `tests/Kape22Importer.Tests/RejectionAtomicityIntegrationTests.cs`
  summary: the three rejection tests call `Kape22FichierProcessor.Import` directly rather than through
  `InboxScanner.RunTick()`, so none of them proves the rejected Fichier is moved to `error/` with its
  sidecar, or that `inbox`/`processing` end up empty on the rejection path (unlike SM-2 on the success
  path).
  evidence: Raised by the /run-review aggregated review of Story 4.7 (Blind Hunter layer). Same accepted
  shortcut already documented in this spec's Design Notes (2026-09-17, AC-FR21-5 channel substitution); a
  full folder-lifecycle proof on the rejection path would need a new dedicated test, out of this
  test-only, no-new-plumbing story's scope. Not blocking.

- source_spec: `tests/Kape22Importer.Tests/EndToEndImportIntegrationTests.cs:130-160`
  summary: the per-file assertions on the 9 downstream tables only check row presence/count matched by OF,
  never the tables' own column values, unlike `L_D_KAPE22` where Client/Coulee/Nuance are compared.
  evidence: Raised by the /run-review aggregated review of Story 4.7 (Blind Hunter layer). This exactly
  mirrors the pre-existing "count matches bundle.X is null ? 0 : 1" pattern the spec explicitly instructs
  reusing from `TransactionalPersistenceTests.cs:390-394` — not a regression introduced by this story;
  deepening it would mean inventing new plumbing the spec's own boundaries forbid.

## Deferred from: story-4.6 implementation (2026-09-16)

**RESOLVED** — see "Resolved by: story-4.3-bis implementation (2026-09-17)" below.

- source_spec: `spec-4-6-persister-bundle-single-transaction.md` / `epics.md` § Story 4.3 / § Story
  4.4 (`annexe-mapping-dispatch-epic4.md` § L_D_ORDRE_FABRICATION, § L_D_SECTIONCHARGE_LINGOT,
  § L_D_SECTIONCHARGE_CHUTAGE, § L_D_SECTIONCHARGE_DECOUPE, § L_D_SECTIONCHARGE_PITS) — escalates the
  decimal-scale gap already opened by the "code review of story-4.3" entry below, and extends it past
  `OrdreFabricationMapper` to the Story 4.4 SectionCharge* mappers.
  summary: five mappers write raw un-rescaled KAPE22 ints straight into narrow `DECIMAL` columns with
  no rescaling: `OrdreFabricationMapper` (`DiametreProduit DECIMAL(4,1)`, the six `Tolerance*`
  `DECIMAL(2,1)`/`DECIMAL(4,0)`, `LongueurCD DECIMAL(5,3)`, `PoidsDemiProduitUnitaire DECIMAL(7,3)`,
  `PoidsPrevuDemiProduit DECIMAL(6,3)`), `SectionChargeLingotMapper` (`SectionLaminage`/
  `EpaisseurEnLaminage DECIMAL(4,1)`, four `Tolerance*1 DECIMAL(2,1)`), `SectionChargeChutageMapper`
  (`ChutageTete`/`ChutagePied DECIMAL(3,2)`, max 9.99), `SectionChargeDecoupeMapper`
  (`LongueurMoyenne DECIMAL(5,3)`) and `SectionChargePitsMapper` (`H2Coulee DECIMAL(2,1)`). Confirmed
  two ways against a live SQL Server round-trip: individually diagnosing `P60_847_682_001`, `_002` and
  `_003` (each threw the same decimal-overflow `SqlException`), and running the Story 3.6 ten-fixture
  SM-2 suite (`EndToEndImportIntegrationTests`) unmodified against the new single-transaction
  `Kape22Persister`, where all 10 real `P60/` samples failed to insert (0/10) before the dimension
  Champs were zeroed out for testing (see below) — not an edge case limited to one or two fixtures.
  Invisible before this story because `Kape22Persister` only ever inserted `L_D_KAPE22` alone;
  Story 4.6's AD-1 single `SaveChanges()` now stages every downstream entity in the same transaction,
  so this defect blocks the **entire** commit (including `L_D_KAPE22`) for real Fichiers.
  evidence: Discovered running `dotnet test --filter Category=Integration` against the local SQL
  Server harness while implementing Story 4.6 — `Kape22FichierProcessorIntegrationTests`,
  `DoubleJournalIntegrationTests`, `EndToEndImportIntegrationTests` (Story 3.6's SM-2, already `done`)
  and `WorkerLoopRobustnessIntegrationTests` all failed on real fixtures until patched (see below).
  `GpaoImportP60WorkerEndToEndTests` (drives the real Launcher via `scripts/e2e-worker-import.ps1`
  against the untouched `P60/P60_847_682_081/082` files) still fails/times out and was left red: its
  fixtures are also relied on byte-for-byte by `Kape22ProductionDataParityTests` (production-mirroring
  parity), so mutating them to dodge this defect would trade one false signal for another. Out of Story
  4.6's authorized scope ("Never: no change inside OrdreFabricationMapper/.../SectionCharge*Mapper").
  User decision (2026-09-16): defer the mapper fix to a dedicated Story 4.3-bis (widened at discovery
  time to cover both the Story 4.3 and Story 4.4 mappers, not Story 4.3 alone); Story 4.6's own tests
  and the pre-existing integration suites above (except `GpaoImportP60WorkerEndToEndTests`) are patched
  to zero out the affected dimension Champs on their fixtures (`ZeroOutOfScaleDimensions`/
  `InsertableFichier`, `tests/Kape22Importer.Tests/TestSupport.cs`, Position/Size sourced from
  `Templates/P60.xml`) so the *transaction/persistence* behavior under test stays provable, at the cost
  of those suites no longer proving the pipeline against real-world dimension values until the mapper
  fix lands. Must be fixed before Epic 4 goes to production — every real P60 Fichier whose applicable
  SectionCharge sections carry a value at or above each column's scale will fail to import until then.

## Resolved by: story-4.3-bis implementation (2026-09-17)

- source_spec: `spec-4-3-bis-correctif-mise-a-l-echelle-decimale.md` / `src/Kape22Importer/DecimalScale.cs`
  summary: resolves the "Deferred from: story-4.6 implementation (2026-09-16)" decimal-scale defect entry
  above, and the two "Deferred from: code review of story-4.6 (2026-09-16)" entries below referencing
  `ZeroOutOfScaleDimensions(XDocument)`/`OutOfScaleDimensionFields` (the hand-maintained-twice risk and the
  missing null-check). Story 4.3-bis wraps the 21 affected columns across the 5 mappers in
  `DecimalScale.Apply` and deletes `ZeroOutOfScaleDimensions`/`OutOfScaleDimensionFields` entirely (no code
  left to carry either risk).
  evidence: `sprint-status.yaml`'s `story-4-6-decimal-scale-defect-story-4-3-bis` action item is marked
  `done` accordingly; the three entries this resolves are superseded (see the "RESOLVED" marker on each,
  below).

## Deferred from: code review of story-4.3-bis (2026-09-17)

- source_spec: `spec-4-3-bis-correctif-mise-a-l-echelle-decimale.md` / `src/Kape22Importer/DecimalScale.cs`
  summary: the 21 per-column scale literals are hand-maintained independently in three places (each
  mapper's call site, its test file, and the `annexe-mapping-dispatch-epic4.md` annex row) with nothing
  cross-checking them against each other — a wrong digit copy-pasted consistently into a mapper and its
  test would pass silently, since `MappingAnnexCompletenessTests` only checks the annex documents *a*
  `Scale`, not that shipped mapper code matches it.
  evidence: Raised by the Blind Hunter layer at Story 4.3-bis's code review. Verified all 21 literals in
  this diff match the annex exactly, so not a live bug today; building a mapper-source-vs-annex
  consistency check is a reasonable Story 4.9 hardening candidate (it already plans "downstream-table
  Unit assertions"), not something this bugfix story's spec asked for.

- source_spec: `src/Kape22Importer/DecimalScale.cs`
  summary: nothing prevents a future 6th mapper from assigning a raw KAPE22 `int`/`int?` directly to a
  narrow `decimal` EF column without going through `DecimalScale.Apply` — no analyzer or reflection-based
  completeness test guards against a regression of the exact defect this story fixes.
  evidence: Raised by the Blind Hunter layer at Story 4.3-bis's code review. Candidate for Story 4.9's
  planned "downstream-table Unit assertions" hardening item rather than this story's own scope.

- source_spec: `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs` /
  `tests/Kape22Importer.Tests/GpaoImportP60WorkerEndToEndTests.cs`
  summary: `Kape22ProductionDataParityTests.MappedFichier_MatchesLegacyProductionRow` only round-trips
  `L_D_KAPE22` (`typeof(L_D_KAPE22).GetProperties()`); it never maps/inserts/reads back
  `L_D_ORDRE_FABRICATION` or the `L_D_SECTIONCHARGE_*` entities, so no test in the verification chain
  (unit, `PersistenceSmokeTests`, `TransactionalPersistenceTests`, or the `GpaoImportP60WorkerEndToEndTests`
  → parity-test E2E path) actually compares a persisted, `DecimalScale`-scaled downstream column against a
  known-correct real production value. Today's 21 scale literals all match the annex, so this is not a
  live bug, but the E2E test proves "no SQL overflow," not "matches production" for these columns.
  evidence: Raised by the Verification Gap Reviewer layer at Story 4.3-bis's code review, via direct
  reading of `Kape22ProductionDataParityTests.cs:100-162` and `L_D_KAPE22.cs:62-186`. Extending production
  parity to the downstream decimal-scale tables is pre-existing scope (set by the Epic 2/3 test design,
  not touched by this diff) and a natural fit for Story 4.9 or a later hardening pass.

- source_spec: `scripts/e2e-worker-import.ps1` (closing `Kape22ProductionDataParityTests` block)
  summary: the script's final `dotnet test ... Kape22ProductionDataParityTests ...` invocation never
  checks `$LASTEXITCODE`, unlike the earlier `dotnet build $launcherProject` step in the same script
  (which does, with an explicit `throw`). A failing production-parity comparison would not fail the
  wrapping process, so `GpaoImportP60WorkerEndToEndTests` (which only asserts `process.ExitCode == 0`)
  could not detect it.
  evidence: Raised by the Verification Gap Reviewer layer at Story 4.3-bis's code review; confirmed
  empirically that a non-zero-exit native command under `$ErrorActionPreference = 'Stop'` does not
  terminate the pwsh script by default. Script is unchanged by this diff (pre-existing gap), out of this
  bugfix story's scope.

## Deferred from: code review of story-4.3-bis (2026-09-18)

- source_spec: `spec-4-3-bis-correctif-mise-a-l-echelle-decimale.md` / `src/Kape22Importer/DecimalScale.cs`
  summary: `DecimalScale.Apply` fixes decimal-point placement (scale) but never validates the scaled result
  against each column's total `DECIMAL(p,s)` precision. An outlier raw KAPE22 int whose scaled result
  still exceeds the column's magnitude limit (not just its scale) would still throw a raw, undiagnosed SQL
  overflow at persist time — the same failure class this story fixes, narrowed but not closed.
  evidence: Raised independently by the Blind Hunter and Edge Case Hunter layers at the code review of
  story-4.3-bis (`git diff a277925^..HEAD`). Explicitly out of this bugfix story's stated boundary ("Ask
  First: None — conversion values are fixed by the annex/DDL, no design choice beyond DecimalScale's shape
  (dictated by AD-2 + existing precedent)"); a magnitude guard would be a new design choice, not a
  literal-scale fix. Candidate for Story 4.9's hardening pass.

## Deferred from: code review of story-4.6 (2026-09-16)

- source_spec: `src/Kape22Importer/Persistence/Kape22Persister.cs` (`PersistMapped`'s
  `couleeAlreadyExists` check)
  summary: the Coulee existence check (`context.CouleeRows.Any(...)`) and the later
  `context.CouleeRows.Add(bundle.Coulee!)` are a classic check-then-act race: two Fichiers naming a
  brand-new, not-yet-committed Coulee (architecturally expected — "several OF routinely dispatch from
  the same cast") and processed close together, each on its own per-Fichier `DbContext`/transaction,
  could both see "not exists" and both try to insert the same `IdCoulee`, producing a PK-violation
  `PersistenceError` on an otherwise entirely valid Fichier.
  evidence: Raised by the Blind Hunter layer at Story 4.6's code review. Not reachable under the
  current architecture: `InboxScanner.RunTick` processes Fichiers one at a time in a single worker
  (Story 3.2/3.5), so no two `Kape22Persister.Persist` calls run concurrently today. Same shape and same
  non-blocking rationale as the pre-existing "Deferred from: Story 2.8" entry below ("Anti-duplicate
  guard reads outside the write transaction... revisit if the orchestrator ever processes Fichiers
  concurrently").

- **RESOLVED** — see "Resolved by: story-4.3-bis implementation (2026-09-17)" above.
  source_spec: `tests/Kape22Importer.Tests/TestSupport.cs` (`ZeroOutOfScaleDimensions(XDocument)` /
  `OutOfScaleDimensionFields`)
  summary: the 21 out-of-scale Champs are hand-maintained twice — once by element name for the
  post-Converter `XDocument` mutation, once by raw `(Position, Size)` tuples for the byte-level fixture
  mutation (cross-referenced only by a comment pointing at `Templates/P60.xml`) — with nothing verifying
  the two lists stay in sync.
  evidence: Raised by the Blind Hunter layer at Story 4.6's code review. Both lists are test-only
  scaffolding for working around the Story 4.3-bis decimal-scale defect above; low risk while that defer
  is short-lived, but a future edit to one list without the other would silently narrow test coverage.

- source_spec: `tests/Kape22Importer.Tests/TestSupport.cs` (`WithDetailChamp`)
  summary: `WithDetailChamp` indexes `lines[1]` after `Split("\r\n")` and slices `[position..(position +
  size)]` with no bounds check; a fixture that doesn't split into at least two lines, or a
  position/size past the Detail line's length, throws a raw `IndexOutOfRangeException`/
  `ArgumentOutOfRangeException` instead of a descriptive failure.
  evidence: Raised by the Edge Case Hunter layer at Story 4.6's code review. Test-only helper; every
  current caller uses fixed, known-good positions from `Templates/P60.xml` against the real fixture
  files, so the gap is latent, not currently reachable.

- **RESOLVED** — see "Resolved by: story-4.3-bis implementation (2026-09-17)" above.
  source_spec: `tests/Kape22Importer.Tests/TestSupport.cs` (`ZeroOutOfScaleDimensions(XDocument)`)
  summary: `ZeroOutOfScaleDimensions(XDocument)` dereferences `document.Root!.Element("message")!` with
  no null check before probing for each Champ; an `XDocument` whose root has no `message` element throws
  a raw `NullReferenceException` instead of a descriptive test-setup failure.
  evidence: Raised by the Edge Case Hunter layer at a second-pass code review of Story 4.6 (2026-09-16).
  Test-only helper, same latent/not-currently-reachable category as the `WithDetailChamp` bounds gap
  above — every current caller passes a real post-Converter `XDocument` from a valid P60 fixture.

## Deferred from: code review of story-4.3 (2026-09-15)

- source_spec: `epics.md` § Story 4.3 / `annexe-mapping-dispatch-epic4.md` § L_D_ORDRE_FABRICATION
  summary: `OrdreFabricationMapper.Map` widens `DiametreProduit`, `Epaisseur` and `LongueurCD` from
  `L_D_KAPE22`'s `int?` columns straight to `L_D_ORDRE_FABRICATION`'s `decimal` columns with no
  scale/unit conversion. Whether the legacy KAPE22 ints actually encode a sub-unit (e.g. tenths of mm)
  that the target decimal columns expect at full-unit precision is not settled by the Story 4.2 annex
  text alone.
  evidence: Constaté à la revue de code Story 4.3 (Edge Case Hunter, hors mandat, confirmé Acceptance
  Auditor). Nécessite une vérification métier/legacy (référent KAPE22 ou lecture directe d'un
  échantillon de production) plutôt qu'une décision de code seule.

## Deferred from: code review of story-4.1 (2026-09-14)

- source_spec: `epics.md` § Story 4.1
  summary: Aucun test ne vérifie l'unicité des clés composites des 10 nouvelles tables aval (par
  exemple deux lignes `(OF, CodeOperation)` dans une `L_D_SECTIONCHARGE_*`) ; le smoke test round-trip
  ajouté (`PersistenceSmokeTests.SchemaApplies_AndAllTenDownstreamTablesRoundTripUnderRollback`) ne
  relit pas non plus les valeurs insérées — il vérifie seulement qu'un unique `SaveChanges()` sur les 10
  `Add` ne lève pas d'exception.
  evidence: Constaté à la revue de code Story 4.1 (Blind Hunter). Non bloquant : aucun `AC` de la story
  ne l'exige, et le smoke test existant pour `L_D_KAPE22` (Story 2.1) suit le même niveau de
  vérification. La parité colonne-par-colonne (nom, nullabilité, type CLR, longueur) est déjà couverte
  ailleurs (`SchemaModelParityTests`, `DownstreamColumnLengthsParityTests`).

- source_spec: `epics.md` § Story 4.1
  summary: Dérive de nommage entre tables à re-vérifier contre le schéma source réel (AFV004-LSI) :
  `L_D_ORDRE_FABRICATION.OFOrigine` vs `L_D_SECTIONCHARGE_REFROIDISSOIRS.OFOrigin` (pas de "e" final),
  et `Nuance` (`L_D_ORDRE_FABRICATION`/`L_D_COULEE`, longueur 7) vs `NuanceMarquage`
  (`L_D_SECTIONCHARGE_REFROIDISSOIRS`, longueur 6).
  evidence: Constaté à la revue de code Story 4.1 (Blind Hunter). Pourrait être une différence réelle du
  schéma (risque R-3 : rien n'est réécrit de mémoire) ou une coquille de transcription lors du sqlcmd du
  2026-09-14 ; ni moi ni les couches de revue n'avons d'accès direct à AFV004-LSI pour trancher. À
  reconfirmer lors d'une prochaine session sqlcmd contre la source.

- source_spec: `epics.md` § Story 4.1
  summary: `L_D_CONSIGNES.ConsigneGPAO` (`BIT`) fait partie de la clé primaire composite
  `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` — un booléen dans une clé métier est inhabituel.
  evidence: Constaté à la revue de code Story 4.1 (Blind Hunter). Cohérent avec l'index réel tel que lu
  par `sqlcmd` sur `sys.indexes` le 2026-09-14 (attesté par le commentaire d'en-tête du script), mais à
  reconfirmer contre le schéma source plutôt que supposé correct par défaut.

- source_spec: `epics.md` § Story 4.1 (hors périmètre AC, correctif groupé dans le même diff)
  summary: Le correctif anti-deadlock de `GpaoImportP60WorkerEndToEndTests`
  (`process.StandardOutput/StandardError.ReadToEndAsync()` + `Task.WaitAll(...)`) fonctionne mais bloque
  un thread du pool pour la durée du process enfant ; un test `async Task` avec
  `await Task.WhenAll(...)` + `process.WaitForExitAsync()` aurait été plus idiomatique.
  evidence: Constaté à la revue de code Story 4.1 (Blind Hunter). Cosmétique, aucun impact fonctionnel
  observé (`Category=Integration`, jamais exécuté en CI sans surveillance).

## Deferred from: code review of story-3.6 (2026-09-11)

- source_spec: `epics.md` § Story 3.6 "Réancrage" — **RÉSOLU 2026-09-11**.
  summary: ~~Le test de fumée niveau `Client` (`MicroServices.sln`) et `AC-FR14-5` n'existaient ni dans ce dépôt ni dans `MicroServices.sln`~~. `AC-FR14-5` existait en fait déjà, mais ailleurs que là où je l'avais cherché la première fois : `Launcher.Tests/WorkerRegistryTests.cs` (`WorkerRegistry_RegistersGpaoImportP60AsAWorkerAdapter_AcFr14_5`), l'endroit architecturalement correct puisque `WorkerAdapter<TClient>` est générique et partagé par tous les workers. Le smoke test manquait réellement ; ajouté dans `GPAO/ImportP60.Tests/EndToEndSmokeTests.cs` (`Tick_OneReferenceFichier_InsertsOneRowThroughTheRealPipeline`) — `Client.CreateAsync` lui-même n'étant pas testable (constructeur + `CreateAsync` ouvrent un vrai sink Serilog SQL, un vrai broker et une vraie connexion SQL Server pour la porte FR-8), le test exerce `Client.RunTickCore` avec le vrai `Kape22FichierProcessor` sur EF InMemory et le fixture réel `P60_847_682_001` (lu depuis le dépôt `TextToXml` voisin).
  evidence: Constaté à la revue de code Story 3.6 (Acceptance Auditor), confirmé par inspection directe du dépôt `MicroServices.sln`, puis corrigé. En écrivant et lançant le smoke test, un test pré-existant sans rapport (`RunTickCoreTests.RunTickCore_WhenTheFileSourceThrows_...`) s'est révélé caduc : la Story 3.5 (côté `TextToXml`) fait désormais avaler par `InboxScanner` les erreurs de listage de dossier en interne (Warning + retry, AC-FR15-2) au lieu de les laisser remonter à `onError` — corrigé et renommé `RunTickCore_WhenTheFileSourceThrows_ContainsTheFailureAndNeverCallsOnError`. `GpaoImportP60.Tests` : 10/10 verts ; `Launcher.Tests` : 2/2 verts.

- source_spec: `epics.md` § Story 3.6
  summary: `Options()`, `Configuration()`, `Now`, `InitiatingServer`, `InboxRoot`, `TenFichiers` sont copiés-collés à l'identique dans les 3 nouveaux fichiers de test (`EndToEndImportIntegrationTests`, `EndToEndPerformanceTests`, `ErrorsReportReadabilityTests`) au lieu d'être centralisés dans `TestSupport.cs` (déjà `using static` par les trois).
  evidence: Constaté à la revue de code Story 3.6 (Blind Hunter). Non corrigé dans le patch de revue : le même motif de duplication pré-existe déjà dans `WorkerLoopRobustnessTests.cs` et `Kape22FichierProcessorTests.cs` (mêmes valeurs `Options()`/`Configuration()`/`InitiatingServer = "AFS017"`) depuis des stories antérieures — un correctif limité aux 3 nouveaux fichiers aurait été un nettoyage incohérent. Correctif = passe hygiène dédiée (façon story-0) migrant tous les appelants vers `TestSupport.Options()`/`Configuration()`/`Now`/`InboxRoot`/`TenFichiers`.

- source_spec: `epics.md` § Story 3.6 (NFR-2)
  summary: Le test NFR-2 (500 Fichiers < 30 s) ne mesure pas 500 insertions réelles : les 50 copies de chaque échantillon partagent un Header, donc le garde-fou anti-doublon D22 court-circuite l'insertion sur 490/500 Fichiers.
  evidence: Auto-documenté dans le code par un commentaire `ponytail:` (`EndToEndPerformanceTests.cs:104-106`) avec chemin d'évolution nommé (« Swap in 500 distinct Headers if NFR-2 must time 500 real inserts »). Non bloquant ; listé ici pour visibilité au-delà du commentaire inline.

- source_spec: `epics.md` § Story 3.6 (SM-2)
  summary: La vérification d'archivage SM-2 (`EndToEndImportIntegrationTests`) ne contrôle que l'existence des fichiers archivés (`source.Exists`), pas l'égalité octet à octet avec le Fichier d'origine.
  evidence: Constaté à la revue de code Story 3.6 (Blind Hunter). Risque limité (l'archivage est une simple copie de fichier), mais une troncature/corruption pendant le déplacement passerait inaperçue.

- source_spec: `epics.md` § Story 3.6 (SM-3)
  summary: Le commentaire de classe d'`ErrorsReportReadabilityTests` promet que chaque cause porte une colonne (« colonne »), mais le test Step-1 (`InvalidInteger`, Diametre non numérique) n'asserte jamais sur `Column`.
  evidence: Constaté à la revue de code Story 3.6 (Blind Hunter). Reste à clarifier si une cause Step-1 doit porter `Column = null` ou une valeur, puis ajouter l'assertion correspondante.

## Deferred from: code review of story-3.5 (2026-09-10)

- source_spec: `epics.md` § Story 3.5
  summary: Aucune borne « poison-pill / max-attempts » dans `InboxScanner`. Un Fichier qui échoue de façon déterministe (faute I/O répétée à la lecture, ou Fichier malformé pendant une longue panne `AscoLSI`) est retraité intégralement à chaque tick — `Converter` + `Kape22Mapper` + tentative DB — un `Warning` par tick, indéfiniment, indistinguable en `processing/` d'un Fichier qui retente légitimement.
  evidence: Constaté à la re-revue Story 3.5 (couches blind-hunter + edge-case-hunter). Aucun `AC-FR15-x` ne l'exige ; `AC-FR15-3` veut au contraire un retry infini tant qu'`AscoLSI` est injoignable. Correctif éventuel = compteur de tentatives par Fichier (ou âge de première apparition) → mise en quarantaine `error/` comme poison après N ticks.

- source_spec: `spec-3-4-gpao-importp60-client-worker.md`
  summary: `InboxScanner.PurgeRetention()` a gagné un `try/catch (IOException/UnauthorizedAccessException)` par racine (Story 3.5, B4) mais toujours pas de `CancellationToken`. Un balayage récursif long sur un gros `archive/` ne peut pas être interrompu dans le budget d'arrêt que la Story 3.4 a établi pour `RunTick`.
  evidence: Jumeau du `CancellationToken` de `RunTick` (résolu 2026-09-09). `PurgeRetention` est appelé après `RunTick` dans `Client.Actions` ; un `Stop()` du Launcher pendant la purge attend la fin du balayage.

- source_spec: `epics.md` § Story 3.5
  summary: `InboxScanner.TryStableInboxFichiers` sonde `List(inbox)` deux fois de suite sans délai ni comparaison de `LastWriteTimeUtc`. Sur le `DirectoryFileSource` réel, deux `stat` consécutifs rapportent la même taille même pour un Fichier en cours d'écriture — la porte de stabilité `AC-FR12-5` est quasi un no-op hors du cas de test in-memory (`MarkUnstableOnce`).
  evidence: Pré-existant Story 3.1 (`StableInboxFichiers`), renommé mais sémantiquement inchangé par la Story 3.5. Correctif = un court délai inter-sondes, ou comparer aussi `LastWriteTimeUtc`.

- source_spec: `epics.md` § Story 3.5
  summary: Dans `InboxScanner.PurgeRetention`, le `fileSource.Delete` par fichier n'est pas gardé individuellement : un seul fichier verrouillé lève dans la boucle `foreach` et bloque la purge du reste de cette racine (le `catch` englobant abandonne toute la racine).
  evidence: Constaté à la re-revue Story 3.5 (edge-case-hunter). Pré-existant Story 3.1 pour la boucle ; le `catch` par racine est neuf (B4). Correctif = `try/catch` autour du `Delete` unitaire + `continue`.

## Deferred from: code review of spec-3-4-gpao-importp60-client-worker (2026-09-09)

- FR-8 tests build the model with `UseInMemoryDatabase` while production `CreateAsync` uses
  `UseSqlServer`; the EF `IModel` differs (column types, nullability) — exactly what FR-8 checks.
  Exhaustive `AC-FR8-1..3` live in `Kape22Importer.Tests` on the AR-12 SQL Server harness by spec;
  the worker-level FR-8 tests are wiring smoke only.
- Non-`StartupCompatibilityException` faults from `NewAscoLsiContext()` / `StartupCompatibilityCheck.Verify`
  inside `Client.CreateAsync` (malformed connection string, provider error) escape the `catch`;
  they are caught by `WorkerAdapter.StartAsync` (sets `LastError`, rethrows) rather than the
  intended "deployment fault → LogError + return without starting the loop".
- `Client.Dispose()` does not `Cancel()` the `CancellationTokenSource`; any disposal path not
  routed through `Stop()` first tears down mid-tick without signalling cancellation. In practice
  `WorkerAdapter.StopAsync` always calls `Stop()` before `Dispose()`.
- `GpaoImportP60.WorkerService` (standalone `dotnet run` host only): `RetryHelper.RetryAsync`
  constructs a fresh `Client` per attempt without disposing the previous one; `StopAsync` calls
  `Stop()` + `Disconnect()` but never `Dispose()`. Inherited from the `OrdresFabricationSync`
  model; the Launcher drives `Client` directly so production is unaffected.
- `WorkerAdapter.IsRunning` (= `Client.IsConnected`) stays true after `CreateAsync` returns on the
  FR-8-incompatible or broker-never-connected branch, with `WorkerAdapter.LastError` null — the
  dashboard shows the worker running while it processes nothing. The spec I/O matrix accepts
  "IsConnected reste vrai"; surfacing the idle state (LastError / a dedicated status) is a
  follow-up.

## Correction of course: Story 3.4 rejected — Launcher alignment (2026-09-09)

Story 3.4 (as implemented 2026-09-08/09) was rejected in adversarial review: it rebuilt, standalone
and disconnected, the whole supervision stack the portal `Launcher` (`MicroServices.sln`) already
owns. User decision (Option A): `Kape22Importer` becomes a **library**; the worker is a thin
`class Client : Publisher` registered in the `Launcher`. See
`_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-09.md`.

- **3.4 working tree rolled back** (nothing was committed). Only survivor from that branch: the
  `Microsoft.Extensions.Hosting` CPM entry stays for now (still used while `Kape22Importer` is a
  Worker project); its removal moves into **Story 3.0** (library conversion).
- **`AddDbContextFactory` / DI composition root abandoned** — there is no host for the worker any
  more. The `Client` builds its `AscoLsiDbContext` per tick by hand
  (`new DbContextOptionsBuilder<>().UseSqlServer(cs)`), like `OrdresFabricationSync.Client`.
  `Kape22FichierProcessor` keeps its `Func<AscoLsiDbContext>` seam (Story 3.2).
- **Logging seam revisited (Story 3.3)** — `Kape22FichierProcessor` no longer takes an `ILogger`
  wired by a `Program.cs` Serilog sink. The `Client` routes the journal to `AbstractService`'s
  shared Serilog `Logger` (`SharedLogger`), via `Serilog.Extensions.Logging` bridge or a small
  `IImportJournal` seam. Decided in Story 3.0 / 3.4.
- **`scripts/schema/02-mqtt-tables.sql`** — `dbo.WorkerSettings` block removed (Launcher-owned).
  `PersistenceSmokeTests` / `SqlServerIntegrationFixture` updated accordingly.
- **Still relevant, moves to Story 3.0 hygiene**: `IConfiguration` + `ImportOptions` overlap in
  `Kape22Persister` (see "Deferred from: code review of Story 3.2 / epics.md" below).
- **Still Story 3.5**: coarse per-tick failure handling (which failures leave the Fichier in
  `processing/` vs `error/`).

## Resolved by: epics.md reconciliation, retro Épic 2 action A-3 / item-10 (2026-09-08)

The four `AC` that crossed the Épic 2 → Épic 3 boundary through deferred-work notes now have an
explicit ledger in `epics.md` (new subsection at the head of Épic 3), each attached to its owning
story. Supersedes every scattered "AC-FR6-4 / AC-FR10-7 logging half / skip-doublon signal" note
below:

- **AC-FR6-4** — done and tested at `ConversionResult` (Story 1.7). Extended to `ImportResult` by
  **Story 3.3**: `Errors` and `Warnings` each sorted by `LineNumber` ascending, **independent lists**.
  The "merged/fusionnée Errors+Warnings list" wording from the 2.7/2.8 notes is dropped — the PRD AC
  and its tests were always about two independently-sorted lists.
- **Logging half of AC-FR10-7 / AC-FR11** — is `AC-FR14-1` / `AC-FR14-8`, already **Story 3.3**.
- **AC-FR12-3 (`ImportResult.XmlArchivePath`)** — **Story 3.2, done**. Residual (`InboxScanner` does not
  read the field) is the Story 3.3 `IFichierProcessor` seam decision.
- **Skip-doublon explicit signal** — **Story 3.3** adds an explicit `ImportResult` discriminator
  (`AlreadyImported` flag or `Outcome` enum) rather than the structural
  `Success && InsertedId == null && Errors.Count == 0` check.
- **AddDbContext vs AddDbContextFactory + persister lifetime (F-11)** — requalified by the 2026-09-09
  correction of course (see top section): no DI container / host for the worker. The `Client` builds
  its `AscoLsiDbContext` per tick by hand; `Kape22FichierProcessor` keeps its `Func<AscoLsiDbContext>`
  seam; `Kape22Persister` built per Fichier in the orchestrator. `AddDbContextFactory` /
  `AddKape22Startup` dropped.

## Resolved by: code review patches of Story 3.2 (2026-09-08)

- **AC-FR13-1/3/4/5 had zero CI coverage** (only `[SkippableFact]` Integration tests) — decision D1.
  `Microsoft.EntityFrameworkCore.InMemory` added as a test-only dependency; `Kape22FichierProcessorTests`
  now runs the mapper-failure and success branches (result shape, `NormalizedXml`, `XmlArchivePath`,
  fresh-context-per-call, warning merge) in `Category=Unit` over an in-memory `AscoLsiDbContext`. The
  Integration tests stay for the real-SQL transaction and column-length assertions.
- **An uncaught exception from `IFichierProcessor.Process` unwound the whole tick** (AC-FR13-4) —
  decision D2. `InboxScanner.ProcessFromProcessing` now wraps `processor.Process` in a try/catch: the
  Fichier is logged at Error, quarantined in `error/` with an `.errors.json`, and the loop continues.
  Story 3.5 still owns the refinement of which failures should instead leave the Fichier in
  `processing/` for a retry (AC-FR15-3).
- **Mapper FR-10 coherence warnings were dropped on a rejected Fichier** — `Kape22Persister.PersistRejected`
  returns no `Warnings`, so `Kape22FichierProcessor` now merges `map.Warnings` directly
  (`[.. conversion.Warnings, .. map.Warnings]`) instead of reading them back off the persister result.
- Minor: `Kape22FichierProcessor.Import` guards `content` / `fichierName` at the trust boundary;
  `ParisTime.Instance` renamed `ParisTime.Zone`; `Merge` helper inlined.

## Deferred from: code review of Story 3.2 / epics.md (2026-09-08)

- **`Kape22FichierProcessor` takes both `IConfiguration` and `ImportOptions`, with overlapping fields** —
  `Kape22Persister` still reads `Import:InitiatingServer` / `Import:Commande` straight from
  `IConfiguration`, while `ImportOptions.InitiatingServer` carries the same value; `options` is otherwise
  used only for `ArchiveFolder`. Two sources of truth in one type. The clean fix changes
  `Kape22Persister`'s constructor to take `ImportOptions` (a Story 2.8 seam), tied to the still-open
  epic-2 retro item 10 ("trancher AddDbContext vs AddDbContextFactory + durée de vie du persister").
- **No orchestrator test for the no-OF (D15) rejection path** — `Import_MapperFailure_*` covers only the
  OF-readable `REJETÉ` case. When deserialization itself fails, `map.OF` is null and `Kape22Persister`
  writes nothing; the orchestrator still returns `NormalizedXml` for `error/`. The persister's D15 branch
  is covered by `TransactionalPersistenceTests`; an end-to-end orchestrator case is hard to construct
  from raw bytes (a Converter-valid Fichier that fails P60.xsd deserialization) and low value. Revisit
  in Story 3.6 (end-to-end harness).
- **`XmlArchivePath` and `InboxScanner.Archive` still read the clock at different instants** — even with a
  shared date-folder helper, `Kape22FichierProcessor.Import` computes the path when it runs and
  `InboxScanner.Archive` writes the file later; a tick that straddles Paris midnight on the 1st of a
  month names one `<yyyy>/<MM>` folder on the result and writes to another. Negligible for a file-import
  worker; the real fix is the Story 3.3 seam decision (`InboxScanner` consumes `ImportResult`, or the
  orchestrator owns the physical archive write).

## Resolved by: Epic 3 story-0 hygiene, retro action A-1 volets (a)(c)(d) (2026-09-08)

- **(c) `Europe/Paris` timezone duplicated four ways** (F-12, notes 2.6 / 2.8) — `Kape22Mapper`,
  `Kape22Persister`, `InboxScanner` and `Kape22FichierProcessor` each carried their own
  `private static readonly TimeZoneInfo ParisTimeZone = FindSystemTimeZoneById("Europe/Paris")`.
  Replaced by one `internal static ParisTime.Instance` (`Lazy<TimeZoneInfo>`, IANA id then
  `"Romance Standard Time"` fallback, `TimeZoneNotFoundException` if neither). All four static fields
  are gone.
- **(d) `Kape22Mapper.Map`'s reflective `SetValue` was unguarded** (F-7, notes 2.4 / 2.5) — a
  Champ/column CLR-type mismatch that escaped the FR-8 startup check would throw a raw reflection
  `ArgumentException` out of `Map`, breaking its "never throw for data reasons" contract. Now caught and
  re-thrown as a `StartupCompatibilityException` (deployment-fault family). No dedicated test: the path
  is unreachable while the descriptor, the P60.xsd, the generated DTO and the entity agree (locked by
  `P60XsdTests` / `SchemaModelParityTests` / `StartupCompatibilityTests` at build time); a softer
  `ConversionError` code would touch the frozen `ErrorCode` contract. The Story 3.2 code review
  (2026-09-08, decision D3) accepted this CC-1 exception as-is.
- **(a) test scaffolding duplicated across the suite** (F-10, notes 2.4 / 2.5 / 2.6 / 2.8) —
  `PersistenceTestSupport` promoted to `tests/Kape22Importer.Tests/TestSupport.cs`. The reference
  Fichier name, `WinterClock` / `FixedClock`, `ConvertReferenceFichier`, `ReadValidFixture`,
  `NonConformantNormalizedXml`, the embedded-resource reader (`EmbeddedP60Xsd`, resource-name consts)
  and `WithText` now live there once; `DerivedFieldsTests`, `CoherenceWarningsTests`, `Kape22MapperTests`,
  `P60XsdTests`, `P60DescriptorTests` and the two Story 3.2 test files consume it via
  `using static Kape22Importer.Tests.TestSupport;` (net -111 lines). Note 2.6 also folded in: the
  `Kape22MapperTests` `Map(xml, name)` call sites now pass `WinterClock()`.

### A-1 volet (b) — done 2026-09-08

- **`Kape22Mapper` split** (F-2) — `CoherenceChecker` (public static, FR-10 Warnings) and `DerivedFields`
  (public sealed class, ctor `TimeProvider`, FR-9 roulette / DateReception / day-of-year) are now their
  own files. `Kape22Mapper` is a `sealed class` with a constructor-injected clock and an instance
  `Map(normalizedXml, sourceFileName)` — the PRD reference API. The Annexe B naming tables (`IsIgnored`,
  `ResolveTargetName`, `NamingExceptions`, `LegacyBlankFillColumns`, `DefaultForNonNullable`) stay
  `public static` on `Kape22Mapper`, so `RequiredFieldCheck` / `StartupCompatibilityCheck` and the
  completeness tests are untouched. Pure refactor, 592 tests unchanged. `Kape22FichierProcessor` now
  does `new Kape22Mapper(timeProvider).Map(...)`; `TestSupport.Map(xml, name)` funnels the mapper tests.
  `epic-2-retro-item-8` (A-1) is now fully closed.

## Deferred from: Story 3.2 (2026-09-08)

- **`ImportResult.InsertedId` / `XmlArchivePath` never reach `InboxScanner`** — `Kape22FichierProcessor`
  exposes the full `ImportResult` through `Import`, but the `IFichierProcessor.Process` adapter narrows
  it to `FichierProcessingResult` (Errors / NormalizedXml / Warnings), which is all `InboxScanner`
  consumes. Story 3.3 (double journalisation) needs `InsertedId`, the row count and the durée for the
  `MQTTnetServices.Logs` `Information` line — decide there whether `IFichierProcessor.Process` returns
  `ImportResult`, or `InboxScanner` calls `Import` directly, or the scanner is folded into the
  orchestrator's caller.
- **Archive-date-folder formula duplicated** — `Kape22FichierProcessor.ArchivePath` and
  `InboxScanner.Archive` both compose `{ArchiveFolder}/{parisYear:D4}/{parisMonth:D2}` from a Paris-
  local `timeProvider.GetUtcNow()`, and both carry their own
  `TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")` static (the tz-data risk already tracked repo-
  wide). Extract one helper (on `ImportOptions`, or a small `ArchiveLayout`) so the scanner's physical
  write and the orchestrator's reported `XmlArchivePath` cannot drift. Story 3.4 hardening.
- **`Kape22FichierProcessor` has no `ILogger`** — dropped from the ctor for now (unread parameter fails
  `-warnaserror`). Story 3.3 adds it back with the double-journalisation wiring.
- **Warnings merge is concatenation, not AC-FR6-4** — `Merge` puts Step 1 Segment warnings ahead of the
  persister's warnings without sorting by `LineNumber`. AC-FR6-4 (one merged Errors+Warnings list sorted
  by `LineNumber`) is still an open reconciliation in `epics.md` (epic-2 retro action item 10); wire it
  through here once that lands.
- **`AC-FR13-1` order is proven by end-state, not call tracing** — the integration test asserts the
  committed rows + non-null `InsertedId`/`NormalizedXml`/`XmlArchivePath`, which can only all be true if
  every step ran in order. The literal "capture XML *before* Map" from the AC is structural (the XML is
  held in memory; physical placement in archive/ vs error/ is `InboxScanner`'s, after the outcome is
  known). No standalone spy-based ordering test.
- **No integration test pairs `Kape22FichierProcessor` with `InboxScanner`** — the orchestrator tests
  drive `Import` directly. The end-to-end tick (in-memory `IFileSource` + real orchestrator + real DB)
  is Story 3.6, and the DI composition root is Story 3.4.

## Deferred from: code review of story 3.1 (2026-09-08)

- **No per-Fichier exception isolation in `RunTick` / `ProcessFromProcessing`** — an unreadable or
  poison Fichier (throw from `fileSource.Read` or `processor.Process`) unwinds the whole tick; every
  later Fichier in that batch is skipped, and next tick the same Fichier is first in the `processing/`
  resume scan and throws again — a permanent head-of-line block. The resume loop is created by
  AC-FR12-6 here, but the fix (try/catch per Fichier + a quarantine/`error` path for infrastructure
  failures) belongs to Story 3.5 (robustesse de la boucle worker).
- **Stability check does two `List("")` probes with no interval between them** — inert against a real
  slow upload on `DirectoryFileSource` (both `FileInfo.Length` reads happen in the same instant); a
  freshly-created 0-byte Fichier also passes both probes. `InboxScannerTests` only pass because
  `InMemoryFileSource.MarkUnstableOnce` fakes the disagreement. Story 3.4 owns worker timing — carry
  size memory across ticks, or gate on `LastWriteTimeUtc` older than N seconds, and drop zero-length.
- **Retention age is the Fichier's own mtime, not the archival time** — `Archive` uses `File.Move`,
  which preserves the original timestamp, and `PurgeRetention` filters on `entry.LastWriteTimeUtc`. A
  Fichier that aged on the FTP share past `RetentionDays` is archived and then purged on the next
  run — the archive copy is lost. Purge should run off archival time (dated folder, sidecar write
  time, or a touch on move). Story 3.2/3.4 hardening.
- **Name collisions overwrite silently** — `Archive` / `Reject` use `Move(..., overwrite: true)` and
  `Write` overwrites; two Fichiers with the same name in the same month, or a Fichier named
  `<x>.xml` colliding with another Fichier's sidecar, clobber the earlier archived copy with no
  guard and no log. Story 3.2.
- **`FichierProcessingResult.Warnings` is declared but never read by `InboxScanner`** — processor
  warnings are dropped on both the success and the rejection branch. Story 3.3 (double logging) is
  the consumer; surface them (log at Warning, or a `<name>.warnings.json` sidecar) there.
- **Rejections log at `Information`, same level as successes** — `ProcessFromProcessing` emits one
  `LogInformation` for both outcomes. A bad-data reject is an operational event and should surface
  at Warning with the error count. Story 3.3.
- **`TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")` in a static initializer with no fallback**
  — `TypeInitializationException` on first use if the host lacks the tz data / runs
  invariant-globalization. Pre-existing pattern, duplicated verbatim from `Kape22Mapper` and
  `Kape22Persister`; fold a shared resilient accessor into a repo-wide hygiene pass.
- **`PurgeRetention` never removes emptied `<yyyy>/<MM>` directories** — `archive/` accumulates empty
  month folders forever; `DirectoryFileSource` has no directory-cleanup path. Story 3.4.
- **`DirectoryFileSource` adapter hardening** — no path-traversal guard on `folder` / `name` (a `..`
  segment escapes the reception root); `List` / `ListRecursive` throw and abort the tick if a file
  vanishes between `EnumerateFiles` and `FileInfo`; `Move` throws `FileNotFoundException` on a
  missing source though the interface only promises silent-missing for `Delete`. Story 3.2/3.4.
- **No integration test pairs `InboxScanner` with `DirectoryFileSource`, and no DI composition root
  roots the adapter at `Import:InboxPath`** — every `InboxScannerTests` case uses the in-memory fake;
  the two implementations can drift (recursive-list folder semantics, move-overwrite, list order).
  Story 3.4 owns DI + the `PeriodicTimer` worker loop and was explicitly deferred by the story.

## Resolved by: spec-parity-kape22-legacy-fill-rules (2026-09-07)

`Kape22ProductionDataParityTests` (Category=Integration, opt-in behind
`ConnectionStrings:AscoLSI_Production`) replays the 100 `P60/P60_847_682_0xx..1xx` sample Fichiers
through `Converter` + `Kape22Mapper`, round-trips the mapped entity through the test database, and
diffs it against the legacy row in the **production** `L_D_KAPE22` (matched by OF + NumeroFichier, a
clean 1:1). FR-7 requires the new insert to be identical. Four columns diverged; a full-table
production profile (17 710 rows, read-only) settled the rules and `Kape22Mapper` now applies them
(Annexe B "Valeurs par défaut du legacy pour un Champ vide"):

- **Blank int Champ → `0`** — general rule in `DefaultForNonNullable`. Production has **no `NULL` in
  any of the ~28 `int` columns**, so the legacy zero-fills every blank int Champ. Covers the
  original `MatriculeClient` / `ChutagePied` divergences and the whole set.
- **`OForiginInterne` blank → `NULL`** — column-scoped. It is the only string column ever `NULL` in
  production; its `OF*` siblings stay `''`.
- **`AcompteSolde` blank → `'S'`** — column-scoped. `'S'` in 100 % of 17 710 rows.

`Kape22ProductionDataParityTests` passes 100/100 with only `Client` left in
`KnownLegacyDivergences`.

### Still deferred

- **Populated-branch of `OForiginInterne` / `AcompteSolde` is `assumed, unverified`** — no sample
  Fichier carries a value for either Champ and the legacy import source is **not available** (it is
  the external current application, not in the database: no proc/view/job references `KAPE22` /
  `L_D_KAPE22`). The mapper copies a populated value verbatim (tests
  `Map_Populated{OForiginInterne,AcompteSolde}Champ_*_AcFr7_2` carry the `assumed, unverified`
  comment). Revisit when a populated sample appears or the legacy source is found — a mapping other
  than verbatim (e.g. `A`/`S` normalisation for `AcompteSolde`) would need a code change.

### Permanent (not deferred)

- **`Client` mojibake** — `P60_847_682_095` / `_151` hold raw `Client` bytes `53 4B 46 20 D6 …`;
  `0xD6` is `Ö` in Windows-1252, so `SKF Österreic` is the correct decode and production stored the
  corruption. The new pipeline decodes correctly and is intended to diverge. `Client` stays in
  `KnownLegacyDivergences` forever. Decision **(b)**: `Client`-only — a wider parity run could
  surface other legacy-mojibake string columns as false positives; diagnosable, revisit then rather
  than building a generic mojibake-aware comparison now.

## Resolved by: Story 2.8 (2026-09-07)

- **`RequiredFieldCheck.Check` / `RequiredFieldMissing` had no production consumer of the rejection** —
  `Kape22Persister.Persist` now turns a failed `MapResult` into an `ImportResult` with no `L_D_KAPE22`
  row and (when the OF is readable) one `L_D_LOG_COMMANDE` "REJETÉ" line.
- **`MapResult.Warnings` not folded into a `ConversionResult`** (from Story 2.7) — still open; the
  persister passes `mapResult.Warnings` straight onto `ImportResult.Warnings` on success without
  merging Step 1 `SegmentMismatch` warnings or sorting by `LineNumber`. The Epic 3 orchestrator owns
  that merge (it is the only place both lists exist).
- **Commit + reset test-isolation regime** (deferred from Story 2.1 review) — `SqlServerIntegrationFixture.ResetData()`
  (TRUNCATE of the two AscoLSI harness tables) now exists; `TransactionalPersistenceTests` runs every
  case in that regime.

## Deferred from: Story 2.8 (2026-09-07)

- **Anti-duplicate guard reads outside the write transaction** — `Kape22Persister.OkLogRowExists` runs
  its SELECT before the insert `SaveChanges`, not inside one ambient transaction. Fine for the
  single-file-at-a-time worker; revisit if the orchestrator ever processes Fichiers concurrently.
- **Duplicate-skip has no explicit signal on `ImportResult`** — it is identified structurally
  (`Success == true && InsertedId == null && Errors.Count == 0`). The PRD "log Warning « déjà importé,
  ignoré »" is left to Epic 3 worker logging (Story 3.3). If the orchestrator needs an unambiguous
  flag, add one there or introduce an importer-side code (not in `TextToXml.ErrorCode`).
- **`ImportResult.XmlArchivePath` is never set** by the persister — it is an Epic 3 orchestrator field
  (written when the normalized XML is archived next to the Fichier, AC-FR12-3).
- **`Kape22Persister.ParisTimeZone` is a `static` field initializer** — same `TypeInitializationException`
  risk on a tz-data-less runtime already tracked for `Kape22Mapper.ParisTimeZone`; same
  not-a-concern-on-target reasoning. Fold both into one shared `Lazy<TimeZoneInfo>` helper if a leaner
  runtime is ever targeted.
- **Persister DI / host wiring absent** — `Program.cs` still does not compose persistence or the
  persister; `Kape22Persister` is constructed directly by its tests. Epic 3 (orchestration) owns
  `AddDbContext` vs `AddDbContextFactory` and the per-Fichier persister lifetime.
- **Rejection summary format is provisional** — `"<n> erreur(s) : <msg> ; <msg>"`. AC-FR14-2 (Story
  3.3) refines the exact `L_D_LOG_COMMANDE` / `Logs` wording ("nb erreurs + libellés").
- **`MQTTnetServices.Logs` half of AC-FR10-7 / AC-FR11 logging** — still not exercised; needs the
  Serilog sink from Story 3.3.

## Deferred from: code review of story-2.8 (2026-09-07)

- **`Kape22Persister` runtime logic runs only in the CI-excluded `Category=Integration` suite** — `ci.yml`
  runs `--filter Category=Unit` only (the SQL Server runner is still an open infra decision), so the
  anti-duplicate guard branch, the atomic-rollback boundary and the `DbUpdateException`/`DbException` →
  `PersistenceError` translation have no always-run coverage. The transaction-boundary cases genuinely
  need a real server, but the branch logic and the exception translation could run against EF Core's
  SQLite/in-memory provider in a `Category=Unit` test. Revisit when the CI SQL Server decision lands.
- **The anti-duplicate guard has no database backstop** — `OkLogRowExists` is the sole defense against a
  duplicate `L_D_KAPE22` insert. `AR-8` forbids migrations, so a unique index cannot be added from this
  repo. A future owner of the AscoLSI schema should add a unique constraint on the D22 key
  (`NumeroFichier` + `OF` + success marker) as the real backstop.
- **`PersistRejected` writes one REJETÉ `L_D_LOG_COMMANDE` row per reprocessing** — the D22 guard only
  suppresses re-inserts after a prior success, not after a rejection. A permanently malformed Fichier
  that the Epic 3 orchestrator retries would accumulate rows. Expected to be moot once the orchestrator
  moves rejected Fichiers to `error/` (Story 3.1, AC-FR12) instead of retrying; confirm there.

- **AC-FR10-7's persistence + logging half is not exercised** — Story 2.7 delivers the mapper side:
  `Kape22Mapper.Map` now returns `MapResult.Warnings` and a Fichier whose only defects are coherence
  Warnings still yields an entity with an empty `Errors`. The "inserted into `L_D_KAPE22`,
  `L_D_LOG_COMMANDE` status OK, Warnings land in `MQTTnetServices.Logs`" assertion needs Story 2.8
  (transactional persistence) and Epic 3 Story 3.3 (the `MQTTnetServices.Logs` sink). Covered by a
  unit test on the mapper only until then (`CoherenceWarningsTests.Map_FichierWithOnlyCoherenceWarnings_StillProducesEntity_AcFr10_7`).
- **`MapResult.Warnings` is not yet folded into a `ConversionResult`** — the Story 2.8 orchestrator
  owns merging Step 1 `ConversionResult.Warnings` (SegmentMismatch) with the mapper's FR-10 Warnings
  and sorting the combined list by `LineNumber` (AC-FR6-4). `Kape22Mapper` returns its Warnings
  unsorted.
- **The existing `Kape22MapperTests` call sites still use `Map(xml, name)` with no clock** — the
  deferred item from Story 2.6 was to fold a fixed clock in "when Story 2.7 edits that file"; Story
  2.7 added a new file (`CoherenceWarningsTests`, fixed clock) instead of editing `Kape22MapperTests`,
  so those call sites still run on `TimeProvider.System`. Still not flaky (reference `Date` "200"
  valid every year); fold in next time that file is touched.
- **`CoherenceWarningsTests` copies the mapper test scaffolding verbatim** — `FixedTimeProvider`,
  `WinterClock`, `ConvertReferenceFichier`, `ReadValidFixture` and the `ReferenceFichierName` const are
  duplicated from `DerivedFieldsTests` (Story 2.6). Same recurring pattern already tracked for the
  embedded-resource helpers; the shared `TestSupport` in `TextToXml.Tests` (Epic 1 retro A-1) now
  exists, so the fold-in target is there. Do it in the next test hardening pass that touches these
  files.

## Deferred from: code review of story 2.6 (2026-09-07)

- **`ParisTimeZone` is resolved in a `static` field initializer** — on a globalization-invariant host
  or one without ICU / tz data, `TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")` would throw a
  `TypeInitializationException` on first touch of any `Kape22Mapper` static member (`IsIgnored`,
  `ResolveTargetName`, …), which `RequiredFieldCheck` and `StartupCompatibilityCheck` also use. Not a
  concern on the .NET 10 / Windows Server target (`InvariantGlobalization` is not set, ICU is bundled).
  Move to a `Lazy<TimeZoneInfo>` with a `"Romance Standard Time"` fallback if a leaner runtime is ever
  targeted.
- **The Paris clock is read twice per `Map` call** — once as `ParisNow(timeProvider)` for
  `DateReception`, once inside `TryConvertHeaderDate` for the year. With `TimeProvider.System` the two
  reads can straddle a New Year / DST boundary. Negligible for a file-import worker; the clean fix
  reworks the deliberate `TryConvertHeaderDate(string, TimeProvider, out DateTime)` seam to take a
  pre-resolved instant, so it waits until Story 2.8 wires the real clock.
- **`Kape22Mapper.DefaultForNonNullable`'s value-type branch (`int` -> `0`) is now unreachable for a
  persisted entity** — `Indice` was the only non-nullable value-type column fed by a Champ, and a
  blank `Indice` is now rejected by `RequiredFieldCheck` before any `Value` is produced. The branch is
  harmless (still correct if a future non-nullable derived column appears); simplify or delete it when
  the mapper is next touched. Its only direct assertion left with `Map_BlankIndice_DoesNotThrowAndDefaultsToZero`.
- **The Story 2.4 / 2.7 `Kape22MapperTests` call sites use `Map(xml, name)` with no clock** — they now
  run on `TimeProvider.System`. Not flaky in practice (the reference fixture's `Date` "200" is valid
  every year and no required Detail Champ is blank), but they should take a fixed clock for
  determinism (AR-12). Fold in when Story 2.7 edits that file anyway.

## Resolved by: Story 2.6 (2026-09-07)

- **How Story 2.6 recombines `DateEnfournementFour1/2` `_Date` / `_Heure`** (deferred from review 2.2)
  — it does not. AC-FR9-5 / D14: the four string slices stay in `Kape22Mapper.IgnoredProperties`, the
  `DateEnfournementFour1/2` `DateTime?` columns are left `NULL`, and `DerivedFieldsTests` covers both
  the reference Fichier and a Fichier where the four Champs carry values.
- **`RequiredFieldCheck.Check` has no production caller** (deferred from review 2.5) — `Kape22Mapper.Map`
  now calls it after the Annexe B copy, so a blank NOT NULL column (Detail Indice for AC-FR9-4, and the
  string columns `Client` / `Coulee` / `Nuance` / `OF` / `Type` from the 2.5 note) rejects the Fichier
  with `RequiredFieldMissing`. The Story 2.8 orchestrator still owns turning that `MapResult` into a
  file move / log line.
- **Blank NOT-NULL string columns mapped through as empty string with no error** (deferred from
  spec/review 2.4) — resolved by the same `RequiredFieldCheck.Check` wiring.

## Deferred from: Story 2.6 (2026-09-07)

- **AC-FR9-1 range tightened from the literal "1..366" to the length of the current Paris year** —
  `TryConvertHeaderDate` rejects `"366"` in a non-leap year so a converted date can never roll into
  the next year (the AC's "jour dans l'année courante" intent). If the source system is ever found to
  emit `"366"` in a 365-day year as a sentinel, revisit with the PM.
- **The converted day-of-year `Header.Date` value is validated but not persisted** — no `L_D_KAPE22`
  column consumes it in Epic 2. `Kape22Mapper.TryConvertHeaderDate` is public so the value is
  reachable, but `Map` only enforces validity (`InvalidDate`). Surface it on the result (or a
  successor type) when Story 2.8 needs "a file date".
- **`Kape22Mapper.Map` stayed a `static` method** — the PRD reference API (`public sealed class
  Kape22Mapper` with an instance `Map`) is still not matched; `TimeProvider` is threaded as an
  optional third parameter instead of constructor-injected. Revisit if Story 2.8's DI wiring makes an
  instance mapper cleaner.
## Resolved by: Story 2.5 (2026-09-07)

- **`Kape22Mapper.Map`'s reflection `SetValue` type-alignment assumption** — mitigated at worker
  startup by `StartupCompatibilityCheck.Verify` (AC-FR8-1): a Champ `datatype` that does not fit the
  `L_D_KAPE22` column CLR type aborts host startup. The `SetValue` call site itself is unchanged, so
  a type mismatch that reaches it at runtime (e.g. a Descripteur edited without redeploy) is still
  unguarded — see the residual item under the Story 2.5 review below.
- **Blank NOT-NULL columns mapped through as empty string with no error** — `RequiredFieldCheck.Check`
  (AC-FR8-5 / AC-FR8-6) returns one `RequiredFieldMissing` per empty NOT NULL Detail column, ordered
  by the Descripteur `<value>` Position. `Kape22Mapper.Map` itself is unchanged, so the Story 2.4
  blank-`Indice` test stays green.

## Deferred from: code review of story-2.5 (2026-09-07)

- **`RequiredFieldCheck.Check` has no production caller** — delivered with unit tests only; the
  orchestrator that runs it per Fichier and turns its `ConversionError`s into a rejection is Story
  2.8. AC-FR8-5 / AC-FR8-6 are accepted on unit behaviour alone until then.
- **`Kape22Mapper.Map`'s reflection `SetValue` is still unguarded at the call site** — the FR-8
  startup check catches an incompatible embedded Descripteur, but a runtime type mismatch that
  reaches `SetValue` (Descripteur changed without a redeploy, or a path the check does not cover)
  still throws a raw reflection exception. Add a guarded conversion in `Map` in a Story 2.4/2.8
  hardening pass.
- **`RequiredFieldCheck` / `StartupCompatibilityCheck` trust the `L_D_KAPE22` NRT annotations** —
  `IsRequired` reads `NullabilityInfoContext.WriteState`; if the entity is ever re-scaffolded with a
  `#nullable disable` region, `WriteState` is `Unknown`, every required string column silently drops
  out and the check becomes a no-op for strings. `SqlColumn.IsNullable` (already parsed for the
  parity test) is an authoritative fallback. Not imminent — `Directory.Build.props` sets
  `<Nullable>enable</Nullable>` and the entity is hand-maintained.
- **No `ILogger` records that the FR-8 startup check ran and passed** — in production there is no way
  to tell "check passed" from "check never wired". Not required by any AC-FR8; Epic 3 (Story 3.3 /
  3.4) owns worker logging and launcher integration — add a "FR-8 check passed: N mapped Champs
  verified" line there.
- **Embedded-descriptor reader still copied in test code** — `EmbeddedDescriptor` now backs the two
  production sites, and `StartupCompatibilityTests` / `RequiredFieldMissingTests` use it, but
  `Kape22MapperTests` / `P60XsdTests` / `P60DescriptorTests` still carry their own
  `GetManifestResourceStream` helpers. Fold into a shared `TestSupport` in the hygiene pass already
  tracked below.

## Deferred from: code review of story-2.4 (2026-09-04)

- **No test exercises a nullable DTO field actually blank landing as `null` on a nullable entity
  column** — only the non-nullable `Indice` blank-path (`Kape22MapperTests.cs:138`) is covered.
  Real coverage gap, not tied to a stated `AC-FRx-y`; worth a follow-up test.

- **Embedded-resource test helpers (`EmbeddedResource`, `EmbeddedP60Xml`, `ReadValidFixture`) are
  copy-pasted into a third test file (`Kape22MapperTests.cs`)** — repeats a pattern already present
  in `P60XsdTests.cs`/`P60DescriptorTests.cs`. Consolidate into a shared `TestSupport` helper in a
  future hardening pass.

## Deferred from: code review of story 2.4 (2026-09-04)

- **`P60Deserializer.Deserialize` throws `ArgumentNullException` on a `null` `normalizedXml`** —
  `Validate` wraps `XmlReader.Create(new StringReader(normalizedXml), ...)` in a `try/catch
  (XmlException)` only; `new StringReader(null)` throws before that catch runs, so a `null` input
  crashes instead of coming back as a `PersistenceError`. Pre-existing from Story 2.3, surfaced while
  reviewing `Kape22Mapper.Map` (Story 2.4), which simply forwards whatever `Deserialize` returns and
  inherits the same gap. Not currently reachable — every caller passes `Converter.Convert`'s own
  non-null `Xml` output — but worth a null-guard in `P60Deserializer.Validate` in a Story 2.3/2.4
  hardening pass.

## Deferred from: code review of story 2.2 (2026-09-04)

- **`DateEnfournementFour1/2` (`DateTime?` columns) are modelled as split `_Date` / `_Heure` `string`
  Champs** — the "derive `datatype` from the target column type" rule of Story 2.2 does not apply to
  them, by design (D6: P60 has no `datetime` Champ), but nothing records how Story 2.6 (`champs
  dérivés combinés`) recombines the two string slices into one `DateTime`, and no test covers the
  `datetime` columns at all. The strengthened `P60Xml_IntDatatypeMatchesTheL_D_KAPE22IntColumns`
  still only checks the `int` direction. Capture the recombination contract in Story 2.6 and add a
  test that the two `_Date` / `_Heure` pairs are the only `<message>` Ids with no direct `L_D_KAPE22`
  column.
- **The `int`-column list is re-derived from the built EF model, not consumed from a Story 2.1 API** —
  Story 2.1's epic AC promised "la liste des colonnes `int` … exposée pour la Story 2.2", but no such
  member exists on `AscoLsiDbContext`; `P60DescriptorTests.Kape22IntColumns()` rebuilds a `DbContext`
  and duplicates the model-only `DbContextOptions` helper already in `AscoLsiDbContextModelTests`.
  Extract one shared model-only context helper for `Kape22Importer.Tests` (rule of three: this is the
  second copy — extract on the third) or expose the int-column set from the persistence project.
- **Pre-existing `Description` typos in `Templates/P60.xml` left in place** — `Emet` → "Rmetteur"
  (Émetteur), "tolérence" ×4 (tolérance), despite every `<value>` line being rewritten in this story.
  Also reconcile the header comment's "Positions 526 to 636" with `epics.md` AC-FR4-5 phrasing
  ("637 > 526"); no test asserts the real Fichier's total record length (Story 2.5 territory).

## Deferred from: code review of story 2.1 (2026-09-04)

- **No CI job runs `Category=Integration`** — `ci.yml` now states outright that the runner providing a
  SQL Server test instance "is still an open infra decision". The AR-12 harness, the `scripts/schema`
  application and the EF round-trip therefore have no automated gate; only the `Category=Unit`
  model/parity tests run in CI. Decided during the review to accept this as a known deferral (D). Fold
  the runner choice (Linux service container vs Windows-native SQL) into the Epic 2 CI story; ties to
  risk R-1. When the job lands, also address the two items below.
- **The test schema is never reset between runs** — `scripts/schema/*.sql` guard every table with
  `IF OBJECT_ID(...) IS NULL` and `SqlServerIntegrationFixture` never drops. After any edit to
  `scripts/schema/`, an existing `AscoLSI_Test` / `MQTTnetServices_Test` keeps the stale tables and the
  integration tests pass against an outdated schema — exactly the R-3 failure mode, on the layer the
  file-vs-model parity test does not cover. Add a fixture drop/recreate of the four harness tables (or
  a documented reset step) when the integration CI job is built.
- **`MQTTnetServices.dbo.Logs` / `WorkerSettings` have no EF entity and no `SchemaModelParityTests`
  coverage** — R-3 drift protection currently exists only for `L_D_KAPE22` and `L_D_LOG_COMMANDE`.
  Extend parity (or add a lightweight column check) when Story 3.3 introduces the Serilog / launcher
  persistence.
- **Schema extraction is not reproducible** — the `.sql` headers describe the `sqlcmd` /
  `INFORMATION_SCHEMA.COLUMNS` + `sys.identity_columns` method but no extraction script or query is
  committed, so R-3's "regenerate from the same source if the production schema changes" cannot be
  followed mechanically. Commit the extraction query (or a small script) alongside `scripts/schema/`.
- **MQTTnetServices connection string is not wired into production configuration** — Story 2.1 AC
  bundles `AscoLSI` **and** `MQTTnetServices` as "read from `IConfiguration`", but only `AscoLSI` has a
  production reader (`AddAscoLsiPersistence`) and a unit test. Decided during the review to defer the
  MQTT wiring to Epic 3, where its entities land; the misleading `AscoLsiConnectionConfigTests` comment
  was corrected in this pass.
- **Second test-isolation regime (commit + reset via Respawn / TRUNCATE) is not built** — only
  `TransactionScope` + rollback exists. `SqlServerIntegrationFixture` documents that commit + reset is
  "the caller's job" but provides no helper. Decided during the review to defer until Story 2.8 (the
  anti-duplicate guard, D22) — the first test that needs committed state.
- **Importer host does not compose persistence** — `Program.cs` never calls `AddAscoLsiPersistence`
  and `appsettings.json` carries no `ConnectionStrings:AscoLSI` placeholder. The extension exists and
  is unit-tested in isolation. Decided during the review to defer host wiring to the story that first
  consumes the `DbContext` (2.4 / 2.8); revisit `AddDbContext` vs `AddDbContextFactory` there, since
  the only current consumer (`Worker`) is a singleton.
- **`SqlServerIntegrationFixture` GO-batch splitter has no unit test** — the `BatchSeparator` regex is
  private and only exercised by the integration path (which itself has no CI gate). Extract the
  splitter into a small internal helper and add a `Category=Unit` test over the real `scripts/schema/`
  files so the parsing half is guarded without a database.

## Resolved: Epic 1 retrospective hygiene pass (2026-09-04)

Closed by the "story 0" hygiene pass (retro action `epic-1-retro-item-1`), commit pending:

- **Test-fixture helpers duplicated** (`Windows1252` ×5, `Row` ×2, `ReadDescriptor` / `ReadInput` /
  `FileRoot` / `Root` / `Ascii` across 4+ files) — folded into
  `tests/TextToXml.Tests/TestSupport.cs`, imported per file with `using static`. The
  `Windows1252` helper now uses the real `Encoding.GetEncoding(1252)` (provider registered in a
  static ctor) instead of the ASCII byte cast, so the name is honest. Notes from reviews 1.4, 1.6,
  1.7, 1.8.
- **Bloc→section-name mapping duplicated three times** (`BlockAssigner.SegmentControlSections`,
  `LineLengthChecker.SectionByBlock`, `NormalizedXmlBuilder.SectionByBlock`) — replaced by a single
  `src/TextToXml/DescriptorSections.cs` (`For(Block)` + `All` + name constants), also used by
  `DescriptorValidator.SectionNames`. Note from review 1.6.
- **Misleading comment in `BlockAssigner`** (`"Members are ordered alphabetically (CC-4)"` above a
  structurally-ordered array) — the array moved to `DescriptorSections.All`, comment rewritten. Note
  from review 1.5.

Still open from those same notes: unifying `Position` / `Size` offset access (three parsing styles
across the pipeline) — deferred, no Epic 2 payoff yet.

Landed (retro action `epic-1-retro-item-2`): repo `.editorconfig` + `.github/workflows/ci.yml`
(`dotnet build -warnaserror` with `EnforceCodeStyleInBuild` + `dotnet test --filter Category=Unit`),
and `AcTraitCoverageTests` — the AC→`[Trait]` aggregator gate (Story 3.6 pulled forward): the build
fails if a test named after an `AC` / `CTR` / `NFR` lacks the matching `[Trait]`. Closes the story 1.1
notes on absent `.editorconfig` and absent CI.

Landed (retro action `epic-1-retro-item-3`, PRD reconciliation): `PRD.md` §3 glossary and the FR-1
descriptor grammar now list `decimalSeparator` and `convert` on `<value>`; the `decimal` / `datetime`
canonical forms and the "datetime requires a `convert` mask" rule are captured as §0bis **D28** plus
`AC-FR1-14` / `AC-FR5-15`. Closes the review 1.8 note on the glossary.

Decided (retro action `epic-1-retro-item-3`): the blank-typed-Champ question from the review 1.6 note
is resolved as §0bis **D27** — `P60.xsd` types `int` / `decimal` / `datetime` Champs strongly
(`minOccurs="0"`), so Étape 1 **omits** the element for a blank typed Champ instead of emitting
`<Id></Id>`; the DTO gets `int?` / `decimal?` / `DateTime?`. `string` Champs keep the empty element.

Landed (retro action `epic-1-retro-item-7`): `NormalizedXmlBuilder.Normalize` now returns a nullable
canonical value — `null` for a blank `int` / `decimal` / `datetime` Champ — and `Build` skips the
element when it is null. `AC-FR5-4` / `AC-FR5-6` tests updated (`Convert_IntChampBlank_OmitsElement`,
`Convert_TrailingIntChampAbsent_OmitsElement`, `Convert_Decimal/DatetimeChampBlank_OmitsElement`).
Closes the review 1.6 note "Blank int Champ emits `<Id></Id>` which will not deserialize into a
non-nullable `int` DTO member".

Landed (retro action `epic-1-retro-item-4`): `AC-FR6-5` — `DescriptorValidator.Validate` no longer
concatenates the English `XmlException.Message` for a not-well-formed Descripteur; it emits
`"Le descripteur XML n'est pas bien formé (ligne N, position M)."`, keeping only the language-neutral
location. `AssertCleanFrenchMessage` now also rejects English framework-text tokens. Closes the
review 1.7 note.

## Deferred from: code review of story 1.8 (2026-09-03)

- **`datetime` canonical output truncates sub-second precision and has no timezone strategy** — `NormalizedXmlBuilder.NormalizeDateTime` always emits `yyyy-MM-dd[THH:mm:ss]`; a `convert` mask carrying `f`/`F` (fractional seconds) or `z`/`K` (offset) would silently lose that component or risk a local-timezone shift. No descriptor in the repo uses such a mask (`P62.xml` masks are `ddMMyy` / `dd/MM/yy HH:mm` style). Revisit when a format needs sub-second or offset-bearing timestamps.
- **AC-FR1-9 genericity is only proven for the "no header, no footer, no Segment" shape** — `fixtures/generic/message-only.xml` follows Annexe A.4 literally. The AC also mentions "présence header/footer différents de P60"; a second synthetic fixture with a non-P60 header/footer and Segment control (non-P60 markers) would exercise that path directly. Header/footer handling is currently covered only indirectly via the KAPE22-like descriptors in `NormalizedXmlTests` / `DescriptorValidationTests`.
- **PRD glossary §3 does not list `decimalSeparator` (nor `convert` on a `<value>`)** — the glossary Champ definition still reads `<value Id Position Size datatype [convert] Description>` and states the attribute list is exhaustive, but Story 1.8 consumes `decimalSeparator` and CTR-1 names it. Update the PRD glossary and the FR-1 descriptor grammar so descriptor and spec agree.
- **Implied-scale decimal (`convert` on a `decimal` Champ) is out of scope for v1** — `NormalizeDecimal` is driven by `decimalSeparator` only; a fixed-width field like `SerrageBil` `Section` (`convert="{0:000.0}"`, raw `1234` meaning `123.4`) would normalize to `1234`. Deliberate per the Story 1.8 contract decision. Implement when a live format requires an implied decimal point.
- **AC-FR16-2 has only a proxy assertion** — `FormatIsolationTests.TextToXml_CarriesNoFormatArtifact_AcFr16_2` checks the library side (no `.xml`/`.xsd`/`EmbeddedResource` in `src/TextToXml`) but does not enumerate the format variation points or scan `Kape22Importer` types for P60 literals, because those artifacts do not exist yet. Strengthen the test in Epic 2 once `P60.xml`/`P60.xsd`/the DTO/the entity exist.
- **New test-fixture helpers duplicated** — `ReadDescriptor` / `ReadInput` / `Message` / `FileRoot` are copied between `ExtendedTypesTests` and `GenericFormatTests`, extending the existing story 1.7 note on `Windows1252` / `Row`. Fold all fixture-path plumbing onto `RepoLayout` (or a `GenericFixtures` helper) in the same hygiene pass.

## Deferred from: code review of story 1.7 (2026-09-03)

- **Test helpers `Windows1252(string)` and `Row(...)` duplicated verbatim across 4+ test files** — `ConversionResultContractTests`, `NormalizedXmlTests`, `LineLengthTests`, `BlockAssignmentTests`, `InputDecodingTests` each carry their own copy (comments included). Extends the story 1.4 deferred note ("extract into a shared test utility when a third copy appears" — that threshold is now passed). When extracting: `Windows1252(string)` is actually an ASCII / Latin-1 byte cast (`text.Select(c => (byte)c)`), **not** CP1252 — bytes 0x80–0x9F map wrong (`€`, `Œ`, `™`, …). Rename to make the ASCII-only scope explicit, or switch to `Encoding.GetEncoding(1252)`. Reason: pre-existing pattern, not introduced by story 1.7, not blocking.
- **`LayoutInvalid` from a malformed descriptor embeds the raw `XmlException.Message` (English framework text)** — `DescriptorValidator.Validate` (`src/TextToXml/DescriptorValidator.cs:34`) produces `"Le descripteur XML n'est pas bien formé : <XmlException.Message>"`; the tail is English ("Unexpected end of file has occurred..."). Tension with AC-FR6-5 ("Message ... en français"). Introduced in story 1.2. The story 1.7 `AssertCleanFrenchMessage` check passes on the French prefix. Consider a fully French rendering (own wording for the well-formedness failure, or map common `XmlException` cases) in a story 1.2 hardening pass.

## Deferred from: code review of story 1.6 (2026-09-03)

- **`decimal` / `datetime` datatypes are silently treated as `string`** — `NormalizedXmlBuilder.Normalize` only special-cases `int`; `decimal` and `datetime` (both accepted by `DescriptorValidator`) fall through to `TrimEnd` and are emitted unnormalized. Story 1.8 (CTR-1/CTR-2) owns their normalization; no current descriptor or fixture uses them (D6: P60 has none), so not reachable today.
- **Blank `int` Champ emits `<Id></Id>` which will not deserialize into a non-nullable `int` DTO member** — tension between AC-FR5-4 ("" → empty element) / AC-FR5-6 and AC-FR5-12 ("deserializable without a custom converter"). To be resolved by the `P60.xsd` nullability / `minOccurs` decision in Story 2.3. AC-FR5-4 mandates the empty element for now.
- **Typed non-last Champ can be silently truncated** — `LineLengthChecker` (Story 1.5) only guarantees a non-last Champ's *starting* Position is covered, so `NormalizedXmlBuilder.ExtractRawValue` can clamp a middle Champ's slice mid-content (e.g. `"005"` out of `"0059000"`), and an `int` field then normalizes that to a plausible but wrong number. Pipeline-wide design point (FR-4 only requires start Position). Consider validating full declared `Size` for typed non-last Champs in a Story 1.5 / Épic 2 hardening pass.
- **Bloc→section-name mapping now duplicated three times** — `BlockAssigner.SegmentControlSections` (tuple array), `LineLengthChecker.SectionByBlock` (dict) and `NormalizedXmlBuilder.SectionByBlock` (identical dict). Extract one shared accessor in a hygiene pass; ties to the existing deferred note on the `BlockAssigner` comment.
- **Per-Ligne rework in `NormalizedXmlBuilder.Build`** — the section lookup, `.Elements("value")` enumeration and `int.Parse` of `Position` / `Size` are redone for every Detail Ligne (O(lignes × champs) tree walks and parses). Precompute the champ list and parsed offsets per section once. Trivial for P60 (3 Lignes); revisit if NFR-2 (500 Fichiers < 30 s) shows pressure.
- **Inconsistent offset access across the pipeline** — `NormalizedXmlBuilder` and `LineLengthChecker` re-parse `Position` / `Size` with `int.Parse((string)attr, NumberStyles.None, CultureInfo.InvariantCulture)`, while `BlockAssigner` uses the `(int)attribute` cast. Unify via a single accessor on the validated Descripteur in the same hygiene pass.

## Deferred from: code review of story 1.5 (2026-09-03)

- **Misleading comment in `BlockAssigner`** — the comment above `SegmentControlSections` (`src/TextToXml/BlockAssigner.cs:15`) states "Members are ordered alphabetically (CC-4)" but the tuple array is ordered `Header / Detail / Footer` (structural order, not alphabetical). Introduced in Story 1.4. Either fix the comment or genuinely sort the array in a Story 1.2/1.4 follow-up hygiene pass.

## Deferred from: code review of story 1.4 (2026-09-03)

- **`DescriptorValidator` accepts a zero-width Segment field (`Size="0"`)** — `IsNonNegativeInteger` allows `0`, so `CheckSegments` slices an empty `rawValue` that never equals a non-empty marker and emits a `SegmentMismatch` warning on every Ligne of that Bloc. Pathological descriptor; fold into a broader Story 1.2 descriptor-validation hardening pass (reject zero-width fields, and possibly non-empty `*Marker` when `segmentField` is set).
- **`Windows1252(string)` test helper duplicated verbatim** between `BlockAssignmentTests` and `InputDecodingTests` (comment included). Extract into a shared test utility when a third copy appears.

## Deferred from: code review of story 1.1 (2026-09-02)

- **`.editorconfig` absent** — house style (English comments per `CLAUDE.md`, naming, redundant `using` directives despite `ImplicitUsings`) is unenforced. Add a repo `.editorconfig` as a dedicated hygiene task.
- **No CI workflow** — the `SolutionStructureTests` build gate and the `Category=Unit` / `Category=Integration` split only guard anything if a pipeline runs them on push. Fold into Story 2.1 (which already needs a Docker-capable runner, AR-12) or a dedicated CI story. Ties to risk R-1.
- ~~**`Kape22Importer.Tests` has no executing test** — no DI smoke test that `Host.CreateApplicationBuilder` composes and `Worker` is registered as an `IHostedService`.~~ **RÉSOLU 2026-09-09 (Story 3.0)** : le host a disparu (lib pure) ; `Kape22Importer.Tests` exécute 288 tests.
- ~~**Worker template dead code** — `Program.cs` / `Worker.cs` are the unmodified `dotnet new worker` scaffold (`Task.Delay(1000)` sample loop, no `OperationCanceledException` handling on shutdown). Replace in Épic 3 (FR-12 / FR-13 orchestration).~~ **RÉSOLU 2026-09-09 (Story 3.0)** : `Program.cs` / `Worker.cs` supprimés (`git rm`).

## Deferred from: bmad-build Story 3.0 multi-goal split (2026-09-09)

- source_spec: none
  summary: Créer le projet `Client : Publisher` (Kape22/ImportP60) dans `MicroServices.sln` et l'enregistrer dans le Launcher (`WorkerRegistry.Factories` + `workers.json`), en `ProjectReference` cross-dépôt vers la lib `Kape22Importer` + `MicroService.csproj`.
  evidence: Objectif B de la Story 3.0, scindé du run bmad-build 2026-09-09. Vit dans un autre dépôt (`MicroServices.sln`, SVN) dont la copie de travail porte du travail voisin non commité (intégration OrdresFabricationSync) ; dépend de l'objectif A (conversion lib) et fusionne naturellement dans la Story 3.4 (boucle `Client.Actions`).

- source_spec: `spec-3-0-repositionnement-structurel-lib.md`
  summary: Le fail-fast sur chaîne de connexion `AscoLSI` manquante/vide a disparu avec `AddAscoLsiPersistence` (Story 3.0) — le `Client` doit valider `ConnectionStrings:AscoLSI` au démarrage (message clair) avant `CreateAsync`, sinon l'échec ne surviendra qu'à la première requête EF.
  evidence: `AddAscoLsiPersistence` jetait `InvalidOperationException` sur valeur null/vide (test `AddAscoLsiPersistence_ThrowsWhenTheConnectionStringIsMissing`, supprimé avec le fichier). Responsabilité déplacée vers le `Client` (objectif B / Story 3.4) ; le modèle `OrdresFabricationSync` ne fait pas ce fail-fast non plus (`configuration["SourceContext"] ?? string.Empty`).

- source_spec: `spec-3-0-repositionnement-structurel-lib.md`
  summary: Le gate FR-8 (`StartupCompatibilityCheck.Verify`) n'a plus **aucun** appelant de production dans `TextToXml.sln` depuis la suppression de `StartupCompatibilityHostedService` (Story 3.0) — le `Client` doit appeler `Verify(context.Model, EmbeddedDescriptor.Xml)` dans `CreateAsync()` avant `Start()` ; échec → `LogError` + la boucle ne démarre pas (défaut de déploiement).
  evidence: `StartupCompatibilityHostedService` enregistré avant le `Worker` garantissait l'exécution du gate au démarrage de l'hôte (test `StartupWiringTests`, supprimé avec le fichier). `Verify` reste inchangé et testé Unit (AC-FR8-1..4, `StartupCompatibilityTests`). Câblage spécifié dans `spec-3-4-gpao-importp60-client-worker.md` (`GpaoImportP60.Client.CreateAsync`) ; jumeau du fail-fast `ConnectionStrings:AscoLSI` ci-dessus.

- source_spec: `spec-3-4-gpao-importp60-client-worker.md`
  summary: ~~Ajouter un `CancellationToken` à `InboxScanner.RunTick`~~ **RÉSOLU 2026-09-09**.
  evidence: `InboxScanner.RunTick(CancellationToken cancellationToken = default)` vérifie le jeton en tête de chaque itération des deux boucles (`processing/` stranded puis inbox) et `return` (jamais passé à `processor.Process`) — test `Tick_WhenTheTokenIsCancelledAfterAFichier_LeavesTheRestForTheNextTick_AcFr14_6` (`Kape22Importer.Tests`). Côté SVN : `GpaoImportP60.Client` détient un `CancellationTokenSource _cancellation`, `override Stop()` l'annule avant `base.Stop()`, `override Dispose()` le libère, `RunTickCore` prend le jeton et le passe à `RunTick`. Test `RunTickCore_WithAnAlreadyCancelledToken_ProcessesNothing`.

- source_spec: `spec-3-4-gpao-importp60-client-worker.md`
  summary: Ré-entrance du timer de `MicroService.Publish.Publisher` : `Start()` arme un `System.Threading.Timer` périodique dont le callback `async void` n'attend pas la fin de l'invocation précédente d'`Execute` ; un tick d'import plus long que `Frequency` peut chevaucher le suivant, deux `InboxScanner.RunTick()` courant sur la même inbox (l'un des `Move` échoue alors).
  evidence: Constaté à la revue du spec 3.4 mais **pré-existant** — même exposition sur `OrdresFabricationSync.Client` / `ImportFiles` / tous les workers `Publisher`. Contenu en partie par le try/catch par Fichier d'`InboxScanner` + le déplacement vers `processing/`. Correctif = un garde de non-ré-entrance (`SemaphoreSlim(1,0)` ou flag `_running`) dans `Publisher.Start`'s callback, côté `MicroService` (hors périmètre worker P60, touche tous les workers).

## Deferred from: code review of story-3-0 (2026-09-09)

- source_spec: `spec-3-0-repositionnement-structurel-lib.md`
  summary: `SolutionStructureTests` ne garde `Kape22Importer` que via une allowlist sur le `.csproj` littéral, pas via les paquets *résolus* (`project.assets.json`) comme pour `TextToXml`. Une réintroduction transitive de `Microsoft.Extensions.Hosting` (ou de la pile `Microsoft.Extensions.Configuration.*`) via `PortalSharedLibrary` passerait tous les tests.
  evidence: Constaté à la revue de la Story 3.0. Pré-existant : l'allowlist csproj-only date de la Story 2.1. La Story 3.0 a retiré la référence directe `Hosting` mais le `git grep` manuel du spec est le seul filet contre un retour transitif. Correctif = porter l'assertion sur les paquets résolus (comme `ResolvedPackagesOf` côté `TextToXml`).

- source_spec: `spec-3-0-repositionnement-structurel-lib.md`
  summary: Aucune assertion automatisée ne vérifie que `Kape22Importer` produit une bibliothèque et non un exécutable (`OutputType` == `Library`, absence d'`.exe`). `Kape22Importer_UsesTheClassLibrarySdk` ne teste que l'attribut `Sdk` du `.csproj` ; un futur `<OutputType>Exe</OutputType>` passerait tous les tests.
  evidence: AC du spec-3-0 (« produit une DLL … pas d'`.exe` ») + commande de vérif `dotnet build -getProperty:OutputType` non automatisées. Correctif = assertion `OutputType` / absence d'`Exe` à côté de `Kape22Importer_DeclaresNoFrameworkReference` (`SolutionStructureTests.cs:108`).

## Deferred from: story-4.2 mapping annex, assumed unverified (2026-09-14)

Pattern reconduit de la parité Épic 2 (`spec-parity-kape22-legacy-fill-rules.md`) : chaque colonne
`à_clarifier` de `annexe-mapping-dispatch-epic4.md` est citée ici littéralement (`Table.Colonne`) pour
que `MappingAnnexCompletenessTests.AnnexeMappingDispatchEpic4_MatchesStory41EfModel_AcFr17_5`
(AC-FR17-5) la reconnaisse comme documentée-en-dette plutôt que comme un trou. Preuves consultées :
`App_Data/Template/{OrdreFabrication,Coulee,KAPE22}.xml` (déployé sur `C:\inetpub\wwwroot\lsi\`) et
`Desktop/kape22/{KAPE22Controller,OrdreDeFabricationManager,CouleeManager}.cs`, en lecture ciblée
(grep) plutôt qu'intégrale pour `OrdreFabrication.cs`/`InterfaceManager.cs` — une réponse peut donc
s'y trouver et rester à confirmer.

- source_spec: `annexe-mapping-dispatch-epic4.md` § L_D_ORDRE_FABRICATION
  summary: assumed, unverified — `L_D_ORDRE_FABRICATION.Etat`, `L_D_ORDRE_FABRICATION.NombreLingotsWagon1Four1`, `L_D_ORDRE_FABRICATION.NombreLingotsWagon1Four2`, `L_D_ORDRE_FABRICATION.NombreLingotsWagon2Four1`, `L_D_ORDRE_FABRICATION.NombreLingotsWagon2Four2`, `L_D_ORDRE_FABRICATION.OFOrigine`, `L_D_ORDRE_FABRICATION.PoidsPesee`, `L_D_ORDRE_FABRICATION.SensLaminage`, `L_D_ORDRE_FABRICATION.SensLaminageGPAO` n'ont aucune source ou règle d'initialisation identifiée dans le legacy lu pour un dispatch P60.
  evidence: `Etat` (enum NOT NULL) — seul `SetOFAsENC` positionne `EtatOF.ENC`, mais dans un flux de planification GPAO manuel ultérieur (`OrdreFabricationController.cs:265-330`), pas au dispatch. Les 4 `NombreLingotsWagon*Four*`, `SensLaminage` et `SensLaminageGPAO` n'apparaissent dans aucun des 4 fichiers legacy lus. `OFOrigine` n'est que lu (`OrdreFabrication.cs:1439`), jamais écrit dans les fichiers legacy lus — site d'écriture non trouvé. `PoidsPesee` n'est que lu (`InterfaceManager.cs:252,1418`, `OrdreFabrication.cs:1488`), jamais écrit — vraisemblablement un poste de pesée distinct, hors P60. Revisiter si `OrdreFabricationController.cs`/`InterfaceManager.cs` sont lus intégralement, ou si un échantillon P60 avec ces champs renseignés apparaît.

- source_spec: `annexe-mapping-dispatch-epic4.md` § L_D_COULEE
  summary: assumed, unverified — la quasi-totalité de `L_D_COULEE` n'a pas d'équivalent dans le dispatch P60 : `L_D_COULEE.AnomalieAPC`, `L_D_COULEE.AnomalieAPCRH`, `L_D_COULEE.AnomalieRH`, `L_D_COULEE.AnomaliesCoulee`, `L_D_COULEE.AnomaliesDegazeur`, `L_D_COULEE.AnomaliesDemoulage`, `L_D_COULEE.ArriveeEnfournementWagon1`, `L_D_COULEE.ArriveeEnfournementWagon2`, `L_D_COULEE.CodeLivraison`, `L_D_COULEE.CouleeFroide`, `L_D_COULEE.DateCOPAPC`, `L_D_COULEE.DateCOPCoulee`, `L_D_COULEE.DateCOPDemoulage`, `L_D_COULEE.DateCOPRH`, `L_D_COULEE.DebutCoulee`, `L_D_COULEE.DebutDemoulage`, `L_D_COULEE.Degazee`, `L_D_COULEE.DelaisLivraisonWagon1`, `L_D_COULEE.DelaisLivraisonWagon2`, `L_D_COULEE.DensiteCoulee`, `L_D_COULEE.EcartWagon1`, `L_D_COULEE.EcartWagon2`, `L_D_COULEE.Enregistrement`, `L_D_COULEE.EstConformiteCoulee`, `L_D_COULEE.EstEnfournementStandard`, `L_D_COULEE.EstHomogene`, `L_D_COULEE.EstTroisQuartsConforme`, `L_D_COULEE.EtatReception`, `L_D_COULEE.FinCoulee`, `L_D_COULEE.FinDemDernierLgtWagon1`, `L_D_COULEE.FinDemDernierLgtWagon2`, `L_D_COULEE.HeureArriveeWagon1`, `L_D_COULEE.HeureArriveeWagon2`, `L_D_COULEE.HeureDepartWagon1`, `L_D_COULEE.HeureDepartWagon2`, `L_D_COULEE.HeurePrevuDemoulage`, `L_D_COULEE.Hydrogene`, `L_D_COULEE.LingotPiscine`, `L_D_COULEE.MarqueFroide`, `L_D_COULEE.ModeElaboration`, `L_D_COULEE.NbLingotRestantARefroidir`, `L_D_COULEE.NombreLingotAir`, `L_D_COULEE.NombreLingotBacVerniculite`, `L_D_COULEE.NombreLingotPitsSec`, `L_D_COULEE.NombreLingotsWagon1`, `L_D_COULEE.NombreLingotsWagon2`, `L_D_COULEE.NombreTypelingot1`, `L_D_COULEE.NombreTypelingot2`, `L_D_COULEE.NumerosLingotRebutes`, `L_D_COULEE.Observation2`, `L_D_COULEE.Observations`, `L_D_COULEE.OperateurCoulee`, `L_D_COULEE.OperateurDegazeur`, `L_D_COULEE.OperateurDemoulage`, `L_D_COULEE.Piscinage`, `L_D_COULEE.PiscinageWagon1`, `L_D_COULEE.PiscinageWagon2`, `L_D_COULEE.PoidsMoyenLingotMere1`, `L_D_COULEE.PoidsMoyenLingotMere2`, `L_D_COULEE.PoidsMoyenLingotMere3`, `L_D_COULEE.PoidsMoyenLingotMere4`, `L_D_COULEE.PoidsUnitaireLingot1`, `L_D_COULEE.PoidsUnitaireLingot2`, `L_D_COULEE.ProgrammeSMQ`, `L_D_COULEE.ResponsableTraitement`, `L_D_COULEE.RetardDemoulage`, `L_D_COULEE.RetardLivraisonWagon1`, `L_D_COULEE.RetardLivraisonWagon2`, `L_D_COULEE.SaturationPits`, `L_D_COULEE.SauvetageWagon1`, `L_D_COULEE.SauvetageWagon2`, `L_D_COULEE.TypeLingot1`, `L_D_COULEE.TypeLingot2`.
  evidence: Le `MappingTemplate` legacy de Coulée (`App_Data/Template/Coulee.xml`) source ses champs depuis un système différent de KAPE22/P60 (identifiants `COULEE`, `NUANCE`, `HEURE_DEPART_WAGON1`... absents de `L_D_KAPE22`), et près de la moitié y est déjà `mapping=""`. `CouleeManager.cs` (lu intégralement, 786 lignes) ne contient aucun chemin de création de Coulée déclenché par un dispatch P60/KAPE22 — seulement des scénarios administratifs distincts (`CreateDefault`, `CreateDefaultFroid`, `CreateFakeBUL`/`UpdateFakeBUL`, tous appelés hors flux d'import). `CouleeFroide` et `EtatReception` sont signalés priorité haute dans l'annexe car ils portent directement le contrôle métier FR-20 (existence coulée froide). Revisiter si `CouleeController.cs` (non fourni) ou un flux d'import Coulée dédié est localisé.

- source_spec: `annexe-mapping-dispatch-epic4.md` § L_D_CONSIGNES
  summary: assumed, unverified — `L_D_CONSIGNES.LibelleConsigne`, `L_D_CONSIGNES.SizeCodeConsigne`, `L_D_CONSIGNES.TypeConsigne` ne suivent pas une règle unique pour toute la table.
  evidence: `LibelleConsigne` est calculé par `LibelleConsigneController.GetLibelle(...)` (`OrdreDeFabricationManager.cs:1408,1412,1424,1428`), fichier non fourni dans `Desktop/kape22/`. `SizeCodeConsigne` (12 ou 18) et `TypeConsigne` sont des constantes déterminées au cas par cas par `CompleteConsignes2` selon le sous-champ décodé (`OrdreDeFabricationManager.cs:1442-1682` — décodage d'un code composite par sous-chaînes), pas une règle column-à-column applicable à toute la table. Revisiter en lisant `LibelleConsigneController.cs` et en modélisant le décodage complet de `CompleteConsignes2` pour la Story 4.4.

- source_spec: `annexe-mapping-dispatch-epic4.md` § L_D_SECTIONCHARGE_LINGOT
  summary: assumed, unverified — `L_D_SECTIONCHARGE_LINGOT.PriseDeFerEpaisseur`, `L_D_SECTIONCHARGE_LINGOT.PriseDeFerEpaisseurGPAO`, `L_D_SECTIONCHARGE_LINGOT.PriseDeFerHauteur`, `L_D_SECTIONCHARGE_LINGOT.PriseDeFerHauteurGPAO`, `L_D_SECTIONCHARGE_LINGOT.PriseDeFerSection`, `L_D_SECTIONCHARGE_LINGOT.PriseDeFerSectionGPAO`, `L_D_SECTIONCHARGE_LINGOT.Programme`, `L_D_SECTIONCHARGE_LINGOT.ProgrammeGPAO` ne viennent pas d'un champ KAPE22.
  evidence: Calculées par `OrdreDeFabricationManager.ComputePriseDeFer`/`GetPriseDeFer` (`OrdreDeFabricationManager.cs:1251-1362`) via une table de référence `PriseDeFer` interrogée par montage + profil composé (`ProfilProduit_CodeDemiProduit[-CodeConsigneScarfing]`) + section (`DiametreProduit*10`) — un lookup base de données, hors périmètre d'un mapper structurel pur sans accès base (AD-2). Revisiter quand la table de référence `PriseDeFer` aura un équivalent dans le nouveau schéma, ou si la Story 4.5/4.6 admet un accès en lecture supplémentaire pour ce cas précis.

- source_spec: `annexe-mapping-dispatch-epic4.md` § L_D_SECTIONCHARGE_PITS
  summary: assumed, unverified — `L_D_SECTIONCHARGE_PITS.DateDefournementFour1`, `L_D_SECTIONCHARGE_PITS.DateDefournementFour2` n'ont aucune source identifiée.
  evidence: Aucune occurrence trouvée dans les 4 fichiers legacy lus. Le défournement (sortie de four) est un événement de production postérieur au dispatch P60 ; par analogie avec `DateEnfournementFour1/2` (volontairement non mappés dans le `MappingTemplate`, commentaire "Enlevé car empêche d'enfourner"), ces deux colonnes restent vraisemblablement NULL au dispatch, mais sans preuve directe contrairement à leurs jumelles `DateEnfournementFour*`. Revisiter si `OrdreFabricationController.cs` est lu intégralement.

## Deferred from: code review of story-4.2 (2026-09-15)

- source_spec: `_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md` § AD-6
  summary: `epic-4-context.md` (compilé depuis ARCHITECTURE-SPINE.md) affirme que `Kape22ImportBundle` porte « les 9 entités avales nullables », alors que le Goal du même fichier et le modèle EF de la Story 4.1 (`ModelColumnsByTable()`, `MappingAnnexCompletenessTests.cs`) dénombrent 10 tables avales (OF + Coulée + Consignes + 7 SectionCharge). Pré-existant dans ARCHITECTURE-SPINE.md ligne 124 (vérifié directement) — pas introduit par la Story 4.2, `epic-4-context.md` ne fait que le recompiler fidèlement.
  evidence: Trouvé à la revue de code Story 4.2 (Blind Hunter + vérification manuelle). Non bloquant pour la Story 4.2 (aucun code de production ici). À trancher avant de coder `Kape22ImportBundleMapper` (Story 4.5) ou le remplacement de `Kape22Persister` (Story 4.6) : soit le compte « 9 » exclut délibérément une des 10 tables pour une raison à documenter, soit c'est une coquille à corriger dans ARCHITECTURE-SPINE.md et sa recompilation.

## Deferred from: story-4.3 mapper implementation (2026-09-15)

- source_spec: `annexe-mapping-dispatch-epic4.md` § L_D_ORDRE_FABRICATION
  summary: assumed, unverified — `L_D_ORDRE_FABRICATION.SuiviDeZoneZone` was marked `sourcée` from `KAPE22.SuiviDeZoneZone` in the Story 4.2 annex, but `L_D_KAPE22` (Story 4.1 EF model) has no property of that name, nor any case/spelling variant. The annex row itself was in error, not a legacy-evidence gap; reclassified `à_clarifier` and left at its CLR default (null) by `OrdreFabricationMapper`, same as any other undocumented column (AC-FR18-4).
  evidence: `grep -i "suivi\|zone"` over `src/Kape22Importer/Persistence/L_D_KAPE22.cs` returns no match. Discovered while wiring `OrdreFabricationMapper.Map` test-first (Story 4.3): the compiler rejected `source.SuiviDeZoneZone`. Revisit if a legacy field with this meaning under a different name is identified (e.g. a KAPE22 Champ not yet accounted for in Annexe B).

## Deferred from: story-4.4 mapper implementation (2026-09-15)

- source_spec: `annexe-mapping-dispatch-epic4.md` § L_D_SECTIONCHARGE_REFROIDISSOIRS
  summary: assumed, unverified — `L_D_SECTIONCHARGE_REFROIDISSOIRS.OFInterne` was marked `sourcée` from `KAPE22.OFInterne` in the Story 4.2 annex, but `L_D_KAPE22` (Story 4.1 EF model) has no property of that exact name — only `OFDestinationInterne` and `OForiginInterne`, neither an unambiguous match. Same annex-drift family as the Story 4.3 `SuiviDeZoneZone` finding, not a legacy-evidence gap; reclassified `à_clarifier` and left at its CLR default (null) by `SectionChargeRefroidissoirsMapper`, same as any other undocumented column (AC-FR19-4).
  evidence: `grep -n "OFInterne" src/Kape22Importer/Persistence/L_D_KAPE22.cs` returns no match (only `OFDestinationInterne`/`OForiginInterne`). Discovered while wiring `SectionChargeRefroidissoirsMapper.Map` test-first (Story 4.4): the compiler rejected `source.OFInterne`. Revisit if a legacy field disambiguating which of the two `*Interne` fields this column means is identified.

- source_spec: `annexe-mapping-dispatch-epic4.md` § L_D_CONSIGNES
  summary: `ConsignesMapper` leaves `TypeConsigne` at its CLR default (`0`) for every section — `à_clarifier` per the Story 4.2 annex, no single column-to-column rule found in the legacy read. `ConsigneGPAO` is not à_clarifier: the annex classifies it `règle` (always `false` for the real row, `true` only for the legacy GPAO mirror row this mapper does not reproduce). `L_D_CONSIGNES`'s own natural key is `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` (Story 4.1 doc comment), so with `TypeConsigne`/`ConsigneGPAO` constant across every row, two sections sharing the same `CodeOperation` value for one OF would produce two rows with an identical key. Not reproducible against any of the 10 reference `P60/` fixtures (each section's `CodeOpe*` field is a distinct KAPE22 Champ with its own value), so left as a documented risk rather than a mapper-level guard, which would need cross-section knowledge this pure per-source mapper does not have.
  evidence: Found at Story 4.4 code review (Blind Hunter). `L_D_CONSIGNES.cs` comment: "The key is the natural composite business key (OF, CodeOperation, TypeConsigne, ConsigneGPAO)". Revisit once `TypeConsigne`'s real value is known (would very likely disambiguate the key by section, matching the annex's own note that `CompleteConsignes2` assigns a distinct type constant per decoded sub-field) or when Story 4.5/4.6 sees an actual collision from a wider fixture set.

## Deferred from: story-4.5 code review (2026-09-16)

- source_spec: `spec-4-5-import-bundle-mapper-orchestrator.md`
  summary: `Kape22ImportBundleMapper.AddHotCouleeFormatViolation` classifies "hot" strictly as `kape22.CodeConsignePits != "1"`, with no defined behavior for a null or whitespace-padded `CodeConsignePits` (the property is nullable on `L_D_KAPE22`) — such a value is silently bucketed as "hot" identically to an explicit non-`'1'` code, and neither `epics.md`'s FR-20 AC text nor the annex's legacy formula (`CodeConsigne != "1"`, `annexe-mapping-dispatch-epic4.md:106`) addresses this case.
  evidence: Found at Story 4.5 code review (edge-case-hunter + blind-hunter, independently). Not reproducible against the reference `P60/` fixture (its `CodeConsignePits` is always the real 12-char consigne code, never blank). Revisit if a null/blank `CodeConsignePits` is observed in a real Fichier, or before Story 4.6 relies on this control's output for the cold-Coulée DB check.

## Deferred from: story-4.2-bis code review (2026-09-17)

- source_spec: `spec-4-2-bis-extension-annexe-mapping-scale-precision.md`
  summary: `MappingAnnexCompleteness.IsInt` only recognizes `int`/`int?` as the KAPE22 source type that triggers the decimal-without-Scale rule. A future KAPE22 field typed `short`, `byte`, or `long` feeding a narrow `decimal` column would carry the same precision-loss risk but would not be caught by the guard this story adds.
  evidence: Found at Story 4.2-bis code review (blind-hunter). No live `L_D_KAPE22` field is currently typed `short`/`byte`/`long` (confirmed by reflection over `L_D_KAPE22.cs` — every numeric field is `int?` or `decimal?`), so this is speculative hardening rather than a live defect. Revisit only if such a field is ever added to `L_D_KAPE22`.

## Deferred from: story-4.10 code review (2026-09-18)

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: B-5's magnitude REJETÉ message (`Kape22Persister.FindMagnitudeOverflow`/the new pre-`SaveChanges` check) embeds a code-shaped fragment (`"<EntityTypeName>.<PropertyName> = <value>"`) directly into an otherwise French business message, unlike the sibling missing-Coulee and Consignes-collision REJETÉ messages beside it, which describe the situation entirely in French business terms (coulée id, CodeOperation value) with no internal type/property names. An operator reading `L_D_LOG_COMMANDE` sees a C#-identifier-shaped fragment mixed into the French prose.
  evidence: Found at Story 4.10 code review (blind-hunter). Same class of message-wording item Story 4.9's own code review already deferred rather than blocked on (`story-4-3-bis-review-item-4-e2e-script-exit-code-gap` sibling entry, A-5's REJETÉ message citing only the raw CodeOperation). Revisit if operators report the message is hard to read, or the next story to touch `Kape22Persister`'s REJETÉ messages.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: B-4's new `$LASTEXITCODE` guard in `scripts/e2e-worker-import.ps1` (the per-`Fichier` production-parity `dotnet test` call) has no automated regression test — `GpaoImportP60WorkerEndToEndTests`'s happy-path run never drives a parity failure, so it cannot tell if the guard were later dropped or broken by a future edit to that loop. The frozen spec's own Verification section already scoped B-4 to a manual/CI spot-check rather than an automated test, so this is a known, accepted gap, not a blocker.
  evidence: Found at Story 4.10 code review (verification-gap). Revisit if `e2e-worker-import.ps1` grows more guarded steps, or a lightweight pwsh-level test harness for this script is ever justified.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: `MappingAnnexSchema.CheckMapperScaleUsage` (B-1/B-2) only cross-checks annex rows with `Status == Sourced`; a `Règle`/`à_clarifier`-status row that later gained both a `Scale` value and a real `DecimalScale.Apply` call site in its mapper would not be checked by this guard at all — it mirrors the original 4.2-bis Scale-presence check's own `Sourced`-only scope, not a gap newly introduced by this story's guard specifically.
  evidence: Found at Story 4.10 code review (edge-case-hunter). No current annex row hits this — all 21 real `DecimalScale.Apply` call sites are `Sourced`-status (verified by `MapperScaleCallSites_MatchTheRealAnnexScale_AcB1B2`). Revisit only if a `Règle`/`à_clarifier` row is ever reclassified to `Sourced` with a KAPE22 int source and a narrow decimal target.

## Deferred from: code review of story-4.10 (2026-09-21)

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: `Kape22Persister`'s missing-Coulee rejection block (`Kape22Persister.cs:92-112`, AC-FR20-5) still duplicates the `ConversionError`/REJETÉ-log/`SaveChanges`+catch shape inline instead of calling the `RejectWithBusinessRuleViolation` helper this story extracted for the Consignes-collision (A-5) and magnitude-overflow (B-5) checks right below it. The refactor unified two of the three identical copies, leaving the file in a mixed style.
  evidence: Found at /run-review of story-4.10 (blind-hunter). Purely a style/consistency nit — the missing-Coulee block's behavior is unchanged and correct. Revisit the next time `Kape22Persister`'s rejection blocks are touched.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: `DownstreamColumnMagnitudesParityTests` (`DownstreamColumnMagnitudesParityTests.cs`) locks `DownstreamColumnMagnitudes` against `10^(p-s)` only, so two columns with different `(p,s)` but the same `p-s` (e.g. `DECIMAL(4,1)` vs `DECIMAL(6,3)`) would yield the same bound and be indistinguishable to this parity test — it cannot catch a `DecimalScale.Apply` literal scale drift as long as `p-s` happens to still match.
  evidence: Found at /run-review of story-4.10 (blind-hunter). No current column pair hits this collision (verified against `scripts/schema/01-ascolsi-tables.sql`'s real `(p,s)` values for the 17 registered columns). Revisit only if a future column addition creates a `p-s` collision with a mismatched actual scale.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: `Kape22Persister.FindMagnitudeOverflow` (`Kape22Persister.cs:243`) reports the first out-of-gabarit column found via `entity.GetType().GetProperties()`, whose enumeration order is a CLR implementation detail, not a documented contract. No test pins which column is named in the REJETÉ message when more than one column overflows simultaneously.
  evidence: Found at /run-review of story-4.10 (blind-hunter). A future refactor could silently change which column an operator sees named; low impact since the message still correctly signals a rejection either way. Revisit if operators ever need a stable "first offender" guarantee.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: `Kape22ProductionDataParityTests.CompareSectionCharge` (`Kape22ProductionDataParityTests.cs:259`) silently returns when a mapper output is null or has no matching production row, with nothing asserting that at least one `SectionCharge*` comparison actually ran across the theory's `P60Fichiers` fixtures. A fixture set that happened to hit only skip branches would report green with zero real assertions for those tables.
  evidence: Found at /run-review of story-4.10 (blind-hunter). This test is `Category=Integration`, opt-in behind a production connection string (AR-12) — never runs in CI unattended. Revisit if this suite becomes part of a required gate.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: `SqlTableSchema.DecimalMagnitudeFor` (`SqlTableSchema.cs:88`) computes `(decimal)Math.Pow(10, precision - scale)` via double-precision floating point before casting to `decimal`. A `DECIMAL(p,s)` column with `p-s >= 29` would overflow the `decimal` cast with an unhandled `OverflowException`, crashing any `SqlTableSchema.Read` call over that table — not just this story's own tests.
  evidence: Found at /run-review of story-4.10 (edge-case-hunter). No column in `scripts/schema/01-ascolsi-tables.sql` today has `p-s >= 29` (largest is 7). Revisit if a future AFV004-LSI schema regeneration ever introduces such a column, or replace with an integer-exact computation as a defensive measure.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: `MappingAnnexCompleteness.CheckMapperScaleUsage` (`MappingAnnexSchema.cs:202`) takes only the first `MapperScaleCallSite` via `FirstOrDefault()` when more than one call site targets the same Table+Column; a second, divergent call site for that same column would go unchecked.
  evidence: Found at /run-review of story-4.10 (edge-case-hunter). Unreachable under the mappers' current object-initializer style (`new T { Property = ... }`), where assigning the same property twice in one initializer is a C# compile error (CS1912). Revisit only if a mapper ever assigns a scaled property outside a single object initializer.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: `scripts/e2e-worker-import.ps1`'s `--filter "FullyQualifiedName~Kape22ProductionDataParityTests&DisplayName~$fichier"` clause (line 188) could match zero tests (a naming/typo mismatch) and `dotnet test` would still exit 0, so the new B-4 `$LASTEXITCODE` guard (line 191) would never fire even though no parity comparison actually ran.
  evidence: Found at /run-review of story-4.10 (edge-case-hunter). The `--filter` clause itself predates this story and is unchanged by it; only the exit-code check is new. Revisit if this script's filter is ever rewritten, or add a "did anything run" assertion alongside the exit-code check.

- source_spec: `spec-4-10-hardening-epic-4-b.md`
  summary: The spec's own "Suggested Review Order" and "Code Map" sections cite exact line numbers (`Kape22Persister.cs:243`, `:267`, `:221`, `DownstreamColumnMagnitudes.cs:19`, etc.) that will silently go stale the next time anyone edits those files — a purely textual, unenforced cross-reference inside a `<frozen-after-approval>`-adjacent document.
  evidence: Found at /run-review of story-4.10 (blind-hunter). Same class of drift risk as every other spec's line-numbered "Suggested Review Order" section in this project; not specific to Story 4.10. Revisit only if stale line references are found to actively mislead a future reviewer.

## Deferred from: implementation of story-4.11 (2026-09-21)

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: C-5's AC asks for a 4th `RejectionAtomicityIntegrationTests` case proving the B-5 magnitude-overflow rejection atomically, alongside the 3 delivered (AC-FR20-3, AC-FR20-4, A-5). It was not added: every one of the 21 real `DecimalScale.Apply` KAPE22 source fields (`Templates/P60.xml` Position/Size) is, by construction, narrower than the raw-value width needed to make its scaled result exceed its target column's `DownstreamColumnMagnitudes` bound — e.g. `DiametreProduit` is a 4-digit fixed-width field (max raw 9999, scale 1 → 999.9), strictly under its bound of 1000. Mutating a fixed-width byte position wide enough to overflow a magnitude either shifts every subsequent field (tripping an unrelated FR-20 control first) or fails structural/positional parsing outright — unlike the existing Unit-level `DecimalMagnitudeGuardTests`, which reach the overflow by mutating the post-Converter XML value directly (`SetChamp(d, ..., "99999")`), a shape no real fixed-width P60 byte content can produce.
  evidence: Found during story-4.11 implementation; confirmed by checking all 21 call sites' raw field widths against their `DownstreamColumnMagnitudes` bounds. B-5 stays proven only at the Persister-unit level (EF InMemory, pre-existing since Story 4.10) — there is no realistic real-fixture trigger for an Integration-level proof, before or after this story. A working substitute already exists one tier down: `DecimalMagnitudeGuardTests` (Category=Unit, EF InMemory) proves the same pre-`SaveChanges` guard by mutating the post-Converter XML value directly, a shape no real fixed-width byte fixture can produce — this is not a closed dead end, just a tier the real-fixture pattern cannot reach. Revisit only if a future format change (e.g. a wider KAPE22 field) makes a real overflow representable.

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: C-4's new pre-`SaveChanges` length-overflow guard (`Kape22Persister.FindLengthOverflow`) only checks `DownstreamColumnLengths` (the 9 nullable downstream entities + `Consignes`), never `L_D_KAPE22`'s own bounded columns (`Kape22ColumnLengths`). This asymmetry became visible because `TransactionalPersistenceTests`'s pre-existing SQL-truncation test had to switch its forced-overflow fixture from `Client` (now pre-empted by C-4, since `Client` is also a `DownstreamColumnLengths`-bounded `L_D_ORDRE_FABRICATION` column) to `LibelleConsigneChutage` (an `L_D_KAPE22`-only column, still an undiagnosed SQL truncation).
  evidence: Found during story-4.11 code review (blind-hunter). Matches B-5's own pre-existing scope (magnitude guard also only covers downstream decimal columns, never `L_D_KAPE22`'s own) — not a new gap introduced by this story, just a pre-existing one made newly visible by C-4 needing a still-open column to keep exercising the SQL-failure test. Revisit only if `L_D_KAPE22`'s own string columns need the same pre-`SaveChanges` diagnosis B-5/C-4 give downstream tables.

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: `DownstreamDecimalEntities` (`Kape22Persister.cs`, 5 entities, feeds `FindMagnitudeOverflow`) and `DownstreamStringEntities` (new in this story, 10 entities, feeds `FindLengthOverflow`) are two independently hand-maintained "yield each non-null bundle entity" iterators, overlapping on `OrdreFabrication` and 4 `SectionCharge*` types. Nothing keeps them in sync — a future `SectionCharge*` table gaining or losing a bounded column has to be edited correctly in both places, with no shared enumerator or test asserting the two views of "the bundle's populated entities" agree.
  evidence: Found during story-4.11 code review (blind-hunter). Neither C-4's nor B-5's AC requires unifying these two enumerations; C-3 already addresses the analogous mapper-file/table-name triplication for a different pair of lists in this same story, but extending that pattern here would be new scope. Revisit if a future SectionCharge* schema change causes the two lists to silently diverge.

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: `FindMagnitudeOverflow` (pre-existing, Story 4.10) and `FindLengthOverflow` (new, C-4) each perform an independent, uncached `entity.GetType().GetProperties()` reflection walk over overlapping bundle entities, on every successful import (not just rejections) — two full reflective scans per Fichier where one shared walk could serve both checks. `EndToEndPerformanceTests`'s NFR-2 comment (C-8, this story) was updated to mention only "the guard query" (singular), without noting a second reflective pass now also runs on the same hot path.
  evidence: Found during story-4.11 code review (blind-hunter). B-5's own reflection walk was already an accepted precedent (Story 4.10); C-4 following the same shape is consistent, not a new architectural deviation, and NFR-2's measured budget still passes green with both walks running. Revisit if NFR-2's margin ever tightens enough for this to matter, or if a third such walk is ever added.

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: The 3 new `RejectionAtomicityIntegrationTests` cases (`AcFr20_3`, `AcFr20_4`, `A5`) each repeat the same ~10-line assertion block verbatim (a `BusinessRuleViolation` error, a single MQTT `Error`-level `[ImportRejected]` log row, a single REJETÉ `L_D_LOG_COMMANDE` row, all eleven tables empty), differing only in the fixture mutation — the same shape the file's 3 pre-existing tests already had before this story.
  evidence: Found during story-4.11 code review (blind-hunter). Pre-existing duplication pattern in this test class, only extended (not introduced) by this story; the class already extracted `AssertAllElevenTablesEmpty` for the one sub-block that was worth it. Revisit if a 5th such case is ever added, or if the shared assertion block needs to evolve in a way that risks the 6 copies drifting apart.

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: `FindLengthOverflow`'s walk covers all 10 downstream bundle entities, but the new `StringLengthGuardTests` only exercises it through `OrdreFabrication.MarqueCommerciale` and `Consignes.CodeConsigne` — `Coulee` and all 7 `SectionCharge*` branches are reached only by the generic reflection loop, with no dedicated test proving an overflow is actually caught through any of them (unlike C-1, whose AC explicitly named and tested the 3 previously-uncovered magnitude branches).
  evidence: Found during story-4.11 code review (verification-gap). C-4's AC only requires "a string exceeding its bound produces a diagnosed ConversionError," verified by the 2 entity types + 1 boundary case delivered; it does not name specific branches the way C-1's AC did for magnitude. Revisit if a `Coulee`/`SectionCharge*`-specific length regression ever needs isolating from a generic reflection-loop failure.

## Deferred from: code review of story-4.11 (2026-09-22)

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: `Kape22Persister.FindLengthOverflow`/`FindMagnitudeOverflow` (`Kape22Persister.cs:339-358`) report only the first overflowing column when two or more downstream string/decimal columns overflow simultaneously — C-6's accumulation only combines across check *types* (A-5/B-5/C-4), not multiple instances within one check.
  evidence: Found at /run-review of story-4.11 (blind-hunter + edge-case-hunter). Pre-existing pattern for `FindMagnitudeOverflow` since Story 4.10 (already logged in this file's own Story 4.10 section, "reports the first out-of-gabarit column found"); C-4's `FindLengthOverflow` mirrors the same reflection-walk shape by explicit design, not a new deviation. Revisit if operators ever need every simultaneous offender named, not just one per check.

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: The new C-1/C-2/C-4/C-6/C-9/A-5-tagged test methods (`_AcC1`, `_AcC2`, `_AcC4`, `_AcC6`, `_AcC9`, `_A5` suffixes) don't match `AcTraitCoverage`'s regex (`AcFr\d+_\d+`/`Ctr`/`Nfr`/`Sm` only, `tests/TextToXml.Tests/AcTraitCoverage.cs:17-19`), so the AC-trait coverage gate silently skips every one of them — `inspected` never increments for these methods, no offender is ever reported either way.
  evidence: Found at /run-review of story-4.11 (blind-hunter). Pre-existing gap since Story 4.10's own `_AcB5`-suffixed tests (`DecimalMagnitudeGuardTests.cs:31,64,94`), which the regex already didn't recognize before this story; story 4.11 only extends the same already-unenforced naming style to more hardening-item ACs. Revisit if the C-x/A-x/B-x hardening-item naming scheme becomes permanent — extend the regex to recognize it, or standardize hardening-item tests back onto the `AcFrN_M` scheme.

- source_spec: `spec-4-11-hardening-epic-4-c.md`
  summary: The `Configuration()` `IConfiguration`-builder helper (identical `Import:Commande`/`Import:InitiatingServer` boilerplate) is duplicated verbatim in the 3 new test classes this story adds (`ConsignesCaseInsensitiveCollisionTests.cs`, `StringLengthGuardTests.cs`, reused unchanged in `DecimalMagnitudeGuardTests.cs`) instead of being extracted into `TestSupport`.
  evidence: Found at /run-review of story-4.11 (blind-hunter). Pre-existing repo-wide pattern — the same private `Configuration()` helper is already duplicated across 10 other test files in `tests/Kape22Importer.Tests/` before this story; not a new problem this story introduced or is scoped to fix alone. Revisit only as part of a dedicated test-helper consolidation pass across the whole test project, not one story at a time.

## Deferred from: story-4.4-bis decomposition of L_D_CONSIGNES (2026-09-23)

- source_spec: `spec-4-4-bis-decomposition-l-d-consignes-sous-champs-consignegpao.md`
  summary: Resolution of the "story-4.4 mapper implementation" entry above (§ `annexe-mapping-dispatch-epic4.md` § L_D_CONSIGNES, `TypeConsigne` at CLR default / natural-key collision risk): Story 4.4-bis gives `TypeConsigne` its real, section-specific value for the 6 sections with a known legacy decode rule (Chutage, Lingot, Pits, Decoupe, PoidsMetrique, Refroidissoir - `ConsignesMapper.cs`), which does disambiguate the `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` natural key by section as that entry anticipated. SVT is the one section left unresolved: `TypeConsigne` stays at its CLR default (`0`) there, `à_clarifier` per the Story 4.2 annex, no legacy decode rule found (`OrdreDeFabricationManager.cs:1668-1669`, a dead, commented-out read) - so the collision risk this entry described remains open for SVT specifically if a future OF ever has SVT sharing a `CodeOperation` with another undecoded section.
  evidence: Story 4.4-bis code map and Boundaries & Constraints (`ConsignesMapper.cs` rewrite, all 6 decodable sections). Revisit if SVT's own decode rule is ever found, or if a real `(OF, CodeOperation)` collision involving SVT is observed.

- source_spec: `spec-4-4-bis-decomposition-l-d-consignes-sous-champs-consignegpao.md`
  summary: assumed, unverified — the Decoupe (XP1) size-18 sub-block (`ConsignesMapper.AddDecoupe`, `TypeConsigne` 24/25-29, `SizeCodeConsigne=18`) has no independent KAPE22 source field. Legacy decodes it from a second, distinct raw code (`OrdreDeFabricationManager.cs:1560`, `ordre.GetConsignes("ConsignesDecoupeLingot", 24, gpao)`), but the P60 wire format (`Templates/P60.xml:84-85`) declares only one `CodeConsigneDecoupe` Champ, `Size="12"`, immediately contiguous with the next Champ (`LongueurMoyenne`, `Position="317"` = 305+12) - there is no room in the record layout for a second, independent size-18 code. `ConsignesMapper` reuses the same `CodeConsigneDecoupe` field for this block, gated on it carrying enough characters (>=13) for every one of the size-18 offsets; real P60 data (always exactly 12 characters once padded to the wire format's own declared width) never satisfies that gate, so the block is reachable only through a directly constructed `L_D_KAPE22` in tests today, never through an actual Fichier.
  evidence: `Templates/P60.xml` read directly (Position/Size attributes for `CodeConsigneDecoupe` and `LongueurMoyenne`); confirmed no other Champ resembling a second Decoupe consigne exists anywhere in the Descripteur. A diagnostic run of `ConsignesMapper` against the reference Fichier (`P60_847_682_001`) shows `CodeConsigneDecoupe` at 11 characters (already right-trimmed by the XML normalization layer), well short of the 13 needed to trigger this block. Revisit if a legacy field or Bloc carrying this second code is ever identified (e.g. positions 526-636 of the real Fichier, deliberately not described by this Descripteur per its own header comment - PRD D5), or if a real `P60/` fixture is found where this block should fire but currently does not.

- source_spec: `spec-4-4-bis-decomposition-l-d-consignes-sous-champs-consignegpao.md`
  summary: The per-section `ConsignesMapperTests` compute their expected sub-field values with the same `Substring`/offset literals `ConsignesMapper` uses internally (both read from the same Code Map), so a single offset transcribed wrong in both would agree with itself and pass. The real cross-check against an independent source is `Kape22ProductionDataParityTests.MappedFichier_ConsignesRows_MatchLegacyProductionRows` (AC-4), which is `[SkippableTheory]`, opt-in, and requires both a local SQL Server instance and a production database connection - not part of this project's normal/CI-run verification path in most environments (same class of gap as every other production-parity test here, AR-12).
  evidence: Raised independently by the code-review's blind-hunter and verification-gap layers (review of story-4.4-bis, 2026-09-23). Not blocking: this is exactly the coverage gap AC-4 was written to close, and the unit tests still pin every offset to a concrete, human-reviewed expected value even without independent verification. Revisit only if AC-4 is ever promoted to a normally-run tier (e.g. a CI service container with production read access), which would make this residual gap moot.

## Deferred from: code review of story-4.4-bis (2026-09-23)

- Rows imported into `L_D_CONSIGNES` by the pre-4.4-bis mapper (`ConsigneGPAO=false`, `TypeConsigne=0`) are indistinguishable from genuine OF-initial `ConsigneGPAO=0` rows; no cleanup/re-import note exists. Revisit only if any pre-4.4-bis import ever reached a shared database.
- `L_D_COULEE` was never covered column by column by `Kape22ProductionDataParityTests` (`epic-4-context.md:91-96`) and had no tracker entry. Revisit if a Coulee-side parity discrepancy is suspected.

## Resolved in: code review of story-4.4-bis (2026-09-23)

- Resolution of the "assumed, unverified — the Decoupe (XP1) size-18 sub-block ... has no independent KAPE22 source field" entry above (§ "Deferred from: story-4.4-bis decomposition of L_D_CONSIGNES"): that entry is superseded, the independent source exists. It is `LibelleConsigneDecoupe` (`Templates/P60.xml`, Position 287, Size 18). Legacy turns the second consigne it adds to the Decoupe section while loading a KAPE22 into `TypeConsigne=24`/`SizeCodeConsigne=18` (`OrdreFabrication.cs:627-634`), and every Decoupe record in the real `P60/` fixtures (324 records across 350 files) carries a structured code there (`.LLLLL BC X.XM`: optimised length, short bar, short bar length), never free text like the other sections' Libelle Champs. `ConsignesMapper.AddDecoupe` now decodes the size-18 block from that field, gated on it being non-empty, independently of the size-12 block; the >=13-character gate on `CodeConsigneDecoupe` is removed. The reference OF `2039771` now yields 30 `ConsigneGPAO=1` rows, matching the production count the sprint-change-proposal recorded (30 of 60).
- Correction to the SVT collision entry above (§ "Deferred from: story-4.4-bis decomposition of L_D_CONSIGNES", first bullet): SVT is the only undecoded section, so "SVT sharing a `CodeOperation` with another undecoded section" cannot happen. The real residual overlap is Chutage's own `TypeConsigne=0` sub-field row (`ConsignesMapper.cs`, XC1 type 0): SVT's CLR-default `TypeConsigne=0` would collide with it on the full `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` key if SVT and Chutage ever shared a `CodeOperation`. The A-5 pre-check (`Kape22Persister`) still rejects that case as a `ConversionError`, so it cannot reach the database silently.
- Correction to the "per-section `ConsignesMapperTests` compute their expected sub-field values with the same `Substring`/offset literals" entry above: the reference-Fichier tests (Chutage, Lingot, Pits, Refroidissoirs, Decoupe both blocks) and the short-code Chutage test now assert literal expected values read from the fixture, not values recomputed with the mapper's own offsets. A transcription error in the mapper is no longer mirrored by its test. The remaining independent cross-check against production is still `Kape22ProductionDataParityTests` (AC-4, opt-in), which now also checks the reverse direction.
