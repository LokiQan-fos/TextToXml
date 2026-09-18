# Review report — Story 4.3-bis

Range : a277925^..HEAD
Spec : `_bmad-output/implementation-artifacts/spec-4-3-bis-correctif-mise-a-l-echelle-decimale.md`
Date : 2026-09-18
Verdict : ACCEPTÉ
Findings : D=0 P=7 F=1 R=6  (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**ACCEPTÉ.** 0 `decision-needed`, 7 `patch` (tous appliqués), 1 `defer` (documenté), 6 rejetés
comme bruit. Aucune violation de critère transverse (CC-1..7) ni d'invariant d'architecture
(AD-1..7) — confirmé par la lentille Acceptance Auditor (0 finding). Build strict et suite
`Category=Unit` verts (480/480, dont les 2 nouveaux tests ajoutés par les patches).

## 2. Findings Decision (à trancher par l'humain)

Aucun.

## 3. Findings Patch (appliqués)

Tous cochés dans la section "Review Findings" du spec ; correction effective vérifiée par
`dotnet build -warnaserror` + `dotnet test --filter Category=Unit` (480/480 verts).

1. **Ancre cassée dans le nouveau `ref:` de sprint-status.yaml** — `story-4.3-bis` se slugifie en
   `story-43-bis` (le point est supprimé, pas remplacé par un tiret), pas `story-4-3-bis` comme
   écrit. [`_bmad-output/implementation-artifacts/sprint-status.yaml:96`]
   Correction : ancre réécrite en `#resolved-by-story-43-bis-implementation-2026-09-17`.

2. **Incohérence de statut dans le spec lui-même** — la section "Suggested Review Order" (hors
   bloc figé) affirme que le flag sprint-status est passé à `in-progress`, alors que le diff le
   met à `review`. [`spec-4-3-bis-correctif-mise-a-l-echelle-decimale.md:166`]
   Correction : texte aligné sur `review`.

3. **4 nouvelles entrées deferred-work.md sans action_items** — les 4 findings produits par la
   propre revue de code de cette story (triplication des littéraux d'échelle, absence de garde
   anti-contournement, trou de parité production, absence de contrôle `$LASTEXITCODE`) n'avaient
   pas d'entrée `action_items` dans sprint-status.yaml, contrairement à la convention établie
   (`epic-4-retro-item-2..5`). [`deferred-work.md:74-117`]
   Correction : 4 entrées `action_items` ajoutées (`story-4-3-bis-review-item-1..4`, epic 4, owner
   Dev, status open, pointant vers Story 4.9).

4. **Entrée "Resolved by" hors gabarit** — ne suivait pas le gabarit `source_spec:`/`summary:`/
   `evidence:` documenté par le fichier lui-même (prose libre). [`deferred-work.md:96-106`]
   Correction : reformatée au gabarit standard.

5. **Entrées obsolètes non marquées résolues** — les sections "Deferred from: story-4.6
   implementation" et les deux sous-entrées `ZeroOutOfScaleDimensions`/`OutOfScaleDimensionFields`
   sous "Deferred from: code review of story-4.6" restaient lisibles comme ouvertes.
   [`deferred-work.md:55,151`]
   Correction : marqueur `**RESOLVED**` ajouté sur chacune, renvoyant vers l'entrée de résolution.

6. **`DecimalScaleTests.MappedFichier` sans garde `Success`** — déréférençait `Map(...).Value!`
   sans vérifier `Success`, produisant une `NullReferenceException` opaque en cas d'échec de
   mapping plutôt qu'un échec d'assertion clair. [`tests/Kape22Importer.Tests/DecimalScaleTests.cs:75`]
   Correction : `Assert.True(result.Success, ...)` ajouté avant le déréférencement.

7. **Aucun test pour la garde de dépassement de `DecimalScale`** — le chemin `throw` de
   `Pow10ForScale` (échelle hors 0-3) n'était exercé par aucun test.
   [`src/Kape22Importer/DecimalScale.cs:17-19`]
   Correction : nouveau `[Fact] Apply_ScaleOutOfRange_ThrowsArgumentOutOfRangeException`.

## 4. Findings Defer (justifiés)

1. **`DecimalScale.Apply` ne valide pas la magnitude du résultat mis à l'échelle contre la
   précision totale `DECIMAL(p,s)` de chaque colonne** — un entier brut anormalement grand,
   correctement mis à l'échelle, pourrait encore dépasser le maximum de la colonne et provoquer
   un débordement SQL brut au moment de la persistance.
   [`src/Kape22Importer/DecimalScale.cs:12`]
   Justification du report : explicitement hors du périmètre déclaré par le spec ("Ask First:
   None — conversion values are fixed by the annex/DDL, no design choice beyond DecimalScale's
   shape"). Une garde de magnitude serait un nouveau choix de conception, pas un correctif
   d'échelle littérale. Candidat pour le hardening de la Story 4.9. Ajouté à `deferred-work.md`
   sous "Deferred from: code review of story-4.3-bis (2026-09-18)".

## 5. Findings rejetés (bruit)

1. **Assertions de mappers tautologiques** (`Assert.Equal(DecimalScale.Apply(source.X, scale), ...)`)
   — un mauvais littéral d'échelle copié de façon cohérente dans le mapper et son test passerait
   silencieusement. Rejeté : déjà documenté et accepté comme risque connu dans l'entrée
   deferred-work.md ajoutée par le développeur lui-même pendant l'implémentation (triplication des
   littéraux), reprise au Finding Patch #3 ci-dessus pour son suivi.

2. **Ligne "Non-nullable target, null source" de la matrice I/O du spec non testée directement**
   — aucun test n'alimente une valeur `null` réelle pour une colonne non-nullable. Rejeté :
   couverture indirecte suffisante (`Apply_ZeroRawValue_ReturnsZero` prouve l'arithmétique une
   fois `?? 0` appliqué ; le `?? 0` lui-même est trivial et déterministe) ; la lentille
   Verification Gap Reviewer a explicitement tracé ce chemin et n'a rapporté aucun trou.

3. **Absence de preuve d'exécution pour le déblocage de `GpaoImportP60WorkerEndToEndTests`** —
   rejeté : la lentille Verification Gap Reviewer a explicitement vérifié ce point (tests Unit
   480/480 verts, tentative Integration bloquée par l'absence de SQL Server dans le bac à sable,
   conforme à la précondition AR-12 déjà documentée par le spec) et n'a rapporté aucun trou.

4-6. **Trois findings portant sur `.claude/commands/build-story.md` / `_bmad-output/project-profile.md`**
   (bundling de portée non lié, risque de duplication de texte, cas `$ARGUMENTS` vide non
   couvert) — rejetés comme hors mandat de cette revue : ces changements appartiennent au commit
   `1b1d2e9` ("chore(bmad): make /build-story project-agnostic..."), sans rapport avec le correctif
   decimal-scale et sans spec associé. Ils ont été balayés dans le diff uniquement parce que
   l'algorithme de calcul de plage (`<oldest-sha>^..HEAD`) va jusqu'à HEAD, pas parce qu'ils font
   partie de la story 4.3-bis. Signalé à l'utilisateur séparément, non noté contre ce spec.

## 6. Auto-vérifications

**4 lentilles lancées** (toutes complètes après une reprise suite à un rate-limit temporaire sur 2
d'entre elles) :
- Blind Hunter — 12 findings bruts
- Edge Case Hunter — 3 findings JSON
- Verification Gap Reviewer — `No verification gaps found.` (0 finding)
- Acceptance Auditor — `[]` (0 finding ; CC-1..7 et AD-1..7 audités, aucune déviation)

**Diff stats** : 20 fichiers, +450/-141 (890 lignes de diff unifié), commits `a277925` (fix) +
`4914a8d` (chore) — plus le commit hors-story `1b1d2e9` balayé par le calcul de plage (voir §5,
items 4-6).

**Vérification post-patch** :
- `dotnet build TextToXml.sln -warnaserror` → 0 warning, 0 erreur.
- `dotnet test TextToXml.sln --filter Category=Unit` → 480/480 verts (192 TextToXml.Tests +
  480 Kape22Importer.Tests, dont les 2 nouveaux tests introduits par les patches #6 et #7).
- Tests `Category=Integration` non exécutés dans ce sandbox (SQL Server local non joignable —
  conforme à la précondition AR-12 déjà documentée par le spec).
