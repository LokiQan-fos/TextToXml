# Review report — Story 5.0

Range : baad85d6555cbb94f2639c2693a0a64b51bbabb7^..HEAD (baad85d)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-5-0-journal-fichier-interface-implementation-lsi.md
Date : 2026-09-25
Verdict : ACCEPTÉ
Findings : D=2 P=7 F=2 R=18  (Decision, Patch, Defer, Rejetés)
Mise à jour 2026-09-25 : D-1 tranché (option 2) → P-8 ; D-2 tranché (option 1) → P-9. Reste : D=0 P=9 F=2 R=18
Mise à jour 2026-09-25 : P-1..P-9 appliqués dans l'arbre de travail (option « tout appliquer ») ; F-1, F-2 restent à consigner par `/commit-review`.

## 1. Verdict

**ACCEPTÉ**, sous réserve de trancher D-1 et D-2, puis d'appliquer P-1..P-7 dans le commit de clôture (`/commit-review`).

- Code de production conforme au bloc figé : forme D8 du message, saut D15, `NumLingot 0`, `Trace true`, repli `User` sur le nom de machine,
  `ArgumentException` à la construction, un contexte + un `SaveChanges` par `Record`, transaction ambiante supprimée (D31).
- Frontières respectées : `FichierJournal` sans référence, `AscoLsiJournal` = `FichierJournal` + EF SqlServer ; aucune ligne sous
  `src/Kape22Importer/` ni `src/TextToXml/` dans le range ; aucun nouveau package (InMemory / SqlServer déjà épinglés).
- Aucune déviation CC-2 / CC-3 (commentaires anglais, capitale + point, aucun trailing) ni CC-4 (propriétés, initialiseurs, items csproj triés).
  AD-3 / AD-5 / AD-7 respectés. CC-7 : aucune chaîne de connexion en dur.
