
/* =========================================================
   ECCO COMMUNICATION MANAGER - DATABASE SCHEMA
   ========================================================= */

PRINT '========================================================';
PRINT 'ECCO Communication Manager - Database Schema Setup';
PRINT 'Date: ' + CONVERT(VARCHAR(10), GETDATE(), 120);
PRINT '========================================================';
GO

/* =========================================================
   Table: CorrespondenceDeliveryMethod
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CorrespondenceDeliveryMethod]') AND type IN (N'U'))
BEGIN
    CREATE TABLE CorrespondenceDeliveryMethod (
        DeliveryMethodId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        DeliveryMethodCode VARCHAR(50) NOT NULL UNIQUE,
        DeliveryMethodName VARCHAR(100) NOT NULL
    );
    PRINT 'Created table: CorrespondenceDeliveryMethod';
END
ELSE
BEGIN
    PRINT 'Table CorrespondenceDeliveryMethod already exists.';
END
GO

/* =========================================================
   Table: CorrespondenceStatusCode
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CorrespondenceStatusCode]') AND type IN (N'U'))
BEGIN
    CREATE TABLE CorrespondenceStatusCode (
        StatusId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        StatusCode VARCHAR(50) NOT NULL UNIQUE,
        StatusDescription VARCHAR(255)
    );
    PRINT 'Created table: CorrespondenceStatusCode';
END
ELSE
BEGIN
    PRINT 'Table CorrespondenceStatusCode already exists.';
END
GO

/* =========================================================
   Table: CorrespondenceRequest
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CorrespondenceRequest]') AND type IN (N'U'))
BEGIN
    CREATE TABLE CorrespondenceRequest (
        CorrespondenceId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        DocId varchar(19) NULL,
        RequestingDomainName VARCHAR(100) NULL,
        DeliveryMethodCode VARCHAR(50) NOT NULL,
        VersionCode VARCHAR(50) NULL,
        ArchiveDocumentTypeCode VARCHAR(50) NULL,
        ArchiveDocumentSubTypeCode VARCHAR(50) NULL,
        PolicyNumber VARBINARY(MAX) NULL,
        ClaimNumber VARCHAR(50) NULL,
        CustomerId VARCHAR(50) NULL,
        RequestPayload VARBINARY(MAX) NULL,
        RequestCreationTS DATETIME NULL,
        PolicyNumberHash VARBINARY(32) NULL,
        FOREIGN KEY (DeliveryMethodCode) REFERENCES CorrespondenceDeliveryMethod(DeliveryMethodCode)
    );
    PRINT 'Created table: CorrespondenceRequest';
END
ELSE
BEGIN
    PRINT 'Table CorrespondenceRequest already exists.';
END
GO

/* =========================================================
   Table: CorrespondenceRequestStatusHistory
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CorrespondenceRequestStatusHistory]') AND type IN (N'U'))
BEGIN
    CREATE TABLE CorrespondenceRequestStatusHistory (
        CorrespondenceRequestStatusHistoryId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        CorrespondenceId UNIQUEIDENTIFIER NOT NULL,
        HistorySequenceNum INT NOT NULL,
        StatusCode VARCHAR(50) NOT NULL,
        StatusUpdatedBy VARCHAR(100) NOT NULL,
        StatusUpdateTS DATETIME DEFAULT GETDATE(),
        FOREIGN KEY (CorrespondenceId) REFERENCES CorrespondenceRequest(CorrespondenceId),
        FOREIGN KEY (StatusCode) REFERENCES CorrespondenceStatusCode(StatusCode)
    );
    PRINT 'Created table: CorrespondenceRequestStatusHistory';
END
ELSE
BEGIN
    PRINT 'Table CorrespondenceRequestStatusHistory already exists.';
END
GO

/* =========================================================
   Table: RoutingConfigurations
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RoutingConfigurations]') AND type IN (N'U'))
BEGIN
    CREATE TABLE [dbo].[RoutingConfigurations] (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        RouteName NVARCHAR(100) NOT NULL,
        CustomerName NVARCHAR(100) NOT NULL,
        CorrespondenceMethod NVARCHAR(50) NOT NULL,
        Route NVARCHAR(MAX) NOT NULL
    );
    PRINT 'Created table: RoutingConfigurations';
END
ELSE
BEGIN
    PRINT 'Table RoutingConfigurations already exists.';
END
GO

/* =========================================================
   Table: NonRecoverableErrorLogs
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[NonRecoverableErrorLogs]') AND type IN (N'U'))
BEGIN
    CREATE TABLE NonRecoverableErrorLogs (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        NonRecoverableErrorLogId varchar(50) NOT NULL,
        ErrorMessage NVARCHAR(MAX) NOT NULL,
        StackTrace NVARCHAR(MAX) NULL,
        ErrorTimestamp DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    PRINT 'Created table: NonRecoverableErrorLogs';
END
ELSE
BEGIN
    PRINT 'Table NonRecoverableErrorLogs already exists.';
END
GO

/* =========================================================
   Table: ProcessRequestEndPoint_OutboxData
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ProcessRequestEndPoint_OutboxData]') AND type IN (N'U'))
BEGIN
    CREATE TABLE [dbo].[ProcessRequestEndPoint_OutboxData] (
        [MessageId] NVARCHAR(200) NOT NULL,
        [Operations] VARBINARY(MAX) NOT NULL,
        [PersistenceVersion] NVARCHAR(23) NOT NULL,
        CONSTRAINT [PK_ProcessRequestEndPoint_OutboxData] PRIMARY KEY ([MessageId])
    );
    PRINT 'Created table: ProcessRequestEndPoint_OutboxData';
END
ELSE
BEGIN
    PRINT 'Table ProcessRequestEndPoint_OutboxData already exists.';
END
GO

/* =========================================================
   Table: ProcessRequestEndPoint_OutboxOperations
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ProcessRequestEndPoint_OutboxOperations]') AND type IN (N'U'))
BEGIN
    CREATE TABLE [dbo].[ProcessRequestEndPoint_OutboxOperations] (
        [MessageId] NVARCHAR(200) NOT NULL,
        [OperationId] INT NOT NULL,
        [Destination] NVARCHAR(200) NOT NULL,
        [Headers] VARBINARY(MAX) NULL,
        [Body] VARBINARY(MAX) NULL,
        CONSTRAINT [PK_ProcessRequestEndPoint_OutboxOperations] PRIMARY KEY ([MessageId], [OperationId])
    );
    PRINT 'Created table: ProcessRequestEndPoint_OutboxOperations';
END
ELSE
BEGIN
    PRINT 'Table ProcessRequestEndPoint_OutboxOperations already exists.';
END
GO

/* =========================================================
   Table: DocumentIds
   ========================================================= */
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DocumentIds]') AND type IN (N'U'))
BEGIN
    CREATE TABLE DocumentIds (
        SeqId INT IDENTITY(1,1) PRIMARY KEY,
        DocIds varchar(19) NOT NULL,
        DocIdStatus varchar(20) NOT NULL,
        RequestCreationTS DATETIME NULL
    );
    PRINT 'Created table: DocumentIds';
