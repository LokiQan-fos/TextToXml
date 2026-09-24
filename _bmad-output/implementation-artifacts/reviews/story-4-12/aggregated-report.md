# Review report — Story 4.12

Range : 21075e5^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-4-12-libelle-consigne.md
Date : 2026-09-24
Verdict : REFUSÉ
Findings : D=1 P=11 F=8 R=23  (Decision, Patch, Defer, Rejetés)

Refused because of zero-tolerance transverse criteria: CC-2 (P7, P8) and CC-3 (P9), plus a missing guard on the "Never write to production" boundary (P6).

Severity count (kept findings): high 1 (P6) · medium 8 (D1, P1, P2, P3, P4, P5, P7, F2) · low 11 (P8, P9, P10, P11, F1, F3, F4, F5, F6, F7, F8).

## 1. Decision — à trancher par l'humain

### D1 — French local identifiers copied from legacy
- Source : acceptance-auditor
- Location : `src/Kape22Importer/LibelleConsigneResolver.cs:136,143,178-179,190,262`
- Description : the port keeps the legacy variable names `entier`, `virgule`, `consignePlus`, `particulierCode`, `degazageCode`. `project-profile.md` § Langues requires English identifiers; the spec's "port faithfully, line for line" rule does not say whether identifier names are exempt.
- Options : (1) rename to English (`integerPart`, `decimalPart`, `particularSuffix`, `particularCode`, `degassingCode`); (2) record a dated exemption in the spec (legacy-mirroring names allowed in a line-for-line port).
- État : à trancher par l'humain.

## 2. Patch

### P1 — deferred-work entry misstates legacy type-22 behaviour
- Source : acceptance-auditor
- Location : `_bmad-output/implementation-artifacts/deferred-work.md:1165`
- Description : the entry says legacy's `decimal.Parse("")` for a NULL `H2Coulee` ran outside `GetLibelle`'s try. It runs inside it (`LibelleConsigneController.cs:224-225`, try at :21, catch at :280), so a NULL `H2Coulee` giving `"?"` is verified legacy behaviour. Only a missing Pits charge (NullReferenceException at the call site) escapes. The call site is `OrdreDeFabricationManager.cs:1408`, not :1407.
- Action : append a dated correction (the file is append-only).

### P2 — DateMaj-skipped labels are never reported
- Source : blind-hunter + verification-gap
- Location : `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:395`
- Description : `Console.WriteLine` output is not captured by xUnit v2, so the spec's "reported as skipped, not failed" is not observable.
- Action : inject `ITestOutputHelper` into the test class and write the skipped labels through it.

### P3 — Parity test proving AC-FR19-5 has no AC trait
- Source : acceptance-auditor
- Location : `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:281`
- Description : AC-FR19-5's production-equality and DateMaj-guard clauses are proven only by `MappedFichier_ConsignesRows_MatchLegacyProductionRows`, which carries no `[Trait("AC","FR19-5")]`.
- Action : add `[Trait("AC", "FR19-5")]` to that theory.

### P4 — Type-22 label on the bundle/processor path never asserted
- Source : verification-gap + blind-hunter
- Location : `tests/Kape22Importer.Tests/Kape22FichierProcessorIntegrationTests.cs:219`
- Description : if `Kape22ImportBundleMapper` stopped forwarding `ordreFabrication`, every type-22 label would silently become `"?"`; every test and the E2E NULL gate would still pass.
- Action : add `Assert.Equal("0", Libelle("XA1", 22))` (empty degassing tables, OF and Pits inputs present).

### P5 — E2E NULL-label gate passes silently
- Source : edge-case-hunter + blind-hunter
- Location : `scripts/e2e-worker-import.ps1:157`
- Description : without `-b`/`$LASTEXITCODE`, a sqlcmd failure yields `$null`, and `[int]$null` is 0; a run that persisted no `L_D_CONSIGNES` row also passes.
- Action : add `-b`, check `$LASTEXITCODE`, and require at least one persisted row.

