-- Minimal test schema for the MQTTnetServices logging tables.
-- Provenance: scripted from the live database MQTTnetServices on server
-- AFV004-LSI (Microsoft SQL Server 2012, Enterprise Edition), read via sqlcmd
-- against INFORMATION_SCHEMA.COLUMNS and sys.identity_columns on 2026-09-04.
-- AscoLSI and MQTTnetServices are on the same SQL Server instance, reached
-- with distinct connection strings.
-- This file is generated, not written from memory (risk R-3).
-- Scope: only dbo.Logs, the one table the integration tests touch (Story 3.3
-- round-trip). The target database is chosen by the test fixture connection
-- string (for example MQTTnetServices_Test); this script does not issue USE.
-- dbo.WorkerSettings is intentionally not created here: it is owned and
-- auto-created by the portal Launcher (MicroServices.sln), and the importer
-- (a library since the 2026-09-09 correction of course) never touches it.
-- Logs matches the Serilog Serilog.Sinks.MSSqlServer layout used by the other
-- portal workers; every text column is NVARCHAR(MAX) in the source.

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Serilog sink table.
IF OBJECT_ID(N'dbo.Logs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Logs
    (
        [Id] INT IDENTITY(1,1) NOT NULL,
        [Message] NVARCHAR(MAX) NULL,
        [MessageTemplate] NVARCHAR(MAX) NULL,
        [Level] NVARCHAR(MAX) NULL,
        [TimeStamp] DATETIME NULL,
        [Exception] NVARCHAR(MAX) NULL,
        [Properties] NVARCHAR(MAX) NULL,
        CONSTRAINT PK_Logs PRIMARY KEY CLUSTERED ([Id])
    );
END;
GO
