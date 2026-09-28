# Review report — Story 5.1

Range : ede1abd67d940ba778d2698a168abaf821bd9408^..HEAD (ede1abd, 6d912be)
Spec : C:\Users\Administrateur\Documents\TextToXml\_bmad-output\implementation-artifacts\spec-5-1-p89converter-dossier-p89-brut-xml-normalise-horodate.md
Date : 2026-09-28
Verdict : ACCEPTÉ
Findings : D=2 P=7 F=5 R=19  (Decision, Patch, Defer, Rejetés)
Mise à jour 2026-09-28 : D-1 tranché (option 1) → P-8 ; D-2 tranché (option 2 : « fichiers créés par le traitement, sans valeur intrinsèque ») → P-9. Reste : D=0 P=9 F=5 R=19
Mise à jour 2026-09-28 : P-1..P-9 appliqués dans l'arbre de travail (option « tout appliquer ») ; build `-warnaserror` 0 erreur, P89Converter.Tests 42/42, AscoLsiJournal.Tests Unit 17/17, `gen.ps1 -Check` (P60 et P89) à jour. F-1..F-5 restent à consigner par `/commit-review`, qui passera aussi sprint-status à `done`.

## 1. Verdict

**ACCEPTÉ**, à deux conditions :
- trancher D-1 et D-2 ;
- appliquer P-1..P-7 dans le commit de clôture (`/commit-review`).

Constats qui fondent le verdict :
- **Bloc figé respecté.**
  - Signature `P89FolderConverter(P89Options, IFichierJournal, TimeProvider)`.
  - Une seule lecture d'horloge, qui donne à la fois le suffixe et `Instant`.
  - `FileMode.CreateNew` et `File.Move` sans écrasement.
  - Annulation vérifiée entre Fichiers uniquement.
  - Toute exception levée par `Record` donne un Deferred.
  - L'entrée porte `Commande="P89"`, des `NumeroFichier`/`OF` bruts complétés par des zéros, et `null` quand ils sont illisibles.
- **Frontières respectées.**
  - `git diff ec4043b HEAD -- src/TextToXml src/Kape22Importer P60/tmp` est vide.
  - `P89Converter` référence exactement `FichierJournal` et `TextToXml`.
  - Aucun nouveau package, aucune base de données dans `P89Converter.Tests`, pas de Docker, pas de `MicroServices.sln`.
- **Critères transverses.** Aucune déviation CC-2/CC-3 (commentaires en anglais, sur leur propre ligne, capitale + point final). Aucune déviation CC-4 (propriétés, initialiseurs, membres d'enum). CC-7 : aucun dossier en dur. AD-3 respecté.
- **Traçabilité des ACs.** Chaque AC-FR22-1..7, ainsi que le F-1 (AC-FR23-2/3), a un test nommé qui porte son `[Trait("AC", …)]`.
- **Ask First sur `P89/raw`** : le corps du commit mentionne « per user decision », mais le spec n'en garde aucune trace datée. Voir D-1.

| Sévérité | Nombre |
|---|---|
| high | 0 |
| medium | 3 (D-1, P-1, P-3) |
| low | 11 (D-2, P-2, P-4, P-5, P-6, P-7, F-1..F-5) |

## 2. Findings Decision — à trancher par l'humain

### D-1 — 248 vrais Fichiers P89 committés sans trace de la validation « Ask First » (medium)

- **Localisation :**
  - `P89/raw/LP89_682_617_001..248` (commit `ede1abd`) ;
  - spec § Boundaries, « Ask First: … committing the real Fichiers of `P89/raw` ».
- **Source :** acceptance-auditor + blind-hunter.
- **Constat :** le commit ajoute 248 Fichiers bruts réels. La seule trace d'accord est la ligne « per user decision » du corps de commit. Le Spec Change Log, `deferred-work.md` et `sprint-status.yaml` n'en disent rien.
- **Point connexe :** l'arbre de travail contient déjà `P89/raw/LP89_682_617_249..257` et un dossier `P89/done/`, tous deux non suivis. Versionner `P89/raw` a donc un coût d'entretien continu, et aucune règle `.gitignore` ne tranche la question.
- **Options :**
  1. Confirmer l'accord : ajouter une entrée datée au Spec Change Log (qui, quand, périmètre `P89/raw`), et ajouter `P89/done/` et `P89/error/` au `.gitignore`.
  2. Retirer les fichiers du suivi (`git rm --cached P89/raw`, puis `.gitignore`). L'historique est conservé.
  3. Purger aussi l'historique (réécriture de l'historique).
