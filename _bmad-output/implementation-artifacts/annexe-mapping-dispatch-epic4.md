# Annexe de mapping — Dispatch `L_D_KAPE22` vers les 10 tables aval (Épic 4)

Story 4.2 (FR-17). Source unique de vérité pour les mappers des Stories 4.3/4.4/4.5 (AC-FR17-4) :
aucun mapper de ces stories ne code une règle absente d'ici. Statut par colonne :

- `sourcée` — champ `L_D_KAPE22` exact cité (préfixe `KAPE22.` dans la colonne Source / Règle).
- `règle` — dérivation exacte décrite (formule, constante, ou "non alimentée ici, mise à jour
  ailleurs" quand cette absence est elle-même documentée par une preuve de code legacy).
- `à_clarifier` — aucune preuve suffisante trouvée dans le legacy lu ; voir `deferred-work.md`
  pour la justification et la portée de chaque entrée (une story avance sur les colonnes
  `sourcée`/`règle`, `à_clarifier` ne bloque pas, AC-FR17-2).

**Scale** (Story 4.2-bis, AC-FR17-1 étendu) : 4e colonne, entier ou `-`. Obligatoire (et non `-`)
pour toute colonne `decimal`/`decimal?` `sourcée` depuis un champ `L_D_KAPE22` `int`/`int?` — c'est
le nombre de décimales de la colonne cible réelle (`DECIMAL(p,s)`, `s` lu dans
`scripts/schema/01-ascolsi-tables.sql`), jamais deviné. `0` est une valeur valide (`DECIMAL(p,0)`),
pas une absence. `-` partout ailleurs (colonne non `decimal`, `decimal` déjà sourcée d'un champ
`decimal`, ou Statut `règle`/`à_clarifier`).

**Preuves consultées** : `App_Data/Template/{OrdreFabrication,Coulee,KAPE22}.xml` (le
`MappingTemplate` legacy, déployé sur `C:\inetpub\wwwroot\lsi\`) ; `Desktop/kape22/{KAPE22Controller,
OrdreDeFabricationManager,CouleeManager}.cs` (lecture seule, AD-3 — jamais modifiés, jamais
référencés). `OrdreFabrication.cs` et `InterfaceManager.cs` n'ont été consultés que par recherche
ciblée (grep), pas lus intégralement — une colonne classée `à_clarifier` peut donc avoir une réponse
qui s'y trouve et qui reste à vérifier.

## Règle d'applicabilité par OF (`L_D_CONSIGNES` et les 7 `L_D_SECTIONCHARGE_*`) — AC-FR17-3

`KAPE22Controller.__ConvertKAPE22toOrdreFabrication` (Desktop/kape22/KAPE22Controller.cs:115-303)
construit chaque sous-objet (Lingot, Chutage, Découpe, Pits, PoidsMétrique, Refroidissoir, SVT,
Consignes) à partir du `MappingTemplate`, puis ne l'attache à l'OF (`AddRef`, ligne 248) que si
**aucune de ses propriétés marquées clé d'entité (`EdmScalarPropertyAttribute.EntityKeyProperty`,
soit `OF` + `CodeOperation` + `RangOperation`) n'est vide** après mapping (variable `_Authadd`,
lignes 226-247). Autrement dit : **une table `L_D_SECTIONCHARGE_*` (ou une ligne `L_D_CONSIGNES`)
concerne l'OF en cours si et seulement si son `CodeOperation` et son `RangOperation` sources dans
`L_D_KAPE22` sont tous deux non vides** — c'est une règle de présence sur les champs sources, pas un
contrôle métier séparé. Les mappers 4.3/4.4 doivent renvoyer `null` (pas de ligne) quand ce n'est pas
le cas (AC-FR19-2).

---

### L_D_ORDRE_FABRICATION

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| AcompteSolde | sourcée | KAPE22.AcompteSolde | - |
| ClasseDeChute | sourcée | KAPE22.ClasseDeChute | - |
| Client | sourcée | KAPE22.Client | - |
| CodeDemiProduit | sourcée | KAPE22.CodeDemiProduit | - |
| Coulee | sourcée | KAPE22.Coulee | - |
| DateDebut | règle | NULL au dispatch ; positionné à DateTime.Now par le déclenchement ultérieur du début de laminage (OrdreDeFabricationManager.cs:160-282, DeclareDebutLaminageOF/DeclareDebutOF), hors flux P60 | - |
| DateDebutLaminage | règle | NULL au dispatch ; même famille que DateDebut, positionné par le même événement ultérieur | - |
| DateEVC | règle | NULL au dispatch ; positionné à DateTime.Now par la validation EVC (OrdreFabricationController.cs:1824,1854), hors flux P60 | - |
| DateFin | règle | NULL au dispatch ; positionné à DateTime.Now par DeclareFinLaminageOF/DeclareFinOF (OrdreDeFabricationManager.cs:299-341), hors flux P60 | - |
| DateFinLaminage | règle | NULL au dispatch ; même famille que DateFin | - |
| DateMaj | règle | Horodatage d'import (TimeProvider), colonne d'audit sans équivalent dans le MappingTemplate legacy | - |
| DateReception | règle | Horodatage d'import (TimeProvider), colonne d'audit sans équivalent dans le MappingTemplate legacy | - |
| DiametreProduit | sourcée | KAPE22.DiametreProduit | 1 |
| Epaisseur | sourcée | KAPE22.Epaisseur | 1 |
| Etat | à_clarifier | Enum EtatOF NOT NULL ; aucune valeur initiale trouvée à l'import P60 dans les fichiers lus. SetOFAsENC positionne EtatOF.ENC mais dans un flux de planification GPAO manuel ultérieur (OrdreFabricationController.cs:265-330), pas au dispatch | - |
| Indice | sourcée | KAPE22.Indice | - |
| LongueurCD | sourcée | KAPE22.LongueurCD | 3 |
| MarqueCommerciale | sourcée | KAPE22.MarqueCommerciale | - |
| NombreDemiProduit | sourcée | KAPE22.NombreDemiProduit | - |
| NombreLingotsWagon1Four1 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus | - |
| NombreLingotsWagon1Four2 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus | - |
| NombreLingotsWagon2Four1 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus | - |
| NombreLingotsWagon2Four2 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus | - |
| Nuance | sourcée | KAPE22.Nuance | - |
| NumeroFichier | sourcée | KAPE22.NumeroFichier | - |
| NumeroMontage | sourcée | KAPE22.NumeroMontage | - |
| OF | sourcée | KAPE22.OF | - |
| OFOrigine | à_clarifier | Uniquement lu (OrdreFabrication.cs:1439), jamais écrit dans les fichiers legacy lus ; site d'écriture non trouvé | - |
| PoidsDemiProduitUnitaire | sourcée | KAPE22.PoidsDemiProduitUnitaire | 3 |
| PoidsPesee | à_clarifier | Uniquement lu (InterfaceManager.cs:252,1418 ; OrdreFabrication.cs:1488), jamais écrit dans les fichiers lus ; vraisemblablement alimenté par un poste de pesée distinct, hors dispatch P60 | - |
| PoidsPrevuDemiProduit | sourcée | KAPE22.PoidsPrevuDemiProduit | 3 |
| ProfilProduit | sourcée | KAPE22.ProfilProduit | - |
| SensLaminage | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus | - |
| SensLaminageGPAO | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus | - |
| SuiviDeZoneZone | à_clarifier | Erreur d'annexe corrigée à la Story 4.3 : aucun champ `SuiviDeZoneZone` (ni variante de casse/orthographe) n'existe sur `L_D_KAPE22` ; voir `deferred-work.md` | - |
| TemperatureScarfing | règle | NULL au dispatch ; positionné ensuite par OrdreFabricationController.cs:2069 (capture de température, hors P60) | - |
| TemperatureT03 | règle | NULL au dispatch ; positionné ensuite par OrdreFabricationController.cs:2080 | - |
| TemperatureT07 | règle | NULL au dispatch ; positionné ensuite par OrdreFabricationController.cs:2099 | - |
| ToleranceMaxEpaisseur | sourcée | KAPE22.ToleranceMaxEpaisseur | 1 |
| ToleranceMaxLongueur | sourcée | KAPE22.ToleranceMaxLongueur | 0 |
| ToleranceMaxSection | sourcée | KAPE22.ToleranceMaxSection | 1 |
| ToleranceMinEpaisseur | sourcée | KAPE22.ToleranceMinEpaisseur | 1 |
| ToleranceMinLongueur | sourcée | KAPE22.ToleranceMinLongueur | 0 |
| ToleranceMinSection | sourcée | KAPE22.ToleranceMinSection | 1 |
| Type | sourcée | KAPE22.Type | - |

---

### L_D_COULEE

Note (Story 4.2-bis) : `L_D_COULEE` porte 2 colonnes `decimal?` (`DensiteCoulee`, `Hydrogene`),
toutes deux `à_clarifier` (non sourcées d'un champ KAPE22) — hors périmètre de la règle Scale, qui
ne s'applique qu'aux lignes `sourcée`.

Le `MappingTemplate` legacy de Coulée (`App_Data/Template/Coulee.xml`) source ses champs depuis un
système différent de KAPE22/P60 (identifiants `COULEE`, `NUANCE`, `HEURE_DEPART_WAGON1`... qui ne
correspondent à aucun champ `L_D_KAPE22`), et près de la moitié de ses propriétés y sont déjà
`mapping=""`. Aucun équivalent de dispatch P60→Coulée n'a été trouvé dans `CouleeManager.cs`. La
quasi-totalité des colonnes est donc `à_clarifier` pour ce dispatch précis.

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| AnomalieAPC | à_clarifier | Aucune source KAPE22 identifiée ; Coulee.xml legacy source d'un système différent | - |
| AnomalieAPCRH | à_clarifier | idem AnomalieAPC | - |
| AnomalieRH | à_clarifier | idem AnomalieAPC | - |
| AnomaliesCoulee | à_clarifier | idem AnomalieAPC | - |
| AnomaliesDegazeur | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| AnomaliesDemoulage | à_clarifier | idem AnomalieAPC | - |
| ArriveeEnfournementWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| ArriveeEnfournementWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| CodeLivraison | à_clarifier | idem AnomalieAPC | - |
| CouleeFroide | à_clarifier | Priorité haute (FR-20) — Coulee.xml mapping="" explicite, aucune formule froide/chaude trouvée. `CouleeManager.ControleCoulee` (Desktop/kape22/CouleeManager.cs:407-466) détermine "coulée chaude" via `OF.GetConsignes("ConsignesEnfournementPits", 12).CodeConsigne != "1"` mais ne persiste pas cette valeur sur Coulee — lien à confirmer | - |
| DateCOPAPC | à_clarifier | idem AnomalieAPC | - |
| DateCOPCoulee | à_clarifier | idem AnomalieAPC | - |
| DateCOPDemoulage | à_clarifier | idem AnomalieAPC | - |
| DateCOPRH | à_clarifier | idem AnomalieAPC | - |
| DateReception | règle | Horodatage d'import (TimeProvider), colonne d'audit NOT NULL sans équivalent Coulee.xml | - |
| DebutCoulee | à_clarifier | idem AnomalieAPC | - |
| DebutDemoulage | à_clarifier | idem AnomalieAPC | - |
| Degazee | à_clarifier | idem AnomalieAPC | - |
| DelaisLivraisonWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| DelaisLivraisonWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| DensiteCoulee | à_clarifier | idem AnomalieAPC | - |
| DerniereModif | règle | Horodatage d'import (TimeProvider), colonne d'audit NOT NULL sans équivalent Coulee.xml | - |
| EcartWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| EcartWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| Enregistrement | à_clarifier | idem AnomalieAPC | - |
| EstConformiteCoulee | à_clarifier | idem AnomalieAPC | - |
| EstEnfournementStandard | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| EstHomogene | à_clarifier | idem AnomalieAPC | - |
| EstTroisQuartsConforme | à_clarifier | idem AnomalieAPC | - |
| EtatReception | à_clarifier | Priorité haute — enum EtatCoulee NOT NULL, aucune valeur initiale trouvée pour un dispatch P60 (CreateDefaultFroid positionne EtatCoulee.Froide mais dans un scénario administratif distinct, CouleeManager.cs:384-404) | - |
| Externe | règle | false — coulée interne issue du dispatch KAPE22 ; Externe=true n'est positionné que par les scénarios legacy CreateDefaultFroid/CreateFakeBUL (CouleeManager.cs:384-404,670-733), non applicables ici. Inférence par exclusion, à confirmer | - |
| FinCoulee | à_clarifier | idem AnomalieAPC | - |
| FinDemDernierLgtWagon1 | à_clarifier | idem AnomalieAPC | - |
| FinDemDernierLgtWagon2 | à_clarifier | idem AnomalieAPC | - |
| HeureArriveeWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| HeureArriveeWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| HeureDepartWagon1 | à_clarifier | idem AnomalieAPC | - |
| HeureDepartWagon2 | à_clarifier | idem AnomalieAPC | - |
| HeurePrevuDemoulage | à_clarifier | idem AnomalieAPC | - |
| Hydrogene | à_clarifier | idem AnomalieAPC | - |
| IdCoulee | sourcée | KAPE22.Coulee (même champ que L_D_ORDRE_FABRICATION.Coulee, la coulée de l'OF) | - |
| LingotPiscine | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| MarqueFroide | à_clarifier | idem AnomalieAPC | - |
| ModeElaboration | à_clarifier | idem AnomalieAPC | - |
| NbLingotRestantARefroidir | à_clarifier | Priorité haute — int NOT NULL, aucune valeur initiale trouvée dans les fichiers legacy lus | - |
| NombreLingotAir | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| NombreLingotBacVerniculite | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| NombreLingotPitsSec | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| NombreLingotsWagon1 | à_clarifier | idem AnomalieAPC | - |
| NombreLingotsWagon2 | à_clarifier | idem AnomalieAPC | - |
| NombreTypelingot1 | à_clarifier | idem AnomalieAPC | - |
| NombreTypelingot2 | à_clarifier | idem AnomalieAPC | - |
| Nuance | sourcée | KAPE22.Nuance | - |
| NumerosLingotRebutes | à_clarifier | idem AnomalieAPC | - |
| Observation2 | à_clarifier | idem AnomalieAPC | - |
| Observations | à_clarifier | idem AnomalieAPC | - |
| OperateurCoulee | à_clarifier | idem AnomalieAPC | - |
| OperateurDegazeur | à_clarifier | idem AnomalieAPC | - |
| OperateurDemoulage | à_clarifier | idem AnomalieAPC | - |
| Piscinage | à_clarifier | idem AnomalieAPC | - |
| PiscinageWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| PiscinageWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| PoidsMoyenLingotMere1 | à_clarifier | idem AnomalieAPC | - |
| PoidsMoyenLingotMere2 | à_clarifier | idem AnomalieAPC | - |
| PoidsMoyenLingotMere3 | à_clarifier | idem AnomalieAPC | - |
| PoidsMoyenLingotMere4 | à_clarifier | idem AnomalieAPC | - |
| PoidsUnitaireLingot1 | à_clarifier | idem AnomalieAPC | - |
| PoidsUnitaireLingot2 | à_clarifier | idem AnomalieAPC | - |
| ProgrammeSMQ | à_clarifier | idem AnomalieAPC | - |
| ResponsableTraitement | à_clarifier | idem AnomalieAPC | - |
| RetardDemoulage | à_clarifier | idem AnomalieAPC | - |
| RetardLivraisonWagon1 | à_clarifier | idem AnomalieAPC | - |
| RetardLivraisonWagon2 | à_clarifier | idem AnomalieAPC | - |
| SaturationPits | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| SauvetageWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| SauvetageWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) | - |
| TypeLingot1 | à_clarifier | idem AnomalieAPC | - |
| TypeLingot2 | à_clarifier | idem AnomalieAPC | - |

---

### L_D_CONSIGNES

Note (Story 4.2-bis) : `L_D_CONSIGNES` ne porte aucune colonne `decimal` (7 colonnes,
`string`/`bool`/`int`) — hors périmètre de la règle Scale.

Table partagée par les 7 sections de charge (un sous-objet `Consignes*` par section dans le
MappingTemplate, tous `type="Consignes"`). `OF`/`CodeOperation`/`CodeConsigne` sont sourcés
directement. `TypeConsigne`/`SizeCodeConsigne` sont désormais une règle documentée par section
(Story 4.4-bis, ci-dessous) pour les 6 sections dont `OrdreDeFabricationManager.CompleteConsignes2`
décode un code composite (Chutage, Lingot, Pits, Decoupe, PoidsMetrique, Refroidissoir) ; SVT reste
`à_clarifier` (CLR default), aucune règle de décomposition legacy trouvée pour cette section
(commentaire mort uniquement, lignes 1668-1669). `LibelleConsigne` reste `à_clarifier`, alimenté par du
code applicatif complexe (`LibelleConsigneController.GetLibelle`), hors périmètre de Story 4.4-bis.

Décodage par section (Story 4.4-bis, offsets lus dans `OrdreDeFabricationManager.CompleteConsignes2`,
`TypeConsigne = Substring(début, longueur)` sur le code brut complété à droite à sa taille) :

- Chutage (XC1, `CodeConsigneChutage`) : 13 = code complet (12) ; 0=Sub(0,2), 1=Sub(3,1), 2=Sub(5,1).Trim, 3=Sub(7,2).Trim, 4=Sub(10,1).Trim — lignes 1520-1547.
- Lingot (LA1, `CodeConsigneLingot`) : 13 (12) ; 15=Sub(0,3), 7=Sub(4,1), 8=Sub(6,3), 9=Sub(10,2) — lignes 1488-1514.
- Pits (PC1, `CodeConsignePits`) : 13 (12) ; 12=Sub(0,1), 10=Sub(2,3), 11=Sub(2,3), 5=Sub(6,2).Trim, 6=Sub(9,3).Trim — lignes 1452-1482.
- Decoupe bloc 12 (XP1, `CodeConsigneDecoupe`) : 13 (12) ; 16=Sub(0,5).Trim, 17=Sub(6,5).Trim — lignes 1557-1573.
- Decoupe bloc 18 (XP1, `LibelleConsigneDecoupe`, Position 287, Size 18) : 24 = code complet (18) ; 25=Sub(0,1).Trim, 26=Sub(1,5).Trim, 27=Sub(7,2).Trim, 28=Sub(9,4).Trim, 29=Sub(11,1).Trim — lignes 1577-1599. Malgré son nom, ce Champ est la seconde consigne Decoupe : le legacy transforme la seconde consigne ajoutée à cette section au chargement d'un KAPE22 en type 24/taille 18 (`OrdreFabrication.cs:627-634`), et les 324 enregistrements Decoupe des fixtures `P60/` (350 fichiers) y portent tous un code structuré (`.LLLLL BC X.XM`). Chaque bloc Decoupe n'est produit que si son propre code est non vide, indépendamment de l'autre.
- PoidsMetrique (XP9, `CodeConsignePoidMetrique`) : 13 (12) ; 18=Sub(0,4).Trim, 19=Sub(5,2).Trim, 20=Sub(8,2).Trim — lignes 1612-1636.
- Refroidissoir (XA1, `CodeConsigneRefroidissoir`) : 13 (12) ; 21=Sub(0,2).Trim, 22=Sub(3,1).Trim, 23=Sub(5,3).Trim — lignes 1642-1664.
- SVT : aucune règle legacy (lignes 1668-1669, lecture commentée) — ligne unique inchangée, voir les lignes `à_clarifier` ci-dessous.


| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| CodeConsigne | sourcée | KAPE22, champ variable selon la section (ex. `CodeConsigneLingot` pour ConsignesLingot, `CodeConsigneChutage` pour ConsignesChutage — cf. OrdreFabrication.xml, sous-objets `ConsignesL/C/D/R/P/S/PM`) | - |
| CodeOperation | sourcée | KAPE22, champ variable selon la section (ex. `CodeOpeLingot`, `CodeOpeChutage`...), même famille que le CodeOperation de la section parente | - |
| ConsigneGPAO | règle | `true` pour toute ligne produite par `ConsignesMapper` — la valeur telle qu'injectée par le dispatch P60, potentiellement déjà ajustée par un opérateur pour une contrainte de production temporaire (confirmé par le donneur d'ordre, 2026-09-23, sprint-change-proposal-2026-09-23.md). `false` porte la valeur initiale prévue par l'OF, un processus antérieur au dispatch P60 et hors périmètre de ce mapper (pas un doublon "miroir" à dédupliquer, ni une ligne dont ce mapper vérifie l'existence — AD-2/AD-7) | - |
| LibelleConsigne | à_clarifier | Calculé par `LibelleConsigneController.GetLibelle(...)` (OrdreDeFabricationManager.cs:1408,1412,1424,1428) ; fichier LibelleConsigneController.cs non fourni | - |
| OF | sourcée | KAPE22.OF | - |
| SizeCodeConsigne | règle | 6 sections décodées uniquement : 12 pour la ligne de code complet et chaque sous-champ, sauf le bloc XP1 sur 18 (`LibelleConsigneDecoupe`, type 24 et ses sous-champs 25-29) qui vaut 18 — paramètre `tailleconsigne` de `CompleteConsignes2` (OrdreDeFabricationManager.cs:1442-1682). Détail : liste « Décodage par section » ci-dessus | - |
| SizeCodeConsigne | à_clarifier | SVT uniquement : CLR default 0, aucune règle de décomposition legacy trouvée (OrdreDeFabricationManager.cs:1668-1669) | - |
| TypeConsigne | règle | 6 sections décodées uniquement : Constante par sous-champ décodé, une par section et par offset `Substring` de son code consigne brut (Chutage `OrdreDeFabricationManager.cs:1520-1547`, Lingot `:1488-1514`, Pits `:1452-1482`, Decoupe `:1557-1599`, PoidsMetrique `:1612-1636`, Refroidissoir `:1642-1664`) ; `13` = code complet de chaque section (`24` pour le bloc Decoupe sur 18). Détail : liste « Décodage par section » ci-dessus | - |
| TypeConsigne | à_clarifier | SVT uniquement : CLR default 0, aucune règle de décomposition legacy trouvée (OrdreDeFabricationManager.cs:1668-1669) | - |

---

### L_D_SECTIONCHARGE_CHUTAGE

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| ChutagePied | sourcée | KAPE22.ChutagePied (OrdreFabrication.xml, sous-objet ConsignesChutage) | 2 |
| ChutageTete | sourcée | KAPE22.ChutageTete | 2 |
| CodeOperation | sourcée | KAPE22.CodeOpeChutage | - |
| Destination | sourcée | KAPE22.Destination | - |
| OF | sourcée | KAPE22.OF | - |
| RangOperation | sourcée | KAPE22.RangOpeChutage | - |

---

### L_D_SECTIONCHARGE_DECOUPE

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpeDecoupe (OrdreFabrication.xml, sous-objet ConsignesDecoupeLingot) | - |
| LongueurMoyenne | sourcée | KAPE22.LongueurMoyenne | 3 |
| OF | sourcée | KAPE22.OF | - |
| OutilDeDecoupe | sourcée | KAPE22.OutilDecoupe | - |
| RangOperation | sourcée | KAPE22.RangOpeDecoupe | - |

---

### L_D_SECTIONCHARGE_LINGOT

Les 8 colonnes `PriseDeFer*`/`Programme*` (dont leurs variantes `GPAO`) ne viennent pas du
MappingTemplate : elles sont calculées par `OrdreDeFabricationManager.ComputePriseDeFer`
(Desktop/kape22/OrdreDeFabricationManager.cs:1251-1324) via une table de référence `PriseDeFer`
interrogée par montage + profil composé + section (`GetPriseDeFer`), donc hors périmètre d'un mapper
structurel pur sans accès base (AD-2). Elles restent `à_clarifier` tant que cette table de référence
n'a pas d'équivalent dans notre schéma.

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpeLingot (OrdreFabrication.xml, sous-objet ConsignesLingot) | - |
| EpaisseurEnLaminage | sourcée | KAPE22.EpaisseurEnLaminage | 1 |
| OF | sourcée | KAPE22.OF | - |
| PriseDeFer | sourcée | KAPE22.PriseDeFer | - |
| PriseDeFerEpaisseur | à_clarifier | Calculé par ComputePriseDeFer via la table de référence PriseDeFer (montage+profil+section), pas un champ KAPE22 direct | - |
| PriseDeFerEpaisseurGPAO | à_clarifier | idem PriseDeFerEpaisseur | - |
| PriseDeFerHauteur | à_clarifier | idem PriseDeFerEpaisseur | - |
| PriseDeFerHauteurGPAO | à_clarifier | idem PriseDeFerEpaisseur | - |
| PriseDeFerSection | à_clarifier | idem PriseDeFerEpaisseur | - |
| PriseDeFerSectionGPAO | à_clarifier | idem PriseDeFerEpaisseur | - |
| ProfileLamine | sourcée | KAPE22.ProfileLamine | - |
| Programme | à_clarifier | idem PriseDeFerEpaisseur | - |
| ProgrammeGPAO | à_clarifier | idem PriseDeFerEpaisseur | - |
| RangOperation | sourcée | KAPE22.RangOpeLingot | - |
| SectionLaminage | sourcée | KAPE22.SectionLaminage | 1 |
| ToleranceMaxEpaisseur | sourcée | KAPE22.ToleranceMaxEpaisseur1 | 1 |
| ToleranceMaxSection | sourcée | KAPE22.ToleranceMaxSection1 | 1 |
| ToleranceMinEpaisseur | sourcée | KAPE22.ToleranceMinEpaisseur1 | 1 |
| ToleranceMinSection | sourcée | KAPE22.ToleranceMinSection1 | 1 |

---

### L_D_SECTIONCHARGE_PITS

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpePits (OrdreFabrication.xml, sous-objet ConsignesEnfournementPits) | - |
| DateDefournementFour1 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus (défournement = sortie de four, événement ultérieur au dispatch) | - |
| DateDefournementFour2 | à_clarifier | idem DateDefournementFour1 | - |
| DateEnfournementFour1 | règle | NULL — volontairement non mappé dans le legacy (OrdreFabrication.xml : `mapping=""`, commentaire "Enlevé car empêche d'enfourner") ; ne pas sourcer depuis KAPE22 | - |
| DateEnfournementFour2 | règle | idem DateEnfournementFour1 (même note legacy) | - |
| H2Coulee | sourcée | KAPE22.H2Coulee | 1 |
| NumeroFour1 | sourcée | KAPE22.NumeroFour1 | - |
| NumeroFour2 | sourcée | KAPE22.NumeroFour2 | - |
| OF | sourcée | KAPE22.OF | - |
| RangOperation | sourcée | KAPE22.RangOpePits | - |

---

### L_D_SECTIONCHARGE_POIDSMETRIQUE

Note (Story 4.2-bis) : `L_D_SECTIONCHARGE_POIDSMETRIQUE` ne porte aucune colonne `decimal` (3
colonnes, toutes clé) — hors périmètre de la règle Scale.

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpePoidMetrique (OrdreFabrication.xml, sous-objet ConsignesPoidsMetrique) | - |
| OF | sourcée | KAPE22.OF | - |
| RangOperation | sourcée | KAPE22.RangOpePoidMetrique | - |

---

### L_D_SECTIONCHARGE_REFROIDISSOIRS

Note (Story 4.2-bis) : `L_D_SECTIONCHARGE_REFROIDISSOIRS` ne porte aucune colonne `decimal` (21
colonnes, toutes `string`/`int`/clé) — hors périmètre de la règle Scale.

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpeRefroidissoir (OrdreFabrication.xml, sous-objet ConsignesRefroidissoir) | - |
| GazScarfing | sourcée | KAPE22.GazScarfing | - |
| LongueurScarfingPied | sourcée | KAPE22.LongueurScarfingPied | - |
| LongueurScarfingTete | sourcée | KAPE22.LongueurScarfingTete | - |
| MatriculeClient | sourcée | KAPE22.MatriculeClient | - |
| MiseAuMille | sourcée | KAPE22.MiseAuMille | - |
| NombreLingotsFour1 | sourcée | KAPE22.NombreLingotsFour1 | - |
| NombreLingotsFour2 | sourcée | KAPE22.NombreLingotsFour2 | - |
| NuanceMarquage | sourcée | KAPE22.NuanceMarquage | - |
| OF | sourcée | KAPE22.OF | - |
| OFDestination | sourcée | KAPE22.OFDestination | - |
| OFInterne | à_clarifier | Erreur d'annexe corrigée à la Story 4.4 : aucun champ `OFInterne` n'existe sur `L_D_KAPE22` (seuls `OFDestinationInterne` et `OForiginInterne`, ni l'un ni l'autre univoque) ; voir `deferred-work.md` | - |
| OFOrigin | sourcée | KAPE22.OFOrigin | - |
| OxygeneInferieur | sourcée | KAPE22.OxygeneInferieur | - |
| OxygeneLatent | sourcée | KAPE22.OxygeneLatent | - |
| OxygeneSuperieur | sourcée | KAPE22.OxygeneSuperieur | - |
| RangOperation | sourcée | KAPE22.RangOpeRefroidissoir | - |
| RefroidissementBloom | sourcée | KAPE22.RefroidissementBloom | - |
| VitesseV1 | sourcée | KAPE22.VitesseV1 | - |
| VitesseV2 | sourcée | KAPE22.VitesseV2 | - |
| VitesseV3 | sourcée | KAPE22.VitesseV3 | - |

---

### L_D_SECTIONCHARGE_SVT

Note (Story 4.2-bis) : `L_D_SECTIONCHARGE_SVT` ne porte aucune colonne `decimal` (3 colonnes,
toutes clé) — hors périmètre de la règle Scale.

| Colonne | Statut | Source / Règle | Scale |
| --- | --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpeSVT (OrdreFabrication.xml, sous-objet ConsignesSVT) | - |
| OF | sourcée | KAPE22.OF | - |
| RangOperation | sourcée | KAPE22.RangOpeSVT | - |
