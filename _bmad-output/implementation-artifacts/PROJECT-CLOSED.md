---
status: 'open'
reopened_date: '2026-09-24'
reopened_reason: 'test/correction session 2 — Story 4.12 (LibelleConsigne)'
closed_date: '2026-09-22'
last_commit: '8f6e79edd7fa8829b7946cbd998999b1c843cde2'
---

# TextToXml / Kape22Importer — Project Closure

> **Reopened 2026-09-24 (test/correction session 2).** Story 4.12 (`L_D_CONSIGNES.LibelleConsigne`,
> `spec-4-12-libelle-consigne.md`) is done; epic-4 stays `in-progress` until the project is re-closed. Sections 1–6 below describe the project as closed on
> 2026-09-22 (commit `8f6e79e`); their test counts and "no story in progress" statement are not updated.

**Decision:** the project is functionally complete. All planned Epics (1–4), including every
post-retrospective hardening story, are `done`. No Epic 5 is planned — confirmed by the donneur
d'ordre. No story is in progress.

---

## 1. Epics — final verdict

| Epic | Scope | Stories | Retrospective verdict |
|---|---|---|---|
| **Epic 1** | Solution scaffolding, descriptor loading/validation, CP1252 decoding, block assignment, line-length control, typed XML extraction, `ConversionResult` purity/thread-safety, generic decimal/datetime typing (1.1–1.8) | 8/8 done | `accepted-with-open-items` (`epic-1-retro-2026-09-03.md`) — all 7 action items done |
| **Epic 2** | EF Database-First entities, SQL Server test harness, P60 XSD/DTO, deserialization/mapping, descriptor-vs-table compatibility check, derived fields, coherence warnings, transactional persistence + anti-duplicate guard (2.1–2.8) | 8/8 done | `accepted-with-open-items` (`epic-2-retro-2026-09-07.md`) — action items done, 2 reconciled at closure (see §3) |
| **Epic 3** | Structural repositioning as a library, inbox scanning/file lifecycle, per-file orchestration, double journalization, worker loop + graceful shutdown, loop robustness, E2E coverage harness (3.0–3.6) | 7/7 done | `accepted-with-open-items` (`epic-3-retro-2026-09-11.md`) — action items done, 1 reconciled at closure (see §3) |
| **Epic 4** | Downstream-table dispatch: 10 `L_D_*` entities, mapping annex, OF/Coulée + Consignes mappers, bundle orchestrator, single-transaction persister, 10-table E2E suite, plus 5 post-retro hardening stories (4.1–4.7, 4.2-bis, 4.3-bis, 4.9, 4.10, 4.11) | 11/11 base + 5/5 hardening = done | `accepted-with-open-items`, 3 retro passes (`epic-4-retro-2026-09-17.md`, `-18.md`, `-21.md`) — every routed action item done as of this closure (see §3) |

Every epic closed with the same verdict shape: **accepted-with-open-items**, never a hard rejection.
Each round of open items was either fixed by a dedicated follow-up story or explicitly accepted as a
non-blocking, documented risk in `deferred-work.md`.

## 2. Post-retrospective correction stories (Epic 4)

Epic 4 went through three retrospective passes because its transactional single-`SaveChanges()` design
(AD-1) kept surfacing latent defects only visible once every downstream mapper landed. Each correction
story exists for a specific, traceable reason:

- **4.2-bis — Extension annexe mapping (scale/precision field).** Root-caused the recurring decimal-scale
  defect (below) to a documentation gap: the Story 4.2 mapping annex had no scale/precision field for
  `int → decimal` columns, so nothing could catch a missing rescale before it shipped.
- **4.3-bis — Correctif mise à l'échelle décimale.** Fixed the defect itself: 5 mappers
  (`OrdreFabricationMapper`, `SectionChargeLingot/Chutage/Decoupe/PitsMapper`) wrote raw un-rescaled KAPE22
  integers into narrow `DECIMAL` columns, discovered via a live SQL Server round-trip failing on all 10 real
  `P60/` fixtures. `DecimalScale.Apply` now wraps all 21 affected columns; blocked production readiness
  until fixed.