- **État :** à trancher par l'humain.

### D-2 — 248 XML `P89/xml/<name>.xml` committés, sans horodatage (low)

- **Localisation :** `P89/xml/LP89_682_617_001.xml … 248.xml` (commit `ede1abd`).
- **Source :** acceptance-auditor + blind-hunter.
- **Constat :** ces noms n'ont pas le suffixe `_<yyyyMMddHHmmss>`. Ils ne viennent donc pas de `P89FolderConverter`, et ils reprennent le nommage que le Problem du spec veut justement supprimer. Du contenu généré est versionné, et le bloc figé ne le couvre pas.
- **Options :**
  1. Les garder comme données de référence et le noter dans l'entrée D-1 du Change Log.
  2. Les retirer du dépôt.
  3. Les régénérer avec `P89FolderConverter` (noms horodatés).
- **État :** à trancher par l'humain.

## 3. Findings Patch

### P-1 — Collision sur la cible done/error : entrée journalisée à chaque tick (medium)

- **Localisation :**
  - `src/P89Converter/P89FolderConverter.cs:111-118` (Accept) ;
  - `src/P89Converter/P89FolderConverter.cs:157-163` (Reject).
- **Source :** blind-hunter + edge-case-hunter + verification-gap + acceptance-auditor (hors mandat).
- **Constat :** `Record` s'exécute avant le `File.Move`.
  - Si la cible `error/<name>_<ts>` existe déjà, chaque tick écrit une nouvelle ligne REJETÉ, puis rend Deferred.
  - Il en va de même côté done : une ligne « — OK » est écrite alors que le XML est supprimé et que le Fichier reste dans la source.
  - Aucun test ne place un fichier dans `error/` avant le tick. Un `File.Move(…, overwrite: true)` sur le chemin d'erreur passerait donc inaperçu.
  - Les Design Notes n'acceptent le doublon que pour une panne de move transitoire. Une collision de nom est, elle, déterministe.
- **Action :**
  - Dans `Accept` (avant `CreateNew`) et dans `Reject` (avant `TryRecord`), vérifier par `File.Exists` si la cible done/error existe. Si oui, rendre Deferred avec une raison `Fichier : …`, sans écrire d'entrée. C'est la même approche par pré-contrôle que A-5 (Épic 4).
  - Ajouter le test `RunTick_ErrorTargetAlreadyExists_DefersWithoutEntryAndKeepsTheEarlierFile_AcFr22_4`.
  - Resserrer `RunTick_DoneTargetAlreadyExists_…` pour qu'il vérifie aussi « 0 entrée ».

### P-2 — Un nettoyage `File.Delete` non protégé masque la cause réelle (low)

- **Localisation :** `src/P89Converter/P89FolderConverter.cs:114` et `:125`.
- **Source :** blind-hunter + edge-case-hunter.
- **Constat :** si la suppression du XML échoue (fichier verrouillé), la nouvelle `IOException` remplace deux choses :
  - la raison `Journal : …` à la ligne 114 ;
  - l'exception d'origine dans le `catch`, à la ligne 125.

  Le résultat ne montre plus que l'échec du nettoyage.
- **Action :**
  - Entourer ces deux `File.Delete` d'un `try { … } catch (IOException) { }`, ou ajouter l'échec de suppression comme raison supplémentaire.
  - Dans les deux cas, garder la raison d'origine.

### P-3 — Scénario F-1 (NumeroFichier ou OF vide dans un XML valide) non testé via P89Converter (medium)

- **Localisation :**
  - `src/P89Converter/P89FichierConverter.cs:57-58, 78-79` ;
  - `tests/P89Converter.Tests/`.
