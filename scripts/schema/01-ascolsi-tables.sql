-- Minimal test schema for the AscoLSI target tables.
-- Provenance: scripted from the live database AscoLSI on server AFV004-LSI
-- (Microsoft SQL Server 2012, Enterprise Edition), read via sqlcmd against
-- INFORMATION_SCHEMA.COLUMNS and sys.identity_columns on 2026-09-04.
-- This file is generated, not written from memory (risk R-3). Regenerate it
-- from the same source if the production schema changes.
-- Scope: only the tables the integration tests touch. Column names, types,
-- lengths (in characters), nullability and identity match the source exactly.
-- Non-test objects (secondary indexes, foreign keys, triggers, the other
-- databases on the instance) are intentionally omitted.
-- The target database is chosen by the test fixture connection string
-- (for example AscoLSI_Test); this script does not issue USE.
-- String lengths here are the real character lengths. PRD Annexe C.1/C.2 lists
-- byte counts (twice the character length) for the nchar/nvarchar columns; the
-- values below supersede that table.

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