END
ELSE
BEGIN
    PRINT 'Table DocumentIds already exists.';
END
GO

/* =========================================================
   SEED DATA - Only insert if not already exists
   ========================================================= */

-- Seed RoutingConfigurations
IF NOT EXISTS (SELECT 1 FROM [dbo].[RoutingConfigurations] WHERE RouteName = 'PGARoute')
BEGIN
    INSERT [dbo].[RoutingConfigurations] ([RouteName], [CustomerName], [CorrespondenceMethod], [Route]) 
    VALUES (N'PGARoute', N'Payment Guard Advantage', N'MAIL', N'BeginRequestHandler;AssignDocumentumIdHandler;AddCorrespondenceSBusHandler;AddCorrespondenceDBHandler;RequestCompositionHandler;UpdateCorrespondenceDBHandler');
    INSERT [dbo].[RoutingConfigurations] ([RouteName], [CustomerName], [CorrespondenceMethod], [Route]) 
    VALUES (N'PGARoute2', N'Payment Guard Advantage Test', N'MAIL', N'BeginRequestHandler;AssignDocumentumIdHandler;AddCorrespondenceSBusHandler;AddCorrespondenceDBHandler;RequestCompositionHandler;UpdateCorrespondenceDBHandler');
    INSERT [dbo].[RoutingConfigurations] ([RouteName], [CustomerName], [CorrespondenceMethod], [Route]) 
    VALUES (N'TestRoute', N'TestCustomer', N'MAIL', N'BeginRequestHandler;AssignDocumentumIdHandler;AddCorrespondenceSBusHandler;AddCorrespondenceDBHandler;RequestCompositionHandler;UpdateCorrespondenceDBHandler');
    INSERT [dbo].[RoutingConfigurations] ([RouteName], [CustomerName], [CorrespondenceMethod], [Route]) 
    VALUES (N'TestRoute2', N'TestCustomer2', N'MAIL', N'BeginRequestHandler;AssignDocumentumIdHandler;AddCorrespondenceSBusHandler;AddCorrespondenceDBHandler;RequestCompositionHandler;UpdateCorrespondenceDBHandler');
    PRINT 'Seed data inserted: RoutingConfigurations';