- `dotnet test tests/AscoLsiJournal.Tests` : 14 passed, 0 skipped (intégration AR-12 incluse), exécuté pendant la revue.
- Les findings retenus portent sur la robustesse des preuves (heure d'été, garde D31 hors SQL Server, TRUNCATE partagé entre assemblies)
  et sur la cohérence des artefacts de planification réécrits par le même commit.

| Sévérité | Nombre |
|---|---|
| high | 0 |
| medium | 5 (D-2, P-1, P-2, P-5, P-6) |
| low | 6 (D-1, P-3, P-4, P-7, F-1, F-2) |

## 2. Decision — à trancher par l'humain

### D-1 — `FichierJournalEntry.Succeeded` hors de la forme figée de l'entrée (low)

- **Source** : acceptance-auditor
- **Localisation** : `src/FichierJournal/FichierJournalEntry.cs:26`
- **Description** : le bloc figé définit l'entrée comme exactement `Commande`, `FichierName`, `Instant`, `NumeroFichier?`, `OF?`, `Reasons`
  (« empty = success »). Le type public expose un 7ᵉ membre, `Succeeded => Reasons.Count == 0`, sans note de renégociation datée dans le Spec Change Log.
  Membre dérivé, sans état, qui ne fait que nommer la règle « Reasons vide = succès ».
- **État** : **tranché par l'humain le 2026-09-25 → option 2** (retirer `Succeeded`). Devient le patch P-8.
- **Options** :
  1. Garder `Succeeded` → ajouter au Spec Change Log une note datée « Succeeded accepted as the derived form of Reasons empty = success ».
  2. Retirer `Succeeded` → `AscoLsiFichierJournal.Record` teste `entry.Reasons.Count == 0` directement.

### D-2 — Deux (bientôt trois) assemblies de test vident les mêmes tables de `AscoLSI_Test` (medium)

- **Source** : verification-gap (+ blind-hunter, partiel)
- **Localisation** : `tests/AscoLsiJournal.Tests/AscoLsiJournal.Tests.csproj:42-43` ; `tests/Kape22Importer.Tests/SqlServerIntegrationFixture.cs:88-117` ;
  `.github/workflows/ci.yml:63`
- **Description** : la fixture AR-12 est désormais compilée dans `Kape22Importer.Tests` **et** `AscoLsiJournal.Tests` (et `P89Converter.Tests`
  non commité la lie aussi). `ResetData()` fait `TRUNCATE TABLE dbo.L_D_LOG_COMMANDE` (et 24 autres tables) sur la même base.
  `[Collection(SqlServerIntegration)]` ne sérialise qu'au sein d'une assembly ; `dotnet test TextToXml.sln` lance les projets de test en parallèle
  (aucun `-m:1` / runsettings dans le dépôt). Les `Assert.Single` / `Assert.Empty` sur `L_D_LOG_COMMANDE` (AC-FR23-4 ici, FR-14 dans
  `DoubleJournalIntegrationTests`) peuvent passer ou échouer selon le timing. Risque vérifié par lecture, collision non observée.
- **État** : **tranché par l'humain le 2026-09-25 → option 1** (sérialiser l'Integration au niveau solution, `-m:1`). Devient le patch P-9.
- **Options** :
  1. Sérialiser l'exécution Integration au niveau solution (`dotnet test TextToXml.sln --filter Category=Integration -m:1` dans CI, README,
     `/commit-review`, `project-profile.md` § Commandes).
  2. Une base de test par assembly (nom de base dérivé de l'assembly dans la fixture ; schéma appliqué par assembly).
  3. Différer : consigner dans `deferred-work.md` et traiter avant la clôture de l'Épic 5 (quand `P89Converter.Tests` ajoutera le 3ᵉ lien).

## 3. Patch

### P-1 — La garde D31 (suppression de la transaction ambiante) n'est prouvée que par un test SQL Server skippable (medium)

- **Source** : blind-hunter
- **Localisation** : `src/AscoLsiJournal/AscoLsiFichierJournal.cs:41` ; `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalIntegrationTests.cs:43-56`
- **Description** : sur toute machine sans instance (CI `ubuntu-latest`), supprimer `using TransactionScope independent = new(TransactionScopeOption.Suppress);`
  laisse la suite verte.
- **Action** : ajouter un test `Category=Unit` (`[Trait("AC","FR23-4")]`) qui ouvre un `TransactionScope`, appelle `Record` avec une fabrique de
  contexte InMemory qui capture `Transaction.Current`, et vérifie qu'il vaut `null` dans la fabrique. Relever le plancher `AcTraitCoverageTests` si besoin.

### P-2 — Conversion Paris testée seulement en hiver (medium)

- **Source** : verification-gap + blind-hunter
- **Localisation** : `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs:16-17, 31` ; `src/AscoLsiJournal/AscoLsiFichierJournal.cs:46`
- **Description** : seul `2026-02-10 08:00Z → 09:00` est vérifié. `UtcDateTime.AddHours(1)` passe tous les tests ; `Instant.LocalDateTime` aussi sur
  l'hôte de dev (Romance Standard Time).
- **Action** : ajouter un cas été (`2026-07-10 08:00Z → 10:00`), en `[Theory]` avec le cas hiver ou en `[Fact]` voisin, `[Trait("AC","FR23-2")]`.

### P-3 — Le test d'intégration ne vérifie pas la colonne `Date` (low)

- **Source** : blind-hunter
- **Localisation** : `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalIntegrationTests.cs:28-37, 60-68`
- **Description** : le Spec Change Log annonce « every column », mais `Date` n'est pas lue contre la vraie colonne `DATETIME` (précision ~3 ms,
  différente d'InMemory).
- **Action** : utiliser un `Instant` fixe dans `Entry()` (ex. `2026-02-10 08:00Z`) et asserter `row.Date == new DateTime(2026, 2, 10, 9, 0, 0)`.

### P-4 — Cas `OF = ""` absent de la théorie D15 (low)

- **Source** : blind-hunter
- **Localisation** : `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs:87-89`
- **Description** : la matrice dit « null or blank » ; la théorie couvre `null` et `" "` mais pas `""` (la théorie `User` voisine le couvre).
- **Action** : ajouter `[InlineData("")]`.

### P-5 — `epic-5-context.md` périmé par rapport au re-planning 5.0 / 5.1 / 5.2 (medium)

- **Source** : blind-hunter
- **Localisation** : `_bmad-output/implementation-artifacts/epic-5-context.md:11, 28, 30, 35`
- **Description** : liste seulement Story 5.1 ; dit que `P89Converter` référence `Kape22Importer` pour l'entité `L_D_LOG_COMMANDE` (contredit
  AC-FR22-7 : `TextToXml` + `FichierJournal` seulement) ; « both new projects join TextToXml.sln » ; « None inside the epic » et « re-closes at
  Story 5.1 closure » alors que la séquence est 5.0 → 5.1 → 5.2. Ce fichier est le contexte chargé par la spec 5.1 : il induirait le dev en erreur.
