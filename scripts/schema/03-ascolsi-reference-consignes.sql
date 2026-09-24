-- Test schema for the 13 AscoLSI L_P_CONSIGNES_* reference tables (Story 4.12).
-- Provenance: read from the live database AscoLSI on server AFV004-LSI via sqlcmd
-- against sys.columns/sys.types/sys.indexes on 2026-09-24, never written from
-- memory (risk R-3). Regenerate it from the same source if the production schema
-- changes.
-- These tables are only ever read: ConsigneReferenceData.Load snapshots them once
-- per Fichier for LibelleConsigneResolver, and scripts/sync-reference-consignes.ps1
-- reloads their rows from production on every end-to-end run.
-- Column order matches production exactly, because that script copies the rows
-- with bcp in native format, which is positional.
-- String lengths are character lengths (sys.columns.max_length halved for
-- nchar/nvarchar). Primary keys are kept; secondary indexes and the foreign keys
-- to the type tables are intentionally omitted.
-- The target database is chosen by the connection; this script does not issue USE.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_CHUTAGE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_CHUTAGE
    (
        [Section] NVARCHAR(4) NOT NULL,
        [Consignes] INT NOT NULL,
        [CodeConsigne] NVARCHAR(12) NOT NULL,
        [Libelle] NVARCHAR(MAX) NOT NULL,
        [DateMaj] DATETIME NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_CHUTAGE PRIMARY KEY CLUSTERED ([CodeConsigne], [Section], [Consignes])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_CODEOUTIL_COUPE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_CODEOUTIL_COUPE
    (
        [Section] NVARCHAR(4) NOT NULL,
        [Consignes] INT NOT NULL,
        [CodeConsigne] NVARCHAR(12) NOT NULL,
        [Libelle] NVARCHAR(MAX) NOT NULL,
        [Longeur] INT NOT NULL,
        [Tete] INT NOT NULL,
        [Pied] INT NOT NULL,
        [DateMaj] DATETIME NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_CODEOUTIL_COUPE PRIMARY KEY CLUSTERED ([Section], [Consignes], [CodeConsigne])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_DECOUPE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_DECOUPE
    (
        [Section] NVARCHAR(4) NOT NULL,
        [Consignes] INT NOT NULL,
        [CodeConsigne] NVARCHAR(12) NOT NULL,
        [Libelle] NVARCHAR(MAX) NOT NULL,
        [DateMaj] DATETIME NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_DECOUPE PRIMARY KEY CLUSTERED ([CodeConsigne], [Section], [Consignes])
    );
END;
GO

-- The only reference table with an identity column; bcp in keeps its values (-E) so the resolver's
-- first-by-Id order is the production one.
IF OBJECT_ID(N'dbo.L_P_CONSIGNES_DEGAZAGE_DETAIL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_DEGAZAGE_DETAIL
    (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Code] INT NOT NULL,
        [ProfilProduit] NVARCHAR(3) NOT NULL,
        [SectionMax] DECIMAL(4,1) NULL,
        [SectionMin] DECIMAL(4,1) NULL,
        [Refroidissement] DECIMAL(4,2) NOT NULL,
        [H21] DECIMAL(4,2) NOT NULL,
        [H22] DECIMAL(4,2) NOT NULL,
        [H23] DECIMAL(4,2) NOT NULL,
        [H24] DECIMAL(4,2) NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_DEGAZAGE_DETAIL PRIMARY KEY CLUSTERED ([Id])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_DEGAZAGE_GLOBAL', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_DEGAZAGE_GLOBAL
    (
        [Code] INT NOT NULL,
        [Tableau] NVARCHAR(10) NOT NULL,
        [StandardM] NVARCHAR(MAX) NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_DEGAZAGE_GLOBAL PRIMARY KEY CLUSTERED ([Code])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_LINGOT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_LINGOT
    (
        [Section] NVARCHAR(4) NOT NULL,
        [Consignes] INT NOT NULL,
        [CodeConsigne] NVARCHAR(12) NOT NULL,
        [Libelle] NVARCHAR(MAX) NOT NULL,
        [DateMaj] DATETIME NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_LINGOT PRIMARY KEY CLUSTERED ([CodeConsigne], [Section], [Consignes])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_MARQUAGE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_MARQUAGE
    (
        [Section] NVARCHAR(4) NOT NULL,
        [Consignes] INT NOT NULL,
        [CodeConsigne] NVARCHAR(12) NOT NULL,
        [DateMaj] DATETIME NOT NULL,
        [Type] NVARCHAR(MAX) NOT NULL,
        [Libelle_Section] NVARCHAR(MAX) NOT NULL,
        [Libelle_Tete] NVARCHAR(MAX) NOT NULL,
        [Libelle_Pied] NVARCHAR(MAX) NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_MARQUAGE PRIMARY KEY CLUSTERED ([Section], [Consignes], [CodeConsigne])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_PITS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_PITS
    (
        [Section] NVARCHAR(4) NOT NULL,
        [Consignes] INT NOT NULL,
        [CodeConsigne] NVARCHAR(12) NOT NULL,
        [Libelle] NVARCHAR(MAX) NOT NULL,
        [DateMaj] DATETIME NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_PITS PRIMARY KEY CLUSTERED ([CodeConsigne], [Section], [Consignes])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_POIDSMETRIQUE', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_POIDSMETRIQUE
    (
        [Section] NVARCHAR(4) NOT NULL,
        [Consignes] INT NOT NULL,
        [CodeConsigne] NVARCHAR(12) NOT NULL,
        [Libelle] NVARCHAR(MAX) NOT NULL,
        [DateMaj] DATETIME NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_POIDSMETRIQUE PRIMARY KEY CLUSTERED ([CodeConsigne], [Consignes], [Section])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_PRECHAUFFAGE_PARTICULIER', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_PRECHAUFFAGE_PARTICULIER
    (
        [Code] INT NOT NULL,
        [Libelle] NVARCHAR(30) NOT NULL,
        [DateMaj] DATETIME NULL,
        CONSTRAINT PK_L_P_CONSIGNES_PRECHAUFFAGE_PARTICULIER PRIMARY KEY CLUSTERED ([Code])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_REFROIDISSEMENT', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_REFROIDISSEMENT
    (
        [Code] NCHAR(2) NOT NULL,
        [Libelle] NVARCHAR(100) NOT NULL,
        [DateMaj] DATETIME NULL,
        CONSTRAINT PK_L_P_CONSIGNES_REFROIDISSEMENT PRIMARY KEY CLUSTERED ([Code])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_REFROIDISSOIRS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_REFROIDISSOIRS
    (
        [Section] NVARCHAR(4) NOT NULL,
        [Consignes] INT NOT NULL,
        [CodeConsigne] NVARCHAR(12) NOT NULL,
        [Libelle] NVARCHAR(MAX) NOT NULL,
        [DateMaj] DATETIME NOT NULL,
        CONSTRAINT PK_L_P_CONSIGNES_REFROIDISSOIRS PRIMARY KEY CLUSTERED ([CodeConsigne], [Section], [Consignes])
    );
END;
GO

IF OBJECT_ID(N'dbo.L_P_CONSIGNES_SMQ', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.L_P_CONSIGNES_SMQ
    (
        [Code] NCHAR(3) NOT NULL,
        [Libelle] NVARCHAR(100) NOT NULL,
        [DateMaj] DATETIME NULL,
        CONSTRAINT PK_L_P_CONSIGNES_SMQ PRIMARY KEY CLUSTERED ([Code])
    );
END;
GO
