# Review report — Story 4.10

Range : 8d9927d72fa57c9338a6eaf5be2af6f9ed469df0^..HEAD
Spec : _bmad-output/implementation-artifacts/spec-4-10-hardening-epic-4-b.md
Date : 2026-09-21
Verdict : ACCEPTÉ
Findings : D=0 P=4 F=8 R=4 (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**ACCEPTÉ.** Aucune violation des invariants d'architecture (AD-1..AD-7) ni des
critères transverses (CC-1..CC-7), aucune déviation non documentée du bloc
`<frozen-after-approval>` du spec. Les 4 findings `Patch` sont cosmétiques ou
des lacunes de couverture de test mineures ; tous ont été corrigés pendant
cette session. Les 8 findings `Defer` sont réels mais non bloquants.

Findings par sévérité : 0 `high`, 0 `medium` bloquant, 12 `low`.

## 2. Findings Decision (à trancher par l'humain)

Aucun.

## 3. Findings Patch (corrigés)

- **P1** — Faute d'accents dans un nouveau message d'assertion ("designer"/"reference").
  Localisation : `tests/Kape22Importer.Tests/Kape22ProductionDataParityTests.cs:276`
  Action corrective : remplacé par "désigner la ligne de référence". Appliqué.

- **P2** — Commentaire coupé au retour à la ligne, scindant "MappingAnnexCompleteness"
  autour d'un trait d'union parasite.
  Localisation : `tests/Kape22Importer.Tests/DownstreamColumnMagnitudesParityTests.cs:13-14`
  Action corrective : commentaire ré-enveloppé proprement. Appliqué.

- **P3** — Le commentaire du test `MapperScaleCallSites_ExtractFrom_ReadsPropertyAndLiteralScale`
  prétend couvrir la forme "avec et sans `?? 0`" mais les deux lignes du extrait
  littéral utilisaient `?? 0`, laissant la forme réelle des mappers SectionCharge*
  (sans `?? 0`) jamais exercée par ce test unitaire ciblé.
  Localisation : `tests/Kape22Importer.Tests/MappingAnnexCompletenessTests.cs:269`
  Action corrective : le second call site du extrait littéral (`LongueurCD`) omet
  désormais `?? 0`, conforme à la forme réelle des mappers SectionCharge*. Appliqué,
  20/20 tests du fichier toujours verts.

- **P4** — Absence de test pour une valeur négative hors-gabarit dans le garde-fou
  de magnitude (`Math.Abs` dans `FindMagnitudeOverflow`).
  Localisation : `tests/Kape22Importer.Tests/DecimalMagnitudeGuardTests.cs`
  Action corrective : investigué, non applicable. `NormalizedXmlBuilder.cs:156`
  impose D17 ("Int Champs are always unsigned", `NumberStyles.None`) : aucun champ
  source KAPE22 alimentant une colonne enregistrée dans `DownstreamColumnMagnitudes`
  ne peut jamais porter de valeur négative. `Math.Abs` y est purement défensif et
  inatteignable par tout input réel — un test synthétique testerait un chemin que
  le pipeline réel ne peut jamais emprunter. Aucun test ajouté ; noté dans le spec.

## 4. Findings Defer (avec justification)

Tous ajoutés à `deferred-work.md` sous `## Deferred from: code review of story-4.10 (2026-09-21)`.

- **DF1** — Le bloc de rejet "coulée manquante" (`Kape22Persister.cs:92-112`,
  AC-FR20-5) duplique encore en ligne la séquence `ConversionError`/log
  REJETÉ/`SaveChanges`+catch au lieu d'appeler `RejectWithBusinessRuleViolation`,
  l'helper que cette story a extrait pour les deux autres contrôles voisins.
  Justification : nit de style/cohérence pur, comportement inchangé et correct.

- **DF2** — `DownstreamColumnMagnitudesParityTests` ne verrouille que `10^(p-s)` :
  deux colonnes de `(p,s)` différents mais de même `p-s` seraient indistinguables.
  Justification : aucune collision réelle aujourd'hui (vérifié contre le schéma SQL réel).

- **DF3** — `FindMagnitudeOverflow` (`Kape22Persister.cs:243`) s'appuie sur l'ordre
  d'énumération de `GetProperties()` (détail d'implémentation CLR, non contractuel)
  pour décider quelle colonne nommer en cas de dépassement multiple ; non testé.
  Justification : impact faible, le message signale correctement le rejet dans tous les cas.

