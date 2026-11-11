/* =========================================================
   BRANDED LETTER TEMPLATES
   Versioned per document type (matches the payload's DocumentDescription, e.g. "Welcome Kit").
   Draft -> Published -> Retired; at most one Published version per document type. When a request's
   document type has a Published template, its PDF is that letter; otherwise the data sheet.
   Placeholders: {{RecipientName}}, {{ClaimNumber}}, ... or {{payload.Some.Path}} (see the portal's list).
   Re-runnable. The two seeded templates are DRAFTS with sample wording: review before publishing.
   ========================================================= */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.LetterTemplate', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LetterTemplate (
        TemplateId      INT IDENTITY(1,1) NOT NULL,
        DocumentType    NVARCHAR(100)     NOT NULL,
        Version         INT               NOT NULL,
        Status          VARCHAR(10)       NOT NULL CONSTRAINT DF_LetterTemplate_Status DEFAULT 'Draft',
        Name            NVARCHAR(200)     NOT NULL,
        Subject         NVARCHAR(300)     NOT NULL,
        Salutation      NVARCHAR(200)     NOT NULL CONSTRAINT DF_LetterTemplate_Salutation DEFAULT N'Dear {{RecipientName}},',
        Body            NVARCHAR(MAX)     NOT NULL,
        Closing         NVARCHAR(200)     NOT NULL CONSTRAINT DF_LetterTemplate_Closing DEFAULT N'Sincerely,',
        SignatureName   NVARCHAR(200)     NULL,
        SignatureTitle  NVARCHAR(200)     NULL,
        CreatedUtc      DATETIME2         NOT NULL CONSTRAINT DF_LetterTemplate_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       NVARCHAR(100)     NULL,
        UpdatedUtc      DATETIME2         NOT NULL CONSTRAINT DF_LetterTemplate_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        PublishedUtc    DATETIME2         NULL,
        PublishedBy     NVARCHAR(100)     NULL,
        CONSTRAINT PK_LetterTemplate PRIMARY KEY (TemplateId),
        CONSTRAINT UQ_LetterTemplate_Version UNIQUE (DocumentType, Version),
        CONSTRAINT CK_LetterTemplate_Status CHECK (Status IN ('Draft', 'Published', 'Retired'))
    );
    CREATE UNIQUE INDEX UX_LetterTemplate_Published ON dbo.LetterTemplate (DocumentType) WHERE Status = 'Published';
    PRINT 'Created table: dbo.LetterTemplate';
END
GO

/* Which template version produced each stored PDF (NULL = data sheet). */
IF OBJECT_ID(N'dbo.CorrespondencePdfDocument', N'U') IS NOT NULL AND COL_LENGTH('dbo.CorrespondencePdfDocument', 'TemplateId') IS NULL
BEGIN
    ALTER TABLE dbo.CorrespondencePdfDocument ADD TemplateId INT NULL, TemplateVersion INT NULL;
    PRINT 'Added template columns to dbo.CorrespondencePdfDocument';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.LetterTemplate WHERE DocumentType = N'Welcome Kit')
    INSERT dbo.LetterTemplate (DocumentType, Version, Status, Name, Subject, Body, SignatureName, SignatureTitle, CreatedBy)
    VALUES (N'Welcome Kit', 1, 'Draft', N'Welcome Kit letter', N'Welcome — your policy information',
N'Thank you for choosing {{CompanyName}}. This letter confirms that we have set up your account and that your welcome kit is on its way.

Your reference numbers are below. Please keep them for your records and quote them whenever you contact us.

Claim number: {{ClaimNumber}}
Policy number: {{PolicyNumber}}

If any of the details in this letter are incorrect, or you have questions about your coverage, call us at {{CompanyPhone}}.',
     N'Customer Service Team', N'{{CompanyName}}', N'seed (review before publishing)');
GO

IF NOT EXISTS (SELECT 1 FROM dbo.LetterTemplate WHERE DocumentType = N'Claim Update')
    INSERT dbo.LetterTemplate (DocumentType, Version, Status, Name, Subject, Body, SignatureName, SignatureTitle, CreatedBy)
    VALUES (N'Claim Update', 1, 'Draft', N'Claim update letter', N'An update on your claim {{ClaimNumber}}',
N'We are writing with an update on your claim {{ClaimNumber}}.

Our team has reviewed the information received so far. There is nothing you need to do right now; we will contact you if we need anything further.

If you have questions about your claim, call us at {{CompanyPhone}} and quote your claim number.',
     N'Claims Team', N'{{CompanyName}}', N'seed (review before publishing)');
GO

PRINT 'Letter templates ready.';
GO
