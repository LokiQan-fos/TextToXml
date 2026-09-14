-- Minimal test schema for the AscoLSI target tables.
-- Provenance: scripted from the live database AscoLSI on server AFV004-LSI
-- (Microsoft SQL Server 2012, Enterprise Edition), read via sqlcmd against
-- INFORMATION_SCHEMA.COLUMNS and sys.identity_columns on 2026-09-04.
-- The 10 downstream dispatch tables (L_D_ORDRE_FABRICATION, L_D_COULEE,
-- L_D_CONSIGNES, the 7 L_D_SECTIONCHARGE_*) were added on 2026-09-14 (Story
-- 4.1), read via sqlcmd against sys.columns/sys.types/sys.indexes on the same
-- server and database. This file is generated, not written from memory (risk
-- R-3). Regenerate it from the same source if the production schema changes.
-- Scope: only the tables the integration tests touch. Column names, types,
-- lengths (in characters), nullability and identity match the source exactly.
-- Non-test objects (secondary indexes, foreign keys, triggers, the other
-- databases on the instance) are intentionally omitted.
-- The target database is chosen by the test fixture connection string
-- (for example AscoLSI_Test); this script does not issue USE.
-- String lengths here are the real character lengths. PRD Annexe C.1/C.2 lists
-- byte counts (twice the character length) for the nchar/nvarchar columns; the
-- values below supersede that table for L_D_KAPE22/L_D_LOG_COMMANDE. The 10
-- downstream tables have no PRD annexe yet (that is Story 4.2's own output);
-- their lengths come straight from sys.columns.max_length (bytes, halved for
-- nchar/nvarchar) with no intermediate table to supersede.
-- None of the 10 downstream tables has an identity column: every key below is
-- a natural, composite business key (sys.indexes), unlike L_D_KAPE22's Id.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Business rows table: one row per imported KAPE22 file (92 columns).
IF OBJECT_ID(N'dbo.L_D_KAPE22', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_KAPE22
    (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [NumeroFichier] NVARCHAR(MAX) NOT NULL,
        [OF] NCHAR(12) NOT NULL,
        [Indice] INT NOT NULL,
        [Type] NCHAR(1) NOT NULL,
        [Coulee] NVARCHAR(6) NOT NULL,
        [ProfilProduit] NCHAR(3) NULL,
        [DiametreProduit] INT NULL,
        [ToleranceMaxSection] INT NULL,
        [ToleranceMinSection] INT NULL,
        [Epaisseur] INT NULL,
        [ToleranceMaxEpaisseur] INT NULL,
        [ToleranceMinEpaisseur] INT NULL,
        [ClasseDeChute] NCHAR(4) NULL,
        [LongueurCD] INT NULL,
        [ToleranceMaxLongueur] INT NULL,
        [ToleranceMinLongueur] INT NULL,
        [MarqueCommerciale] NCHAR(9) NULL,
        [NumeroMontage] NCHAR(3) NULL,
        [CodeDemiProduit] NVARCHAR(4) NULL,
        [PoidsDemiProduitUnitaire] INT NULL,
        [NombreDemiProduit] INT NULL,
        [AcompteSolde] NCHAR(1) NULL,
        [PoidsPrevuDemiProduit] INT NULL,
        [RangOpePits] NCHAR(3) NULL,
        [CodeOpePits] NCHAR(3) NULL,
        [LibelleConsignePits] NVARCHAR(18) NULL,
        [CodeConsignePits] NVARCHAR(12) NULL,
        [H2Coulee] INT NULL,
        [NumeroFour1] INT NULL,
        [DateEnfournementFour1] DATETIME NULL,
        [NumeroFour2] INT NULL,
        [DateEnfournementFour2] DATETIME NULL,
        [RangOpeLingot] NCHAR(3) NULL,
        [CodeOpeLingot] NCHAR(3) NULL,
        [CodeConsigneLingot] NVARCHAR(12) NULL,
        [LibelleConsigneLingot] NVARCHAR(18) NULL,
        [ProfileLamine] NCHAR(1) NULL,
        [SectionLaminage] INT NULL,
        [ToleranceMaxSection1] INT NULL,
        [ToleranceMinSection1] INT NULL,
        [EpaisseurEnLaminage] INT NULL,
        [ToleranceMaxEpaisseur1] INT NULL,
        [ToleranceMinEpaisseur1] INT NULL,
        [PriseDeFer] INT NULL,
        [CodeOpeChutage] NCHAR(3) NULL,
        [RangOpeChutage] NCHAR(3) NULL,
        [CodeConsigneChutage] NVARCHAR(12) NULL,
        [LibelleConsigneChutage] NVARCHAR(18) NULL,
        [Destination] NCHAR(1) NULL,
        [ChutageTete] INT NULL,
        [ChutagePied] INT NULL,
        [CodeOpeDecoupe] NCHAR(3) NULL,
        [RangOpeDecoupe] NCHAR(3) NULL,
        [CodeConsigneDecoupe] NVARCHAR(12) NULL,
        [LibelleConsigneDecoupe] NVARCHAR(18) NULL,
        [OutilDecoupe] NVARCHAR(1) NULL,
        [LongueurMoyenne] INT NULL,
        [CodeOpePoidMetrique] NCHAR(3) NULL,
        [RangOpePoidMetrique] NCHAR(3) NULL,
        [CodeConsignePoidMetrique] NVARCHAR(12) NULL,
        [LibelleConsignePoidMetrique] NVARCHAR(18) NULL,
        [CodeOpeRefroidissoir] NCHAR(3) NULL,
        [RangOpeRefroidissoir] NCHAR(3) NULL,
        [CodeConsigneRefroidissoir] NVARCHAR(12) NULL,
        [LibelleConsigneRefroidissoir] NVARCHAR(18) NULL,
        [MatriculeClient] INT NULL,
        [RefroidissementBloom] NCHAR(8) NULL,
        [NombreLingotsFour1] INT NULL,
        [NombreLingotsFour2] INT NULL,
        [OFOrigin] NCHAR(12) NULL,
        [OFDestination] NCHAR(12) NULL,
        [OForiginInterne] NCHAR(12) NULL,
        [OFDestinationInterne] NCHAR(12) NULL,
        [NuanceMarquage] NVARCHAR(6) NULL,
        [GazScarfing] NCHAR(3) NULL,
        [OxygeneSuperieur] NCHAR(3) NULL,
        [OxygeneInferieur] NCHAR(3) NULL,
        [OxygeneLatent] NCHAR(3) NULL,
        [VitesseV1] NCHAR(2) NULL,
        [VitesseV2] NCHAR(2) NULL,
        [VitesseV3] NCHAR(2) NULL,
        [LongueurScarfingPied] NCHAR(2) NULL,
        [LongueurScarfingTete] NCHAR(2) NULL,
        [MiseAuMille] NCHAR(4) NULL,
        [CodeOpeSVT] NCHAR(3) NULL,
        [RangOpeSVT] NCHAR(3) NULL,
        [CodeConsigneSVT] NVARCHAR(12) NULL,
        [LibelleConsigneSVT] NVARCHAR(18) NULL,
        [Nuance] NVARCHAR(7) NOT NULL,
        [Client] NVARCHAR(13) NOT NULL,
        [DateReception] DATETIME NOT NULL,
        CONSTRAINT PK_L_D_KAPE22 PRIMARY KEY CLUSTERED ([Id])
    );
END;
GO

-- Business log table, one row per processed file (OK or rejected).
IF OBJECT_ID(N'dbo.L_D_LOG_COMMANDE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_LOG_COMMANDE
    (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Commande] NVARCHAR(50) NOT NULL,
        [Message] NVARCHAR(MAX) NOT NULL,
        [OF] NVARCHAR(12) NOT NULL,
        [User] NVARCHAR(50) NOT NULL,
        [Date] DATETIME NOT NULL,
        [NumLingot] INT NOT NULL,
        [Trace] BIT NULL,
        CONSTRAINT PK_L_D_LOG_COMMANDE PRIMARY KEY CLUSTERED ([Id])
    );
END;
GO

-- Story 4.1 downstream dispatch tables (10 tables, AFV004-LSI sys.columns/sys.indexes, 2026-09-14).
-- One row per Ordre de Fabrication.
IF OBJECT_ID(N'dbo.L_D_ORDRE_FABRICATION', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_ORDRE_FABRICATION
    (
        [OF] NCHAR(12) NOT NULL,
        [Indice] INT NOT NULL,
        [Etat] INT NOT NULL,
        [Type] NCHAR(1) NOT NULL,
        [Coulee] NCHAR(6) NULL,
        [ProfilProduit] NCHAR(3) NOT NULL,
        [DiametreProduit] DECIMAL(4,1) NOT NULL,
        [ToleranceMaxSection] DECIMAL(2,1) NOT NULL,
        [ToleranceMinSection] DECIMAL(2,1) NOT NULL,
        [Epaisseur] DECIMAL(4,1) NOT NULL,
        [ToleranceMaxEpaisseur] DECIMAL(2,1) NOT NULL,
        [ToleranceMinEpaisseur] DECIMAL(2,1) NOT NULL,
        [ClasseDeChute] NCHAR(4) NOT NULL,
        [LongueurCD] DECIMAL(5,3) NOT NULL,
        [ToleranceMaxLongueur] DECIMAL(4,0) NOT NULL,
        [ToleranceMinLongueur] DECIMAL(4,0) NOT NULL,
        [MarqueCommerciale] NCHAR(9) NOT NULL,
        [NumeroMontage] NCHAR(3) NOT NULL,
        [CodeDemiProduit] NVARCHAR(4) NOT NULL,
        [PoidsDemiProduitUnitaire] DECIMAL(7,3) NULL,
        [NombreDemiProduit] INT NOT NULL,
        [AcompteSolde] NCHAR(1) NOT NULL,
        [PoidsPrevuDemiProduit] DECIMAL(6,3) NULL,
        [SuiviDeZoneZone] NVARCHAR(50) NULL,
        [Nuance] NVARCHAR(7) NOT NULL,
        [Client] NVARCHAR(13) NOT NULL,
        [NumeroFichier] NVARCHAR(8) NOT NULL,
        [TemperatureScarfing] NVARCHAR(MAX) NULL,
        [TemperatureT07] INT NULL,
        [SensLaminage] NCHAR(1) NULL,
        [SensLaminageGPAO] NCHAR(1) NULL,
        [TemperatureT03] INT NULL,
        [DateReception] DATETIME NOT NULL,
        [DateDebutLaminage] DATETIME NULL,
        [DateFinLaminage] DATETIME NULL,
        [DateEVC] DATETIME NULL,
        [DateDebut] DATETIME NULL,
        [DateFin] DATETIME NULL,
        [NombreLingotsWagon1Four1] INT NULL,
        [NombreLingotsWagon1Four2] INT NULL,
        [NombreLingotsWagon2Four1] INT NULL,
        [NombreLingotsWagon2Four2] INT NULL,
        [OFOrigine] NVARCHAR(12) NULL,
        [DateMaj] DATETIME NOT NULL,
        [PoidsPesee] INT NOT NULL,
        CONSTRAINT PK_L_D_ORDRE_FABRICATION PRIMARY KEY CLUSTERED ([OF])
    );
END;
GO

-- One row per Coulee.
IF OBJECT_ID(N'dbo.L_D_COULEE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_COULEE
    (
        [IdCoulee] NCHAR(6) NOT NULL,
        [Nuance] NCHAR(7) NOT NULL,
        [NombreLingotBacVerniculite] INT NULL,
        [NombreLingotPitsSec] INT NULL,
        [NombreLingotAir] INT NULL,
        [Observations] NVARCHAR(MAX) NULL,
        [HeureDepartWagon1] DATETIME NULL,
        [HeureDepartWagon2] DATETIME NULL,
        [HeureArriveeWagon1] DATETIME NULL,
        [HeureArriveeWagon2] DATETIME NULL,
        [SaturationPits] BIT NULL,
        [EstEnfournementStandard] BIT NULL,
        [Degazee] BIT NULL,
        [EstConformiteCoulee] BIT NULL,
        [EstTroisQuartsConforme] BIT NULL,
        [ModeElaboration] INT NULL,
        [TypeLingot1] NCHAR(4) NULL,
        [PoidsUnitaireLingot1] INT NULL,
        [NombreTypelingot1] INT NULL,
        [TypeLingot2] NCHAR(4) NULL,
        [PoidsUnitaireLingot2] INT NULL,
        [NombreTypelingot2] INT NULL,
        [NumerosLingotRebutes] NVARCHAR(MAX) NULL,
        [Piscinage] NVARCHAR(MAX) NULL,
        [AnomaliesCoulee] BIT NULL,
        [AnomaliesDemoulage] BIT NULL,
        [AnomaliesDegazeur] BIT NULL,
        [PoidsMoyenLingotMere1] INT NULL,
        [PoidsMoyenLingotMere2] INT NULL,
        [PoidsMoyenLingotMere3] INT NULL,
        [PoidsMoyenLingotMere4] INT NULL,
        [DensiteCoulee] DECIMAL(5,3) NULL,
        [ProgrammeSMQ] NVARCHAR(3) NULL,
        [Hydrogene] DECIMAL(3,2) NULL,
        [EstHomogene] BIT NULL,
        [OperateurDegazeur] NVARCHAR(10) NULL,
        [OperateurCoulee] NVARCHAR(30) NULL,
        [OperateurDemoulage] NVARCHAR(10) NULL,
        [DebutCoulee] DATETIME NULL,
        [DebutDemoulage] DATETIME NULL,
        [FinCoulee] DATETIME NULL,
        [FinDemDernierLgtWagon1] DATETIME NULL,
        [FinDemDernierLgtWagon2] DATETIME NULL,
        [DelaisLivraisonWagon1] DATETIME NULL,
        [DelaisLivraisonWagon2] DATETIME NULL,
        [EcartWagon1] DATETIME NULL,
        [EcartWagon2] DATETIME NULL,
        [PiscinageWagon1] BIT NULL,
        [PiscinageWagon2] BIT NULL,
        [SauvetageWagon1] BIT NULL,
        [SauvetageWagon2] BIT NULL,
        [ArriveeEnfournementWagon1] DATETIME NULL,
        [ArriveeEnfournementWagon2] DATETIME NULL,
        [LingotPiscine] BIT NULL,
        [CouleeFroide] BIT NULL,
        [NombreLingotsWagon1] INT NULL,
        [NombreLingotsWagon2] INT NULL,
        [DateReception] DATETIME NOT NULL,
        [EtatReception] INT NOT NULL,
        [CodeLivraison] NVARCHAR(10) NULL,
        [DerniereModif] DATETIME NOT NULL,
        [RetardLivraisonWagon1] NVARCHAR(MAX) NULL,
        [RetardLivraisonWagon2] NVARCHAR(MAX) NULL,
        [RetardDemoulage] BIT NULL,
        [DateCOPDemoulage] DATETIME NULL,
        [DateCOPCoulee] DATETIME NULL,
        [HeurePrevuDemoulage] DATETIME NULL,
        [ResponsableTraitement] NVARCHAR(30) NULL,
        [AnomalieAPCRH] BIT NULL,
        [AnomalieAPC] BIT NULL,
        [AnomalieRH] BIT NULL,
        [DateCOPAPC] DATETIME NULL,
        [Observation2] NVARCHAR(100) NULL,
        [Enregistrement] NCHAR(3) NULL,
        [DateCOPRH] DATETIME NULL,
        [NbLingotRestantARefroidir] INT NOT NULL,
        [MarqueFroide] BIT NULL,
        [Externe] BIT NOT NULL,
        CONSTRAINT PK_L_D_COULEE PRIMARY KEY CLUSTERED ([IdCoulee])
    );
END;
GO

-- Operating instructions, one row per (OF, CodeOperation, TypeConsigne, ConsigneGPAO).
IF OBJECT_ID(N'dbo.L_D_CONSIGNES', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_CONSIGNES
    (
        [OF] NCHAR(12) NOT NULL,
        [CodeOperation] NCHAR(3) NOT NULL,
        [CodeConsigne] NVARCHAR(18) NOT NULL,
        [LibelleConsigne] NVARCHAR(MAX) NULL,
        [TypeConsigne] INT NOT NULL,
        [SizeCodeConsigne] INT NOT NULL,
        [ConsigneGPAO] BIT NOT NULL,
        CONSTRAINT PK_L_D_CONSIGNES PRIMARY KEY CLUSTERED ([OF], [CodeOperation], [TypeConsigne], [ConsigneGPAO])
    );
END;
GO

-- Chutage charge section, one row per (OF, CodeOperation).
IF OBJECT_ID(N'dbo.L_D_SECTIONCHARGE_CHUTAGE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_SECTIONCHARGE_CHUTAGE
    (
        [OF] NCHAR(12) NOT NULL,
        [CodeOperation] NCHAR(3) NOT NULL,
        [RangOperation] NCHAR(3) NOT NULL,
        [Destination] NCHAR(1) NULL,
        [ChutageTete] DECIMAL(3,2) NULL,
        [ChutagePied] DECIMAL(3,2) NULL,
        CONSTRAINT PK_L_D_SECTIONCHARGE_CHUTAGE PRIMARY KEY CLUSTERED ([OF], [CodeOperation])
    );
END;
GO

-- Decoupe charge section, one row per (OF, CodeOperation).
IF OBJECT_ID(N'dbo.L_D_SECTIONCHARGE_DECOUPE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_SECTIONCHARGE_DECOUPE
    (
        [OF] NCHAR(12) NOT NULL,
        [CodeOperation] NCHAR(3) NOT NULL,
        [RangOperation] NCHAR(3) NOT NULL,
        [LongueurMoyenne] DECIMAL(5,3) NULL,
        [OutilDeDecoupe] NCHAR(1) NULL,
        CONSTRAINT PK_L_D_SECTIONCHARGE_DECOUPE PRIMARY KEY CLUSTERED ([OF], [CodeOperation])
    );
END;
GO

-- Lingot charge section, one row per (OF, CodeOperation).
IF OBJECT_ID(N'dbo.L_D_SECTIONCHARGE_LINGOT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_SECTIONCHARGE_LINGOT
    (
        [OF] NCHAR(12) NOT NULL,
        [CodeOperation] NCHAR(3) NOT NULL,
        [RangOperation] NCHAR(3) NOT NULL,
        [ProfileLamine] NCHAR(1) NULL,
        [SectionLaminage] DECIMAL(4,1) NULL,
        [ToleranceMaxSection] DECIMAL(2,1) NULL,
        [ToleranceMinSection] DECIMAL(2,1) NULL,
        [EpaisseurEnLaminage] DECIMAL(4,1) NULL,
        [ToleranceMaxEpaisseur] DECIMAL(2,1) NULL,
        [ToleranceMinEpaisseur] DECIMAL(2,1) NULL,
        [PriseDeFer] INT NULL,
        [Programme] INT NULL,
        [PriseDeFerHauteur] DECIMAL(4,1) NULL,
        [PriseDeFerSection] INT NULL,
        [PriseDeFerEpaisseur] DECIMAL(4,1) NULL,
        [ProgrammeGPAO] INT NULL,
        [PriseDeFerHauteurGPAO] DECIMAL(4,1) NULL,
        [PriseDeFerSectionGPAO] INT NULL,
        [PriseDeFerEpaisseurGPAO] DECIMAL(4,1) NULL,
        CONSTRAINT PK_L_D_SECTIONCHARGE_LINGOT PRIMARY KEY CLUSTERED ([OF], [CodeOperation])
    );
END;
GO

-- Pits charge section, one row per (OF, CodeOperation).
IF OBJECT_ID(N'dbo.L_D_SECTIONCHARGE_PITS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_SECTIONCHARGE_PITS
    (
        [OF] NCHAR(12) NOT NULL,
        [CodeOperation] NCHAR(3) NOT NULL,
        [RangOperation] NCHAR(3) NOT NULL,
        [H2Coulee] DECIMAL(2,1) NULL,
        [NumeroFour1] INT NULL,
        [DateEnfournementFour1] DATETIME NULL,
        [DateDefournementFour1] DATETIME NULL,
        [NumeroFour2] INT NULL,
        [DateEnfournementFour2] DATETIME NULL,
        [DateDefournementFour2] DATETIME NULL,
        CONSTRAINT PK_L_D_SECTIONCHARGE_PITS PRIMARY KEY CLUSTERED ([OF], [CodeOperation])
    );
END;
GO

-- PoidsMetrique charge section, one row per (OF, CodeOperation). No measure columns in the real schema.
IF OBJECT_ID(N'dbo.L_D_SECTIONCHARGE_POIDSMETRIQUE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_SECTIONCHARGE_POIDSMETRIQUE
    (
        [OF] NCHAR(12) NOT NULL,
        [CodeOperation] NCHAR(3) NOT NULL,
        [RangOperation] NCHAR(3) NOT NULL,
        CONSTRAINT PK_L_D_SECTIONCHARGE_POIDSMETRIQUE PRIMARY KEY CLUSTERED ([OF], [CodeOperation])
    );
END;
GO

-- Refroidissoirs charge section, one row per (OF, CodeOperation).
IF OBJECT_ID(N'dbo.L_D_SECTIONCHARGE_REFROIDISSOIRS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_SECTIONCHARGE_REFROIDISSOIRS
    (
        [OF] NCHAR(12) NOT NULL,
        [CodeOperation] NCHAR(3) NOT NULL,
        [RangOperation] NCHAR(3) NOT NULL,
        [MatriculeClient] INT NULL,
        [RefroidissementBloom] NCHAR(8) NULL,
        [NombreLingotsFour1] INT NULL,
        [NombreLingotsFour2] INT NULL,
        [OFOrigin] NCHAR(12) NULL,
        [OFDestination] NCHAR(12) NULL,
        [OFInterne] NCHAR(12) NULL,
        [NuanceMarquage] NVARCHAR(6) NULL,
        [GazScarfing] NCHAR(3) NULL,
        [OxygeneSuperieur] NCHAR(3) NULL,
        [OxygeneInferieur] NCHAR(3) NULL,
        [OxygeneLatent] NCHAR(3) NULL,
        [VitesseV1] NCHAR(4) NULL,
        [VitesseV2] NCHAR(4) NULL,
        [VitesseV3] NCHAR(4) NULL,
        [LongueurScarfingPied] NCHAR(2) NULL,
        [LongueurScarfingTete] NCHAR(2) NULL,
        [MiseAuMille] NCHAR(4) NULL,
        CONSTRAINT PK_L_D_SECTIONCHARGE_REFROIDISSOIRS PRIMARY KEY CLUSTERED ([OF], [CodeOperation])
    );
END;
GO

-- SVT charge section, one row per (OF, CodeOperation). No measure columns in the real schema.
IF OBJECT_ID(N'dbo.L_D_SECTIONCHARGE_SVT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_D_SECTIONCHARGE_SVT
    (
        [OF] NCHAR(12) NOT NULL,
        [CodeOperation] NCHAR(3) NOT NULL,
        [RangOperation] NCHAR(3) NOT NULL,
        CONSTRAINT PK_L_D_SECTIONCHARGE_SVT PRIMARY KEY CLUSTERED ([OF], [CodeOperation])
    );
END;
GO