END
ELSE
BEGIN
    PRINT 'RoutingConfigurations seed data already exists. Skipping insert.';
END
GO

-- Seed CorrespondenceDeliveryMethod
IF NOT EXISTS (SELECT 1 FROM [dbo].[CorrespondenceDeliveryMethod] WHERE DeliveryMethodCode = '01')
BEGIN
    INSERT [dbo].[CorrespondenceDeliveryMethod] ([DeliveryMethodId], [DeliveryMethodCode], [DeliveryMethodName]) 
    VALUES (N'e6fab6db-b1a7-4ed5-8d30-5775eede0f78', N'01', N'Mail');
    INSERT [dbo].[CorrespondenceDeliveryMethod] ([DeliveryMethodId], [DeliveryMethodCode], [DeliveryMethodName]) 
    VALUES (N'09904935-f2ea-46d1-89c3-74ad53215680', N'02', N'Email');
    INSERT [dbo].[CorrespondenceDeliveryMethod] ([DeliveryMethodId], [DeliveryMethodCode], [DeliveryMethodName]) 
    VALUES (N'6396b924-75ca-4d18-8a10-57c238a9e063', N'03', N'ePresentment');
    INSERT [dbo].[CorrespondenceDeliveryMethod] ([DeliveryMethodId], [DeliveryMethodCode], [DeliveryMethodName]) 
    VALUES (N'21f62e3e-3794-4b10-8c41-1a99487fbb46', N'04', N'Fax');
    PRINT 'Seed data inserted: CorrespondenceDeliveryMethod';
END
ELSE
BEGIN
    PRINT 'CorrespondenceDeliveryMethod seed data already exists. Skipping insert.';
END
GO

-- Seed CorrespondenceStatusCode
IF NOT EXISTS (SELECT 1 FROM [dbo].[CorrespondenceStatusCode] WHERE StatusCode = '01')
BEGIN
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'da2d43b9-8609-4daa-b8eb-ef5419aae66a', N'01', N'New');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'426e5e71-a2b5-4ad7-8203-aa07a6343c3a', N'02', N'EDeliveryConfirmed');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'c5d46e0f-1292-4eb1-a759-073de4cb6ae6', N'03', N'BouncedToPrint');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'a7bec410-c8b5-4d8a-869c-ab7d556e2ca1', N'04', N'AwaitingImaging');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'2182af95-250f-4d73-8999-2e42f04607e6', N'05', N'SentForImaging');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'cfdd40bd-d22f-42d2-9a89-835966c26c9e', N'06', N'SentForPrint');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'720cb3a4-a8ae-442e-904f-cb4ba1d8acb0', N'07', N'Delivered');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'05330a2e-4b24-498c-9dbc-be1b4e053d8d', N'08', N'Failed');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'eb892372-6b12-4ee3-91e9-7c0d41e481bd', N'09', N'Archived');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'e0172fdf-48dd-4c9c-8f9c-47acc852405f', N'10', N'AwaitingEDelivery');
    INSERT [dbo].[CorrespondenceStatusCode] ([StatusId], [StatusCode], [StatusDescription]) 
    VALUES (N'7c5fba97-93e8-4cd8-b437-b25a4aef62ca', N'11', N'ImagingConfirmed');
    PRINT 'Seed data inserted: CorrespondenceStatusCode';
END
ELSE
BEGIN
    PRINT 'CorrespondenceStatusCode seed data already exists. Skipping insert.';
END
GO

/* =========================================================
   INDEXES FOR FOREIGN KEYS AND PERFORMANCE
   ========================================================= */

-- Index on CorrespondenceRequest for DeliveryMethodCode
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='IX_CorrespondenceRequest_DeliveryMethodCode')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrespondenceRequest_DeliveryMethodCode
    ON CorrespondenceRequest(DeliveryMethodCode);
    PRINT 'Created index: IX_CorrespondenceRequest_DeliveryMethodCode';
END
GO

-- Index on CorrespondenceRequestStatusHistory for CorrespondenceId
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='IX_CorrReqStatusHistory_CorrespondenceId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrReqStatusHistory_CorrespondenceId
    ON CorrespondenceRequestStatusHistory(CorrespondenceId);
    PRINT 'Created index: IX_CorrReqStatusHistory_CorrespondenceId';
END
GO

