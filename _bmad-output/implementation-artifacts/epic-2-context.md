# Epic 2 Context: `Kape22Importer` — contrat de format, mapping & persistance

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Livrer la chaîne « Étape 2 » du traitement P60 : une entité EF database-first
fidèle au schéma réel (`L_D_KAPE22`, `L_D_LOG_COMMANDE`), un Descripteur
`P60.xml` enrichi d'un `datatype` par Champ, un XSD statique + DTO généré
validant le XML normalisé avant désérialisation, un mapper qui désérialise et
transforme ce XML en entité insérable (règles dérivées + contrôles de
cohérence non bloquants), et une persistance transactionnelle protégée par un
garde-fou anti-doublon. Un contrôle de compatibilité descripteur ↔ table
s'exécute au démarrage du worker : toute incompatibilité doit être un défaut
de déploiement détecté tôt, jamais un rejet fichier par fichier en
production. À l'issue de l'épic, `Kape22Mapper.Map(xml, fileName)` produit une
entité insérable et la persistance est atomique et sans doublon.

## Stories

- Story 2.1: Entités EF (database-first) & harnais de test SQL Server local
- Story 2.2: Descripteur `P60.xml` enrichi & embarqué en ressource
- Story 2.3: `P60.xsd` statique + génération du DTO `Kape22File` + validation avant désérialisation
- Story 2.4: Désérialisation & mapping DTO `Kape22File` → entité `L_D_KAPE22`
- Story 2.5: Contrôle de compatibilité descripteur ↔ table (au démarrage)
- Story 2.6: Champs dérivés & combinés
- Story 2.7: Contrôles de cohérence (Warnings, non bloquants)
- Story 2.8: Persistance transactionnelle & garde-fou anti-doublon

## Requirements & Constraints

- Le mapping DTO → entité se fait **par nom de propriété, insensible à la
  casse** ; les exceptions (renommages et propriétés source volontairement
  non copiées) sont documentées et vérifiées par un test paramétré ; toute
  propriété du DTO ni mappée ni listée comme ignorée fait **échouer le build
  des tests** (complétude).
- Règles dérivées propres à P60 (jour-de-l'année pour `Date`, horodatage de
  réception, roulette `NumeroFichier`, Champs `DateEnfournementFour1/2`
  ignorés) vivent **dans le microservice**, jamais dans `TextToXml` — vérifié
  par un test d'architecture.
- Contrôles de cohérence (compteur `Footer.Records`, cohérence inter-blocs
  `File`, nom de fichier vs Entête) sont des **avertissements non bloquants** :
  un fichier avec seulement ces écarts est quand même inséré.
- Le fichier n'est rejeté que si une donnée ne peut pas être produite/typée ou
  si une colonne NOT NULL reste vide.
- Insertion `L_D_KAPE22` + `L_D_LOG_COMMANDE` (« OK ») dans une **même
  transaction** ; échec SQL → rollback des deux, pas d'exception qui remonte,
  résultat `{Block:File, Code:PersistenceError}`.
- Avant tout insert, un garde-fou vérifie qu'aucune ligne
  `L_D_LOG_COMMANDE … — OK` n'existe déjà pour ce `NumeroFichier` + `OF`
  (protection contre un crash post-commit relançant un traitement déjà
  réussi) ; si trouvée, aucun nouvel insert, fichier déplacé en `archive/`,
  simple avertissement loggé.
- Aucune chaîne de connexion ni secret en dur : tout vient de la
  configuration (`IConfiguration`), compte SQL `sa` existant, aucun secret
  au dépôt.
- Tests d'intégration base de données (EF, transactions, garde-fou
  anti-doublon) tournent contre une **instance SQL Server locale** (schéma
  minimal versionné) ; ils sont catégorisés `Integration` et s'ignorent
  proprement (pas en échec) si aucune instance n'est joignable. Les tests
  unitaires (mapping hors persistance) n'en dépendent pas.
- Isolation des tests d'intégration : `TransactionScope` + rollback par
  défaut ; commit réel + reset de données pour les tests qui dépendent d'un
  état commité entre deux actions ou qui testent des frontières de
  transaction (garde-fou anti-doublon, rollback sur échec).

## Technical Decisions

- **Database-first, sans migration.** `AscoLsiDbContext` fige les tables
  cibles d'après le schéma réel ; `Id` est identity sur les deux tables.
  Aucune requête `sys.columns` au runtime — les contraintes de longueur
  (`max_length`) viennent de constantes portées par la configuration
  d'entité EF, dérivées une fois du schéma réel, jamais interrogées en
  live au démarrage.