- **Action** : aligner sur `epics.md` Épic 5 : lister 5.0/5.1/5.2, références `TextToXml` + `FichierJournal`, journal via `IFichierJournal`,
  dépendances 5.0 → 5.1 → 5.2, clôture à la Story 5.2.

### P-6 — PRD D30 contredit FR-22 réécrit (« worker P89 : plus tard ») (medium)

- **Source** : blind-hunter
- **Localisation** : `_bmad-output/planning-artifacts/PRD.md:124` (D30)
- **Description** : D30 dit « Mapping, persistance et worker P89 : plus tard », alors que FR-22, AC-FR22-8 et Story 5.2 livrent le worker
  `GpaoConvertP89` dans l'Épic 5.
- **Action** : retirer « worker » de D30 et ajouter « (worker `GpaoConvertP89` : Épic 5, Story 5.2, correction 2026-09-25) ».

### P-7 — README : « Deux livrables » alors que le tableau en liste quatre (low)

- **Source** : blind-hunter
- **Localisation** : `README.md:3`
- **Description** : l'intro annonce deux livrables ; le tableau a maintenant 4 lignes (`TextToXml`, `Kape22Importer`, `FichierJournal`, `AscoLsiJournal`).
- **Action** : remplacer par « Livrables : » (ou « Quatre livrables : »). `P89Converter` sera ajouté par la Story 5.1.

### P-8 — Retirer `FichierJournalEntry.Succeeded` (issu de D-1, low)