### P6 — Sync script can truncate production
- Source : acceptance-auditor (out of mandate) — severity high
- Location : `scripts/sync-reference-consignes.ps1:93`
- Description : nothing prevents `ConnectionStrings:AscoLSI` from naming the same server/database as `AscoLSI_Production`; the script would then CREATE/TRUNCATE/bcp-in on production, violating the spec's "Never: write to production".
- Action : throw before any write when the test target's server and database equal the production source's.

### P7 — CC-2: French noun "libellé(s)" in English comments
- Source : acceptance-auditor
- Location : `LibelleConsigneResolver.cs`, `ConsignesMapper.cs`, `ConsigneReferenceData.cs`, `Kape22ImportBundleMapper.cs`, `Kape22FichierProcessor.cs`, the 4 test files, `scripts/e2e-worker-import.ps1`
- Description : "libellé" is not on the CC-2 whitelist and had no precedent in comments before this commit.
- Action : replace with "label(s)" in comments only; identifiers and string literals are unchanged.

### P8 — CC-2: sentences starting with a lowercase identifier
- Source : acceptance-auditor
- Location : `LibelleConsigneResolver.cs:35-37`, `ConsigneReferenceData.cs:100-102`, `Kape22ImportBundleMapper.cs:17`
- Action : rephrase so each sentence starts with a capital letter ("The section argument is …").

### P9 — CC-3: comments not directly above the block they describe
- Source : acceptance-auditor
- Location : `src/Kape22Importer/ConsigneReferenceData.cs:19,24`
- Description : "share this column list" sits above the ORDER BY constant, while the column list is the next constant (CC-4 fixes the declaration order).
- Action : reword each comment to cover both constants of its pair (column list and ordering).

### P10 — PROJECT-CLOSED.md contradicts its reopened status
- Source : blind-hunter
- Location : `_bmad-output/implementation-artifacts/PROJECT-CLOSED.md:11`
- Action : add a dated reopening note stating that §1–§6 describe the 2026-09-22 closure and Story 4.12 is open.