-- Index on CorrespondenceRequestStatusHistory for StatusCode
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='IX_CorrReqStatusHistory_StatusCode')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrReqStatusHistory_StatusCode
    ON CorrespondenceRequestStatusHistory(StatusCode);
    PRINT 'Created index: IX_CorrReqStatusHistory_StatusCode';
END
GO

-- Composite index for faster history lookups
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='IX_CorrReqStatusHistory_Composite')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrReqStatusHistory_Composite
    ON CorrespondenceRequestStatusHistory(CorrespondenceId, HistorySequenceNum);
    PRINT 'Created index: IX_CorrReqStatusHistory_Composite';
END
GO

-- Unique filtered index on DocId to prevent duplicate DocId assignment
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='UX_CorrespondenceRequest_DocId')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UX_CorrespondenceRequest_DocId
    ON CorrespondenceRequest(DocId)
    WHERE DocId IS NOT NULL;
    PRINT 'Created index: UX_CorrespondenceRequest_DocId';
END
GO

-- Index on NonRecoverableErrorLogId for faster lookups
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_NonRecoverableErrorLogs_NonRecoverableErrorLogId')
BEGIN
    CREATE INDEX IX_NonRecoverableErrorLogs_NonRecoverableErrorLogId ON NonRecoverableErrorLogs(NonRecoverableErrorLogId);
    PRINT 'Created index: IX_NonRecoverableErrorLogs_NonRecoverableErrorLogId';
END
GO

-- Index on ErrorTimestamp for efficient date filtering/sorting
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_NonRecoverableErrorLogs_ErrorTimestamp')
BEGIN
    CREATE INDEX IX_NonRecoverableErrorLogs_ErrorTimestamp ON NonRecoverableErrorLogs(ErrorTimestamp);
    PRINT 'Created index: IX_NonRecoverableErrorLogs_ErrorTimestamp';
END
GO

-- Index on DocumentIds for SeqId
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentIds_SeqId')
BEGIN
    CREATE INDEX IX_DocumentIds_SeqId ON DocumentIds(SeqId);
    PRINT 'Created index: IX_DocumentIds_SeqId';
END
GO

/* =========================================================
   COVERING INDEXES FOR STATUS RETRIEVAL QUERIES
   ========================================================= */

-- CorrespondenceRequest: Lookup by DocId
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CorrespondenceRequest_DocId')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrespondenceRequest_DocId
    ON [dbo].[CorrespondenceRequest] ([DocId])
    INCLUDE ([CorrespondenceId], [RequestingDomainName], [DeliveryMethodCode], [ArchiveDocumentTypeCode], [ClaimNumber], [CustomerId], [RequestCreationTS]);
    PRINT 'Created index: IX_CorrespondenceRequest_DocId';
END
GO

-- CorrespondenceRequest: Lookup by ClaimNumber with pagination (OrderBy RequestCreationTS DESC)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CorrespondenceRequest_ClaimNumber_RequestCreationTS')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrespondenceRequest_ClaimNumber_RequestCreationTS
    ON [dbo].[CorrespondenceRequest] ([ClaimNumber], [RequestCreationTS] DESC)
    INCLUDE ([CorrespondenceId], [DocId], [RequestingDomainName], [DeliveryMethodCode], [ArchiveDocumentTypeCode], [CustomerId]);
    PRINT 'Created index: IX_CorrespondenceRequest_ClaimNumber_RequestCreationTS';
END
GO

-- CorrespondenceRequest: Lookup by ArchiveDocumentTypeCode with RequestCreationTS for pagination
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CorrespondenceRequest_ArchiveDocumentTypeCode_RequestCreationTS')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrespondenceRequest_ArchiveDocumentTypeCode_RequestCreationTS
    ON [dbo].[CorrespondenceRequest] ([ArchiveDocumentTypeCode], [RequestCreationTS] DESC)
    INCLUDE ([CorrespondenceId], [DocId], [RequestingDomainName], [DeliveryMethodCode], [ClaimNumber], [CustomerId]);
    PRINT 'Created index: IX_CorrespondenceRequest_ArchiveDocumentTypeCode_RequestCreationTS';
END
GO

-- CorrespondenceRequestStatusHistory: Batch lookup by CorrespondenceId with SequenceNum (covering)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CorrespondenceRequestStatusHistory_CorrespondenceId_SeqNum')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrespondenceRequestStatusHistory_CorrespondenceId_SeqNum
    ON [dbo].[CorrespondenceRequestStatusHistory] ([CorrespondenceId], [HistorySequenceNum])
    INCLUDE ([StatusCode], [StatusUpdatedBy], [StatusUpdateTS]);
    PRINT 'Created index: IX_CorrespondenceRequestStatusHistory_CorrespondenceId_SeqNum';