- **4.9 — Hardening Épic 4.** Closed the first retro's action items: downstream-table Unit assertions
  (not just `[SkippableFact]` Integration), OF/Coulée trimming moved to the mapper source, a shared
  hot/cold Coulée constant, and widening the persister's rejection path to cover the A-5
  Consignes-collision case.
- **4.10 — Hardening Épic 4 (B).** Closed 5 items the second retro found: 4 had been mis-routed into
  Story 4.9's frozen scope and never actually covered (scale-literal triplication, anti-bypass guard,
  production-parity scope gap, e2e script exit-code gap), plus a magnitude/precision guard (B-5) on
  `DecimalScale.Apply`'s output that the first 4.3-bis review had raised but never promoted to a tracked
  item.
- **4.11 — Hardening Épic 4 (C) + business notes Q-3/Q-5.** Closed the third retro's 10 remaining
  hardening/coverage/documentation gaps (C-1..C-10: magnitude-guard branch coverage, case-insensitive
  Consignes collision, mapper-file/table list de-duplication, a string-length overflow guard mirroring
  B-5, atomicity coverage for 3 more rejection causes, accumulating A-5/B-5/C-4 into one REJETÉ message,
  Pits-invariant visibility, a stale NFR comment, an architecture-doc wording fix), plus two business
  questions the process owner answered (an OF cannot be resubmitted; a Coulée is created once and reused
  as-is) — both confirmed current behavior correct, documented in code comments only, no behavior change.

## 3. Action-item reconciliation at closure

All `sprint-status.yaml` action items are `done` as of this closure. Three older items were superseded by
later work and are now marked `done` with a pointer to what actually closed them, rather than left
formally `open`:

- **`epic-2-retro-item-9`** (CI Integration gate) — superseded by `epic-3-retro-item-1` (service-container
  Linux SQL Server wired into `ci.yml`, 2026-09-14). Its "SQLite Unit-tier fallback" sub-clause was made
  moot by that choice and never built.
- **`epic-2-retro-item-11`** (pre-commit hook for the `AC → test` commit-body convention) — superseded by
  `epic-3-retro-item-2`, implemented as a CI step instead of a local hook (rationale: an uninstalled hook
  protects nothing).
- **`epic-3-retro-item-4`** (extend the `AC → test` commit convention to the `MicroServices.sln` SVN repo)
  — `epic-4-retro-2026-09-17.md` recorded no SVN commit occurred during Epic 4, so the convention was
  never exercised either way; moot now that no further SVN commits are planned.

The ten Epic-4-retro-#3 items (`epic-4-retro3-item-c1`…`-c10`) were found still marked `open` in
`sprint-status.yaml` despite Story 4.11's frozen spec (`spec-4-11-hardening-epic-4-c.md`) showing all
associated tasks `[x]` — a tracking-file gap fixed as part of this closure. All ten are now `done`, each
referencing the exact code/test site that closed it. One scope reduction is called out explicitly: C-5
delivered 3 of its 4 planned integration tests (AC-FR20-3, AC-FR20-4, A-5); the 4th (B-5, magnitude
overflow) is not producible through a real fixed-width P60 fixture — every real source field is narrower
than the width needed to overflow its target column — approved by the user during implementation and
recorded in `deferred-work.md` § "Deferred from: implementation of story-4.11". B-5 stays proven at the
Unit tier (`DecimalMagnitudeGuardTests`, since Story 4.10).

## 4. Deferred work — not resolved at closure

`deferred-work.md` accumulates ~50 dated sections of review findings the team explicitly chose not to fix,
each with its own non-blocking rationale. None are closure blockers; none are tracked as open
`sprint-status.yaml` action items (they were deliberately left as documentation, not commitments). The
recurring categories, with pointers to their sections in `deferred-work.md`:

