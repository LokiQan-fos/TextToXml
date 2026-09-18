# Review report — Story 4.9

Range : 331461edaf73f1e943bdebec5e15ec9710cbb7a8^..HEAD
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-4-9-hardening-epic-4.md
Date : 2026-09-18
Verdict : ACCEPTÉ
Findings : D=0 P=1 F=4 R=7  (Decision, Patch, Defer, Rejetés)

## 1. Verdict

**ACCEPTÉ.** Aucun finding `high`, aucun `decision-needed`. Un seul `patch` (documentation, non bloquant), quatre `defer` déjà couverts ou dûment justifiés, sept findings de bruit écartés après lecture du code.

## 2. Findings Decision (à trancher par l'humain)

Aucun.

## 3. Findings Patch

- **[P-1]** Code Map miscounts `AscoLsiDbContext`'s DbSets as 10 (`Kape22Rows + 9 downstream`) — the referenced line range declares 12 DbSets; the A-2 assertions actually cover 9 dispatch tables + `ConsignesRows`, and `LogCommandeRows` is uncounted either way.
  Localisation : `_bmad-output/implementation-artifacts/spec-4-9-hardening-epic-4.md` (Code Map) / `src/Kape22Importer/Persistence/AscoLsiDbContext.cs:15-37`
  Correction proposée : corriger la phrase du Code Map pour refléter le compte réel (12 `DbSet` au total ; 9 tables de dispatch Story 4.1 + `ConsignesRows` couverts par les assertions A-2).

## 4. Findings Defer (justification)

- **[F-1]** A-5's pre-check reports only the first colliding `CodeOperation` group (`.FirstOrDefault(group => group.Count() > 1)`) — a bundle with two independent collisions only ever surfaces one.
  Localisation : `src/Kape22Importer/Persistence/Kape22Persister.cs:121`
  Justification : conforme à la contrainte figée du spec ("a collision produces one ConversionError") — l'élargir exigerait une renégociation du spec. Déjà journalisé dans `deferred-work.md` par la passe de revue précédente.

- **[F-2]** The missing-Coulee and Consignes-collision checks in `PersistMapped` are sequential early-returns, not accumulated — a Fichier failing both only ever reports the missing-Coulee error.
  Localisation : `src/Kape22Importer/Persistence/Kape22Persister.cs:91,110`
  Justification : cohérent avec le pattern établi de `Kape22Persister` (un contrôle à la fois, retour anticipé). Déjà journalisé dans `deferred-work.md` par la passe de revue précédente ; hors du périmètre déclaré de cette story.

- **[F-3]** No test exercises a blank/whitespace-only `OF` or `Coulee` Champ through the new unconditional `entity.OF.Trim()`/`entity.Coulee.Trim()`.
  Localisation : `src/Kape22Importer/Kape22Mapper.cs:112`
  Justification : vérifié sûr par lecture du code — `Kape22FileMessage.OF`/`.Coulee` valent `string.Empty` par défaut, jamais `null`, donc `.Trim()` ne peut pas lever d'exception. Lacune de couverture uniquement, pas de risque de correction. Nouvelle entrée ajoutée à `deferred-work.md`.

- **[F-4]** A-5's REJETÉ message cites only the raw colliding `CodeOperation` value, not which two sections collided (e.g. Chutage vs Decoupe).
  Localisation : `src/Kape22Importer/Persistence/Kape22Persister.cs:124`
  Justification : confort de diagnostic, non bloquant — le spec figé exige seulement de nommer l'OF et de signaler la collision, ce qui est fait. Nouvelle entrée ajoutée à `deferred-work.md`.

## 5. Findings rejetés (bruit)

- sprint-status.yaml reste à `review` alors que le spec affiche `status: 'done'` — état attendu avant l'exécution de `/commit-review`, qui est l'étape qui bascule le statut à `done`.
- `review_loop_iteration: 0` malgré des patches déjà appliqués — convention déjà en vigueur sur ce projet (ex. `spec-4-3-bis`, `spec-2-4`) : 0 signifie « pas de boucle décision→retravail », pas « aucune revue exécutée ».
- « Ask First: None ... no open design choice » prétendument contredit par les deux choix mineurs déférés — ce ne sont pas des choix de conception nécessitant une porte humaine, mais des limites documentées et justifiées ; pas une violation de boundary.
- `ColdConsignePits_IsTheOneSharedConstantBothCollaboratorsReference_A4` ne détecterait pas un doublon reconstruit sous un autre nom — sur-ingénierie du test suggéré ; portée du test conforme à son intention (A-4 anti-régression nommée).
- Le tuple 4 parties `(OF, CodeOperation, TypeConsigne, ConsigneGPAO)` s'appuie sur `TypeConsigne`/`ConsigneGPAO` non vérifiés — explicitement couvert par la clause « Never » du spec figé (« No changing L_D_CONSIGNES's TypeConsigne/ConsigneGPAO defaults »).
- Duplication supposée dans les 2 nouveaux tests A-5 (`Assert.Empty` répétés au lieu d'un helper) — vérifié faux par lecture du fichier : `TransactionalPersistenceTests.cs` répète déjà ce motif littéral à 4 endroits préexistants (lignes 132-141, 192-203, 313-329, 447-460) ; les nouveaux tests suivent la convention établie du fichier, pas une déviation.
- `Kape22Mapper.cs:112` trim s'exécute avant `RequiredFieldCheck` (signalé hors mandat par l'Acceptance Auditor) — comportement préexistant inchangé par ce diff (le même trim inconditionnel existait déjà à l'ancienne ligne 146) ; aucun risque de NRE confirmé (valeurs par défaut `string.Empty`).

## 6. Auto-vérifications

**4 lentilles lancées** (mode `full`, spec chargé) :

| Layer | Résultat |
|---|---|
| Blind Hunter | 12 observations brutes (avant dédoublonnage/triage) |
| Edge Case Hunter | 2 observations (doublons des mêmes 2 points que Blind Hunter) |
| Verification Gap Reviewer | Aucun écart — 5 changements comportementaux tracés jusqu'à leur test le plus proche |
| Acceptance Auditor | Aucun finding formel ; 1 remarque hors mandat (pré-existante, non bloquante) |

**Diff stats** : 10 fichiers changés, +380/-10 (558 lignes de diff) — `deferred-work.md`, `spec-4-9-hardening-epic-4.md` (nouveau), `sprint-status.yaml`, `Kape22ImportBundle.cs`, `Kape22ImportBundleMapper.cs`, `Kape22Mapper.cs`, `Persistence/Kape22Persister.cs`, `Kape22FichierProcessorTests.cs`, `Kape22MapperTests.cs`, `TransactionalPersistenceTests.cs`.

**Contexte notable** : ce diff correspond à un unique commit (`331461e`) qui inclut déjà, de façon inhabituelle pour ce projet, les 7 patches d'une revue informelle antérieure appliqués directement (documenté dans le message de commit et dans `deferred-work.md`). Cette revue `/run-review` formelle constitue une seconde passe indépendante sur le même diff ; elle confirme la disposition des 2 items déjà déférés et en ajoute 2 nouveaux, mineurs.
