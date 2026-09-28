# TextToXml

Chaîne d'ingestion des fichiers SAP → LSI. Livrables :

| Projet | Rôle |
|---|---|
| `src/TextToXml` | Bibliothèque .NET **pure et générique** : fichier plat largeur fixe → XML normalisé, piloté par un Descripteur XML. Zéro dépendance NuGet runtime. |
| `src/Kape22Importer` | **Bibliothèque** de format P60 : descripteur, XSD, DTO, entités EF, mapping (`Kape22Mapper`), contrôles de cohérence, scrutation du dossier de réception (`InboxScanner`), orchestration par Fichier (`Kape22FichierProcessor` : archive XML → EF → `AscoLSI`, avec double journalisation) et persistance transactionnelle (`Kape22Persister`). Depuis l'Épic 6 (FR-24), le journal `L_D_LOG_COMMANDE` passe par `IFichierJournal`, après la transaction métier et pour chaque issue ; le worker `GpaoImportP60` injecte `AscoLsiFichierJournal` (clé `Import:InitiatingServer` inchangée), et la garde anti-doublon D22 lit `L_D_KAPE22`. Destinée à être consommée par le worker exécutable (différé — voir ci-dessous). |
| `src/FichierJournal` | Contrat du journal de Fichier (D31, FR-23) : `IFichierJournal.Record(FichierJournalEntry)`. Aucune dépendance — un format journalise le résultat de ses imports sans connaître l'application cible. |
| `src/AscoLsiJournal` | Implémentation LSI de `IFichierJournal` : une ligne `L_D_LOG_COMMANDE` par Fichier (D8, D15), écrite hors de toute transaction métier. |
| `src/P89Converter` | **Bibliothèque** de format P89, Étape 1 seule (Épic 5, FR-22) : descripteur + XSD embarqués, transcodage UTF-8 → Windows-1252, XML normalisé horodaté, journal via `IFichierJournal` (référence `TextToXml` + `FichierJournal` seulement). Consommée par le worker `GpaoConvertP89` de `MicroServices.sln` — voir « P89 » ci-dessous. |

Voir `_bmad-output/planning-artifacts/PRD.md` et `epics.md` pour le détail fonctionnel.

> **Correction de cap (2026‑09‑09, `sprint-change-proposal-2026-09-09.md`).**
> `src/Kape22Importer` est une **bibliothèque** (Story 3.0). Le worker exécutable
> est une classe `Client : Publisher` mince à enregistrer dans le `Launcher` de
> `MicroServices.sln` (objectif différé — voir `deferred-work.md` ; prérequis :
> `Launcher` de `MicroServices.sln` aligné sur `net10.0`).

## Prérequis

- SDK **.NET 10.0** (`net10.0`). La version plancher est épinglée par `global.json`
  (`rollForward: latestMinor`).
- Le dépôt **`PortalFosMarcegaglia`** doit être cloné en tant que dépôt frère de
  celui-ci. `Kape22Importer` référence `PortalSharedLibrary` par chemin relatif
  (PRD D20, risque R-2) :

  ```
  <racine commune>
  ├── Documents/TextToXml/                 (ce dépôt)
  └── RiderProjects/PortalFosMarcegaglia/
      └── PortalSharedLibrary/PortalSharedLibrary.csproj
  ```

  Chemin exact utilisé par `src/Kape22Importer/Kape22Importer.csproj` :
  `..\..\..\..\RiderProjects\PortalFosMarcegaglia\PortalSharedLibrary\PortalSharedLibrary.csproj`.
  Adapter cette ligne si le dépôt `PortalFosMarcegaglia` se trouve ailleurs.

  La référence est **conditionnelle** (`Condition="Exists(...)"`) et n'est **pas**
  listée dans `TextToXml.sln` : un clone isolé de `TextToXml` compile et teste
  toute la solution sans le dépôt voisin (le CI en dépend). Quand
  `PortalFosMarcegaglia` est présent, `Kape22Importer` le référence
  automatiquement à la compilation ; ouvrir son `.csproj` séparément dans l'IDE
  si l'on veut naviguer dans les sources de `PortalSharedLibrary`.

- Une **instance SQL Server** joignable (édition Developer, gratuite) pour la
  catégorie de tests `Integration` (AR-12). Renseigner les chaînes de connexion
  dans `tests/Kape22Importer.Tests/appsettings.Test.json` (gitignoré ; modèle :
  `appsettings.Test.json.example`) ou la variable d'environnement
  `KAPE22_TEST_ConnectionStrings__AscoLSI`. **Jamais la base de production.** Le
  harnais crée uniquement les tables de `scripts/schema/`. Sans instance
  configurée, ces tests sont **ignorés** (pas en échec) ; les tests `Unit` n'en
  ont pas besoin.

## P89 (Épic 5, Étape 1 seule)

`P89FolderConverter.RunTick` (bibliothèque `P89Converter`, hébergée par le
worker `GpaoConvertP89` de `MicroServices.sln` — Story 5.2) traite le dossier
source à chaque tick :

1. chaque Fichier `LP89_*` du dossier source est transcodé **strictement** de
   UTF-8 vers Windows-1252 (D29 — un octet invalide ou un caractère hors
   Windows-1252 met le Fichier en échec, jamais de caractère de remplacement) ;