END
GO

-- CorrespondenceStatusCode: Lookup by StatusCode including description
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CorrespondenceStatusCode_StatusCode')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrespondenceStatusCode_StatusCode
    ON [dbo].[CorrespondenceStatusCode] ([StatusCode])
    INCLUDE ([StatusDescription]);
    PRINT 'Created index: IX_CorrespondenceStatusCode_StatusCode';
END
GO

-- CorrespondenceDocumentType: Lookup by DocumentType including SubType and CabinetName
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CorrespondenceDocumentType_DocumentType')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrespondenceDocumentType_DocumentType
    ON [dbo].[CorrespondenceDocumentType] ([DocumentType])
    INCLUDE ([DocumentSubType], [CabinetName]);
    PRINT 'Created index: IX_CorrespondenceDocumentType_DocumentType';
END
GO

/* =========================================================
   ADD COLUMNS TO EXISTING TABLES
   ========================================================= */

-- Add SFMCEventInstanceId to CorrespondenceRequestStatusHistory
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.CorrespondenceRequestStatusHistory') 
    AND name = 'SFMCEventInstanceId'
)
BEGIN
    ALTER TABLE [dbo].[CorrespondenceRequestStatusHistory]
    ADD SFMCEventInstanceId NVARCHAR(100) NULL;
    PRINT 'Added column: SFMCEventInstanceId to CorrespondenceRequestStatusHistory';
END
ELSE
BEGIN
    PRINT 'Column SFMCEventInstanceId already exists in CorrespondenceRequestStatusHistory.';
END
GO

-- Add VFResponseId column to CorrespondenceRequest table
IF NOT EXISTS (
    SELECT 1 
    FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.CorrespondenceRequest') 
    AND name = 'VFResponseId'
)
BEGIN
    ALTER TABLE dbo.CorrespondenceRequest
    ADD VFResponseId NVARCHAR(100) NULL;
    
    PRINT 'Column VFResponseId added to CorrespondenceRequest table successfully.';
END
ELSE
BEGIN
    PRINT 'Column VFResponseId already exists in CorrespondenceRequest table.';
END
GO

-- create CorrespondenceDocumentType table
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CorrespondenceDocumentType]') AND type IN (N'U'))
BEGIN
CREATE TABLE [dbo].[CorrespondenceDocumentType] (
    CorrespondenceDocumentTypeId INT IDENTITY(1,1) PRIMARY KEY,
    DocumentType NVARCHAR(100) NOT NULL,
    DocumentSubType NVARCHAR(100) NOT NULL,
    CabinetName NVARCHAR(100) NOT NULL
);
END
GO

-- Create index on DocumentType and DocumentSubType for faster lookups
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CorrespondenceDocumentType_DocumentType_DocumentSubType')
BEGIN
    CREATE NONCLUSTERED INDEX IX_CorrespondenceDocumentType_DocumentType_DocumentSubType 
    ON [dbo].[CorrespondenceDocumentType](DocumentType, DocumentSubType);
END
GO


