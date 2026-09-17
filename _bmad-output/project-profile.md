# Project Profile — TextToXml

Ce fichier est chargé comme fait persistant par les workflows BMAD. Il sert de
référence unique pour les conventions du projet. Il **référence** les artefacts
amont au lieu de les dupliquer — toute règle vit dans son fichier d'origine.

## Identité

- Langage : C#
- Runtime : .NET 10.0
- Frameworks : xUnit (tests), Entity Framework Core 10 (persistance), Serilog (journalisation)
- Racine du dépôt : `TextToXml.sln`

## Layout

- Code de production : `src/TextToXml/` (bibliothèque pure), `src/Kape22Importer/` (bibliothèque de format P60)
- Tests : `tests/TextToXml.Tests/`, `tests/Kape22Importer.Tests/`
- Specs de story : `_bmad-output/implementation-artifacts/spec-{slug}.md`
- Rapports de revue : `_bmad-output/implementation-artifacts/reviews/`
- Ledger de dette : `_bmad-output/implementation-artifacts/deferred-work.md`
- Suivi de sprint : `_bmad-output/implementation-artifacts/sprint-status.yaml`
- Artefacts amont : `_bmad-output/planning-artifacts/`

## Commandes

- Build strict : `dotnet build TextToXml.sln -warnaserror`
- Tests unitaires : `dotnet test TextToXml.sln --filter Category=Unit`
- Tests d'intégration : `dotnet test TextToXml.sln --filter Category=Integration`
- Gates d'architecture (inclus dans Category=Unit) :
  `AcTraitCoverageTests`, `AcCoverageCompletenessTests`, `SolutionStructureTests`,
  `FormatIsolationTests`

## Catégories de tests

- `[Trait("Category","Unit")]` — aucun prérequis, tourne partout.
- `[Trait("Category","Integration")]` — nécessite une instance SQL Server locale
  accessible, skip propre si absente (voir AR-12).

## Conventions de test

- Nommage : `Methode_Scenario_ResultatAttendu_AcFrX_Y` (le suffixe cite l'AC).
- Portage de l'AC : `[Trait("AC","FRx-y")]` sur chaque test nommé d'après un AC.
- Structure : Arrange / Act / Assert explicite.
- Les tests-barrière-à-la-compilation (réflexion, complétude) sont exemptés du
  cycle rouge→vert propre (CC-1).

## Conventions de commit

- Format : `<type>(story-N.M): <titre>` — types `feat`, `fix`, `chore`, `refactor`.
- Corps du commit : pour chaque AC du périmètre, une ligne `AC-FRx-y → FichierTest.NomDuTest`.
- Le gate CI refuse un commit `chore(story-*)` dont le corps (hors trailer
  Co-Authored-By) ne mentionne ni `AC-FRx-y` ni « test » (epic-3-retro-item-2).

## Langues

- Commentaires de code : anglais (CC-2).
- Identifiants (types, méthodes, variables) : anglais.
- Rapports de revue : français autorisé.
- Documentation projet : français autorisé.

## Critères transverses

Référence : `_bmad-output/planning-artifacts/epics.md` § « Critères d'acceptation
transverses (CC) ». Rappel des identifiants :

- CC-1 — TDD strict (résultat + attestation commit).
- CC-2 — Commentaires anglais, capitale + point, pas de liste numérotée.
- CC-3 — Pas de trailing comment, commentaire au-dessus du bloc, préservation.
- CC-4 — Tri alphabétique (+ dérogation R-7 tuples nommés/déconstructions ;
  + exemption R-5 DTO `Kape22File.cs` généré).
- CC-5 — Vocabulaire du glossaire PRD §3 réutilisé à l'identique.
- CC-6 — `TextToXml` reste pur (zéro I/O, zéro dépendance métier P60).
- CC-7 — Aucun secret ni chaîne de connexion en dur.

## CC-2 — Dérive vocabulaire

Les valeurs littérales du vocabulaire du glossaire PRD §3 (`sourcée`, `règle`,
`à_clarifier`, `Fichier`, `Ligne`, `Bloc`, `Champ`, `Descripteur`, etc.) ne
violent pas CC-2 quand elles apparaissent dans le code ou les commentaires
comme identifiants ou valeurs d'énumération réutilisées à l'identique (CC-5).
CC-2 vise les phrases de commentaire multi-mots en français.

## Invariants d'architecture

Référence : `_bmad-output/planning-artifacts/architecture/architecture-kape22-dispatch-2026-09-14/ARCHITECTURE-SPINE.md`.

Applicables dès qu'une story touche le dispatch aval (Épic 4) :

- AD-1 — Un seul `SaveChanges()` couvre `L_D_KAPE22` + les 9 tables aval.
- AD-2 — Mappers explicites, zéro `System.Reflection`, zéro `Activator.CreateInstance`.
- AD-3 — Étanchéité avec le legacy `Ascometal.LSI.*` : jamais référencé en assembly,
  jamais appelé au runtime.
- AD-4 — Toute cause d'échec passe par le circuit existant
  (`L_D_LOG_COMMANDE` + `MQTTnetServices.Logs` + `error/`), pas de nouveau canal.
- AD-5 — Entités EF database-first depuis `AFV004-LSI`, aucune migration.
- AD-6 — `Kape22Persister.Persist` remplacé (pas surchargé).
- AD-7 — FK explicites en colonnes scalaires, aucune propriété de navigation EF.

## Stratégie d'intégration

- Instance SQL Server locale + `scripts/schema/` versionné (AR-12).
- **Jamais Docker ni Testcontainers** — décision utilisateur du 2026-09-04,
  documentée dans `_bmad-output/planning-artifacts/sprint-change-proposal-2026-09-04.md`.
- Deux régimes d'isolation :
  - `TransactionScope` + rollback (défaut) ;
  - commit réel + reset (garde-fou anti-doublon, frontières de transaction).
- `[SkippableFact]` si aucune instance joignable, avec dépendance bloquante citée.

## Patron `assumed, unverified`

Une colonne ou une valeur marquée `à_clarifier` dans l'annexe de mapping se code
avec un commentaire `assumed, unverified` citant l'entrée `deferred-work.md`
correspondante. Jamais de règle inventée. (AC-FR17-5 mécanise ce contrôle.)

## Ledger de dette

Vit dans `deferred-work.md`, sous un titre daté `## Deferred from: <origine> (<date>)`.
Format d'entrée (un bloc par item déféré) :

- `source_spec:` document(s) source (epics.md § Story, annexe de mapping, etc.)
- `summary:` description factuelle du point différé
- `evidence:` comment il a été constaté (revue de code, lecture legacy, etc.) et pourquoi il n'est pas bloquant


## Discipline de routage bmad-build

Toutes les stories de ce projet passent par la route `plan-code-review`.
La route `one-shot` est interdite — la discipline spec-first (spec figé au
step-02, validé par checkpoint humain, avant toute implémentation) est
non négociable.

Motivation : la conformité aux critères transverses (CC-1 TDD strict,
CC-4 tri alphabétique, CC-5 vocabulaire du glossaire) et aux invariants
d'architecture (AD-1 à AD-7, listés ci-dessus) ne peut être garantie sans
spec figé préalable.