2. converti par `TextToXml` avec `Templates/P89.xml`, puis validé contre
   `Templates/P89.xsd` (tous deux embarqués dans `P89Converter.dll`) ;
3. chaque Fichier traité, succès ou échec, donne une entrée
   `IFichierJournal.Record` (`Commande="P89"`, `NumeroFichier`/`OF` bruts,
   `null` si illisibles, raisons en cas d'échec). Le worker injecte
   `AscoLsiJournal`, qui décide de la ligne `L_D_LOG_COMMANDE` (D8, D15 :
   `"<NumeroFichier> — OK"` / `"<NumeroFichier> — REJETÉ : <raisons>"`, aucune
   ligne sans `OF` lisible, `FichierName` à la place d'un `NumeroFichier`
   illisible) ;
4. succès : XML écrit sous `<XmlPath>/<nom>_<yyyyMMddHHmmss>.xml`, entrée sans
   raison, puis Fichier déplacé en `<DonePath>/<nom>_<yyyyMMddHHmmss>` ;
5. échec : aucun XML, entrée avec ses raisons, Fichier déplacé en
   `<ErrorPath>/<nom>_<yyyyMMddHHmmss>` ;
6. `Record` en échec (toute exception) : le XML déjà écrit est supprimé, le Fichier reste
   dans le dossier source et sera retraité au tick suivant.

Le suffixe horodaté évite qu'une rotation d'index 999 → 001 n'écrase une
conversion antérieure ; aucun fichier n'est jamais écrasé.

Configuration (`GpaoConvertP89.json`, copiée à côté du `Launcher`) : la section
`P89` — `SourcePath`, `XmlPath`, `DonePath`, `ErrorPath` (obligatoires, chemins
absolus et distincts deux à deux, sinon le worker refuse de démarrer ;
l'imbrication reste permise), `PollingInterval` (défaut 30 s) — et, pour le
journal, `ConnectionStrings:AscoLSI` (obligatoire) et
`AscoLsiJournal:InitiatingServer` (`L_D_LOG_COMMANDE.User`, nom de machine si
vide, trop long ⇒ refus de démarrer). Le worker valide ces clés à la
construction et nomme la ou les clés fautives.

Après une modification de `Templates/P89.xml` : `pwsh scripts/gen.ps1 -Format P89`
régénère `Templates/P89.xsd` (`-Check -Format P89` pour vérifier sans écrire).

## Build & tests

```sh
dotnet build TextToXml.sln
dotnet test  TextToXml.sln --filter Category=Unit          # aucune base requise
dotnet test  TextToXml.sln --filter Category=Integration -m:1   # instance SQL Server de test requise ; -m:1 = un projet à la fois (base de test partagée)
dotnet test  TextToXml.sln -m:1                             # tout
```

Les versions de packages sont centralisées dans `Directory.Packages.props`
(central package management) ; les réglages de framework communs dans
`Directory.Build.props`.

À partir de la Story 1.2, chaque test porte un nom référençant le critère
d'acceptation qu'il couvre (`AC-FRx-y` / `CTR-x`) et le trait
`[Trait("AC", "...")]`. Les tests de la Story 1.1 sont structurels (CC-1 sans
objet) et sont classés `[Trait("Category", "Unit")]` / `"Integration"`.
`AcTraitCoverageTests` échoue le build si un test nommé d'après un `AC` / `CTR` /
`NFR` n'a pas le `[Trait]` correspondant.

## Contribution

- **CI** (`.github/workflows/ci.yml`) : sur chaque push / PR, `dotnet build
  -warnaserror` (avec `EnforceCodeStyleInBuild`, donc les règles `.editorconfig`
  sont bloquantes) puis `dotnet test --filter Category=Unit`.
- **TDD strict (CC-1)** : les tests xUnit dérivés des `AC-FRx-y` / `CTR-x` sont
  écrits **avant** le code de production, et vus rouges avant d'être verts.
- **Message de commit** — un commit par story, `chore(story-x.y): <résumé>`. Le
  merge écrasant l'historique (squash), le **corps du commit atteste le TDD** :
  pour chaque `AC` de la story, le nom du test qui le couvre et la mention que le
  test a été écrit (et vu rouge) avant le code. Les écarts de standard transverse
  reportés vont dans `_bmad-output/implementation-artifacts/deferred-work.md` sous
  un titre daté.

## Fixtures

`tests/TextToXml.Tests/fixtures/` :

- `valid/` — les 10 fichiers de référence `P60_847_682_001..010` (copie binaire
  des échantillons de `P60/`).
- `generic/` — descripteur synthétique non-P60 et ses entrées (peuplé en
  Story 1.8, AR-11).

Les fixtures fautives (`two_lines.txt`, `segment_mismatch.txt`, …) sont ajoutées
par la story qui en a besoin (Annexe A.4 du PRD).

`.gitattributes` marque `P60/**` et `tests/TextToXml.Tests/fixtures/**` en
`-text` : ces fichiers `Windows-1252` (octets hauts, `CR LF`) ne doivent jamais
être normalisés. Idem pour `P89/**` et `tests/P89Converter.Tests/fixtures/**`
(trois Fichiers P89 réels, UTF-8 ; les variantes fautives sont construites dans
les tests).