-- Seed CorrespondenceDocumentType Data
IF NOT EXISTS (SELECT 1 FROM [dbo].[CorrespondenceDocumentType] WHERE DocumentType = 'cmg_sample_a')
BEGIN
    INSERT INTO [dbo].[CorrespondenceDocumentType] 
        ([DocumentType], [DocumentSubType], [CabinetName])
    VALUES 
        ('cmg_sample_a', 'SampleA', 'SampleACabinet'),
        ('cmg_sample_a', 'PGA Welcome Kit', 'SampleACabinet'),
        ('cmg_payg_claims', 'Subtype', 'Payment Guard'),
        ('cmg_payg_policy_form', 'Subtype', 'Payment Guard'),
        ('cmg_payg_reports', 'Subtype', 'Payment Guard'),
        ('cmg_payg_servbill', 'Subtype', 'Payment Guard'),
        ('cmg_payg_claims', 'ASSIGN', 'Payment Guard'),
        ('cmg_payg_claims', 'ATTYLTR', 'Payment Guard'),
        ('cmg_payg_claims', 'AUTHOR', 'Payment Guard'),
        ('cmg_payg_claims', 'BILL', 'Payment Guard'),
        ('cmg_payg_claims', 'CONCERN', 'Payment Guard'),
        ('cmg_payg_claims', 'DENIAL', 'Payment Guard'),
        ('cmg_payg_claims', 'ECONSENT', 'Payment Guard'),
        ('cmg_payg_claims', 'EMAILRECD', 'Payment Guard'),
        ('cmg_payg_claims', 'EMAILSENT', 'Payment Guard'),
        ('cmg_payg_claims', 'EMPRT', 'Payment Guard'),
        ('cmg_payg_claims', 'EOB', 'Payment Guard'),
        ('cmg_payg_claims', 'FAX', 'Payment Guard'),
        ('cmg_payg_claims', 'MEDICAL', 'Payment Guard'),
        ('cmg_payg_claims', 'POA', 'Payment Guard'),
        ('cmg_payg_claims', 'REQU', 'Payment Guard'),
        ('cmg_payg_claims', 'RETU', 'Payment Guard'),
        ('cmg_payg_claims', 'SD', 'Payment Guard'),
        ('cmg_payg_claims', 'SIU', 'Payment Guard'),
        ('cmg_payg_claims', 'SSAWARD', 'Payment Guard'),
        ('cmg_payg_claims', 'TAX', 'Payment Guard'),
        ('cmg_payg_policy_form', 'APP', 'Payment Guard'),
        ('cmg_payg_policy_form', 'COI', 'Payment Guard'),
        ('cmg_payg_policy_form', 'LOI', 'Payment Guard'),
        ('cmg_payg_policy_form', 'PN', 'Payment Guard'),
        ('cmg_payg_policy_form', 'POL', 'Payment Guard'),
        ('cmg_payg_policy_form', 'RETU', 'Payment Guard'),
        ('cmg_payg_policy_form', 'SA', 'Payment Guard'),
        ('cmg_payg_policy_form', 'WEL', 'Payment Guard'),
        ('cmg_payg_reports', 'TBA', 'Payment Guard'),
        ('cmg_payg_servbill', '123ALERT', 'Payment Guard'),
        ('cmg_payg_servbill', '131SECUREACCT', 'Payment Guard'),
        ('cmg_payg_servbill', '21LAPSE', 'Payment Guard'),
        ('cmg_payg_servbill', '42SERVRQT', 'Payment Guard'),
        ('cmg_payg_servbill', '61CANCEL', 'Payment Guard'),
        ('cmg_payg_servbill', '61CANCELFLP', 'Payment Guard'),
        ('cmg_payg_servbill', '96DELETE', 'Payment Guard'),
        ('cmg_payg_servbill', 'ACHAUTH', 'Payment Guard'),
        ('cmg_payg_servbill', 'ACHAUTH64', 'Payment Guard'),
        ('cmg_payg_servbill', 'BILLEMAILRECD', 'Payment Guard'),
        ('cmg_payg_servbill', 'BILLEMAILSENT', 'Payment Guard'),
        ('cmg_payg_servbill', 'BILLNOTICE', 'Payment Guard'),
        ('cmg_payg_servbill', 'BILLRQT', 'Payment Guard'),
        ('cmg_payg_servbill', 'CCAUTH', 'Payment Guard'),
        ('cmg_payg_servbill', 'CHGCONTACT', 'Payment Guard'),
        ('cmg_payg_servbill', 'CONC', 'Payment Guard'),
        ('cmg_payg_servbill', 'ECONSENT', 'Payment Guard'),
        ('cmg_payg_servbill', 'EMAILRECD', 'Payment Guard'),
        ('cmg_payg_servbill', 'EMAILSENT', 'Payment Guard'),
        ('cmg_payg_servbill', 'FAILEDPAY', 'Payment Guard'),
        ('cmg_payg_servbill', 'LEGA', 'Payment Guard'),
        ('cmg_payg_servbill', 'LEGDO', 'Payment Guard'),
        ('cmg_payg_servbill', 'MISC', 'Payment Guard'),
        ('cmg_payg_servbill', 'NOTICECAN', 'Payment Guard'),
        ('cmg_payg_servbill', 'OUTG63', 'Payment Guard'),
        ('cmg_payg_servbill', 'RENEWAL', 'Payment Guard'),
        ('cmg_payg_servbill', 'RETU', 'Payment Guard'),
        ('cmg_payg_servbill', 'TERM', 'Payment Guard');
    
    PRINT 'Seed data inserted: CorrespondenceDocumentType - 62 records inserted';
END
ELSE
BEGIN
    PRINT 'CorrespondenceDocumentType seed data already exists. Skipping insert.';
END
GO

