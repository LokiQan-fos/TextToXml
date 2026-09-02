---
title: TextToXml
created: 2026-09-02
updated: 2026-09-02
status: validé — prêt pour epics & stories
---

# PRD : Chaîne d'ingestion des fichiers SAP → LSI

Dépôt `TextToXml`. Deux livrables : la bibliothèque **`TextToXml`** (fichier plat
→ XML normalisé, générique) et le microservice **`Kape22Importer`** (format P60 →
table `L_D_KAPE22`).

## 0. Objet du document

Ce PRD s'adresse au PM, au(x) développeur(s) .NET, et à l'exploitation LSI. Il
décrit **deux livrables v1** :

1. **`TextToXml`** — bibliothèque .NET **pure** qui convertit un fichier texte
   largeur fixe « au kilomètre » en **document XML normalisé**, piloté par un
   **descripteur de layout** XML. Générique — réutilisable pour de futurs formats.
2. **`Kape22Importer`** — premier **microservice** d'ingestion : il récupère dans
   un dossier de réception les fichiers **P60 (KAPE22)** produits par SAP, les
   convertit avec `TextToXml`, valide contre `P60.xsd`, archive le XML, mappe le
   DTO sur l'entité EF **`L_D_KAPE22`** et l'insère dans **`AscoLSI`**
   (`AFV004-LSI`), avec double journalisation.

Le vocabulaire est fixé au §3. Les exigences fonctionnelles (FR) sont numérotées
globalement `FR-1..FR-N`. Chaque FR porte une liste **« Conséquences
(testables) »** dont **chaque puce est un cas de test xUnit** (`AC-FRx-y`). Les
décisions arrêtées sont au **§0bis** (D1–D26, avec leur source). **Aucune
question ouverte, aucune hypothèse.** Le PRD est prêt pour le découpage en
epics & stories (§9).

**Décision d'architecture (imposée) :** **un microservice / projet .NET par
format**. `TextToXml` est le seul code partagé. `Kape22Importer` est le gabarit
des suivants (§4.4).

**Frontière générique / spécifique :**

| | Générique (`TextToXml`, réutilisable) | Spécifique au format (dans le microservice) |
|---|---|---|
| **Étape 1** — Fichier → **XML normalisé** | ✅ tout : découpage en Blocs, contrôle longueur, contrôle `Segment`, extraction & typage des Champs, sérialisation | rien — **le descripteur `P60.xml`** |
| **Étape 2** — XML → **Entité** → **base** | ❌ | mapping Champ→propriété, contrôle vs schéma SQL, règles dérivées, EF, transaction |
| **Orchestration** — dossier, archive, log, Launcher | ⚠️ pattern commun (copié de `FactoryScope`), pas encore une lib | config + branchement de l'entité |

**Généricité de l'Étape 1** — `TextToXml` est piloté par le seul descripteur, donc
réutilisable pour un futur format. Les 3 autres templates (`P62`, `SerrageBil`,
`SortieStock`) sont **hors périmètre** (§0bis D24) — fournis à titre d'exemple —
mais ont fixé le contrat générique :

| Axe | Décision `TextToXml` |
|---|---|
| Structure | `<header>` / `<footer>` **optionnels**, `<message>` obligatoire, **1..N** messages (`expectedMessageCount`) |
| Typage | **le descripteur est directeur** : `datatype` par `<value>` (`string` défaut / `int` ; la lib gère aussi `decimal` / `datetime` + `convert`). La table SQL cible **doit accepter** ces types (contrôle au démarrage, FR‑8) |
| Récupération | XML normalisé **désérialisable** (`XmlSerializer`) en **DTO** généré du **XSD statique** du format (§0bis D10) |
| Filler | **pas** de « magie Filler » : tout `<value>` est émis ; le mapping (Étape 2) ignore ce qu'il veut |
| Encodage | `Windows-1252`, figé (§0bis D2) |
| Découpage | `format="Fixed"` implémenté ; `Semicolon` (`SerrageBil`) → `LayoutInvalid` (§0bis D24) |