- **Diagnosability, not correctness** — several rejection paths report only the *first* offending
  cause/column when more than one fires (`FindMagnitudeOverflow`/`FindLengthOverflow`, the A-5 collision
  group). See §"Deferred from: code review of story-4.9", §"Deferred from: code review of story-4.11".
- **Test-tooling gaps, not production risk** — the `AcTraitCoverage` regex doesn't recognize the
  `_AcC*`/`_A5`/`_B5`-style hardening-item test suffixes, so the AC-coverage gate silently skips them
  (pre-existing since Story 4.10); `Configuration()` IConfiguration-builder boilerplate is duplicated
  across ~13 test files. See §"Deferred from: code review of story-4.11" (both dated sections),
  §"Deferred from: implementation of story-4.11".
- **Two independently hand-maintained bundle-entity enumerations** (`DownstreamDecimalEntities` vs. the
  new `DownstreamStringEntities`) not unified into one shared source, unlike C-3's mapper/table list.
  See §"Deferred from: implementation of story-4.11".
- **`L_D_KAPE22`'s own bounded string columns** are outside C-4's new length guard (which only covers the
  9 downstream tables + Consignes) — a pre-existing asymmetry also true of B-5's magnitude guard.
  See §"Deferred from: implementation of story-4.11".
- **Concurrency edge cases assumed unreachable under the current single-worker architecture** — a
  check-then-act race on Coulée creation, an anti-duplicate guard read outside the write transaction.
  Both explicitly gated on "revisit if the orchestrator ever processes Fichiers concurrently," which it
  does not. See §"Deferred from: code review of story-4.6", §"Deferred from: Story 2.8".
- **Historical, low-risk gaps from Epics 1–3** — worker resilience items (`InboxScanner` has no
  poison-pill/max-attempts bound; `PurgeRetention` has no `CancellationToken`; per-file `Delete` inside
  `PurgeRetention` isn't individually guarded), test-fixture hygiene (duplicated `Options()`/`Configuration()`
  helpers, SM-2 archival checked by existence not byte-equality), and documentation-only wording drifts.
  See the dated sections under "Deferred from: code review of story-3.5", "…story-3.2", "…story-2.8",
  "…story-2.1", and the Epic 1 retrospective hygiene pass.

None of these block closure: each was evaluated against its story's actual acceptance criteria and judged
either genuinely out of scope, structurally unreachable under the shipped architecture, or a cosmetic
nice-to-have. If the project is ever reopened, this section plus the full `deferred-work.md` is the
starting punch list.

## 5. Final test state

Re-run at closure time (`dotnet test TextToXml.sln`, commit `8f6e79e`):

- **Unit** (`Category=Unit`): **691 passed, 0 failed, 0 skipped** — 192 in `TextToXml.Tests`, 499 in
  `Kape22Importer.Tests`.
- **Integration** (`Category=Integration`): **146 passed, 0 failed, 100 skipped** (`Kape22Importer.Tests`;
  `TextToXml.Tests` carries no Integration-category tests — CC-6, pure library). The 100 skips are exactly
  the SQL-Server-gated `[SkippableFact]`s (AR-12) — no local SQL Server instance is reachable in this
  environment, which is the expected, documented state for this tier outside of CI (the CI pipeline runs
  these against a service container per `epic-3-retro-item-1`). Not a regression: prior epic
  retrospectives recorded the same 100-test set passing fully instead of skipping when a SQL Server
  instance was reachable in their session.

## 6. Closure

TextToXml / Kape22Importer is closed as of 2026-09-22, last commit `8f6e79e`
(`chore(story-4.11): apply review patches`). No story is in progress; no Epic 5 is planned. Any further
work on this codebase starts as a new, explicitly re-opened initiative, not a continuation of Epic 4's
sprint.