-------------------------------------------------------
-- NewCorrespondenceStatusCode table
-------------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[NewCorrespondenceStatusCode]') AND type IN (N'U'))
BEGIN
CREATE TABLE [dbo].[NewCorrespondenceStatusCode] (
    StatusCode INT NOT NULL,
    StatusName VARCHAR(100) NOT NULL,
    StatusDescription VARCHAR(255) NOT NULL,
    StatusCategory VARCHAR(50) NOT NULL,
    DeliveryMethod VARCHAR(20) NOT NULL,
    IsTerminal BIT NOT NULL,
    IsRetryable BIT NOT NULL,
    IsActive BIT NOT NULL,
    CreatedDate DATETIME2 NOT NULL,
    CONSTRAINT PK_NewCorrespondenceStatusCode PRIMARY KEY (StatusCode),
    CONSTRAINT UQ_NewCorrespondenceStatusCode UNIQUE (StatusName)
);
END
GO

-- Insert records into NewCorrespondenceStatusCode table
IF NOT EXISTS (SELECT 1 FROM [dbo].[NewCorrespondenceStatusCode] WHERE StatusCode = 10)
BEGIN
    INSERT INTO [dbo].[NewCorrespondenceStatusCode] (
        StatusCode, StatusName, StatusDescription, StatusCategory, DeliveryMethod, IsTerminal, IsRetryable, IsActive, CreatedDate
    )
    VALUES
    (10, 'REQUEST_RECEIVED', 'Request received by ECCO', 'REQUEST', 'BOTH', 0, 0, 1, SYSUTCDATETIME()),
    (20, 'VALIDATION_COMPLETE', 'Request validation completed', 'REQUEST', 'BOTH', 0, 0, 1, SYSUTCDATETIME()),
    (30, 'AWAITING_IMAGE', 'Imaging request sent to Venture Fusion', 'DOCUMENT', 'BOTH', 0, 0, 1, SYSUTCDATETIME()),
    (40, 'ARCHIVE_VERIFIED', 'Document verified in Documentum', 'DOCUMENT', 'BOTH', 0, 0, 1, SYSUTCDATETIME()),
    (45, 'ARCHIVE_VERIFICATION_FAILED', 'Document not found in Documentum', 'FAILURE', 'BOTH', 0, 1, 1, SYSUTCDATETIME()),
    (50, 'DOCUMENT_RETRIEVAL_FAILED', 'Document exists but retrieval failed', 'FAILURE', 'BOTH', 0, 1, 1, SYSUTCDATETIME()),
    (60, 'RUNWAY_VALIDATED', 'Runway approved ePresent routing', 'DELIVERY', 'EPRESENT', 0, 0, 1, SYSUTCDATETIME()),
    (65, 'NOTIFICATION_STARTED', 'SFMC Journey initiated', 'DELIVERY', 'EPRESENT', 0, 0, 1, SYSUTCDATETIME()),
    (70, 'DELIVERY_SUCCESS', 'ePresent notification delivered successfully', 'DELIVERY', 'EPRESENT', 0, 0, 1, SYSUTCDATETIME()),
    (75, 'EPRESENT_DELIVERY_FAILED', 'ePresent notification failed', 'FAILURE', 'EPRESENT', 0, 1, 1, SYSUTCDATETIME()),
    (80, 'BOUNCE_TO_PRINT', 'ePresent failure routed to Mail delivery', 'DELIVERY', 'BOTH', 0, 0, 1, SYSUTCDATETIME()),
    (85, 'DELIVERY_METHOD_SWITCHED_TO_MAIL', 'Delivery method switched to Mail', 'DELIVERY', 'BOTH', 0, 0, 1, SYSUTCDATETIME()),
    (90, 'MAIL_FULFILLMENT_REQUESTED', 'Mail fulfillment requested from Venture Fusion', 'DELIVERY', 'MAIL', 0, 0, 1, SYSUTCDATETIME()),
    (95, 'MAIL_DELIVERY_SENT', 'Mail handed off for delivery', 'DELIVERY', 'MAIL', 0, 0, 1, SYSUTCDATETIME()),
    (100, 'MAIL_FULFILLMENT_FAILED', 'Mail fulfillment failed', 'FAILURE', 'MAIL', 0, 1, 1, SYSUTCDATETIME()),
    (110, 'AZURE_FUNCTION_RETRY_STARTED', 'Azure Function retry started', 'TECHNICAL', 'BOTH', 0, 1, 1, SYSUTCDATETIME()),
    (115, 'AZURE_FUNCTION_RETRY_EXHAUSTED', 'Azure Function retries exhausted', 'TECHNICAL', 'BOTH', 0, 0, 1, SYSUTCDATETIME()),
    (120, 'AZURE_FUNCTION_FAILED', 'Azure Function failed permanently', 'TECHNICAL', 'BOTH', 1, 0, 1, SYSUTCDATETIME()),
    (130, 'REQUEST_COMPLETE', 'Request completed successfully', 'TERMINAL', 'BOTH', 1, 0, 1, SYSUTCDATETIME()),
    (140, 'REQUEST_TERMINATED', 'Request terminated', 'TERMINAL', 'BOTH', 1, 0, 1, SYSUTCDATETIME());