**Seul invariant en dur** : lire le descripteur (attributs listés §4.1), produire
un XML normalisé désérialisable, entrée `Windows-1252`, découpe par offset.
Tout le reste — noms/positions/tailles/**types** des Champs, présence
header/footer, nb de messages, marqueurs `Segment` — est **dans le descripteur**.

**Changements depuis la version précédente de ce PRD** *(rendus caducs par la
lecture de `P60.xml` et des échantillons `P60/`)* :
- Le « modèle XML » n'est **pas** un template de sortie à placeholders : c'est un
  **descripteur de layout** (`<header>` / `<message>` / `<footer>` de
  `<value Id Position Size>`).
- Le layout n'est plus « codé en dur en C# » : il est **embarqué comme ressource
  XML** dans le microservice du format (fichier de type `P60.xml`).
- La sortie n'est plus « XML uniquement » : c'est **XML archivé PUIS insertion EF**.
- **Profil KAPE22 (P60)** : un fichier contient **exactement 3 Lignes** (Entête,
  Détail, Pied) → **1 ligne** insérée dans `L_D_KAPE22`. C'est une contrainte du
  *format P60*, pas de la lib : `TextToXml` accepte 0..1 entête, 1..N messages,
  0..1 pied ; le descripteur P60 fixe `expectedMessageCount="1"`. Blocs identifiés
  par **position** ; `Segment` (`000`/`EOF`/`999`) = **contrôle de cohérence**.
- Les templates d'exemple ont montré que le **descripteur est directeur pour le
  typage** et que header/footer sont **optionnels** — le générique le couvre
  (tableau ci‑dessus, §4.1). Mais **v1 ne traite que P60** (§0bis D24).
- **Typage & récupération** : le template décrit les types ; l'Étape 1 émet des
  valeurs typées/normalisées ; l'Étape 2 **désérialise** le XML en DTO C#
  (`XmlSerializer`) puis alimente l'entité EF. La table `L_D_KAPE22` doit être
  compatible avec les types du template (vérifié au démarrage du worker).

## 0bis. Décisions arrêtées (avec source)

*Ces points **ne sont pas** des hypothèses : ils sont confirmés (utilisateur) ou
dérivés des données réelles. Ce qui reste ouvert est au §8, et n'est asserté nulle
part ailleurs.*

| # | Décision | Source |
|---|---|---|
| D1 | Fichiers pris dans **`D:\Site-FTP\Reception\GPAO`** (serveur `AFS017`), chemin en **configuration** (`Import:InboxPath`). Accès système de fichiers / partage, pas de protocole FTP. Cible d'évolution : MQTT — sans toucher `TextToXml`. | utilisateur |
| D2 | Encodage d'entrée **`Windows-1252`**, figé. | utilisateur |
| D3 | Fichier P60 = **3 Lignes** (entête / message / pied) → **1 ligne** `L_D_KAPE22`. Blocs par **position** ; `Segment` @9/3 (`000`/`EOF`/`999`) = contrôle. | utilisateur |
| D4 | Champ `Date` de l'entête = **numéro du jour dans l'année**, interprété dans l'**année courante**, **heure de Paris** (`245` = 2 septembre). | utilisateur |
| D5 | Message positions **526→636 : données inutilisées, ignorées**. Le **template fait foi** pour tout le reste. | utilisateur |
| D6 | **Typage : le template est directeur.** Les `datatype` du descripteur `P60.xml` sont **dérivés du type de la colonne** `L_D_KAPE22` : colonne `int` → `datatype="int"`, sinon `"string"`. **P60 : 0 `datetime`** (Champs `DateEnfournementFourN` non utilisés, D14), 0 `decimal`, ~30 `int`, le reste `string`. Table figée Annexe A. | données `sys.columns` (Annexe C) |
| D7 | **Pas de déduplication** de fichiers. Un fichier redéposé est **réimporté** (nouvelle ligne, `Id` identity). La sûreté de reprise vient du dossier **`processing/`** (FR‑12), pas d'une clé. | utilisateur |
| D8 | **Journalisation double**, à chaque fichier : (a) `MQTTnetServices.dbo.Logs` — Serilog sink MSSqlServer, format `[Kape22Importer][<Event>] : <texte>`, comme les workers existants ; (b) `AscoLSI.dbo.L_D_LOG_COMMANDE` — 1 ligne : `Commande="P60"`, `Message` = `"<NumeroFichier> — OK"` ou `"<NumeroFichier> — REJETÉ : <résumé des erreurs de mapping>"`, `OF` = `OF` **brut** du bloc message, `User` = **`Import:InitiatingServer`** (serveur initiateur du traitement), `Date` = horodatage local, `NumLingot=0`, `Trace=1`. Écrite **seulement si l'`OF` est lisible** (D15). | utilisateur + schémas réels (Annexe C) |
| D9 | Enregistrement Launcher via `MQTTnetServices.dbo.WorkerSettings` (`WorkerName`, `IsActive`), comme les autres workers. | schéma réel |
| D10 | **1 XSD statique, écrit à la main, par format** (`P60.xsd`), versionné, décrivant le **XML normalisé**. Le **DTO C# (`Kape22File`) est généré** de ce XSD (`xsd.exe /classes`). Le XML normalisé est **validé contre le XSD avant désérialisation**. **Pas** de méta‑schéma des descripteurs (`commande.xsd`) : chaque format a son propre XSD, ça suffit. | utilisateur |
| D11 | Le **XML normalisé est conservé** (`archive/<yyyy>/<MM>/<nom>.xml`, ou à côté du fichier en `error/`) pour consultation des données champ par champ. | utilisateur |
| D12 | **Objectif central** : tout fichier refusé produit une **raison lisible par l'exploitant** — logs ServicesMicroScope (`MQTTnetServices.Logs`) + `Message` de `L_D_LOG_COMMANDE` + XML conservé. Pas d'écran dédié. | utilisateur |
| D13 | **Rétention paramétrable** (`Import:RetentionDays`) : purge automatique des fichiers de `archive/` et `error/` au‑delà de ce délai. | utilisateur |
| D14 | Les Champs `DateEnfournementFour1/2` (`_Date`/`_Heure`) **ne sont pas utilisés** → ignorés ; colonnes `datetime` correspondantes laissées `NULL`. | utilisateur |
| D15 | Si l'`OF` du bloc message n'est pas lisible (erreur structurelle) → **aucun** traitement possible, rejet loggé **uniquement** dans `MQTTnetServices.Logs`. La **ventilation** ultérieure de `L_D_KAPE22` vers d'autres tables `AscoLSI` = **futur microservice**, hors périmètre. | utilisateur |
| D16 | **Contrôles de cohérence non bloquants** : `Segment` faux, `Footer.Records` ≠ 3, `File` incohérent entre blocs, nom de fichier ≠ entête → **avertissements** (loggés « pour contrôle »), **pas** de rejet. Le fichier est rejeté **uniquement** si une donnée ne peut pas être produite/typée ou si une colonne `NOT NULL` est vide. | utilisateur (Q13) |
| D17 | Champs `int` : **toujours non signés** ; un signe `-` ou un caractère non numérique → `InvalidInteger` (rejet). Valeur cadrée à droite, zéros de tête retirés ; vide/espaces → `NULL` si colonne nullable, `RequiredFieldMissing` si `NOT NULL`. | utilisateur (Q14a) + défaut |
| D18 | `Footer.Records` compte **3** = entête + message + pied. | utilisateur (Q17) |
| D19 | Octet non décodable en `Windows-1252` → **rejet** (`UndecodableInput`). | utilisateur (Q16) |
| D20 | Solution **`TextToXml.sln` autonome** dans ce dépôt (`TextToXml` lib + `Kape22Importer` worker + tests), référence `PortalSharedLibrary` pour l'identité/log. | utilisateur (Q18a) |
| D21 | Chaîne(s) de connexion : compte **`sa`** existant (comme les autres workers), lu depuis la configuration, jamais en dur. | utilisateur (Q12) |
| D22 | Avant l'`INSERT` `L_D_KAPE22`, le worker vérifie qu'aucune ligne `L_D_LOG_COMMANDE` `… — OK` n'existe déjà pour ce `NumeroFichier` + `OF` (garde‑fou anti‑doublon sur crash post‑commit). Si trouvée → fichier déplacé en `archive/`, log `Warning`, pas de ré‑insertion. | utilisateur (Q21) |
| D23 | Chevauchements de tranches entre Champs (ex. `Segment` et `NumeroFichier` du message, `Position=9`) : **acceptés** par `TextToXml` (Champs = tranches indépendantes, aucune erreur de layout). | données (`P60.xml` corrigé) |
| D24 | Formats `P62` / `SerrageBil` / `SortieStock` : **hors périmètre**, fournis à titre d'exemple. v1 = **P60 uniquement**. `format="Semicolon"` → `LayoutInvalid`. | utilisateur (Q19) |
| D25 | `L_D_LOG_COMMANDE` : `NumLingot = 0`, `Trace = 1` pour toutes les lignes P60. | utilisateur |
| D26 | PRD validé, **déplacé dans `_bmad-output/planning-artifacts/PRD.md`** (convention BMAD, §9). | utilisateur |

## 1. Vision

SAP (GPAO) dépose des fichiers plats KAPE22 dans un dossier de réception
(`D:\Site-FTP\Reception\GPAO` sur `AFS017`). Aujourd'hui, **un fichier en erreur
est refusé sans que personne ne sache pourquoi** — c'est le principal problème à
corriger. `Kape22Importer` automatise la chaîne : **dossier → conversion →
archive XML → insertion `AscoLSI`**, en tournant comme *worker* supervisé par le
**Launcher** existant, en journalisant dans `MQTTnetServices.Logs` **et**
`AscoLSI.L_D_LOG_COMMANDE`, et en produisant pour **chaque rejet** une raison
lisible par l'exploitant.

La conversion est **tout ou rien par fichier** : au moindre problème (structure,
type, valeur, contrainte base), **rien n'est inséré**, le fichier est déplacé en
**erreur**, et une entrée de log **exploitable par un non‑technicien** liste
**toutes** les raisons (bloc, ligne, champ, colonne, code, message, valeur
fautive).

`TextToXml` reste **pure** : pas de disque, pas de réseau, pas d'état, pas d'EF.
Elle prend des octets + un descripteur et rend un objet résultat. Tout le reste
(dossier de réception, EF, fichiers, logs, Launcher) vit dans le microservice.

## 2. Utilisateur cible

### 2.1 Jobs To Be Done

- **Développeur du microservice** : « Je veux ingérer un format SAP largeur fixe
  sans réécrire un parseur, avec un rapport d'erreurs prêt à logger. »
- **Exploitant LSI** : « Quand un fichier KAPE22 est rejeté, je veux savoir en
  français *quelle ligne / quel champ* corriger, et pouvoir le rejouer. »
- **Mainteneur** : « Quand SAP fait évoluer le format, je modifie le descripteur
  XML + la table de mapping à un seul endroit, et `dotnet test` me dit ce que
  j'ai cassé. »
- **Intégrateur d'un nouveau format** : « Je copie `Kape22Importer`, je remplace
  le descripteur et le mapping, et j'ai un nouveau microservice. »

### 2.2 Non-utilisateurs (v1)

- Les flux **sortants** LSI → SAP (génération de fichiers) — hors périmètre.
- Les **accusés de réception** renvoyés à SAP — hors périmètre.
- Le découpage **délimité** (`format="Semicolon"`, ex. `SerrageBil`) : prévu dans
  le schéma du descripteur, **non implémenté** en v1 (§6.2).
- Les formats non tabulaires (EDI, JSON, XML entrant).
- Toute conversion **partielle / tolérante** (voir §5).

### 2.3 Parcours utilisateur

- **UJ-1. Ingestion nominale.**
  Le worker scrute `D:\Site-FTP\Reception\GPAO`, trouve `P60_847_682_011`, le
  déplace dans `processing/`. `TextToXml` produit le XML normalisé (validé contre
  `P60.xsd`) → archivé dans `archive/2026/09/`. Le mappeur désérialise le
  DTO, construit l'entité `L_D_KAPE22`, l'insère dans `AscoLSI` avec la ligne
  `L_D_LOG_COMMANDE` (`OK`) sous la même transaction. Le fichier passe en
  `archive/`. `MQTTnetServices.Logs` reçoit une ligne `Information`. Réalise SM-1.

- **UJ-2. Rejet d'un fichier erroné.**
  Même flux, mais la ligne détail a `DiametreProduit = "11A0"` (non numérique,
  `datatype="int"`) et `Coulee` vide (colonne NOT NULL). Étape 1 renvoie
  `InvalidInteger` ; Étape 2 renvoie `RequiredFieldMissing` sur `Coulee`.
  Aucune insertion `L_D_KAPE22`. Fichier → `error/` + `P60_847_682_011.errors.json`.
  `MQTTnetServices.Logs` : ligne `Error` avec les 2 raisons complètes.
  `L_D_LOG_COMMANDE` : ligne `REJETÉ`. L'exploitant lit la raison (page
  ServicesMicroScope ou `.errors.json`), corrige avec SAP, redépose le fichier.

- **UJ-3. Évolution de format.**
  SAP ajoute un champ de 8 caractères en fin de ligne détail. Le mainteneur
  ajoute un `<value Id datatype>` dans `P60.xml`, la propriété
  correspondante dans le DTO et l'entité, lance `dotnet test`. Les cas `AC-FR4-*`,
  `AC-FR7-*`, `AC-FR8-*` confirment la non‑régression ; un nouveau test couvre le
  champ
  ajouté.

- **UJ-4. Ajout du format suivant (ex. `KAPE30`).**
  L'intégrateur duplique le projet `Kape22Importer` → `Kape30Importer`, remplace
  `*.layout.xml`, la classe d'entité cible et la table de mapping, ajuste
  `appsettings` (dossier FTP, table). Le worker est enregistré auprès du
  Launcher. `TextToXml` n'est pas modifiée.

## 3. Glossaire

Termes à utiliser **à l'identique** dans les FR, UJ, tests et code.

- **Fichier** — Fichier texte d'entrée produit par SAP, encodé **Windows‑1252**,
  composé de **Lignes** séparées par `LF` (tolérer `CR LF`). Nom de forme
  `<File>_<Emet>_<Recepteur>_<NumeroFichier>` (ex. `P60_847_682_001`). Ces 4
  segments doivent correspondre aux 4 Champs homonymes du Bloc Entête (FR‑10).
- **Champs d'identification (Entête)** — `File` (code type de flux, `P60`),
  `NumeroFichier` (numéro de roulette / séquence d'émission), `Emet` (n° du
  programme émetteur, `847`), `Recepteur` (n° du programme récepteur, `682`).
  Portés à la fois par le Bloc Entête et par le **nom du Fichier**.
- **Ligne** — Une ligne physique du Fichier, hors séparateur de fin. Numérotée à
  partir de **1**.
- **Bloc** — Section d'un Fichier : **Entête** (`<header>`, 0..1), **Détail**
  (`<message>`, 1..N Lignes), **Pied** (`<footer>`, 0..1). Ordre imposé
  entête → détail(s) → pied. Un Bloc = une Ligne, **sauf** le Détail qui peut en
  porter plusieurs. Le nombre de Lignes attendu est déterminé par les sections
  présentes dans le Descripteur + `expectedMessageCount`.
- **Descripteur** — Le fichier `P60.xml` (`Templates/`), embarqué comme ressource
  dans le microservice. Racine `<commande type format>` ; sections `<header>`
  (opt.), `<message>` (req.), `<footer>` (opt.), chacune contenant des
  `<value Id Position Size datatype [convert] Description>`. Attributs optionnels
  de la racine : `expectedMessageCount` (P60 = `1`), `segmentField` /
  `headerMarker` / `messageMarker` / `footerMarker` (contrôle `Segment`) —
  absents ⇒ contrôle désactivé. **Pas de méta‑schéma** : `TextToXml` lit ce
  format directement (attributs compris = ceux listés ici) ; seule évolution v1 =
  **ajouter les `datatype`** à `P60.xml` (§0bis D6).
- **Champ** — Élément `<value>` : `Id` (nom), `Position` (offset 0‑based), `Size`
  (longueur), `datatype` (`string` par défaut | `int` — P60 n'utilise que ces
  deux ; la lib gère aussi `decimal` / `datetime` + `convert` pour d'autres
  formats). En `format="Fixed"` : `Position` = offset
  0‑based, `Size` = longueur. Deux Champs peuvent se **chevaucher** — des
  tranches indépendantes (ex. `Segment` et `NumeroFichier` du message, tous deux
  `Position=9`) ; `TextToXml` l'accepte sans erreur (§0bis D23).
- **DTO de format** — Classe(s) C# du microservice, une propriété par Champ
  (`[XmlElement(Id)]`), typée d'après le `datatype` du template. Le **XML
  normalisé** s'y désérialise directement (`XmlSerializer`). Ex. `Kape22File`
  { `Header`, `Message`, `Footer` }.
- **Contrôle `Segment`** — Vérification **optionnelle** (attributs racine
  `segmentField` / `*Marker`) : le Champ désigné doit valoir le marqueur attendu
  sur son Bloc. Écart ⇒ `SegmentMismatch` en **`Warning`** (non bloquant, §0bis
  D16). KAPE22 : `Segment` @9/3 = `000` / `EOF` / `999`.
- **Champ ignoré** — Aucune détection automatique par nom : `TextToXml` émet
  **tout** `<value>` dans le XML. C'est l'Étape 2 (le Mapping) qui décide quels
  Champs sont sans cible (`Filler`, `Reserve*`, `libre`, `FinDeLigne`…).
- **Valeur brute** — Contenu exact du Champ (tranche `Fixed` ou cellule
  `Semicolon`), padding compris.
- **Valeur normalisée** — Valeur brute nettoyée & mise en **forme canonique**
  selon `datatype` : `string` → `TrimEnd` (espaces internes conservés) ;
  `int` → `Trim` + zéros de tête retirés, **non signé** (`-` ⇒ `InvalidInteger`,
  §0bis D17) ; (`decimal` / `datetime` : autres formats). Valeur non conforme ⇒
  `InvalidInteger` / `InvalidDecimal` / `InvalidDate` (Étape 1, `Error`). La
  forme canonique est écrite dans le **XML normalisé** pour que `XmlSerializer`
  la relise sans convertisseur custom.
- **XML normalisé** — Document produit par `TextToXml` : racine `<file>`, un
  élément par Bloc présent (`<header>`/`<message>`/`<footer>`), un enfant par
  Champ nommé d'après son `Id` (`<Id>valeur canonique</Id>`), échappement XML,
  **UTF‑8 sans BOM**. Archivé tel quel **et** désérialisé en **DTO de format**.
- **Entité cible** — Classe EF Core correspondant à une table de `AscoLSI`. Pour
  KAPE22 : `L_D_KAPE22` (schéma `dbo`, PK `Id` identity). Voir Annexe C.
- **Mapping** — Table (Champ `Id` → propriété de l'Entité cible) définie dans le
  microservice. Par défaut : correspondance **par nom** (`Id` == nom de
  propriété, insensible à la casse). Exceptions listées Annexe B.
- **ConversionError** — `Block` (`Header`|`Detail`|`Footer`|`File`),
  `LineNumber` (int, `0` = Fichier), `FieldId` (string?), `Column` (string?),
  `Code` (**ErrorCode**), `Message` (français), `RawValue` (string?).
- **ConversionResult** — Résultat de `TextToXml` : `Success` (bool = `Errors`
  vide), `Xml` (string?, `null` si `!Success`),
  `Errors` (`IReadOnlyList<ConversionError>` — **bloquantes**),
  `Warnings` (`IReadOnlyList<ConversionError>` — contrôles non bloquants, §0bis
  D16). Un `Warning` n'empêche **pas** la production du XML ni l'insertion.
- **ImportResult** — Résultat de bout en bout pour un Fichier : `Success` (bool),
  `InsertedId` (int?), `Errors`, `Warnings`, `XmlArchivePath` (string?).
- **ErrorCode** — Énumération :
  - *`Errors` (bloquant)* — Structure (Étape 1) : `EmptyFile`, `UndecodableInput`,
    `LayoutInvalid`, `WrongBlockCount`, `LineTooShort` ; Typage (Étape 1) :
    `InvalidInteger`, `InvalidDecimal`, `InvalidDate` ; Étape 2 :
    `RequiredFieldMissing`, `PersistenceError`.
  - *`Warnings` (contrôle, non bloquant — §0bis D16)* : `SegmentMismatch`,
    `InterBlockMismatch`, `FileNameMismatch`.
  - *(Compatibilité template ↔ colonne — type, longueur — vérifiée **au démarrage**
    du worker, pas via `ErrorCode` : FR‑8.)*

## 4. Fonctionnalités

### 4.1 Bibliothèque générique `TextToXml` — Étape 1 : Fichier → XML normalisé

**Description :** composant **réutilisable** — piloté par le seul **descripteur**
(fichier `P60.xml`), jamais recompilé pour un nouveau format. API publique figée
v1 :

```csharp
namespace TextToXml;

public static class Converter
{
    // input      : octets bruts du Fichier (toujours Windows-1252)
    // descriptor : contenu du Descripteur P60.xml — SEUL paramètre de format
    public static ConversionResult Convert(ReadOnlySpan<byte> input, string descriptor);
}
```

**Seule règle en dur :** le squelette `[entête] + détail(s) + [pied]`, dans cet
ordre, et l'encodage d'entrée `Windows-1252`. Présence de l'entête/du pied,
nombre de messages (`expectedMessageCount`), noms/positions/tailles/**types** des
Champs, marqueurs `Segment` : **tout est dans le descripteur**.

`Convert` : (1) charge & valide le Descripteur, (2) décode en `Windows-1252`
(décodeur strict — octet invalide ⇒ `UndecodableInput`), (3) découpe en Lignes,
(4) affecte les Blocs selon les
sections déclarées (1re Ligne = Entête si `<header>` présent ; dernière = Pied si
`<footer>` présent ; le reste = Détail) et vérifie `expectedMessageCount` si
présent,
(5) contrôle la longueur de chaque Ligne + la valeur `Segment`, (6) extrait &
**type** les Champs (selon `datatype`/`convert`), (7) **si et seulement si
`Errors` est vide**, produit le XML normalisé. Réalise UJ‑1, UJ‑2. **Aucun arrêt
anticipé** (sauf Descripteur invalide). **Pure** : ne lève jamais d'exception
pour une entrée mal formée (`ArgumentNullException` sur `descriptor == null`
admise).

**Portée MVP :** `format="Fixed"` implémenté ; `format="Semicolon"` → erreur
`LayoutInvalid` « non supporté en v1 » (§0bis D24).

**Exigences fonctionnelles :**

---

#### FR-1 : Chargement & validation du Descripteur

`TextToXml` lit le Descripteur `P60.xml` directement (racine `<commande type
format [expectedMessageCount] [segmentField] [*Marker]>`, sections `<header>`
opt. / `<message>` req. / `<footer>` opt., `<value Id Position Size datatype
[convert]>`). Pas de méta‑schéma (§0bis D10).

**Consequences (testables) :**
- `AC-FR1-1` : Descripteur non bien formé (XML cassé) → `Success=false`, 1
  `Error` `{Block:File, Line:0, Code:LayoutInvalid}`, pas d'exception.
- `AC-FR1-2` : section `<message>` absente → `LayoutInvalid`.
- `AC-FR1-3` : deux `<value>` de même `Id` dans le même Bloc → `LayoutInvalid`.
- `AC-FR1-4` : `<value>` avec `Position` ou `Size` absent, négatif ou non
  entier → `LayoutInvalid` citant l'`Id`.
- `AC-FR1-5` : `datatype` non reconnu (≠ `string|int|decimal|datetime`) →
  `LayoutInvalid`.
- `AC-FR1-6` : Descripteur valide sans `<header>` ni `<footer>` → accepté ;
  toutes les Lignes sont des Détails.
- `AC-FR1-7` : deux Champs aux tranches qui se chevauchent (ex. `Segment` et
  `NumeroFichier` du message, `Position=9`) → **accepté sans erreur** (§0bis D23).
- `AC-FR1-8` : `descriptor == null` → `ArgumentNullException` (seul cas
  d'exception autorisé).
- `AC-FR1-9` **(généricité)** : un **descripteur synthétique** — `Id`, positions,
  tailles, marqueurs, présence header/footer **différents** de P60 — produit un
  XML cohérent **sans modification de `TextToXml`** (fixtures `fixtures/generic/`).
- `AC-FR1-10` : descripteur **sans** `segmentField` / `*Marker` → conversion
  réussie, aucun `SegmentMismatch`.
- `AC-FR1-11` : `segmentField` désignant un `Id` absent d'un Bloc → `LayoutInvalid`.
- `AC-FR1-12` : `format="Semicolon"` → `LayoutInvalid` « non supporté en v1 ».
- `AC-FR1-13` : `P60.xsd` (statique, §0bis D10) est cohérent avec le descripteur —
  test de conformité : chaque `<value Id>` non ignoré a un `<xs:element name="Id">`
  du bon type (`xs:int` / `xs:string`), présence header/footer alignée.

---

#### FR-2 : Décodage Windows‑1252 & découpage en Lignes

**Consequences (testables) :**
- `AC-FR2-1` : fichier de 0 octet → `{Block:File, Line:0, Code:EmptyFile}`.
- `AC-FR2-2` : fichier composé uniquement d'espaces / sauts de ligne → `EmptyFile`.
- `AC-FR2-3` : octet `0xE9` dans un Champ texte → Valeur normalisée contient `"é"`
  (pas `"?"`, pas d'exception). *(la lib enregistre elle‑même
  `CodePagesEncodingProvider`.)*
- `AC-FR2-4` : octet non décodable en `Windows-1252` (décodeur strict) →
  `{Block:File, Line:0, Code:UndecodableInput}` (§0bis D19), jamais d'exception.
- `AC-FR2-5` : fichier terminé sans `LF` final → la dernière Ligne est quand même
  prise en compte.
- `AC-FR2-6` : fins de ligne mixtes `LF` / `CR LF` → Lignes correctement
  détectées, le `CR` résiduel retiré avant analyse.
- `AC-FR2-7` : le `LF` final n'ajoute pas de Ligne vide.

---

#### FR-3 : Affectation des Blocs + contrôle du nombre de Lignes + `Segment`

**Consequences (testables) :**
- `AC-FR3-1` : descripteur avec `<header>` + `<footer>`, `expectedMessageCount="1"`
  (profil KAPE22), fichier ≠ 3 Lignes non vides → `{Block:File, Line:0,
  Code:WrongBlockCount}` citant attendu vs trouvé ; aucune analyse de Champ.
- `AC-FR3-2` : même descripteur, 3 Lignes → ligne 1 = `Header`, 2 = `Detail`,
  3 = `Footer`.
- `AC-FR3-3` : descripteur **sans `<header>` ni `<footer>`** (`SerrageBil`),
  `expectedMessageCount` absent, 5 Lignes → 5 Blocs `Detail`, aucune erreur.
- `AC-FR3-4` : descripteur avec `<header>` seul (pas de `<footer>`), 4 Lignes →
  ligne 1 = `Header`, lignes 2‑4 = `Detail`.
- `AC-FR3-5` : `expectedMessageCount="1"` mais 2 Lignes de Détail → `WrongBlockCount`.
- `AC-FR3-6` : `Segment` d'un Bloc ≠ son marqueur (ex. Détail lu `"000"`, attendu
  `"EOF"`) → **`Warning`** `{Block:Detail, Line:2, FieldId:"Segment",
  Code:SegmentMismatch, RawValue:"000"}` ; `Success` **inchangé**, le fichier est
  traité (§0bis D16). Chaque écart = un `Warning` distinct.
- `AC-FR3-7` : lignes vides en **fin** de fichier (ou `LF` final) → ignorées
  avant le décompte des 3 blocs. Une ligne vide **au milieu** → compte comme une
  Ligne (→ `WrongBlockCount`).
- `AC-FR3-8` : `SegmentMismatch` (Warning) coexiste avec tout le reste ;
  `WrongBlockCount` (Error) court‑circuite l'analyse des Champs.

---

#### FR-4 : Contrôle de longueur des Lignes (`format="Fixed"`)

Règle : une Ligne doit couvrir la **`Position` de départ** de chacun de ses
Champs (aucun Champ entièrement absent). Un dernier Champ **tronqué** est toléré
(lu partiellement / vide) — l'obligation réelle est contrôlée en Étape 2 contre
les colonnes `NOT NULL`.

**Consequences (testables) :**
- `AC-FR4-1` : Ligne couvrant la `Position` de tous ses Champs → aucune
  `LineTooShort`, même si le dernier Champ (`Filler`, `Reserve…`) est tronqué ou
  absent en fin.
- `AC-FR4-2` : Ligne trop courte pour atteindre la `Position` d'un Champ (ce
  Champ manque entièrement) → `{Block, Line, Code:LineTooShort}` citant la
  `Position` manquante vs longueur réelle.
- `AC-FR4-3` : Ligne Entête réelle de 19 caractères, dernier Champ `Filler`
  @18 → **valide** (Position 18 < 19).
- `AC-FR4-4` : Ligne Pied de 12 caractères, Champ `Records` @12 absent →
  `LineTooShort` ; 17 caractères → valide.
- `AC-FR4-5` : Ligne Détail plus **longue** que le dernier Champ déclaré
  (échantillons : 637 > 526) → **pas d'erreur** ; le surplus est **ignoré**
  (§0bis D5 — données inutilisées).
- `AC-FR4-6` : une Ligne trop courte → **une seule** `LineTooShort` (pas une par
  Champ).

---

#### FR-5 : Extraction, typage & sérialisation du XML normalisé

**Structure de sortie (convention, identique pour tout format) :** racine
`<file>`, un enfant par Bloc présent (`<header>` 0..1, `<message>` 1..N,
`<footer>` 0..1), et sous chacun un élément **nommé d'après l'`Id` du Champ**
contenant sa **valeur canonique** (§3). **Tous** les `<value>` sont émis. Le
document est **désérialisable** par `XmlSerializer` sans convertisseur custom.

**Consequences (testables) :**
- `AC-FR5-1` : sur le fichier de référence (Annexe A), le XML =
  `<file><header>…</header><message>…</message><footer>…</footer></file>`, chaque
  section avec un enfant par Champ, nom = `Id`.
- `AC-FR5-2` : descripteur sans `<header>`/`<footer>`, N lignes → `<file>` avec
  N `<message>` et aucun `<header>`/`<footer>`.
- `AC-FR5-3` : Champ `string` `"APERAM ALLOYS"` + padding → `<Client>APERAM
  ALLOYS</Client>` (`TrimEnd`, espaces internes conservés).
- `AC-FR5-4` : `datatype="int"` `"0005900"` → `<DiametreProduit>5900</…>` ;
  `"0000000"` → `<…>0</…>` ; `""` → `<…></…>` (vide).
- `AC-FR5-5` : `datatype="int"` `"11A0"` ou `"-12"` → `Error` `{Block, Line,
  FieldId, Code:InvalidInteger, RawValue}` (§0bis D17 — non signé).
- `AC-FR5-6` : Champ `string` vide/espaces → élément **vide** (l'obligation est
  jugée en Étape 2 contre les colonnes NOT NULL).
- `AC-FR5-7` : Champ sans `datatype` → `string` (`TrimEnd`).
- `AC-FR5-8` : `&`, `<`, `>` échappés ; rechargeable via `XDocument.Parse`.
- `AC-FR5-9` : deux Champs qui se chevauchent (`Segment` / `NumeroFichier` @9) →
  les deux éléments émis avec leur valeur (mêmes 3 caractères).
- `AC-FR5-10` : sortie sans BOM ; déclaration `<?xml version="1.0"
  encoding="utf-8"?>`.
- `AC-FR5-11` : conversion **déterministe** — deux appels → sortie octet pour
  octet identique.
- `AC-FR5-12` : le XML normalisé d'un fichier valide se **désérialise** en un DTO
  (`record` avec `[XmlElement]`) sans perte : round‑trip valeur→XML→DTO conserve
  int/decimal/DateTime.
- `AC-FR5-12` : le XML normalisé d'un fichier valide se **désérialise** en
  `Kape22File` (généré du XSD) sans perte : round‑trip valeur→XML→DTO conserve
  `int` et `string`.
- `AC-FR5-13` **(généricité)** : avec un descripteur `fixtures/generic/`, noms
  d'éléments = `Id` de CE descripteur, aucune balise P60 en dur.
- `AC-FR5-14` : le XML normalisé **valide `P60.xsd`** (`XmlReader` + schéma).

---

#### FR-6 : Contrat `ConversionResult` (Étape 1)

**Consequences (testables) :**
- `AC-FR6-1` : `Success == true` ⇒ `Errors.Count == 0` **et** `Xml != null` **et**
  `Xml` bien formé. Des `Warnings` peuvent être présents.
- `AC-FR6-2` : `Success == false` ⇒ `Errors.Count >= 1` **et** `Xml == null`.
- `AC-FR6-3` : un `SegmentMismatch` **seul** (aucune `Error`) → `Success == true`,
  `Xml != null`, `Warnings.Count == 1`.
- `AC-FR6-4` : `Errors` **et** `Warnings` triés par `LineNumber` croissant (`0`
  en tête).
- `AC-FR6-5` : `Message` non nul, en français, sans stack trace ni nom de type .NET.
- `AC-FR6-6` : `Convert` ne lève jamais d'exception pour 20 entrées corrompues
  générées (fuzz : octets aléatoires, tailles 0..2000).
- `AC-FR6-7` : `ConversionError` sérialisable en JSON par `System.Text.Json` sans
  configuration.

---

### 4.2 Désérialisation, mapping & persistance — Étape 2 (`Kape22Importer`)

**Description :** le typage a déjà été fait en Étape 1 (template directeur).
L'Étape 2 : (a) **désérialise** le XML normalisé en **DTO de format**
(`Kape22File`), (b) mappe le DTO vers l'**Entité cible** `L_D_KAPE22` (Annexe B —
surtout du 1:1), (c) applique les règles dérivées & inter‑blocs & nom de fichier,
(d) insère. **Tout ou rien** : la moindre erreur ⇒ 0 insertion. Un **contrôle de
compatibilité template ↔ table** tourne **au démarrage** du worker (pas par
fichier).

API de référence :

```csharp
namespace Kape22Importer;

public sealed class Kape22Mapper
{
    // Désérialise + mappe + valide. N'écrit rien. Collecte toutes les erreurs.
    public MapResult<L_D_KAPE22> Map(string normalizedXml, string sourceFileName);
}
```

**Exigences fonctionnelles :**

---

#### FR-7 : Désérialisation & mapping DTO → Entité

**Consequences (testables) :**
- `AC-FR7-1` : le XML normalisé est **validé contre `P60.xsd` avant
  désérialisation** ; un XML non conforme → `{Block:File, Code:PersistenceError}`
  citant l'erreur de schéma *(filet — ne doit pas arriver si Étape 1 a réussi)*.
- `AC-FR7-2` : XML valide → `Kape22File` désérialisé (`XmlSerializer`), puis
  entité `L_D_KAPE22` dont chaque propriété homonyme reçoit la valeur typée du DTO.
- `AC-FR7-3` : propriété du DTO **non mappée** vers l'entité **et** absente de la
  liste « ignorés » (Annexe B) → échec au **build des tests** (complétude).
- `AC-FR7-4` : `OFOriginInterne` (DTO) → `OForiginInterne` (entité) — exception
  de casse, Annexe B.
- `AC-FR7-5` : `Filler`, `Reserve*`, `libre`, `Element`, `KAP`, `Segment`, `Date`
  (Détail) → liste **ignorés** : présents dans le DTO, non copiés vers l'entité,
  aucune erreur.
- `AC-FR7-6` : test paramétré — chaque entrée d'Annexe B : propriété source
  existe dans `Kape22File`, propriété cible existe dans `L_D_KAPE22`.

---

#### FR-8 : Contrôle de compatibilité template ↔ table (au démarrage)

Le template étant directeur, la table `L_D_KAPE22` **doit accepter** ce qu'il
décrit. Ce contrôle échoue **au démarrage du worker** (défaut de configuration),
jamais par fichier.

**Consequences (testables)** *(test sur le `DbContext` + le descripteur
embarqués)* :
- `AC-FR8-1` : pour chaque Champ mappé, `datatype` compatible avec le type CLR de
  la propriété d'entité (`int`→`int/int?`, `decimal`→`decimal/decimal?`,
  `datetime`→`DateTime/DateTime?`, `string`→`string`) — sinon exception de
  démarrage listant les couples fautifs.
- `AC-FR8-2` : pour chaque Champ `string` mappé, `Size` du template ≤ `max_length`
  de la colonne (en caractères) — sinon exception de démarrage `{Champ, Size,
  colonne, max_length}`.
- `AC-FR8-3` : toute colonne **NOT NULL** (Annexe C) a une source (Champ mappé ou
  règle dérivée FR‑9) — sinon exception de démarrage.
- `AC-FR8-4` : template & table compatibles (cas nominal KAPE22) → worker démarre.
- `AC-FR8-5` : une valeur `null`/vide en Étape 1 pour une colonne **NOT NULL** →
  `{Block, Line, FieldId, Column, Code:RequiredFieldMissing}` (contrôle **par
  fichier**, lui).
- `AC-FR8-6` : plusieurs colonnes NOT NULL vides → une `RequiredFieldMissing` par
  colonne ; `Errors` trié par ordre des Champs du template.

---

#### FR-9 : Champs dérivés & combinés

**Consequences (testables) :**
- `AC-FR9-1` : le Champ `Date` de l'Entête = **numéro du jour dans l'année**
  (§0bis D4), interprété **année courante, heure de Paris** : `"245"` en 2026 →
  `2026-09-02` partout où une date « fichier » est nécessaire ; `"000"` ou hors
  `1..366` → `InvalidDate` sur `Date`.
- `AC-FR9-2` : `NumeroFichier` de l'entité = valeur du Bloc **Entête** (roulette),
  pas du Détail ; égal au 4ᵉ segment du nom (FR‑10). *(confirmé §0bis D3)*
- `AC-FR9-3` : `DateReception` (NOT NULL, hors template) = horodatage de
  traitement en **heure de Paris**, via `TimeProvider` injectable.
- `AC-FR9-4` : `Indice` (int NOT NULL) = Champ Détail `Indice` ; vide →
  `RequiredFieldMissing`.
- `AC-FR9-5` : les Champs `DateEnfournementFour1/2_Date` / `_Heure` sont
  **ignorés** (§0bis D14) ; les colonnes `DateEnfournementFour1/2` restent `NULL`.
- `AC-FR9-6` : règles dérivées définies **dans le microservice** (pas dans
  `TextToXml` ni le template) — test d'architecture : `TextToXml` n'a aucune
  notion de `DateReception` / jour‑de‑l'année.

---

#### FR-10 : Contrôles de cohérence (Warnings, non bloquants)

`Kape22Mapper.Map(xml, sourceFileName)` reçoit le nom du Fichier. Tous les écarts
ci‑dessous sont des **`Warnings`** (§0bis D16) : loggés « pour contrôle »,
`Success` inchangé, le fichier est **quand même** intégré si les données sont
valides.

**Consequences (testables) :**
- `AC-FR10-1` : `Footer.Records` ≠ `3` (= entête+message+pied, §0bis D18) →
  `Warning` `{Block:Footer, FieldId:"Records", Code:InterBlockMismatch}`.
- `AC-FR10-2` : `Footer.Records` == `3` → aucun `Warning`.
- `AC-FR10-3` : `File` (Position 0, Size 3) différent entre Entête, Détail et
  Pied → `Warning` `{Code:InterBlockMismatch, FieldId:"File"}`.
- `AC-FR10-4` : nom `P60_847_682_001` décomposé en `File`/`Emet`/`Recepteur`/
  `NumeroFichier` ; un segment ≠ Champ homonyme de l'Entête (zéros de tête
  ignorés pour `NumeroFichier`) → `Warning` `{Block:File, FieldId:"<champ>",
  Code:FileNameMismatch, RawValue:"<segment du nom>"}`.
- `AC-FR10-5` : nom hors motif `A_B_C_D` (3 `_`) → `Warning`
  `{Block:File, Code:FileNameMismatch}` citant le nom ; l'extension éventuelle
  (`.txt`) est ignorée dans la comparaison.
- `AC-FR10-6` : nom et Entête concordants → aucun `Warning`.
- `AC-FR10-7` : un fichier avec **uniquement** des `Warnings` de cohérence + des
  données valides → **inséré** dans `L_D_KAPE22`, `L_D_LOG_COMMANDE` statut `OK`,
  les `Warnings` figurent dans le log `MQTTnetServices.Logs`.
- *(Pas de comparaison `NumeroFichier` entête↔détail : dans `P60.xml` corrigé le
  Champ `NumeroFichier` du message occupe la même tranche que `Segment` — §0bis
  D23 — la comparaison n'aurait pas de sens.)*

---

#### FR-11 : Persistance transactionnelle & garde‑fou anti‑doublon

Pas de déduplication fonctionnelle (§0bis D7). **Garde‑fou** (§0bis D22) : avant
l'`INSERT`, le worker vérifie qu'aucune ligne `L_D_LOG_COMMANDE` finissant par
` — OK` n'existe déjà pour ce `NumeroFichier` + `OF`.

**Consequences (testables)** *(intégration EF — LocalDB / conteneur SQL, schéma
miroir `L_D_KAPE22` + `L_D_LOG_COMMANDE`)* :
- `AC-FR11-1` : `MapResult.Success == true` → **1** ligne `L_D_KAPE22`,
  `ImportResult.InsertedId` = l'`Id` identity généré.
- `AC-FR11-2` : `MapResult.Success == false` → **0** ligne `L_D_KAPE22`,
  `InsertedId == null`.
- `AC-FR11-3` : **succès** → insert `L_D_KAPE22` + insert `L_D_LOG_COMMANDE`
  (` — OK`) dans **une même transaction** ; échec de l'un ⇒ rollback des deux.
- `AC-FR11-4` : **rejet** avec `OF` lisible → 0 insert `L_D_KAPE22`, **1**
  `L_D_LOG_COMMANDE` (` — REJETÉ : <résumé>`) en transaction dédiée.
- `AC-FR11-5` : échec SQL → `{Block:File, Code:PersistenceError}`, **pas**
  d'exception qui remonte ; rollback vérifié.
- `AC-FR11-6` : garde‑fou — le retraitement d'un fichier dont un
  `L_D_LOG_COMMANDE … — OK` existe déjà (même `NumeroFichier` + `OF`) →
  **0 insert**, fichier déplacé en `archive/`, log `Warning`
  « déjà importé, ignoré ».
- `AC-FR11-7` : retraitement d'un fichier **jamais** importé avec succès (pas de
  ligne `OK`) → import normal.
- `AC-FR11-8` : chaînes de connexion (`AscoLSI`, `MQTTnetServices`) lues de la
  configuration, jamais en dur ; compte `sa` existant (§0bis D21).

---

### 4.3 Microservice `Kape22Importer`

**Description :** worker .NET (`net10.0`) hébergé et supervisé par le **Launcher**
existant, sur le pattern `BackgroundService` + `PeriodicTimer` déjà en place dans
`FactoryScope` (`FileImportBackgroundService`). Il orchestre uniquement : dossier
de réception → `TextToXml` → archive XML → `Kape22Mapper` → EF → déplacement
fichier → double log.

**Exigences fonctionnelles :**

---

#### FR-12 : Scrutation du dossier de réception & cycle de vie du Fichier

Dossier de réception = `Import:InboxPath` (défaut `D:\Site-FTP\Reception\GPAO`,
§0bis D1), via `IFileSource` (impl. `DirectoryFileSource` en prod ; impl. mémoire
en test). Sous‑dossiers de travail : `processing/`, `archive/`, `error/`
(paramétrables, sous `Import:*`).

**Consequences (testables) :**
- `AC-FR12-1` : à chaque tick, tous les Fichiers de l'inbox sont traités du plus
  ancien au plus récent (ordre déterministe).
- `AC-FR12-2` : le Fichier est d'abord **déplacé dans `processing/`** avant tout
  traitement ; c'est cette copie qui est lue.
- `AC-FR12-3` : succès → Fichier déplacé dans `archive/<yyyy>/<MM>/<nom>` **et**
  XML normalisé écrit à côté `<nom>.xml` (§0bis D11).
- `AC-FR12-4` : rejet → Fichier déplacé dans `error/<nom>` + `<nom>.errors.json`
  (tableau `Errors`) écrit à côté.
- `AC-FR12-5` : Fichier encore en cours d'écriture (taille instable entre deux
  lectures) → laissé dans l'inbox, retenté au tick suivant, aucune erreur loggée.
- `AC-FR12-6` : worker tué pendant le traitement → au redémarrage, un Fichier
  resté dans `processing/` est **repris** ; le garde‑fou anti‑doublon (§0bis D22,
  FR‑11‑6) empêche une 2ᵉ insertion si le commit avait eu lieu.
- `AC-FR12-7` : inbox vide → tick sans effet, sans erreur.
- `AC-FR12-8` : tous les chemins + l'intervalle de polling + `Import:InitiatingServer`
  + `Import:RetentionDays` viennent de la configuration ; aucune valeur en dur.
- `AC-FR12-9` : à chaque tick (ou 1×/jour), les fichiers de `archive/` et
  `error/` dont l'âge > `Import:RetentionDays` sont **supprimés** (§0bis D13) ;
  `RetentionDays ≤ 0` → purge désactivée.

---

#### FR-13 : Orchestration par Fichier

**Consequences (testables) :**
- `AC-FR13-1` : ordre strict : lecture octets → `Converter.Convert` → (si succès)
  archive XML → `Kape22Mapper.Map` → (si succès) insertion → déplacement archive.
- `AC-FR13-2` : `Converter` échoue → pas d'archive XML, pas de mapping, fichier en
  `error/`, `ImportResult.Errors` = erreurs Étape 1.
- `AC-FR13-3` : `Converter` réussit, `Mapper` échoue → le fichier **et** son XML
  normalisé (`<nom>.xml`, utile au diagnostic) vont dans `error/` avec
  `<nom>.errors.json` ; `Errors` = erreurs Étape 2.
- `AC-FR13-4` : un Fichier n'impacte jamais le traitement d'un autre Fichier du
  même tick (isolation : erreurs, transaction, scope EF par fichier).
- `AC-FR13-5` : `ImportResult` d'un succès : `Success=true`, `InsertedId` non nul,
  `Errors` vide (des `Warnings` possibles), `XmlArchivePath` non nul.

---

#### FR-14 : Double journalisation & intégration Launcher

Deux cibles (§0bis D8), à **chaque** fichier :
1. **`MQTTnetServices.dbo.Logs`** — Serilog, sink `MSSqlServer`, message préfixé
   `[Kape22Importer][<Event>] : …` comme les workers existants. Écrit **toujours**
   (y compris rejet structurel, `OF` illisible).
2. **`AscoLSI.dbo.L_D_LOG_COMMANDE`** — 1 ligne, **seulement si l'`OF` du bloc
   message est lisible** (§0bis D15) : `Commande="P60"`, `Message` = `"<NumeroFichier>
   — OK"` ou `"<NumeroFichier> — REJETÉ : <résumé des erreurs de mapping>"`,
   `OF=<OF brut>`, `User=Import:InitiatingServer`, `Date=<horodatage local>`,
   `NumLingot=0`, `Trace=1`.

**Consequences (testables) :**
- `AC-FR14-1` : succès → `Logs` `Information` (nom fichier, `NumeroFichier`, `OF`,
  `InsertedId`, nb Lignes, durée ms) **et** `L_D_LOG_COMMANDE` `Message` finissant
  par ` — OK`.
- `AC-FR14-2` : rejet avec `OF` lisible → `Logs` `Error` listant **toutes** les
  `Errors` **et** `L_D_LOG_COMMANDE` `Message` = `"<NumeroFichier> — REJETÉ : "`
  + résumé (nb erreurs + libellés).
- `AC-FR14-3` : rejet **structurel**, `OF` non lisible → **seul** `Logs` est
  écrit (`Error`) ; **aucune** ligne `L_D_LOG_COMMANDE`.
- `AC-FR14-8` : un fichier importé **avec** des `Warnings` de cohérence → `Logs`
  `Warning` listant les `Warnings` (`SegmentMismatch`, `InterBlockMismatch`,
  `FileNameMismatch`) en plus de la ligne `Information` de succès.
- `AC-FR14-4` : `L_D_LOG_COMMANDE.User` = valeur de `Import:InitiatingServer`
  (config) ; `OF` = valeur brute trimée du bloc message ; `NumLingot=0`,
  `Trace=1`.
- `AC-FR14-5` : le worker expose au Launcher un `WorkerStatus` (`IsRunning`,
  `IsActive`, `LastStartedAt`, `LastError`) — même contrat que
  `ServicesMicroScope.LauncherApiClient` ; s'enregistre dans `WorkerSettings`.
- `AC-FR14-6` : `Stop` du Launcher pendant un tick → le Fichier en cours finit ou
  reste dans `processing/` (jamais à moitié inséré) ; arrêt propre < 5 s.
- `AC-FR14-7` : `Logs` indisponible n'empêche pas l'insertion `L_D_KAPE22` (log
  best‑effort) ; `L_D_LOG_COMMANDE` fait partie de la transaction de succès
  (`AC-FR11-3`).

---

#### FR-15 : Robustesse de la boucle worker

**Consequences (testables) :**
- `AC-FR15-1` : une exception non prévue sur un Fichier est capturée, loggée
  `ERROR`, le Fichier va en `error/`, la boucle continue avec le Fichier suivant.
- `AC-FR15-2` : dossier de réception injoignable un tick → log `Warning`, aucun
  Fichier perdu, retry au tick suivant.
- `AC-FR15-3` : base `AscoLSI` injoignable → Fichiers laissés dans `processing/`,
  log `Warning`, retry ultérieur (pas de passage en `error/`).
- `AC-FR15-4` : redémarrage du worker (recycle) au milieu d'un lot → reprise
  complète au tick suivant, sans perte ni doublon (garde‑fou §0bis D22).

---

### 4.4 Extensibilité : ajouter un format

#### FR-16 : Anatomie d'un microservice de format

**Description :** *(cadre pour les formats futurs — v1 ne livre que P60.)* Un
nouveau format = un nouveau projet, copié sur `Kape22Importer`. `TextToXml` n'est
jamais modifiée : **Étape 1 = 0 ligne de code** (le descripteur `<format>.xml`) ;
**Étape 2 = XSD + DTO + entité + mapping + config**.

**Consequences (testables / vérifiables en revue) :**
- `AC-FR16-1` : le seul code partagé référencé par `Kape22Importer` est
  `TextToXml` (+ `PortalSharedLibrary` pour l'identité/log) — inspection des
  `ProjectReference`.
- `AC-FR16-2` : les points de variation d'un format sont **exactement** :
  `<format>.xml`, `<format>.xsd`, DTO, entité + `DbContext`, table de mapping,
  `appsettings`. Test d'architecture : les types de `Kape22Importer` hors
  mapping/config/entité/DTO ne référencent aucun littéral propre à P60.
- `AC-FR16-3` : la suite de tests de `TextToXml` est indépendante de tout
  format (n'importe aucun projet `*Importer`) et inclut un descripteur
  synthétique non‑P60 (`fixtures/generic/`, `AC-FR1-9`).
- `AC-FR16-4` : `TextToXml` ne contient **aucune** constante littérale propre à
  P60 (`"EOF"`, `"Segment"`, position `9`, longueurs de Champs…). Seul
  `Windows-1252` est figé. Revue + test : la lib passe ses tests en ne connaissant
  que `fixtures/generic/`.

**Feature‑specific NFRs :**
- Performance : un Fichier (3 Lignes, ~700 octets) traité de bout en bout en
  < 200 ms hors latence FTP/SQL ; un tick de 500 Fichiers en < 30 s.
- `TextToXml` : sans état, thread‑safe (`AC-FR6` étendu : 100 `Convert`
  concurrents → résultats == séquentiel).

**Cross‑cutting NFRs (§ Adapt‑in) :**
- **Sécurité** : compte SQL **`sa`** existant (§0bis D21), comme les autres
  workers du portail — lu depuis la configuration, **jamais en dur**. Secrets via
  *User Secrets* (dev) / *appsettings* protégé ou variables d'environnement
  (prod). Le PRD ne contient aucun secret.
- **Cibles** : `net10.0`, `Microsoft.EntityFrameworkCore` 10.0.x + `Serilog` +
  `Serilog.Sinks.MSSqlServer` (aligné sur les workers existants). `TextToXml` :
  **zéro dépendance runtime** hors BCL (`System.Xml`, `System.Text.Encoding.CodePages`).
- **Observabilité** : chaque `ImportResult` traçable du nom de fichier à l'`Id`
  inséré ou à la liste d'erreurs, via `MQTTnetServices.Logs`, `L_D_LOG_COMMANDE`
  et les fichiers `*.errors.json`.
- **Reprise** : rejouer = redéposer le fichier corrigé dans le dossier de
  réception. Aucune action en base.

**Notes :**
- `P60.xml` décrit **526** caractères, les fichiers réels en font **637**.
  Décision §0bis D5 : positions 526‑636 **ignorées** (données inutilisées, le
  template fait foi) ; aucune colonne de `L_D_KAPE22` n'en dépend (`AC-FR4-5`).

### 4.5 Standards et conventions de code

L'agent de développement doit impérativement appliquer les règles de formatage et d'architecture suivantes lors de toute génération ou modification de code :

**1. C# et Règles de Commentaires :**
- **Langue :** Tous les commentaires doivent être rédigés exclusivement en anglais.
- **Syntaxe :** Chaque phrase de commentaire doit obligatoirement commencer par une majuscule et se terminer par un point. L'utilisation de listes numérotées dans les commentaires est interdite.
- **Positionnement :** Les commentaires en fin de ligne (trailing comments) sont strictement prohibés. Tout commentaire doit être placé sur sa propre ligne isolée, immédiatement au-dessus du bloc de code qu'il décrit.
- **Préservation :** Les commentaires existants doivent être conservés dans leur état d'origine, à moins que le code sous-jacent ne subisse une modification.
- **Tri :** Les propriétés des objets et des classes doivent toujours être listées par ordre alphabétique.

**2. Front-end, Web et UI (pour les futurs microservices) :**
- **Séparation des préoccupations :** Une séparation stricte et claire doit toujours être maintenue entre la logique C# et le code JavaScript.
- **Framework UI :** Le rendu HTML doit s'appuyer systématiquement sur Bootstrap.
- **Feuilles de style :** Dans les règles CSS, l'utilisation du mot-clé `!important` ne doit intervenir que lorsque cela s'avère strictement nécessaire. Les propriétés CSS doivent également respecter un classement par ordre alphabétique.  

## 5. Non‑Goals (explicites)

- Pas de conversion **partielle** : 1 erreur ⇒ 0 insertion, fichier en `error/`.
- Pas de **fail‑fast** en Étape 1/2 : on collecte **toutes** les erreurs (sauf
  Descripteur cassé).
- Pas de flux **sortant** LSI → SAP, pas d'**accusé de réception**.
- Pas de **détection d'encodage** ni d'option d'encodage : tous les fichiers
  entrants sont `Windows-1252` (figé).
- Pas de **microservice générique multi‑format** : 1 microservice par format
  (imposé). **Mais** l'Étape 1 (`TextToXml`) est, elle, entièrement générique.
- Pas de **ventilation** de `L_D_KAPE22` vers les autres tables `AscoLSI` :
  `Kape22Importer` s'arrête à l'insertion dans `L_D_KAPE22`. La distribution vers
  les tables métier est un **futur microservice** (§0bis D15).
- Pas d'**UI** ni d'**écran dédié** : supervision via ServicesMicroScope /
  Launcher existants + XML conservé + `Message` de `L_D_LOG_COMMANDE` (§0bis D12).
- Pas de **rejeu automatique** ni d'ordonnanceur : scrutation périodique simple.
- Pas de **déduplication** fonctionnelle (§0bis D7).
- `TextToXml` ne connaît **ni EF, ni fichiers, ni la base** : purge stricte des
  responsabilités.

## 6. Périmètre MVP

### 6.1 Dans le périmètre

- **`TextToXml`** : `Converter.Convert`, chargement/validation
  du Descripteur, Étape 1 complète et **générique**, contrat `ConversionResult`.
  Aucune dépendance à KAPE22.
- **`Kape22Importer`** : `Kape22Mapper` (Étape 2), persistance transactionnelle
  `AscoLSI` + `L_D_LOG_COMMANDE`, worker de scrutation dossier (via `IFileSource`),
  cycle de vie fichier (`processing`/`archive`/`error`), double log
  (`MQTTnetServices.Logs` + `L_D_LOG_COMMANDE`), intégration Launcher.
- **`P60.xml`** (descripteur) : le fichier de `Templates/` **+ un `datatype` par
  `<value>`** (§0bis D6) + attribut `expectedMessageCount="1"`. Embarqué en
  ressource. Positions 526‑636 ignorées (§0bis D5).
- **`P60.xsd`** (statique, écrit à la main, §0bis D10) — valide le XML normalisé
  avant désérialisation ; le **DTO `Kape22File` est généré** de ce XSD.
- Entité EF `L_D_KAPE22` + `L_D_LOG_COMMANDE` + `DbContext` `AscoLsiDbContext`
  (database‑first, Annexe C ; `Id` identity, aucune migration) + contrôle de
  compatibilité descripteur ↔ table au démarrage (FR‑8).
- Purge de rétention (`Import:RetentionDays`, §0bis D13).
- Suite **xUnit** couvrant **tous** les `AC-FRx-y` (unitaires `TextToXml` +
  `Kape22Mapper` ; intégration EF pour FR‑11/14 ; `IFileSource` mémoire pour
  FR‑12/13/15).
- Jeu de fixtures : les 10 fichiers `P60/` + variantes fautives dérivées +
  1 descripteur synthétique non‑P60 (`fixtures/generic/`) (Annexe A.4).

### 6.2 Hors périmètre MVP

- **Formats `P62` / `SerrageBil` / `SortieStock`** (§0bis D24) — exemples,
  traités par de futurs microservices. `format="Semicolon"` → `LayoutInvalid`.
- **Plusieurs `<message>` par fichier** — non géré (YAGNI).
- **Ventilation** de `L_D_KAPE22` vers les tables métier — futur microservice
  (§0bis D15).
- **Méta‑schéma `commande.xsd`** des descripteurs — non retenu (§0bis D10).
- Écran dédié, alerting (mail/Teams), rejeu automatique des fichiers `error/` —
  *v2 ; en v1 : logs + `L_D_LOG_COMMANDE` + XML conservé.*

## 7. Métriques de succès

**Primaire**
- **SM‑1** : 100 % des `AC-FRx-y` de ce PRD couverts par ≥ 1 test xUnit vert.
  Valide FR‑1..FR‑16.

**Secondaire**
- **SM‑2** : sur les 10 fichiers `P60/`, `Kape22Importer` insère 10 lignes
  `L_D_KAPE22` cohérentes avec les données visibles (OF, Coulee, Client, Nuance)
  — test d'intégration de bout en bout. Valide FR‑7..FR‑13.
- **SM‑3** : un fichier volontairement corrompu (Annexe A.4) produit un
  `*.errors.json` qu'un exploitant comprend sans aide — revue avec l'exploitation.
  Valide FR‑6, FR‑8, FR‑14.

**Contre‑métriques (à ne pas optimiser)**
- **SM‑C1** : ne pas réduire le nombre d'entrées `Errors` pour « faire propre » —
  chaque cause distincte reste listée. Contrebalance SM‑1.
- **SM‑C2** : ne pas ajouter d'état / cache dans `TextToXml` pour gagner sur
  SM‑2 — pureté et thread‑safety priment. Contrebalance SM‑2.
- **SM‑C3** : ne pas « réparer » silencieusement une valeur SAP douteuse
  (troncature, complétion, valeur par défaut) pour éviter un rejet — un doute =
  un rejet explicite. Contrebalance SM‑2.

## 8. Questions ouvertes

**Aucune.** Toutes les décisions sont figées au §0bis (D1–D26).

*Traçabilité des 21 questions posées en revue → décisions :* Q1→D14, Q2→D10
(pas de méta‑schéma), Q3→D10, Q4→D4, Q5→D4, Q6→D8, Q7→D8, Q8→D15, Q9→D25,
Q10→D12, Q11→D13, Q12→D21, Q13→D16, Q14→D17, Q15→D23, Q16→D19, Q17→D18, Q18→D20,
Q19→D24, Q20→§9, Q21→D22.

## 9. Emplacement de ce PRD & suite BMAD

**Emplacement (D26)** : PRD validé → **déplacé dans
`_bmad-output/planning-artifacts/PRD.md`** (chemin `planning_artifacts` de
`_bmad/bmm/config.yaml`), où les workflows BMAD suivants le trouvent par défaut.

**Suite :** `@pm` *create‑epics‑and‑stories* pour découper —
**Épic 1** `TextToXml` (FR‑1..FR‑6, FR‑16),
**Épic 2** `Kape22Importer` — mapping & persistance (FR‑7..FR‑11),
**Épic 3** `Kape22Importer` — worker & exploitation (FR‑12..FR‑15) —
puis `@sm` par story. Les ~115 `AC-FRx-y` de ce PRD alimentent directement les
tests xUnit de chaque story.

---

## Annexe A — Layout KAPE22 (d'après `P60.xml`)

`P60.xml` actuel ne porte pas de `datatype` ; l'unique évolution v1 en ajoute un
par Champ, **dérivé du type de la colonne** `L_D_KAPE22` cible (§0bis D6,
Annexe C.1) : colonne `int` → `datatype="int"`, sinon `"string"` (P60 n'a ni
`datetime` ni `decimal`). Colonne « datatype » ci‑dessous.

### A.1 Bloc Entête (`Segment` = `000`) — longueur min 18

| Id | Position | Size | datatype | Sens | Nom de Fichier |
|---|---|---|---|---|---|
| File | 0 | 3 | string | code type de flux (`P60`) | segment 1 |
| Date | 3 | 3 | string | jour dans l'année (`jjj`), converti par le worker (§0bis D4) | — |
| NumeroFichier | 6 | 3 | string | n° de roulette / séquence | segment 4 |
| Segment | 9 | 3 | string | contrôle (`000`) | — |
| Emet | 12 | 3 | string | n° programme émetteur (`847`) | segment 2 |
| Recepteur | 15 | 3 | string | n° programme récepteur (`682`) | segment 3 |
| Filler | 18 | 62 | string | *(ignoré)* | — |

Le Bloc Détail ne contient **qu'un seul `<message>`** (pas de messages multiples
pour P60). Son Champ `Date` (Position 3, Size 6) = `jjj` + `NumeroFichier`
concaténés (d'où la sous‑tranche `NumeroFichier` @6/3).

### A.2 Bloc Détail (`Segment` = `EOF`) — longueur min = `Position` du dernier Champ (FR‑4)

Champs identifiants (extrait ; liste complète = section `<message>` de `P60.xml`) :

| Id | Position | Size | datatype attendu | Colonne cible | Type colonne |
|---|---|---|---|---|---|
| File | 0 | 3 | string | *(ignoré)* | — |
| Date | 3 | 6 | string | *(ignoré)* | — |
| NumeroFichier | 6 | 3 | string | *(ignoré — pris de l'Entête)* | — |
| Segment | 9 | 3 | string | *(ignoré — contrôle `EOF`)* | — |
| Element | 12 | 1 | string | *(ignoré)* | — |
| KAP | 13 | 2 | string | *(ignoré)* | — |
| Reserve | 15 | 6 | string | *(ignoré)* | — |
| Type | 21 | 1 | string | `Type` | nchar(2) NOT NULL |
| OF | 22 | 7 | string | `OF` | nchar(24) NOT NULL |
| Indice | 29 | 1 | int | `Indice` | int NOT NULL |
| Client | 30 | 13 | string | `Client` | nvarchar(26) NOT NULL |
| Nuance | 43 | 7 | string | `Nuance` | nvarchar(14) NOT NULL |
| Coulee | 50 | 6 | string | `Coulee` | nvarchar(12) NOT NULL |
| ProfilProduit | 56 | 3 | string | `ProfilProduit` | nchar(6) NULL |
| DiametreProduit | 59 | 4 | int | `DiametreProduit` | int NULL |
| … *(colonne `int` L_D_KAPE22)* | … | … | int | *(homonyme)* | int NULL |
| … *(colonne `nchar`/`nvarchar`)* | … | … | string | *(homonyme)* | (n)var/char NULL |
| DateEnfournementFour1_Date / _Heure | 162 / 166 | 4 / 4 | string | **ignoré (§0bis D14)** — colonne `DateEnfournementFour1` laissée NULL | — |
| DateEnfournementFour2_Date / _Heure | 172 / 176 | 4 / 4 | string | **ignoré (§0bis D14)** — colonne `DateEnfournementFour2` laissée NULL | — |
| OFOriginInterne | 429 | 1 | string | `OForiginInterne` *(casse)* | nchar(24) NULL |
| ReserveSVT | 510 | 16 | string | *(ignoré)* | — |
| *(526..636)* | — | — | — | **ignoré (§0bis D5)** | — |

P60 n'a **aucun** Champ `datetime` ni `decimal` : `int` (~30 Champs, `int.Parse`
après `Trim`, pas de `convert`) et `string` (le reste). Aucun `convert` requis.

### A.3 Bloc Pied (`Segment` = `999`) — longueur min 17

| Id | Position | Size | datatype | Usage |
|---|---|---|---|---|
| File | 0 | 3 | string | contrôle inter‑blocs (FR‑10) |
| Date | 3 | 3 | string | *(ignoré)* |
| NumeroFichier | 6 | 3 | string | contrôle inter‑blocs |
| Segment | 9 | 3 | string | contrôle (`999`) |
| Records | 12 | 5 | int | contrôle `== 3` (§0bis D18) → `Warning` si ≠ (FR‑10) |
| Filler | 17 | 63 | string | *(ignoré)* |

### A.4 Fixtures de test attendues (dossier `TextToXml.Tests/fixtures/`)

- `valid/P60_847_682_001..010` — copie des échantillons ; chacun → `Success` +
  1 ligne, `Errors` vide.
- `two_lines.txt` / `four_lines.txt` — profil KAPE22, ≠ 3 Lignes → `WrongBlockCount`.
- `segment_mismatch.txt` — ligne 2 `Segment = "000"` → `SegmentMismatch`.
- `detail_too_short.txt` — Détail tronqué avant un Champ → `LineTooShort`.
- `non_numeric_diametre.txt` — `DiametreProduit = "11A0"` → `InvalidInteger` (Étape 1).
- `empty_required.txt` — `Coulee` vide → `RequiredFieldMissing` (Étape 2).
- `bad_footer_count.txt` — `Records = "00009"` → `InterBlockMismatch`.
- `bad_filename.txt` — contenu OK, nom `P60_847_999_001` → `FileNameMismatch`.
- `cp1252.txt` — `é` (0xE9) dans `Client` → normalisé `"é"`, `Success`.
- `undecodable.bin` — octet interdit → `UndecodableInput`.
- `empty.txt` — 0 octet → `EmptyFile`.
- `generic/message-only.xml` + `.txt` — descripteur **sans header/footer**,
  `datatype` déclarés, N messages → `Success`, `<file>` avec N `<message>`.
  Généricité (`AC-FR1-6/9/10`, `AC-FR3-3`, `AC-FR5-2/4`, `AC-FR16-3/4`).
- `generic/typed-values.xml` + `.txt` — Champs `datatype="datetime"` /
  `"decimal"` valides + invalides → liste `InvalidDate` / `InvalidDecimal`
  (Étape 1), 0 XML.
- `generic/roundtrip.xml` + `.txt` — fichier valide → XML → désérialisation en
  DTO `record` : int/decimal/DateTime conservés (`AC-FR5-12`).

## Annexe B — Mapping DTO `Kape22File` → entité `L_D_KAPE22`

**Règle par défaut :** propriété du DTO (nom = `Id` du Champ) == propriété
homonyme de `L_D_KAPE22` (insensible à la casse) ⇒ copie directe (types déjà
alignés, contrôlés au démarrage FR‑8).

**Exceptions de nommage :**

| Propriété DTO | Propriété entité |
|---|---|
| `OFOriginInterne` | `OForiginInterne` |

**Dérivés (pas de Champ Détail direct) :**

| Propriété entité | Source |
|---|---|
| `NumeroFichier` | DTO `Header.NumeroFichier` (roulette) |
| `DateReception` | horodatage de traitement (worker), heure de Paris (§0bis D5) |

**Propriétés DTO ignorées (aucune colonne) :**
`File`, `Date`, `NumeroFichier` (du Détail), `Segment`, `Element`, `KAP`,
`Reserve`, `Filler`, `ReserveSVT`, `DateEnfournementFour1/2_Date`,
`DateEnfournementFour1/2_Heure` (§0bis D14), tout `Id` commençant par `Reserve`.

**Contrôles automatiques :** au **build des tests** (FR‑7‑3), toute propriété DTO
non mappée et non ignorée ⇒ échec ; toute colonne NOT NULL sans source ⇒ échec.
Au **démarrage du worker** (FR‑8), incompatibilité de type ou `Size > max_length`
⇒ le worker refuse de démarrer.

## Annexe C — Schémas des tables (`AFV004-LSI`)

### C.1 `AscoLSI.dbo.L_D_KAPE22` (cible)

PK `Id` `int` identity. ~17 648 lignes. Colonnes **NOT NULL** (hors `Id`) :
`NumeroFichier` (nvarchar max), `OF` (nchar 24), `Indice` (int), `Type` (nchar 2),
`Coulee` (nvarchar 12), `Nuance` (nvarchar 14), `Client` (nvarchar 26),
`DateReception` (datetime). Toutes les autres colonnes sont **nullable**.

`datatype` du template dérivé du type de colonne (§0bis D6) : **`int`** pour les
~30 colonnes numériques SAP (`Indice`, `DiametreProduit`, toutes les `Tolerance*`,
`Epaisseur*`, `H2Coulee`, `NumeroFour1/2`, `SectionLaminage`, `ChutageTete/Pied`,
`LongueurMoyenne`, `MatriculeClient`, `NombreLingotsFour1/2`, `PriseDeFer`, …) ;
**`string`** (`nchar`/`nvarchar`) pour tout le reste — préserve les zéros de tête
(`Coulee="063127"`, `NumeroFichier="108"`). Les colonnes `datetime`
`DateEnfournementFour1/2` restent **NULL** (Champs source non utilisés, §0bis D14) ;
`DateReception` (datetime NOT NULL) est **dérivée** (horodatage worker). Index
non‑uniques : `OF`, `Client`, `Coulee`, `DateReception`. Aucune contrainte
d'unicité métier ⇒ pas de dédup (§0bis D7). Les 92 colonnes sont figées dans
`AscoLsiDbContext` (database‑first).

### C.2 `AscoLSI.dbo.L_D_LOG_COMMANDE` (journal métier)

| Colonne | Type | Null | Écrit par `Kape22Importer` |
|---|---|---|---|
| `Id` | int identity | non | — |
| `Commande` | nvarchar(100) | non | `"P60"` |
| `Message` | nvarchar(max) | non | `"<NumeroFichier> — OK"` / `"<NumeroFichier> — REJETÉ : <résumé erreurs de mapping>"` |
| `OF` | nvarchar(24) | non | `OF` **brut** du bloc message (§0bis D8) — ligne écrite **seulement si lisible** (§0bis D15) |
| `User` | nvarchar(100) | non | `Import:InitiatingServer` (config, §0bis D8) |
| `Date` | datetime | non | horodatage de traitement (heure de Paris) |
| `NumLingot` | int | non | `0` (§0bis D25) |
| `Trace` | bit | oui | `1` (§0bis D25) |

### C.3 `MQTTnetServices.dbo.Logs` (Serilog) & `dbo.WorkerSettings`

`Logs` : `Id` (identity), `Message`, `MessageTemplate`, `Level`, `TimeStamp`,
`Exception`, `Properties` — sink `Serilog.Sinks.MSSqlServer`, message
`[Kape22Importer][<Event>] : <texte>` (même style que les workers `ImportFiles` /
`ConvertAndSave` existants). `WorkerSettings` : `WorkerName` (nvarchar 200),
`IsActive` (bit) — le worker s'y enregistre pour le Launcher.

## Annexe D — Catalogue des `ErrorCode`

| Code | Étape | Niveau | Champ requis dans l'erreur |
|---|---|---|---|
| `EmptyFile` | 1 | File | — |
| `UndecodableInput` | 1 | File | — |
| `LayoutInvalid` | 1 | File | `Message` cite l'`Id`/section/cause XSD |
| `WrongBlockCount` | 1 | File | `Message` : attendu vs trouvé |
| `SegmentMismatch` | 1 | Ligne | `FieldId`, `RawValue` = valeur lue |
| `LineTooShort` | 1 | Ligne | `Message` : `Position` manquante vs réel |
| `InvalidInteger` | 1 | Ligne | `FieldId`, `RawValue` |
| `InvalidDecimal` | 1 | Ligne | `FieldId`, `RawValue` |
| `InvalidDate` | 1 | Ligne | `FieldId`, `RawValue` |
| `RequiredFieldMissing` | 2 | Ligne | `FieldId`, `Column` |
| `InterBlockMismatch` | 2 | Ligne/File | `FieldId` |
| `FileNameMismatch` | 2 | File | `FieldId` + `RawValue` = segment du nom |
| `PersistenceError` | 2 | File | `Message` : cause SQL / schéma résumée |

## Annexe E — Templates d'exemple (hors périmètre, §0bis D24)

`Templates/` contient `P62.xml`, `SerrageBil.xml`, `SortieStock.xml` **en plus**
de `P60.xml`. Ils ne sont **pas** traités en v1 ; ils ont servi à cadrer la
généricité de `TextToXml` :

- **`P62`** (`Fixed`, header/message/footer, `datatype`+`convert`, champs
  `Anomalie1..12` énumérés) → header/footer optionnels, `datatype` directeur,
  pas de groupe répété.
- **`SerrageBil`** (`format="Semicolon"`, **message seul**, `datatype="decimal"`
  + `decimalSeparator`) → `<header>`/`<footer>` optionnels, 2ᵉ stratégie de
  découpe (non implémentée), type `decimal`.
- **`SortieStock`** (`Fixed`, dialecte `Type="C/N"` + `Remarque`) → un futur
  format devra être migré au vocabulaire de `P60.xml` (`datatype`, `Description`).

Chacun aura, le moment venu, son propre microservice + `<format>.xml` +
`<format>.xsd` + entité EF.