- **Scripts de schéma de test** (`scripts/schema/`) générés depuis la base
  réelle (jamais rédigés de mémoire), idempotents, en-tête daté portant
  serveur/base source, créant uniquement les tables strictement nécessaires
  aux tests. Un test verrouille que le modèle EF correspond exactement à ces
  scripts, pour éviter toute dérive silencieuse.
- **Le Descripteur reste directeur pour le typage** : chaque `<value>` du
  Descripteur reçoit un `datatype` dérivé du type de colonne cible
  (`int` si la colonne est `int`, `string` sinon) ; le format P60 n'a ni
  `datetime` ni `decimal`, et aucun `convert`. Positions non utilisées du
  message restent non déclarées (ignorées). La racine du Descripteur porte
  `expectedMessageCount="1"`. Le Descripteur est embarqué comme ressource,
  lisible à l'exécution sans accès disque.
- **1 XSD statique par format**, écrit à la main, décrivant le XML normalisé,
  dans le même ordre d'enfants que le Descripteur (le XML normalisé est émis
  dans cet ordre). Le DTO C# est **généré** depuis ce XSD ; le tri
  alphabétique des propriétés (règle de style transverse) ne s'applique pas
  au fichier généré — toute propriété ajoutée à la main l'est dans une
  classe partielle, elle-même triée. Le XML normalisé est **validé contre le
  XSD avant désérialisation** (filet de sécurité qui ne doit normalement pas
  se déclencher).
- **Champ typé vide → élément omis dans le XML normalisé** ; ces éléments
  sont typés `minOccurs="0"` dans le XSD, et le DTO reçoit des types
  nullables (`int?`/`decimal?`/`DateTime?`). L'obligation NOT NULL n'est
  jugée qu'à cette étape (mapping), jamais à l'étape de génération du XML.
  Un Champ `string` vide, lui, émet toujours son élément (chaîne vide).
- **Journalisation double** à chaque fichier : un sink de logs applicatif
  (`MQTTnetServices.dbo.Logs`) et une ligne `L_D_LOG_COMMANDE`
  (`Commande="P60"`, `Message` = résumé OK/REJETÉ, `OF` brut, `User` =
  serveur initiateur configuré, `NumLingot=0`, `Trace=1`) — écrite
  uniquement si l'`OF` est lisible.
- **Pas de déduplication fonctionnelle** : un fichier redéposé est réimporté
  (nouvelle ligne). Seul le garde-fou anti-doublon protège contre un
  double-commit après crash, pas contre un redépôt volontaire.
- Chaque story de développement de cet épic doit respecter : TDD strict
  (tests nommés par AC, écrits avant le code de production) ; commentaires
  en anglais, une phrase par ligne au-dessus du code décrit, jamais en fin de
  ligne ; propriétés de classes/entités triées alphabétiquement (sauf le DTO
  généré depuis le XSD) ; vocabulaire du glossaire métier réutilisé à
  l'identique dans le code et les noms de test ; aucun secret en dur.

## Cross-Story Dependencies

- Story 2.1 fournit le socle dont dépendent toutes les autres : le
  `DbContext`, la liste des colonnes `int` (consommée par 2.2), les
  constantes de longueur par colonne (consommées par 2.5), et le harnais
  d'intégration SQL Server réutilisé par 2.8.
- Story 2.2 (Descripteur enrichi) dépend du typage de colonnes exposé par
  2.1, et alimente la génération du XSD en Story 2.3.
- Story 2.3 (XSD + DTO généré + validation) est un pré-requis du mapping de
  la Story 2.4 : le DTO généré est ce que `Kape22Mapper` désérialise.
- Story 2.4 (mapping DTO → entité) est un pré-requis des Stories 2.5 à 2.8 :
  le contrôle de démarrage (2.5), les règles dérivées (2.6), les Warnings de
  cohérence (2.7) et la persistance (2.8) s'appuient tous sur l'entité
  produite par le mapper.
- Une dépendance amont sur l'Épic 1 existe : la génération du XML normalisé
  omettant les éléments de Champs typés vides (comportement révisé en
  rétrospective de l'Épic 1) doit être livrée côté `TextToXml` avant que la
  Story 2.3 puisse valider ce comportement contre le XSD.
- `TextToXml` (Épic 1) reste strictement générique : les règles métier P60
  (dérivés, cohérence, persistance) de cet épic vivent entièrement dans
  `Kape22Importer`, jamais dans la bibliothèque partagée.