- **Source** : acceptance-auditor (D-1, option 2)
- **Localisation** : `src/FichierJournal/FichierJournalEntry.cs:26` ; `src/AscoLsiJournal/AscoLsiFichierJournal.cs:35`
- **Action** : supprimer la propriété `Succeeded` (seul usage : `Record`) ; `Record` teste `entry.Reasons.Count == 0`. L'entrée revient à la forme
  figée exacte. Aucun test à modifier (aucun test n'utilise `Succeeded`).

### P-9 — Sérialiser l'exécution `Category=Integration` au niveau solution (issu de D-2, medium)

- **Source** : verification-gap (D-2, option 1)
- **Localisation** : `.github/workflows/ci.yml:62-64` ; `README.md:93` ; `.claude/commands/commit-review.md:90` ; `_bmad-output/project-profile.md` § Commandes
- **Action** : ajouter `-m:1` à chaque `dotnet test TextToXml.sln ... --filter Category=Integration`, pour que les assemblies qui partagent
  `SqlServerIntegrationFixture` (`Kape22Importer.Tests`, `AscoLsiJournal.Tests`, puis `P89Converter.Tests`) ne vident pas `AscoLSI_Test` en
  concurrence. Un commentaire anglais au-dessus de l'étape CI en donne la raison.

## 4. Defer

### F-1 — `NumeroFichier` null avec `OF` lisible → message « — OK » sans numéro (low)

- **Source** : blind-hunter + edge-case-hunter + acceptance-auditor (hors mandat)
- **Localisation** : `src/AscoLsiJournal/AscoLsiFichierJournal.cs:34-37`
- **Justification** : le contrat autorise `NumeroFichier` null, mais ni D8 ni la matrice ne fixent le texte à écrire dans ce cas. P60 ne peut pas le
  produire ; pour P89, le cas n'existe que si `NumeroFichier` et `OF` sont lus séparément. À trancher par la Story 5.1, qui connaît le
  producteur (repli sur `FichierName` ou marqueur explicite), avec un test.

### F-2 — L'`ArgumentException` nomme `InitiatingServer`, pas la clé de configuration complète (low)

- **Source** : blind-hunter
- **Localisation** : `src/AscoLsiJournal/AscoLsiFichierJournal.cs:14, 63-66`
- **Justification** : la bibliothèque ne lit pas la configuration ; la clé réelle (`AscoLsiJournal:InitiatingServer`, epics.md Story 5.2) est
  propre au worker. La Story 5.2 doit valider/nommer la clé complète au démarrage (AC-FR22-8, même esprit que « dossier non configuré »).

## 5. Rejetés (bruit)

| # | Source | Finding | Justification du rejet |
|---|---|---|---|
| R-1 | edge-case + blind + auditor | Pas de garde de longueur sur `OF` (12) / `Commande` (50) | `OF` P89 = Size 7 (`Templates/P89.xml:22`), P60 identique ; `Commande` est une constante du format. Un dépassement propage l'exception SQL, conformément à la spec (« any failure propagates »). |
| R-2 | edge-case + blind | `Instant` par défaut → hors plage `DATETIME` | Erreur de programmation de l'appelant ; l'exception SQL propage (spec). Pas un cas métier. |
| R-3 | edge-case + blind + auditor | `Reasons = null` → NRE | `Nullable` activé + `-warnaserror` : une affectation `null` explicite ne compile pas. |
| R-4 | edge-case + blind | Raisons vides/blanches → « REJETÉ : » vide | Contenu des raisons = responsabilité du format ; aucun producteur n'émet de raison vide. |
| R-5 | edge-case + blind | Fabrique `newContext` non vérifiée null | Paramètre non-nullable sous `Nullable` + `-warnaserror` ; câblage DI unique (Story 5.2). |
| R-6 | blind | Nom de paramètre `"initiatingServer"` codé en dur dans `ResolveUser` | C'est le nom exact du paramètre du constructeur (appel unique) ; patché volontairement à la step-04 (Change Log). |
| R-7 | edge-case | Transaction explicite d'un `DbConnection` partagé | La fabrique crée son propre contexte/connexion ; aucun appelant ne partage une connexion. Hypothétique. |
| R-8 | blind | `FailingContext` : `Assert.Empty` ne peut pas échouer | Un seul `SaveChanges` atomique : « no row » découle de l'échec ; la moitié « propage » est bien prouvée. |
| R-9 | blind | Repli `ParisTime` (IANA → Windows) non testé | Copie verbatim du `ParisTime` P60, temporaire jusqu'à la migration P60 (deferred-work). |
| R-10 | blind | Le projet de test référence `Kape22Importer` (P60) | Test-only, documenté dans le csproj (fixture AR-12 typée sur `AscoLsiDbContext`) ; AC-FR23-1 porte sur les projets de production. |
| R-11 | edge-case + blind | AC-FR23-1 ignore `Directory.Build.props` / `Reference` / `FrameworkReference` | Vérifié : `Directory.Build.props` n'injecte aucune référence ; `Directory.Packages.props` n'épingle que des versions. |
| R-12 | edge-case | `Update`/`Remove` au lieu d'`Include` → NRE dans le test | Aucun csproj du dépôt n'utilise ces formes pour ces éléments ; le test échouerait bruyamment, ce qui est acceptable. |
| R-13 | blind | `FichierName` ignoré par l'implémentation LSI | Membre du contrat figé, destiné à d'autres implémentations ; `L_D_LOG_COMMANDE` n'a pas de colonne pour lui. |
| R-14 | blind + auditor | Spec `status: done` / `review_loop_iteration: 0` vs sprint-status `review` | Cycle normal : `/commit-review` passe la story à `done` dans `sprint-status.yaml`. |
| R-15 | blind | Échec d'encodage → aucune ligne LSI | Voulu (D15), et déjà écrit dans `epic-5-context.md` (« REJETÉ row when the OF is readable ») et AC-FR22-6 (« l'OF s'il est lisible »). |
| R-16 | blind + verification-gap | `Kape22Importer.csproj` modifié dans l'arbre de travail | Vérifié hors du range (`git diff --stat` n'y touche pas) ; la spec le documente comme brouillon 5.1. |
| R-17 | blind | Story 5.1 ne liste pas CC-7 | La Story 5.1 ne porte aucune configuration ni chaîne de connexion (journal factice) ; CC-7 est sur 5.0 et 5.2. |
| R-18 | auditor | Réécriture FR-22 / Story 5.1-5.2 non listée dans les Tasks de la spec 5.0 | Contenu du correct-course du même jour, embarqué dans le commit ; tracé dans `epics.md` (Séquencement) et `deferred-work.md`. |

## 6. Auto-vérifications

- **Lentilles lancées : 4/4**, en sous-agents parallèles, aucune en échec (`failed_layers` vide).
  - blind-hunter : 21 findings
  - edge-case-hunter : 10 findings
  - verification-gap : 2 findings principaux + 1 annexe
  - acceptance-auditor : 1 finding + 5 hors mandat