### P11 — Legacy line ranges cited inconsistently
- Source : blind-hunter
- Location : `LibelleConsigneResolver.cs:10-12`, `ConsignesMapper.cs:112`, spec Code Map, annex row
- Description : `GetLibelle` is cited as 14-281 and 14-285; the call site as 1405-1408, 1405-1415 and 1405-1416.
- Action : use `LibelleConsigneController.cs:14-285` (the method's full extent) and `OrdreDeFabricationManager.cs:1405-1416` everywhere (spec Code Map is outside the frozen block).

## 3. Defer (appended to deferred-work.md, "Deferred from: code review of story-4.12 (2026-09-24)")

- F1 — Any `DbException` on the reference read goes to retry, a permanent schema fault included (`Kape22FichierProcessor.cs:82`). Same classification as the pre-existing persister catch.
- F2 — Type-22 `DiametreProduit` scale vs `DEGAZAGE_DETAIL` range unverified: 12 production OFs have an `Xh` label, none in `P60/`. Depends on the open Story 4.3 scale item.
- F3 — Sync not atomic, no row-count check (`sync-reference-consignes.ps1:104`). Test database only.
- F4 — Degraded snapshot / swallowed resolver exceptions leave no trace. The pure resolver has no logging seam.
- F5 — Snapshot is 13 non-transactional SELECTs per Fichier. Reference edits are rare.
- F6 — DateMaj guard blind to deleted/re-coded reference rows. No such delta today.
- F7 — 11 of 13 `Load` projections exercised on SQL Server only by opt-in tests (both ran green 2026-09-24).
- F8 — E2E script always needs production reachable. No offline use case today.

## 4. Rejetés (bruit)

- R1 XC1 unanchored regexes give negative values for codes like `1B` — faithful legacy quirk, required by spec "Always".
- R2 Thousandths overflow/float rounding — identical to legacy `int.Parse` + `(float)`.
- R3 Section with padding/case falls to `"?"` — legacy `switch` identical; parity green.
- R4 NULL `SectionMin/Max` never match — same as SQL NULL comparison in legacy.
- R5 Optional null `ordreFabrication`/`referenceData` — mandated by the spec Code Map (keeps ~20 callers compiling); P4 covers the forwarding.
- R6 `Lazy` caches a production exception — `Skip.If` on an empty connection string runs first; an unreachable configured production fails every other production read the same way.
- R7 `DateReception` from `FirstOrDefault` — `ReadProductionRows` orders by Id and the L_D_KAPE22 theory asserts a single row.
- R8 bcp without `-u` — the installed tools copied all 13 tables in the E2E run.
- R9 No `COLLATE` in the DDL — test database is `French_CI_AS`, same as production (checked).
- R10 CI ordering between `X`/`x` — legacy `First()` was unordered too.
- R11 Early-return branch untested — `Tick_RealPipelineWithAnUnreachableDatabase_LeavesFichierInProcessing_AcFr15_3` goes through it (it failed before the fix and passes after).
- R12 Early return lacks mapper warnings — the Fichier is retried; transient.
- R13 Per-Fichier snapshot cost — human decision 1a in the spec.
- R14 Epic 4 marked done with an open story — `epic-4` is `in-progress` (checked).
- R15 Stale test counts in PROJECT-CLOSED.md — historical snapshot, covered by P10's note.
- R16 `ConsigneReferenceData` as a record — cosmetic.
- R17 `IsNullOrEmpty(Trim())` — mirrors the legacy line verbatim.
- R18 `-P` password on the command line — local test tooling, same as existing sqlcmd usage.
- R19 Positional bcp on schema change — the DDL header documents regeneration from production.
- R20 E2E does not compare the 30 labels to production — step 7 runs the parity theory for the same Fichier.
- R21 Absolute path in the spec Code Map — cosmetic.
- R22 `ConsignesMapper_SetsLibelleOnEveryRow_AcFr19_5` naming/location — grouped with the other FR19-5 tests.
- R23 Production context without `ApplicationIntent` — the connection string already carries `ApplicationIntent=ReadOnly`.

## 5. Auto-vérifications

- Layers launched : 4/4 (blind-hunter, edge-case-hunter, verification-gap, acceptance-auditor); failed layers : none.
- Diff : 27 files, +1663/−45 (review diff 2064 lines, `P60/` fixtures excluded as raw data).
- Checks run during triage : test DB and production collation (`French_CI_AS` both); production type-22 label distribution (SELECT only); presence of degassing-hour OFs in `P60/` (none); `sprint-status.yaml` epic-4 status (`in-progress`); baseline comments for "libellé" (none).

## 6. Resolution (2026-09-24)

- D1 → option 1 (rename to English), became **P12**: `entier`→`integerPart`, `virgule`→`decimalPart`, `consignePlus`→`particularSuffix`, `particulierCode`→`particularCode`, `particulier`→`particular`, `particulierDateMaj`→`particularDateMaj`, `degazageCode`→`degassingCode`, `diametre`→`diameter`.
- Patches P1–P12 all applied (option 1, "apply every patch"). P11 note: the spec's Boundaries line citing `OrdreDeFabricationManager.cs:1405-1415` is inside the frozen block and left as is; every non-frozen citation now reads `1405-1416` and `LibelleConsigneController.cs:14-285`.
- Verification after patches: `dotnet build -warnaserror` 0/0; Unit 192 + 815 green; Integration 1092 green, 18 skipped; E2E `P60_847_682_352` green (30 rows, 0 NULL label, parity 3/3); P6 guard checked with a settings file pointing the test target at production → refused before any write.
- Story status: `done` (spec + `sprint-status.yaml`).
