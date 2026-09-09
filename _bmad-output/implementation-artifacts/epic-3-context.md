# Epic 3 Context: `Kape22Importer` — worker, orchestration & exploitation

<!-- Compiled from planning artifacts. Edit freely. Regenerate with compile-epic-context if planning docs change. -->

## Goal

Épic 3 fait tourner le format P60 en production : un worker supervisé par le
`Launcher` du portail scrute le dossier de réception, orchestre chaque Fichier
(lecture → `TextToXml` → archive XML → `Kape22Mapper` → EF → déplacement),
journalise deux fois (`MQTTnetServices.Logs` + `L_D_LOG_COMMANDE`), survit aux
pannes de boucle, et est validé de bout en bout (les 10 fichiers `P60/` insèrent
10 lignes cohérentes ; tout rejet produit une raison lisible par l'exploitant).

**Correction de cap (2026‑09‑09, `sprint-change-proposal-2026-09-09.md`).**
L'intégration Launcher s'aligne sur le modèle réel du portail
(`MicroServices.sln`) : `Kape22Importer` devient une **bibliothèque** et le worker
exécutable est une classe mince `Client : Publisher` enregistrée dans le
`Launcher`. Stories 3.1 / 3.2 livrées et réutilisées telles quelles ; 3.3 livrée,
seam de journalisation revisité ; 3.4 / 3.5 / 3.6 réécrites contre le modèle
`WorkerAdapter`/`Client`.

## Stories

- Story 3.0 : Repositionnement structurel (lib + `Client` Launcher) — *ajoutée à la correction de cap*
- Story 3.1 : Scrutation du dossier de réception & cycle de vie du Fichier — *done*
- Story 3.2 : Orchestration par Fichier — *done*
- Story 3.3 : Double journalisation (`MQTTnetServices.Logs` + `L_D_LOG_COMMANDE`) — *done, seam journal à revisiter*
- Story 3.4 : Boucle worker `Client` & arrêt propre — *réécrite*
- Story 3.5 : Robustesse de la boucle `Client` — *réécrite*
- Story 3.6 : Validation de bout en bout & harnais de couverture (SM‑1/2/3) — *réécrite*

## Requirements & Constraints

- **Cycle de vie du Fichier** : chaque Fichier est déplacé dans `processing/`
  avant lecture ; succès → `archive/<yyyy>/<MM>/<nom>` + `<nom>.xml` à côté ;
  rejet → `error/<nom>` + `<nom>.errors.json`. Traitement du plus ancien au plus
  récent. Inbox vide = tick sans effet ni log au‑dessus d'Information. Reprise :
  un Fichier resté dans `processing/` est repris au redémarrage ; le garde‑fou
  anti‑doublon empêche une double insertion si le commit avait eu lieu.
- **Stabilité d'écriture** : un Fichier dont la taille change entre deux lectures
  est laissé dans l'inbox, retenté, sans erreur loggée.
- **Rétention** : purge de `archive/` et `error/` au‑delà de `Import:RetentionDays`
  (≤ 0 = désactivée).
- **Orchestration** : ordre strict lecture octets → `Converter.Convert` → (si
  succès) capture XML normalisé → `Kape22Mapper.Map` → (si succès) insertion →
  déplacement. Isolation totale par Fichier (erreurs, transaction, scope EF).
- **Double journalisation** : `MQTTnetServices.Logs` écrit **toujours** (même
  rejet structurel `OF` illisible) ; `L_D_LOG_COMMANDE` écrit **seulement si
  l'`OF` du bloc message est lisible** — `Message` = `"<NumeroFichier> — OK"` ou
  `"<NumeroFichier> — REJETÉ : <résumé>"`. `Errors` et `Warnings` de l'`ImportResult`
  chacun trié par `LineNumber` croissant (listes indépendantes). Un Fichier déjà
  importé porte un discriminant explicite « déjà importé » et se loggue en
  `Warning`.
- **Intégration Launcher** : le worker est **enregistré** dans le `Launcher`
  (`WorkerRegistry` + `workers.json`), supervisé via `WorkerAdapter<Client>`
  (`IsRunning = Client.IsConnected`). Il n'implémente ni `WorkerStatus` ni
  endpoint HTTP, n'a **aucune** dépendance ASP.NET Core, et ne touche jamais la
  table `WorkerSettings` (détenue par le Launcher).
- **Arrêt propre** : sur `Stop()` du `WorkerAdapter`, annulation coopérative
  vérifiée **entre Fichiers** → le Fichier en cours finit ou reste dans
  `processing/`, jamais à moitié inséré ; arrêt < 5 s.
- **Robustesse** : exception par Fichier capturée (log `ERROR`, on continue) ;
  source injoignable → `Warning`, retry ; base injoignable → Fichiers laissés
  dans `processing/` (pas `error/`), `Warning`, retry.
- **Reprise (NFR)** : rejouer = redéposer le Fichier corrigé ; aucune action en
  base.
- **Performance** : un Fichier de bout en bout < 200 ms hors latence FTP/SQL ;
  un tick de 500 Fichiers < 30 s.
- **Couverture (SM‑1)** : chaque `AC-FRx-y` / `CTR-x` a au moins un test portant
  le `[Trait]` correspondant ; l'agrégateur `AcTraitCoverageTests` casse le build
  sinon.

## Technical Decisions

- **Modèle worker** : `class Client : Publisher, IPublisher, IService` (base
  `MicroService` de `MicroServices.sln`), ctor `Client(IConfiguration)`,
  `CreateAsync()` (connexion broker MQTT avec retry, puis `Start()`), boucle sur
  le timer de `Publisher` (`Frequency` = intervalle de polling, `Execute` = un
  tick). Modèle de référence : `Laminoir/OrdresFabricationSync/Client.cs`.
- **Frontière solution** : `TextToXml.sln` = `TextToXml` (lib pure) +
  `Kape22Importer` (**bibliothèque** de format P60 : descripteur, XSD, DTO,
  entité + `AscoLsiDbContext`, `Kape22Mapper`, `Kape22Persister`, `InboxScanner`,
  `Kape22FichierProcessor`) + tests. Le `Client` vit dans `MicroServices.sln`
  (`ProjectReference` cross‑dépôt vers la lib + `MicroService.csproj`). La lib ne
  référence que `TextToXml` + `PortalSharedLibrary` (`AC-FR16-1`). Prérequis :
  `MicroServices.sln`/`Launcher` sur `net10.0` (fait, bump minimal).
- **Pas de conteneur DI / hôte** pour le worker. Le `Client` construit ses
  `AscoLsiDbContext` à la main par tick (`new DbContextOptionsBuilder<>()
  .UseSqlServer(cs)`). `Kape22FichierProcessor` garde son seam
  `Func<AscoLsiDbContext> newContext` ; `Kape22Persister` construit par Fichier
  dans l'orchestrateur, jamais en DI. `AddDbContextFactory` / `AddKape22Startup`
  abandonnés.
- **Journalisation** : `Serilog` + `Serilog.Sinks.MSSqlServer` proviennent de
  `AbstractService` / `SharedLogger` (cache un `Logger` par (chaîne, table)),
  jamais câblés par le worker ni un `Program.cs`. `Kape22FichierProcessor` route
  son journal via un pont `Serilog.Extensions.Logging` ou un seam `IImportJournal`
  implémenté par le `Client`.
- **FR‑8** (compatibilité descripteur ↔ table) : exposé en méthode statique
  appelable (`StartupCompatibilityCheck.Verify(...)`), invoquée dans
  `CreateAsync()` avant `Start()` ; échec → `LogError` + la boucle ne démarre pas
  (défaut de déploiement).
- **`IFileSource`** inchangé : `DirectoryFileSource` en prod, impl. mémoire en
  test. Sous‑dossiers `processing/` / `archive/` / `error/` paramétrables sous
  `Import:*`. Dates d'archive en heure de Paris.
- **Tests base de données (AR‑12)** : instance SQL Server locale + `scripts/schema/`
  versionné (`L_D_KAPE22`, `L_D_LOG_COMMANDE`, `MQTTnetServices.dbo.Logs` —
  `WorkerSettings` retirée, Launcher‑owned). `[Trait("Category","Integration")]`,
  ignorés proprement si aucune instance. Deux régimes : `TransactionScope` +
  rollback (défaut) ; commit réel + reset pour l'état inter‑actions / frontières
  de transaction. Pas de Docker.
- **Standards transverses** : CC‑1 (TDD strict, tests avant code, attestation PR),
  CC‑2/CC‑3 (commentaires anglais, une phrase capitalisée finissant par un point,
  sur leur propre ligne au‑dessus du code, pas de trailing, pas de liste
  numérotée), CC‑4 (propriétés de classes/records/entités/config en ordre
  alphabétique insensible à la casse ; initialiseurs suivent l'ordre de
  déclaration ; ne vise pas les tuples nommés locaux ni les déconstructions),
  CC‑5 (vocabulaire du glossaire à l'identique : `Fichier`, `Ligne`, `Bloc`,
  `Champ`, `Descripteur`…), CC‑7 (aucun secret / chaîne de connexion en dur, tout
  vient de `IConfiguration`).

## Cross-Story Dependencies

- Story 3.0 (lib) débloque 3.4/3.5/3.6 et le projet `Client` (différé, repris en
  3.4). 3.0 ne dépend que du bump `Launcher` net10 (fait).
- Story 3.3 (livrée) fournit `Kape22FichierProcessor.Journal` + le discriminant
  skip‑doublon + le tri `AC-FR6-4` ; son seam `ILogger` est revisité en 3.0/3.4.
- Stories 3.1 / 3.2 (livrées) fournissent `InboxScanner`, `IFileSource`,
  `DirectoryFileSource`, `IFichierProcessor`, `Kape22FichierProcessor`,
  `Kape22Mapper` (instance, ctor `TimeProvider`), `Kape22Persister`,
  `DerivedFields`, `CoherenceChecker`, `ParisTime`.
- Garde‑fou anti‑doublon (D22) : livré Story 2.8, réutilisé par 3.1 (reprise
  `processing/`) et 3.5 (recycle).
- Le `Client` dépend de `MicroService.csproj` (`AbstractService`, `Publisher`,
  `SharedLogger`) et du `Launcher` (`WorkerRegistry.Factories` + `workers.json`)
  dans `MicroServices.sln`.