END
GO

-- Create index on StatusCategory for faster filtering
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_NewCorrespondenceStatusCode_StatusCategory')
BEGIN
    CREATE NONCLUSTERED INDEX IX_NewCorrespondenceStatusCode_StatusCategory
    ON NewCorrespondenceStatusCode(StatusCategory);
END
GO

-- Create index on DeliveryMethod for faster filtering
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_NewCorrespondenceStatusCode_DeliveryMethod')
BEGIN
    CREATE NONCLUSTERED INDEX IX_NewCorrespondenceStatusCode_DeliveryMethod
    ON NewCorrespondenceStatusCode(DeliveryMethod);
END
GO

-- Seed Annuity Correspondence Route in RoutingConfigurations Table
IF NOT EXISTS (SELECT 1 FROM [dbo].[RoutingConfigurations] WHERE CustomerName = N'Annuity Correspondence')
BEGIN
    INSERT [dbo].[RoutingConfigurations] ([RouteName], [CustomerName], [CorrespondenceMethod], [Route]) 
    VALUES (N'AnnuityRoute', N'Annuity Correspondence', N'MAIL', N'BeginRequestHandler;AssignDocumentumIdHandler;AddCorrespondenceSBusHandler;AddCorrespondenceDBHandler;RequestCompositionHandler;UpdateCorrespondenceDBHandler');
    PRINT 'Seed data inserted: RoutingConfigurations - Annuity Correspondence';
END
ELSE
BEGIN
    PRINT 'RoutingConfigurations for Annuity Correspondence already exists. Skipping insert.';
END
GO

PRINT '';
PRINT '========================================================';
PRINT 'Database schema setup completed successfully!';
PRINT 'All tables, indexes, and seed data ready.';
PRINT '========================================================';
GO

/* =========================================================
/* -- EXAMPLE: Add new Column to Existing Table
IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.CorrespondenceRequest') 
    AND name = 'MyNewColumn1'
)
BEGIN
    ALTER TABLE [dbo].[CorrespondenceRequest]
    ADD MyNewColumn1 NVARCHAR(100) NULL;
    PRINT 'Added column: MyNewColumn1 to CorrespondenceRequest';
END
ELSE
BEGIN
    PRINT 'Column MyNewColumn1 already exists in CorrespondenceRequest.';
END
GO */

/* -- EXAMPLE: Add New Table
IF NOT EXISTS (
    SELECT * FROM sys.objects 
    WHERE object_id = OBJECT_ID(N'[dbo].[MyNewTable]') 
    AND type IN (N'U')
)
BEGIN
    CREATE TABLE [dbo].[MyNewTable] (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),--column1
        Name NVARCHAR(100) NOT NULL,---column2
        CreatedDate DATETIME2 NOT NULL DEFAULT GETUTCDATE(),---column3
        Status NVARCHAR(50) NULL---column4
    );
    PRINT 'Created table: MyNewTable';
END
ELSE
BEGIN
    PRINT 'Table MyNewTable already exists.';
END
GO */

/* -- EXAMPLE: Add Index on New Table
IF NOT EXISTS (
    SELECT * FROM sys.indexes 
    WHERE name = 'IX_MyNewTable_Status'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_MyNewTable_Status
    ON [dbo].[MyNewTable](Status)
    WHERE Status IS NOT NULL;
    PRINT 'Created index: IX_MyNewTable_Status';
END
GO */

/* -- EXAMPLE: Add Multiple Rows at Once
-- This example shows how to add multiple delivery methods in a single INSERT statement
-- More efficient than separate INSERT statements
IF NOT EXISTS (SELECT 1 FROM [dbo].[CorrespondenceDeliveryMethod] WHERE DeliveryMethodCode = '05')
BEGIN
    INSERT [dbo].[CorrespondenceDeliveryMethod] ([DeliveryMethodId], [DeliveryMethodCode], [DeliveryMethodName]) 
    VALUES 
        (NEWID(), N'05', N'SMS'),
        (NEWID(), N'06', N'Push Notification'),
        (NEWID(), N'07', N'Webhook');
    PRINT 'Seed data inserted: Multiple new Delivery Methods';
END
ELSE
BEGIN
    PRINT 'Delivery Method with code 05 already exists. Skipping insert.';
END
GO */

========================================================= */

 