- **Source :** verification-gap.
- **Constat :** le spec affirme que F-1 est atteignable en P89 (`minOccurs="0"`). Pourtant, le repli n'est testé que dans `AscoLsiFichierJournalTests`, avec une entrée construite à la main. Aucun test P89 ne fait passer un Fichier converti avec `NumeroFichier` ou `OF` vide.
  - Si on retire la garde `IsNullOrWhiteSpace` de `Raw`, on obtient une `NullReferenceException` qui fait avorter le tick.
  - Si on complète un vide en `"000"`, on obtient une ligne `"000 — OK"`.
  - Aucune de ces deux régressions ne serait détectée.
- **Action :**
  - Dans `TestSupport`, ajouter deux variantes d'une fixture de référence : une avec `NumeroFichier` d'en-tête à blanc, une avec `OF` de message à blanc (même style que `TruncatedFichier`).
  - Vérifier avec `Convert`, puis avec `RunTick` et `RecordingJournal` :
    - statut `Converted` ;
    - entrée avec `NumeroFichier == null` (ou `OF == null`), jamais `"000"` ni `""` ;
    - Fichier déplacé en done.

### P-4 — Trait AC mal rattaché pour le repli FichierName « — OK » (low)

- **Localisation :** `tests/AscoLsiJournal.Tests/AscoLsiFichierJournalTests.cs:100`.
- **Source :** blind-hunter.
- **Constat :** `Record_SuccessEntryWithUnreadableNumeroFichier_WritesTheFichierNameOkRow_AcFr23_2` porte le tag AC-FR23-2. Or `PRD.md:1074-1077` ne décrit le repli F-1 que dans AC-FR23-3.
- **Action :** retaguer le test en `FR23-3` / `_AcFr23_3`. L'autre option est d'étendre AC-FR23-2 dans le PRD, mais retaguer est plus simple.

### P-5 — Vocabulaire périmé dans un commentaire de test (CC-5) (low)

- **Localisation :** `tests/P89Converter.Tests/P89FichierConverterTests.cs:45`.
- **Source :** acceptance-auditor (hors mandat).
- **Constat :** le commentaire dit « D8: the log row carries the raw, zero-padded values… ». Depuis le patch P-5 de l'itération précédente, le converter produit une entrée `IFichierJournal`, pas une ligne de log.
- **Action :** remplacer par « the IFichierJournal entry carries the raw, zero-padded values… ».

### P-6 — En-tête et Usage de `gen.ps1` incomplets pour P89 (low)

- **Localisation :** `scripts/gen.ps1:1-17`.
- **Source :** blind-hunter.
- **Constat :** l'en-tête parle de « the two dependent artifacts », ce qui est faux pour P89. Le bloc Usage omet `-Check -Format P89`, alors que le README et la section Verification du spec s'appuient dessus.
- **Action :** ajouter la ligne `pwsh scripts/gen.ps1 -Check -Format P89` au bloc Usage, et préciser que « two dependent artifacts » ne vaut que pour P60.

### P-7 — Le test de Ligne tronquée ne vérifie pas la raison de l'échec (low)

- **Localisation :** `tests/P89Converter.Tests/P89FichierConverterTests.cs:84-92`.
- **Source :** blind-hunter.
- **Constat :** le test vérifie seulement `Assert.NotEmpty(conversion.Reasons)`. Les Lignes header et footer réelles (18 et 17 caractères, pour des Blocs déclarés à 80) se convertissent sans Error. Le test pourrait donc passer pour une autre raison que la troncature de la Ligne message.
- **Action :** vérifier qu'au moins une raison désigne la Ligne message, par exemple qu'elle contient « ligne 2 ».

### P-8 — Consigner l'accord Ask First sur `P89/raw` (issu de D-1, option 1) (low)

- **Localisation :** spec § Spec Change Log ; `.gitignore`.
- **Action :**
  - Ajouter au Spec Change Log l'entrée datée suivante : « 2026-09-28, Ask First gate (user) : committing the real P89 Fichiers under `P89/raw` approved (reference data); `P89/xml` generated output removed from the repository (review D-2) ».
  - Ajouter `P89/done/`, `P89/error/` et `P89/xml/` au `.gitignore`.