- **Écart de procédure** : le diff (~1150 lignes) a été transmis aux lentilles par chemin de fichier (patch dans le scratchpad de la session),
  pas en ligne. Le contenu est identique à `git diff baad85d^..HEAD`, et la consigne « context-free » a été maintenue pour le blind-hunter.
- **Diff stats** : 22 fichiers, +874 / −40.
  - Code : `src/FichierJournal` 3, `src/AscoLsiJournal` 6, `tests/AscoLsiJournal.Tests` 5.
  - Planification : PRD, epics, epic-5-context, spec, sprint-status, deferred-work.
  - Racine : `TextToXml.sln`, `README.md`.
- **Tests** : `dotnet test tests/AscoLsiJournal.Tests` = 14 passed, 0 failed, 0 skipped.
- **Vérifications sur le code réel** :
  - `Directory.Build.props` ne porte aucune référence (R-11).
  - `ResetData` fait un `TRUNCATE` de `L_D_LOG_COMMANDE` (D-2).
  - Le fixture est lié par `AscoLsiJournal.Tests` et par `P89Converter.Tests` (D-2).
  - La CI tourne sur `ubuntu-latest` (P-1, P-2).
  - L'`OF` P89 a une taille de 7 (R-1).
  - La clé P60 est `Import:InitiatingServer` (F-2).

## 7. Application des patchs (2026-09-25)

Les 9 patchs sont appliqués dans l'arbre de travail et ne sont pas commités. Les defers F-1 et F-2 ne sont pas encore dans `deferred-work.md` : `/commit-review` les y ajoutera.

| ID | Fichier(s) | Changement |
|---|---|---|
| P-1 | `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs` | Nouveau `Record_InsideACallerTransaction_WritesOutsideIt_AcFr23_4` : la fabrique capture `Transaction.Current` sous un `TransactionScope` et le test attend `null`. Mutation `Suppress` → `Required` : **rouge** (`Assert.Null() Failure`), puis vert après restauration. |
| P-2 | idem | Nouveau `Record_SummerInstant_WritesParisSummerTime_AcFr23_2` : 2026-07-10 08:00Z donne 10:00. `Entry()` reçoit un paramètre `instant` (paramètres triés instant < of < reasons). |
| P-3 | `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalIntegrationTests.cs` | `Instant` fixe 2026-02-10 08:00Z et `Assert.Equal(new DateTime(2026, 2, 10, 9, 0, 0), row.Date)`. |
| P-4 | `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs` | `[InlineData("")]` ajouté à `Record_UnreadableOf_WritesNoRow_AcFr23_3`. |
| P-5 | `_bmad-output/implementation-artifacts/epic-5-context.md` | Stories 5.0 / 5.1 / 5.2 ; `P89Converter` = `TextToXml` + `FichierJournal` ; ordre 5.0 → 5.1 → 5.2 ; clôture à la Story 5.2 ; migration P60 hors épic. |
| P-6 | `_bmad-output/planning-artifacts/PRD.md` (D30) | « Mapping et persistance P89 : plus tard » ; le worker `GpaoConvertP89` est livré par la Story 5.2 (correction 2026-09-25). |
| P-7 | `README.md:3` | « Deux livrables : » devient « Livrables : ». |
| P-8 | `src/FichierJournal/FichierJournalEntry.cs`, `src/AscoLsiJournal/AscoLsiFichierJournal.cs` | `Succeeded` supprimé ; `Record` teste `entry.Reasons.Count == 0`. |
| P-9 | `.github/workflows/ci.yml`, `README.md:93-94`, `.claude/commands/commit-review.md:90`, `_bmad-output/project-profile.md:28` | `-m:1` sur les runs `Category=Integration` au niveau solution (et sur le run « tout » du README) ; commentaire anglais dans `ci.yml`. |

**Vérification :**

- `dotnet build TextToXml.sln -warnaserror` : 0 warning, 0 erreur.
- `dotnet test tests/AscoLsiJournal.Tests` : 17 passed, 0 skipped (14 → 17).
- `dotnet test TextToXml.sln --no-build --filter Category=Integration -m:1` : 0 échec.
  - Kape22Importer.Tests : 1092 passed, 18 skipped.
  - P89Converter.Tests : 1 passed.
  - AscoLsiJournal.Tests : 2 passed.

**Incident pendant la mutation :** le `git checkout` qui restaurait le fichier après la mutation P-1 a aussi annulé P-8 dans `AscoLsiFichierJournal.cs`. P-8 a été réappliqué et revérifié (build et tests verts).
