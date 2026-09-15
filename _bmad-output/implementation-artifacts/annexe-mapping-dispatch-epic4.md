# Annexe de mapping — Dispatch `L_D_KAPE22` vers les 10 tables aval (Épic 4)

Story 4.2 (FR-17). Source unique de vérité pour les mappers des Stories 4.3/4.4/4.5 (AC-FR17-4) :
aucun mapper de ces stories ne code une règle absente d'ici. Statut par colonne :

- `sourcée` — champ `L_D_KAPE22` exact cité (préfixe `KAPE22.` dans la colonne Source / Règle).
- `règle` — dérivation exacte décrite (formule, constante, ou "non alimentée ici, mise à jour
  ailleurs" quand cette absence est elle-même documentée par une preuve de code legacy).
- `à_clarifier` — aucune preuve suffisante trouvée dans le legacy lu ; voir `deferred-work.md`
  pour la justification et la portée de chaque entrée (une story avance sur les colonnes
  `sourcée`/`règle`, `à_clarifier` ne bloque pas, AC-FR17-2).

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

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| AcompteSolde | sourcée | KAPE22.AcompteSolde |
| ClasseDeChute | sourcée | KAPE22.ClasseDeChute |
| Client | sourcée | KAPE22.Client |
| CodeDemiProduit | sourcée | KAPE22.CodeDemiProduit |
| Coulee | sourcée | KAPE22.Coulee |
| DateDebut | règle | NULL au dispatch ; positionné à DateTime.Now par le déclenchement ultérieur du début de laminage (OrdreDeFabricationManager.cs:160-282, DeclareDebutLaminageOF/DeclareDebutOF), hors flux P60 |
| DateDebutLaminage | règle | NULL au dispatch ; même famille que DateDebut, positionné par le même événement ultérieur |
| DateEVC | règle | NULL au dispatch ; positionné à DateTime.Now par la validation EVC (OrdreFabricationController.cs:1824,1854), hors flux P60 |
| DateFin | règle | NULL au dispatch ; positionné à DateTime.Now par DeclareFinLaminageOF/DeclareFinOF (OrdreDeFabricationManager.cs:299-341), hors flux P60 |
| DateFinLaminage | règle | NULL au dispatch ; même famille que DateFin |
| DateMaj | règle | Horodatage d'import (TimeProvider), colonne d'audit sans équivalent dans le MappingTemplate legacy |
| DateReception | règle | Horodatage d'import (TimeProvider), colonne d'audit sans équivalent dans le MappingTemplate legacy |
| DiametreProduit | sourcée | KAPE22.DiametreProduit |
| Epaisseur | sourcée | KAPE22.Epaisseur |
| Etat | à_clarifier | Enum EtatOF NOT NULL ; aucune valeur initiale trouvée à l'import P60 dans les fichiers lus. SetOFAsENC positionne EtatOF.ENC mais dans un flux de planification GPAO manuel ultérieur (OrdreFabricationController.cs:265-330), pas au dispatch |
| Indice | sourcée | KAPE22.Indice |
| LongueurCD | sourcée | KAPE22.LongueurCD |
| MarqueCommerciale | sourcée | KAPE22.MarqueCommerciale |
| NombreDemiProduit | sourcée | KAPE22.NombreDemiProduit |
| NombreLingotsWagon1Four1 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus |
| NombreLingotsWagon1Four2 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus |
| NombreLingotsWagon2Four1 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus |
| NombreLingotsWagon2Four2 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus |
| Nuance | sourcée | KAPE22.Nuance |
| NumeroFichier | sourcée | KAPE22.NumeroFichier |
| NumeroMontage | sourcée | KAPE22.NumeroMontage |
| OF | sourcée | KAPE22.OF |
| OFOrigine | à_clarifier | Uniquement lu (OrdreFabrication.cs:1439), jamais écrit dans les fichiers legacy lus ; site d'écriture non trouvé |
| PoidsDemiProduitUnitaire | sourcée | KAPE22.PoidsDemiProduitUnitaire |
| PoidsPesee | à_clarifier | Uniquement lu (InterfaceManager.cs:252,1418 ; OrdreFabrication.cs:1488), jamais écrit dans les fichiers lus ; vraisemblablement alimenté par un poste de pesée distinct, hors dispatch P60 |
| PoidsPrevuDemiProduit | sourcée | KAPE22.PoidsPrevuDemiProduit |
| ProfilProduit | sourcée | KAPE22.ProfilProduit |
| SensLaminage | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus |
| SensLaminageGPAO | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus |
| SuiviDeZoneZone | à_clarifier | Erreur d'annexe corrigée à la Story 4.3 : aucun champ `SuiviDeZoneZone` (ni variante de casse/orthographe) n'existe sur `L_D_KAPE22` ; voir `deferred-work.md` |
| TemperatureScarfing | règle | NULL au dispatch ; positionné ensuite par OrdreFabricationController.cs:2069 (capture de température, hors P60) |
| TemperatureT03 | règle | NULL au dispatch ; positionné ensuite par OrdreFabricationController.cs:2080 |
| TemperatureT07 | règle | NULL au dispatch ; positionné ensuite par OrdreFabricationController.cs:2099 |
| ToleranceMaxEpaisseur | sourcée | KAPE22.ToleranceMaxEpaisseur |
| ToleranceMaxLongueur | sourcée | KAPE22.ToleranceMaxLongueur |
| ToleranceMaxSection | sourcée | KAPE22.ToleranceMaxSection |
| ToleranceMinEpaisseur | sourcée | KAPE22.ToleranceMinEpaisseur |
| ToleranceMinLongueur | sourcée | KAPE22.ToleranceMinLongueur |
| ToleranceMinSection | sourcée | KAPE22.ToleranceMinSection |
| Type | sourcée | KAPE22.Type |

---

### L_D_COULEE

Le `MappingTemplate` legacy de Coulée (`App_Data/Template/Coulee.xml`) source ses champs depuis un
système différent de KAPE22/P60 (identifiants `COULEE`, `NUANCE`, `HEURE_DEPART_WAGON1`... qui ne
correspondent à aucun champ `L_D_KAPE22`), et près de la moitié de ses propriétés y sont déjà
`mapping=""`. Aucun équivalent de dispatch P60→Coulée n'a été trouvé dans `CouleeManager.cs`. La
quasi-totalité des colonnes est donc `à_clarifier` pour ce dispatch précis.

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| AnomalieAPC | à_clarifier | Aucune source KAPE22 identifiée ; Coulee.xml legacy source d'un système différent |
| AnomalieAPCRH | à_clarifier | idem AnomalieAPC |
| AnomalieRH | à_clarifier | idem AnomalieAPC |
| AnomaliesCoulee | à_clarifier | idem AnomalieAPC |
| AnomaliesDegazeur | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| AnomaliesDemoulage | à_clarifier | idem AnomalieAPC |
| ArriveeEnfournementWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| ArriveeEnfournementWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| CodeLivraison | à_clarifier | idem AnomalieAPC |
| CouleeFroide | à_clarifier | Priorité haute (FR-20) — Coulee.xml mapping="" explicite, aucune formule froide/chaude trouvée. `CouleeManager.ControleCoulee` (Desktop/kape22/CouleeManager.cs:407-466) détermine "coulée chaude" via `OF.GetConsignes("ConsignesEnfournementPits", 12).CodeConsigne != "1"` mais ne persiste pas cette valeur sur Coulee — lien à confirmer |
| DateCOPAPC | à_clarifier | idem AnomalieAPC |
| DateCOPCoulee | à_clarifier | idem AnomalieAPC |
| DateCOPDemoulage | à_clarifier | idem AnomalieAPC |
| DateCOPRH | à_clarifier | idem AnomalieAPC |
| DateReception | règle | Horodatage d'import (TimeProvider), colonne d'audit NOT NULL sans équivalent Coulee.xml |
| DebutCoulee | à_clarifier | idem AnomalieAPC |
| DebutDemoulage | à_clarifier | idem AnomalieAPC |
| Degazee | à_clarifier | idem AnomalieAPC |
| DelaisLivraisonWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| DelaisLivraisonWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| DensiteCoulee | à_clarifier | idem AnomalieAPC |
| DerniereModif | règle | Horodatage d'import (TimeProvider), colonne d'audit NOT NULL sans équivalent Coulee.xml |
| EcartWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| EcartWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| Enregistrement | à_clarifier | idem AnomalieAPC |
| EstConformiteCoulee | à_clarifier | idem AnomalieAPC |
| EstEnfournementStandard | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| EstHomogene | à_clarifier | idem AnomalieAPC |
| EstTroisQuartsConforme | à_clarifier | idem AnomalieAPC |
| EtatReception | à_clarifier | Priorité haute — enum EtatCoulee NOT NULL, aucune valeur initiale trouvée pour un dispatch P60 (CreateDefaultFroid positionne EtatCoulee.Froide mais dans un scénario administratif distinct, CouleeManager.cs:384-404) |
| Externe | règle | false — coulée interne issue du dispatch KAPE22 ; Externe=true n'est positionné que par les scénarios legacy CreateDefaultFroid/CreateFakeBUL (CouleeManager.cs:384-404,670-733), non applicables ici. Inférence par exclusion, à confirmer |
| FinCoulee | à_clarifier | idem AnomalieAPC |
| FinDemDernierLgtWagon1 | à_clarifier | idem AnomalieAPC |
| FinDemDernierLgtWagon2 | à_clarifier | idem AnomalieAPC |
| HeureArriveeWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| HeureArriveeWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| HeureDepartWagon1 | à_clarifier | idem AnomalieAPC |
| HeureDepartWagon2 | à_clarifier | idem AnomalieAPC |
| HeurePrevuDemoulage | à_clarifier | idem AnomalieAPC |
| Hydrogene | à_clarifier | idem AnomalieAPC |
| IdCoulee | sourcée | KAPE22.Coulee (même champ que L_D_ORDRE_FABRICATION.Coulee, la coulée de l'OF) |
| LingotPiscine | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| MarqueFroide | à_clarifier | idem AnomalieAPC |
| ModeElaboration | à_clarifier | idem AnomalieAPC |
| NbLingotRestantARefroidir | à_clarifier | Priorité haute — int NOT NULL, aucune valeur initiale trouvée dans les fichiers legacy lus |
| NombreLingotAir | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| NombreLingotBacVerniculite | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| NombreLingotPitsSec | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| NombreLingotsWagon1 | à_clarifier | idem AnomalieAPC |
| NombreLingotsWagon2 | à_clarifier | idem AnomalieAPC |
| NombreTypelingot1 | à_clarifier | idem AnomalieAPC |
| NombreTypelingot2 | à_clarifier | idem AnomalieAPC |
| Nuance | sourcée | KAPE22.Nuance |
| NumerosLingotRebutes | à_clarifier | idem AnomalieAPC |
| Observation2 | à_clarifier | idem AnomalieAPC |
| Observations | à_clarifier | idem AnomalieAPC |
| OperateurCoulee | à_clarifier | idem AnomalieAPC |
| OperateurDegazeur | à_clarifier | idem AnomalieAPC |
| OperateurDemoulage | à_clarifier | idem AnomalieAPC |
| Piscinage | à_clarifier | idem AnomalieAPC |
| PiscinageWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| PiscinageWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| PoidsMoyenLingotMere1 | à_clarifier | idem AnomalieAPC |
| PoidsMoyenLingotMere2 | à_clarifier | idem AnomalieAPC |
| PoidsMoyenLingotMere3 | à_clarifier | idem AnomalieAPC |
| PoidsMoyenLingotMere4 | à_clarifier | idem AnomalieAPC |
| PoidsUnitaireLingot1 | à_clarifier | idem AnomalieAPC |
| PoidsUnitaireLingot2 | à_clarifier | idem AnomalieAPC |
| ProgrammeSMQ | à_clarifier | idem AnomalieAPC |
| ResponsableTraitement | à_clarifier | idem AnomalieAPC |
| RetardDemoulage | à_clarifier | idem AnomalieAPC |
| RetardLivraisonWagon1 | à_clarifier | idem AnomalieAPC |
| RetardLivraisonWagon2 | à_clarifier | idem AnomalieAPC |
| SaturationPits | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| SauvetageWagon1 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| SauvetageWagon2 | à_clarifier | idem AnomalieAPC (Coulee.xml mapping="" explicite) |
| TypeLingot1 | à_clarifier | idem AnomalieAPC |
| TypeLingot2 | à_clarifier | idem AnomalieAPC |

---

### L_D_CONSIGNES

Table partagée par les 7 sections de charge (un sous-objet `Consignes*` par section dans le
MappingTemplate, tous `type="Consignes"`). `OF`/`CodeOperation`/`CodeConsigne` sont sourcés
directement ; `LibelleConsigne` et `TypeConsigne` sont `mapping=""` dans les 7 sous-objets du
template et alimentés par du code applicatif complexe (décodage d'un code composite,
`CompleteConsignes2`), pas par une règle unique.

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| CodeConsigne | sourcée | KAPE22, champ variable selon la section (ex. `CodeConsigneLingot` pour ConsignesLingot, `CodeConsigneChutage` pour ConsignesChutage — cf. OrdreFabrication.xml, sous-objets `ConsignesL/C/D/R/P/S/PM`) |
| CodeOperation | sourcée | KAPE22, champ variable selon la section (ex. `CodeOpeLingot`, `CodeOpeChutage`...), même famille que le CodeOperation de la section parente |
| ConsigneGPAO | règle | false pour la consigne "réelle", true pour sa consigne miroir GPAO ; `AddOrModifyConsigne` (OrdreDeFabricationManager.cs:1362-1435) crée les deux lignes en parallèle quand gpao=true |
| LibelleConsigne | à_clarifier | Calculé par `LibelleConsigneController.GetLibelle(...)` (OrdreDeFabricationManager.cs:1408,1412,1424,1428) ; fichier LibelleConsigneController.cs non fourni |
| OF | sourcée | KAPE22.OF |
| SizeCodeConsigne | à_clarifier | Paramètre `tailleconsigne` (12 ou 18) passé par `CompleteConsignes2` selon le type de consigne décodé (OrdreDeFabricationManager.cs:1442-1682) ; pas une règle unique pour toute la table |
| TypeConsigne | à_clarifier | Constante par sous-champ décodé, définie au cas par cas dans `CompleteConsignes2` (OrdreDeFabricationManager.cs:1442-1682, ex. type 13 = consigne globale, type 12 = code enfournement...) ; pas une règle unique pour toute la table |

---

### L_D_SECTIONCHARGE_CHUTAGE

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| ChutagePied | sourcée | KAPE22.ChutagePied (OrdreFabrication.xml, sous-objet ConsignesChutage) |
| ChutageTete | sourcée | KAPE22.ChutageTete |
| CodeOperation | sourcée | KAPE22.CodeOpeChutage |
| Destination | sourcée | KAPE22.Destination |
| OF | sourcée | KAPE22.OF |
| RangOperation | sourcée | KAPE22.RangOpeChutage |

---

### L_D_SECTIONCHARGE_DECOUPE

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpeDecoupe (OrdreFabrication.xml, sous-objet ConsignesDecoupeLingot) |
| LongueurMoyenne | sourcée | KAPE22.LongueurMoyenne |
| OF | sourcée | KAPE22.OF |
| OutilDeDecoupe | sourcée | KAPE22.OutilDecoupe |
| RangOperation | sourcée | KAPE22.RangOpeDecoupe |

---

### L_D_SECTIONCHARGE_LINGOT

Les 8 colonnes `PriseDeFer*`/`Programme*` (dont leurs variantes `GPAO`) ne viennent pas du
MappingTemplate : elles sont calculées par `OrdreDeFabricationManager.ComputePriseDeFer`
(Desktop/kape22/OrdreDeFabricationManager.cs:1251-1324) via une table de référence `PriseDeFer`
interrogée par montage + profil composé + section (`GetPriseDeFer`), donc hors périmètre d'un mapper
structurel pur sans accès base (AD-2). Elles restent `à_clarifier` tant que cette table de référence
n'a pas d'équivalent dans notre schéma.

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpeLingot (OrdreFabrication.xml, sous-objet ConsignesLingot) |
| EpaisseurEnLaminage | sourcée | KAPE22.EpaisseurEnLaminage |
| OF | sourcée | KAPE22.OF |
| PriseDeFer | sourcée | KAPE22.PriseDeFer |
| PriseDeFerEpaisseur | à_clarifier | Calculé par ComputePriseDeFer via la table de référence PriseDeFer (montage+profil+section), pas un champ KAPE22 direct |
| PriseDeFerEpaisseurGPAO | à_clarifier | idem PriseDeFerEpaisseur |
| PriseDeFerHauteur | à_clarifier | idem PriseDeFerEpaisseur |
| PriseDeFerHauteurGPAO | à_clarifier | idem PriseDeFerEpaisseur |
| PriseDeFerSection | à_clarifier | idem PriseDeFerEpaisseur |
| PriseDeFerSectionGPAO | à_clarifier | idem PriseDeFerEpaisseur |
| ProfileLamine | sourcée | KAPE22.ProfileLamine |
| Programme | à_clarifier | idem PriseDeFerEpaisseur |
| ProgrammeGPAO | à_clarifier | idem PriseDeFerEpaisseur |
| RangOperation | sourcée | KAPE22.RangOpeLingot |
| SectionLaminage | sourcée | KAPE22.SectionLaminage |
| ToleranceMaxEpaisseur | sourcée | KAPE22.ToleranceMaxEpaisseur1 |
| ToleranceMaxSection | sourcée | KAPE22.ToleranceMaxSection1 |
| ToleranceMinEpaisseur | sourcée | KAPE22.ToleranceMinEpaisseur1 |
| ToleranceMinSection | sourcée | KAPE22.ToleranceMinSection1 |

---

### L_D_SECTIONCHARGE_PITS

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpePits (OrdreFabrication.xml, sous-objet ConsignesEnfournementPits) |
| DateDefournementFour1 | à_clarifier | Aucune occurrence trouvée dans les fichiers legacy lus (défournement = sortie de four, événement ultérieur au dispatch) |
| DateDefournementFour2 | à_clarifier | idem DateDefournementFour1 |
| DateEnfournementFour1 | règle | NULL — volontairement non mappé dans le legacy (OrdreFabrication.xml : `mapping=""`, commentaire "Enlevé car empêche d'enfourner") ; ne pas sourcer depuis KAPE22 |
| DateEnfournementFour2 | règle | idem DateEnfournementFour1 (même note legacy) |
| H2Coulee | sourcée | KAPE22.H2Coulee |
| NumeroFour1 | sourcée | KAPE22.NumeroFour1 |
| NumeroFour2 | sourcée | KAPE22.NumeroFour2 |
| OF | sourcée | KAPE22.OF |
| RangOperation | sourcée | KAPE22.RangOpePits |

---

### L_D_SECTIONCHARGE_POIDSMETRIQUE

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpePoidMetrique (OrdreFabrication.xml, sous-objet ConsignesPoidsMetrique) |
| OF | sourcée | KAPE22.OF |
| RangOperation | sourcée | KAPE22.RangOpePoidMetrique |

---

### L_D_SECTIONCHARGE_REFROIDISSOIRS

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpeRefroidissoir (OrdreFabrication.xml, sous-objet ConsignesRefroidissoir) |
| GazScarfing | sourcée | KAPE22.GazScarfing |
| LongueurScarfingPied | sourcée | KAPE22.LongueurScarfingPied |
| LongueurScarfingTete | sourcée | KAPE22.LongueurScarfingTete |
| MatriculeClient | sourcée | KAPE22.MatriculeClient |
| MiseAuMille | sourcée | KAPE22.MiseAuMille |
| NombreLingotsFour1 | sourcée | KAPE22.NombreLingotsFour1 |
| NombreLingotsFour2 | sourcée | KAPE22.NombreLingotsFour2 |
| NuanceMarquage | sourcée | KAPE22.NuanceMarquage |
| OF | sourcée | KAPE22.OF |
| OFDestination | sourcée | KAPE22.OFDestination |
| OFInterne | sourcée | KAPE22.OFInterne |
| OFOrigin | sourcée | KAPE22.OFOrigin |
| OxygeneInferieur | sourcée | KAPE22.OxygeneInferieur |
| OxygeneLatent | sourcée | KAPE22.OxygeneLatent |
| OxygeneSuperieur | sourcée | KAPE22.OxygeneSuperieur |
| RangOperation | sourcée | KAPE22.RangOpeRefroidissoir |
| RefroidissementBloom | sourcée | KAPE22.RefroidissementBloom |
| VitesseV1 | sourcée | KAPE22.VitesseV1 |
| VitesseV2 | sourcée | KAPE22.VitesseV2 |
| VitesseV3 | sourcée | KAPE22.VitesseV3 |

---

### L_D_SECTIONCHARGE_SVT

| Colonne | Statut | Source / Règle |
| --- | --- | --- |
| CodeOperation | sourcée | KAPE22.CodeOpeSVT (OrdreFabrication.xml, sous-objet ConsignesSVT) |
| OF | sourcée | KAPE22.OF |
| RangOperation | sourcée | KAPE22.RangOpeSVT |