### P-9 — Retirer `P89/xml` du dépôt (issu de D-2, option 2) (low)

- **Localisation :** `P89/xml/LP89_682_617_001.xml … 248.xml`.
- **Justification :** décision de l'utilisateur. Ce sont des fichiers produits par le traitement, sans valeur intrinsèque.
- **Action :**
  - Lancer `git rm -r --cached P89/xml`. Les fichiers locaux sont conservés et deviennent ignorés via P-8.
  - Consigner la suppression dans le même commit de clôture.

## 4. Findings Defer

À consigner dans `deferred-work.md` par `/commit-review`.

### F-1 — Défaut de `PollingInterval` à `TimeSpan.Zero`, alors que le README annonce 30 s (low)

- **Localisation :** `src/P89Converter/P89Options.cs:19`, `README.md:86`.
- **Constat :** la bibliothèque ne lit jamais `PollingInterval` ni `SectionName`, qui relèvent du worker.
- **Justification du report :** c'est au worker `GpaoConvertP89` de fixer ce défaut (Story 5.2). Il peut aussi déplacer ces deux membres dans ses propres options.

### F-2 — Aucune validation des options (low)

- **Localisation :** `src/P89Converter/P89FolderConverter.cs:37-52`.
- **Constat :**
  - aucune garde contre les `null` dans le constructeur ;
  - un chemin vide fait lever une `ArgumentException` par `Directory.CreateDirectory("")` dans `RunTick` ;
  - des dossiers qui se chevauchent (Xml ou Done sous Source) seraient rescannés en boucle.
- **Justification du report :** le README attribue à Story 5.2 le refus de démarrer si la configuration est incomplète. Cette validation appartient au worker, avec la garde de `RunTick` déjà reportée.

### F-3 — Heuristique `xs:date` / `xs:dateTime` dupliquée et divergente (low)

- **Localisation :** `scripts/gen.ps1` (`HasTime`, `-cmatch '[Hhms]'`) et `tests/P89Converter.Tests/P89TemplatesTests.cs:87`.
- **Constat :** le test recopie la règle simplifiée du script. Elle diverge de `TextToXml.NormalizedXmlBuilder.MaskHasTimeComponent` (qui gère `f`, `F`, `t`, `z`, `K` et les littéraux entre guillemets).
- **Justification du report :** c'est sans effet aujourd'hui. Le seul Champ datetime est `DateEnvoi`, au format `ddMMyy`, et les fixtures AC-FR22-1 le valident contre le XSD. À corriger quand un masque avec heure apparaîtra.

### F-4 — Ordre de traitement non chronologique après une rotation 999 → 001 (low)

- **Localisation :** `src/P89Converter/P89FolderConverter.cs:61`.
- **Constat :** les Fichiers sont triés par nom. S'il reste un backlog au moment de la rotation, `…_001` est journalisé avant `…_999`.
- **Justification du report :** le spec n'impose aucun ordre. Toutes les entrées d'un tick partagent le même `Instant`, et aucun consommateur ne dépend de l'ordre. À revoir si un consommateur finit par en dépendre.

### F-5 — Un Fichier UTF-8 avec BOM est rejeté en Encodage (low)

- **Localisation :** `src/P89Converter/P89FichierConverter.cs:33`.
- **Constat :** U+FEFF n'existe pas en Windows-1252, donc un BOM fait rejeter le Fichier.
- **Justification du report :** les Fichiers réels n'ont pas de BOM (vérifié sur `P89/raw/LP89_682_617_001`, qui commence par `P89`). Le rejet est bruyant, en error, et non silencieux. À traiter si le producteur change.

## 5. Findings rejetés (bruit)

