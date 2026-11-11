-- AI Assistant chat history schema
-- Run manually against the CustomerCorrespondence database (no EF migrations exist in this project).

CREATE TABLE dbo.AiChatConversation (
    ConversationId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_AiChatConversation_ConversationId DEFAULT NEWID(),
    UserId          NVARCHAR(200)    NOT NULL,
    Title           NVARCHAR(200)    NULL,
    CreatedTS       DATETIME2        NOT NULL CONSTRAINT DF_AiChatConversation_CreatedTS DEFAULT GETUTCDATE(),
    UpdatedTS       DATETIME2        NOT NULL CONSTRAINT DF_AiChatConversation_UpdatedTS DEFAULT GETUTCDATE(),
    CONSTRAINT PK_AiChatConversation PRIMARY KEY (ConversationId)
);

CREATE INDEX IX_AiChatConversation_UserId ON dbo.AiChatConversation (UserId);

CREATE TABLE dbo.AiChatMessage (
    MessageId       UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_AiChatMessage_MessageId DEFAULT NEWID(),
    ConversationId  UNIQUEIDENTIFIER NOT NULL,
    Role            NVARCHAR(20)     NOT NULL,
    Content         NVARCHAR(MAX)    NOT NULL,
    CreatedTS       DATETIME2        NOT NULL CONSTRAINT DF_AiChatMessage_CreatedTS DEFAULT GETUTCDATE(),
    CONSTRAINT PK_AiChatMessage PRIMARY KEY (MessageId),
    CONSTRAINT FK_AiChatMessage_AiChatConversation FOREIGN KEY (ConversationId)
        REFERENCES dbo.AiChatConversation (ConversationId) ON DELETE CASCADE
);

CREATE INDEX IX_AiChatMessage_ConversationId ON dbo.AiChatMessage (ConversationId);
