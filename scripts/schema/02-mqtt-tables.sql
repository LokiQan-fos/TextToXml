-- Minimal test schema for the MQTTnetServices logging tables.
-- Provenance: scripted from the live database MQTTnetServices on server
-- AFV004-LSI (Microsoft SQL Server 2012, Enterprise Edition), read via sqlcmd
-- against INFORMATION_SCHEMA.COLUMNS and sys.identity_columns on 2026-09-04.
-- AscoLSI and MQTTnetServices are on the same SQL Server instance, reached
-- with distinct connection strings.
-- This file is generated, not written from memory (risk R-3).
-- Scope: only the tables the integration tests touch. The target database is
-- chosen by the test fixture connection string (for example
-- MQTTnetServices_Test); this script does not issue USE.
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

-- Launcher registration table. The primary key is WorkerName; there is no Id.
IF OBJECT_ID(N'dbo.WorkerSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkerSettings
    (
        [WorkerName] NVARCHAR(100) NOT NULL,
        [IsActive] BIT NOT NULL,
        CONSTRAINT PK_WorkerSettings PRIMARY KEY CLUSTERED ([WorkerName])
    );
END;
GO