| ID | Constat | Justification du rejet |
|---|---|---|
| R-1 | Spec `status: done` alors que sprint-status est à `review` | Cycle normal : bmad-build marque le spec `done`, et le sprint passe à `done` au `/commit-review`. |
| R-2 | Le Code Map annonce « 35 tests », il y en a 37 | Vérifié par `dotnet test` (37). Le Code Map décrit l'intention au moment de la rédaction, sans conséquence. |
| R-3 | Le README décrit le worker `GpaoConvertP89` comme livré | Le README renvoie explicitement à la « Story 5.2 » (ligne 51). |
| R-4 | `MicroServices.sln` ne compile plus avec le nouveau constructeur | Déjà documenté dans les Design Notes et attendu jusqu'à la Story 5.2. |
| R-5 | Un avertissement XSD rejette le Fichier | Le schéma compilé couvre tous les éléments. Un avertissement signale un écart de schéma, donc le rejet est légitime. |
| R-6 | Complétage par des zéros d'un int négatif | `OF` et `NumeroFichier` ne sont jamais négatifs dans le format P89. |
| R-7 | `NumeroFichier` et `FichierName` tous deux vides | `FichierName` vient toujours de `Path.GetFileName`, il n'est jamais vide. |
| R-8 | `message` a `maxOccurs=1` | Le format P89 compte 3 Lignes (header, message, footer), et les 248 Fichiers réels le confirment. |
| R-9 | Header et footer plus courts que les 80 caractères déclarés | Comportement générique de `TextToXml` (Épic 1). Le risque sur le test est couvert par P-7. |
| R-10 | Le test de succès ne relit pas le XML écrit | AC-FR22-1 valide la sortie de `Convert`. L'écriture est une copie UTF-8 de la même chaîne, et la déclaration est `utf-8` (vérifié). |
| R-11 | Seul un jeton déjà annulé est testé | La boucle ne vérifie qu'un `IsCancellationRequested` entre deux Fichiers : il n'y a rien d'autre à piloter. |
| R-12 | La ligne « File-system fault » de la matrice n'a pas de trait AC | Un test existe (`RunTick_SourceUnreadable_DefersWithoutEntry`). La liste des ACs l'exclut par choix du spec. |
| R-13 | Double espace dans la raison quand `FieldId` est null | Cosmétique. |
| R-14 | `TryRecord` perd le type de l'exception externe | Choix issu de la revue de l'itération 0. Le diagnostic détaillé revient au worker (5.2). |
| R-15 | Casse `"Done"` dans `TempFolders` | Tests Windows uniquement, sans effet. |
| R-16 | Le test de structure ne vérifie pas que Kape22Importer est inchangé | Couvert par la commande de Verification du spec. L'auditeur a confirmé que le diff est vide. |
| R-17 | Une panne de move transitoire après `Record` provoque une seconde entrée | Accepté explicitement dans les Design Notes. Le cas de collision déterministe est traité par P-1. |
| R-18 | `deferred-work` parle d'« iteration 2 » alors que le spec dit `review_loop_iteration: 1` | Tenue de registre, sans conséquence. |
| R-19 | Aucune vérification de cohérence header/message/footer (`NumeroFichier` contre l'index du nom, compte `Records`) | Hors périmètre : D30 exclut tout mapping métier à l'Étape 1. |

Deux constats doublonnent des défauts déjà consignés le 2026-09-25 et ne sont pas recomptés :
- l'absence de garde de stabilité à l'entrée ;
- les Fichiers Deferred retentés sans limite.

## 6. Auto-vérifications

- **4 lentilles lancées en sous-agents, aucun échec :**
  - blind-hunter : 23 constats ;
  - edge-case-hunter : 9 ;
  - verification-gap : 2 majeurs et 2 annexes ;
  - acceptance-auditor : 2 constats, 7 hors mandat, 6 points vérifiés conformes.
- **Diff du range :** 525 fichiers, +2744 / −25 (6309 lignes).
  - Les lentilles ont reçu le diff filtré, sans `P89/raw` ni `P89/xml` : 29 fichiers, 2093 lignes. Ce choix a été validé au checkpoint.
  - Les 496 fichiers de données ont été audités à part (`git show --stat ede1abd`, voir D-1 et D-2).
- **Tests exécutés pendant la revue :**
  - `dotnet test tests/P89Converter.Tests` : 37 réussis, 0 ignoré ;
  - `dotnet test tests/AscoLsiJournal.Tests --filter Category=Unit` : 17 réussis.
- **Frontières :** `git diff ec4043b HEAD -- src/TextToXml src/Kape22Importer P60/tmp` est vide (vérifié par l'acceptance-auditor).