- **DF4** — `CompareSectionCharge` (`Kape22ProductionDataParityTests.cs:259`) ne
  garantit pas qu'au moins une comparaison a réellement eu lieu par jeu de fixtures.
  Justification : test `Category=Integration`, opt-in, jamais exécuté en CI sans opt-in.

- **DF5** — `DecimalMagnitudeFor` (`SqlTableSchema.cs:88`) utilise `Math.Pow` en
  double précision avant conversion en `decimal` ; lèverait `OverflowException`
  pour un `DECIMAL(p,s)` avec `p-s >= 29`.
  Justification : aucune colonne du schéma réel n'atteint ce seuil aujourd'hui (max observé : 7).

- **DF6** — `CheckMapperScaleUsage` (`MappingAnnexSchema.cs:202`) ne vérifie que le
  premier `MapperScaleCallSite` via `FirstOrDefault()` par couple Table+Colonne.
  Justification : inatteignable avec le style d'initialiseur d'objet actuel des
  mappers (assigner deux fois la même propriété dans un même initialiseur est une
  erreur de compilation C# CS1912).

- **DF7** — Le filtre `--filter` du script e2e (`e2e-worker-import.ps1:188`)
  pourrait ne matcher aucun test et `dotnet test` sortirait quand même en 0.
  Justification : ce filtre préexiste à cette story ; seul le contrôle
  `$LASTEXITCODE` (ligne 191) est nouveau.

- **DF8** — Les références de numéros de ligne dans "Suggested Review Order"/"Code
  Map" du spec se périmeront silencieusement au prochain changement des fichiers cités.
  Justification : même classe de risque que tout autre spec de ce projet, pas spécifique à 4.10.

## 5. Findings rejetés (bruit)

- **R1** — Déréférencement `bundle.OrdreFabrication!` non gardé, signalé comme
  incohérent avec le style de vérifications nulles environnant.
  Justification du rejet : faux positif — cohérent avec le pattern déjà établi
  dans la même méthode (`bundle.Kape22!`, `bundle.NumeroFichier!`, `bundle.OF!`,
  `bundle.Coulee!`) pour les champs garantis non-null sur le chemin de succès.

- **R2** — Portée `Sourced`-only de `CheckMapperScaleUsage` (B-1/B-2).
  Justification du rejet : déjà consigné mot pour mot dans `deferred-work.md`
  par cette même diff (entrée `story-4.10 code review (2026-09-18)`, edge-case-hunter).

- **R3** — Message REJETÉ de B-5 mêlant un fragment façon C# au français.
  Justification du rejet : déjà consigné mot pour mot dans `deferred-work.md`
  par cette même diff (blind-hunter), suit la convention "differé accepté" du projet.

- **R4** — Absence de test de régression automatisé pour le garde-fou
  `$LASTEXITCODE` du script e2e (B-4).
  Justification du rejet : déjà consigné mot pour mot dans `deferred-work.md`
  par cette même diff (verification-gap) ; reconfirmé indépendamment par la
  couche Verification Gap elle-même comme "pas une nouvelle découverte".

## 6. Auto-vérifications

**4 lentilles lancées** (toutes actives, aucun `when` non satisfait, aucun échec) :
- Blind Hunter — 13 findings bruts
- Edge Case Hunter — 3 findings bruts
- Verification Gap Reviewer — 1 finding brut (auto-identifié comme déjà consigné)
- Acceptance Auditor — 0 finding (JSON vide), 2 items `out_of_mandate` non analysés

**Diff stats** (range `8d9927d72fa57c9338a6eaf5be2af6f9ed469df0^..HEAD`) :
11 fichiers changés, +864/-15, 1018 lignes de diff.

**Vérification post-patch** :
- `dotnet build TextToXml.sln -warnaserror` → clean, 0 warning, 0 erreur.
- `dotnet test TextToXml.sln --filter Category=Unit` → 192 (TextToXml.Tests) +
  490 (Kape22Importer.Tests) = 682 tests verts, 0 échec.
