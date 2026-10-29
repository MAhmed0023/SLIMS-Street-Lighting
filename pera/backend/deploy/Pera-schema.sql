IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [AppSettings] (
        [Key] nvarchar(64) NOT NULL,
        [Json] nvarchar(max) NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [At] datetime2 NOT NULL,
        [Kind] nvarchar(16) NOT NULL,
        [Action] nvarchar(64) NOT NULL,
        [Result] nvarchar(12) NOT NULL,
        [EntityType] nvarchar(24) NULL,
        [EntityId] nvarchar(64) NULL,
        [ActorId] nvarchar(160) NULL,
        [ActorName] nvarchar(120) NULL,
        [ActingForId] nvarchar(48) NULL,
        [Role] nvarchar(40) NULL,
        [PrevJson] nvarchar(max) NULL,
        [NextJson] nvarchar(max) NULL,
        [Reason] nvarchar(max) NULL,
        [CorrelationId] nvarchar(64) NULL,
        [UserAgent] nvarchar(160) NULL,
        [Ip] nvarchar(64) NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Designations] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(24) NOT NULL,
        [Title] nvarchar(120) NOT NULL,
        [Rank] int NOT NULL,
        [Active] bit NOT NULL,
        CONSTRAINT [PK_Designations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [DocSequences] (
        [Key] nvarchar(64) NOT NULL,
        [Next] int NOT NULL,
        CONSTRAINT [PK_DocSequences] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [DocumentTypes] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(16) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [AllowedExtensions] nvarchar(200) NOT NULL,
        [MaxSizeMb] int NOT NULL,
        [RequireAttachment] bit NOT NULL,
        [EnforceAuthority] bit NOT NULL,
        [AllowSelfApproval] bit NOT NULL,
        [AllowParallel] bit NOT NULL,
        [AmendmentNeedsApproval] bit NOT NULL,
        [RequireApprovalStage] bit NOT NULL,
        [ApprovalCommentRequired] bit NOT NULL,
        [DefaultClassification] nvarchar(24) NOT NULL,
        [StageServiceHours] int NOT NULL,
        [DefaultTemplateId] int NULL,
        [Active] bit NOT NULL,
        CONSTRAINT [PK_DocumentTypes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [EscalationLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [At] datetime2 NOT NULL,
        [EntityType] nvarchar(12) NOT NULL,
        [EntityId] nvarchar(24) NOT NULL,
        [Reference] nvarchar(200) NOT NULL,
        [Level] int NOT NULL,
        [Recipients] nvarchar(400) NOT NULL,
        [Reason] nvarchar(600) NOT NULL,
        CONSTRAINT [PK_EscalationLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Files] (
        [Id] uniqueidentifier NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [ContentType] nvarchar(120) NOT NULL,
        [Size] bigint NOT NULL,
        [Sha256] nvarchar(64) NOT NULL,
        [Path] nvarchar(400) NOT NULL,
        [UploadedById] nvarchar(48) NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Files] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] bigint NOT NULL IDENTITY,
        [UserId] nvarchar(48) NOT NULL,
        [At] datetime2 NOT NULL,
        [Kind] nvarchar(24) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Body] nvarchar(1000) NULL,
        [EntityType] nvarchar(24) NULL,
        [EntityId] nvarchar(24) NULL,
        [Channel] nvarchar(12) NOT NULL,
        [ReadAt] datetime2 NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [OrgUnits] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(24) NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Kind] nvarchar(12) NOT NULL,
        [ParentId] int NULL,
        [HeadUserId] nvarchar(48) NULL,
        [EffectiveFrom] datetime2 NOT NULL,
        [EffectiveTo] datetime2 NULL,
        [Active] bit NOT NULL,
        CONSTRAINT [PK_OrgUnits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrgUnits_OrgUnits_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [OrgUnits] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Roles] (
        [Key] nvarchar(40) NOT NULL,
        [Label] nvarchar(80) NOT NULL,
        [Perms] nvarchar(300) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [TaskCategories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(80) NOT NULL,
        [Active] bit NOT NULL,
        CONSTRAINT [PK_TaskCategories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [AuthorityRules] (
        [Id] int NOT NULL IDENTITY,
        [DocumentTypeId] int NULL,
        [DesignationId] int NOT NULL,
        [Authority] nvarchar(24) NOT NULL,
        [EffectiveFrom] datetime2 NOT NULL,
        [EffectiveTo] datetime2 NULL,
        [Active] bit NOT NULL,
        [Note] nvarchar(400) NULL,
        CONSTRAINT [PK_AuthorityRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuthorityRules_Designations_DesignationId] FOREIGN KEY ([DesignationId]) REFERENCES [Designations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AuthorityRules_DocumentTypes_DocumentTypeId] FOREIGN KEY ([DocumentTypeId]) REFERENCES [DocumentTypes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [WorkflowTemplates] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(24) NOT NULL,
        [Name] nvarchar(160) NOT NULL,
        [Version] int NOT NULL,
        [DocumentTypeId] int NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [EffectiveFrom] datetime2 NOT NULL,
        [EffectiveTo] datetime2 NULL,
        [OwnerId] nvarchar(48) NOT NULL,
        [AllowCustomization] bit NOT NULL,
        CONSTRAINT [PK_WorkflowTemplates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowTemplates_DocumentTypes_DocumentTypeId] FOREIGN KEY ([DocumentTypeId]) REFERENCES [DocumentTypes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] nvarchar(48) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Email] nvarchar(160) NULL,
        [EmployeeId] nvarchar(40) NULL,
        [Phone] nvarchar(40) NULL,
        [RoleKey] nvarchar(40) NOT NULL,
        [DesignationId] int NULL,
        [OrgUnitId] int NULL,
        [ReportsToId] nvarchar(48) NULL,
        [Active] bit NOT NULL,
        [PasswordHash] nvarchar(64) NULL,
        [PasswordSalt] nvarchar(32) NULL,
        [PasswordSetAt] datetime2 NULL,
        [MustChangePassword] bit NOT NULL,
        [FailedAttempts] int NOT NULL,
        [LockedUntil] datetime2 NULL,
        [LastLogin] datetime2 NULL,
        [RefreshTokenHash] nvarchar(64) NULL,
        [RefreshTokenExpires] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Users_Designations_DesignationId] FOREIGN KEY ([DesignationId]) REFERENCES [Designations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Users_OrgUnits_OrgUnitId] FOREIGN KEY ([OrgUnitId]) REFERENCES [OrgUnits] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Users_Roles_RoleKey] FOREIGN KEY ([RoleKey]) REFERENCES [Roles] ([Key]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Users_Users_ReportsToId] FOREIGN KEY ([ReportsToId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [WorkflowTemplateStages] (
        [Id] int NOT NULL IDENTITY,
        [TemplateId] int NOT NULL,
        [Step] int NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Type] nvarchar(24) NOT NULL,
        [Mandatory] bit NOT NULL,
        [AssigneeMode] nvarchar(24) NOT NULL,
        [UserId] nvarchar(48) NULL,
        [DesignationId] int NULL,
        [ServiceHours] int NULL,
        [InitiatorMayChangeAssignee] bit NOT NULL,
        CONSTRAINT [PK_WorkflowTemplateStages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowTemplateStages_WorkflowTemplates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [WorkflowTemplates] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Delegations] (
        [Id] int NOT NULL IDENTITY,
        [FromUserId] nvarchar(48) NOT NULL,
        [ToUserId] nvarchar(48) NOT NULL,
        [Scope] nvarchar(24) NOT NULL,
        [DocumentTypeId] int NULL,
        [StartsOn] datetime2 NOT NULL,
        [EndsOn] datetime2 NOT NULL,
        [Reason] nvarchar(400) NOT NULL,
        [CreatedById] nvarchar(48) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [RevokedAt] datetime2 NULL,
        [RevokedById] nvarchar(48) NULL,
        CONSTRAINT [PK_Delegations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Delegations_Users_FromUserId] FOREIGN KEY ([FromUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Delegations_Users_ToUserId] FOREIGN KEY ([ToUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Tasks] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(40) NOT NULL,
        [Title] nvarchar(240) NOT NULL,
        [Purpose] nvarchar(2000) NULL,
        [CategoryId] int NULL,
        [Priority] nvarchar(24) NOT NULL,
        [Status] nvarchar(24) NOT NULL,
        [OwnerId] nvarchar(48) NULL,
        [AssignedById] nvarchar(48) NOT NULL,
        [ClosureAuthorityId] nvarchar(48) NULL,
        [ParentId] int NULL,
        [OrgUnitId] int NULL,
        [DueOn] datetime2 NULL,
        [Deliverable] nvarchar(1000) NULL,
        [Instructions] nvarchar(max) NULL,
        [RequireDocumentsComplete] bit NOT NULL,
        [Progress] int NOT NULL,
        [ProgressNote] nvarchar(2000) NULL,
        [NextAction] nvarchar(400) NULL,
        [ForecastOn] datetime2 NULL,
        [Blocker] nvarchar(1000) NULL,
        [BlockedSince] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedById] nvarchar(48) NOT NULL,
        [AssignedAt] datetime2 NULL,
        [StartedAt] datetime2 NULL,
        [SubmittedAt] datetime2 NULL,
        [ClosedAt] datetime2 NULL,
        [ClosedById] nvarchar(48) NULL,
        [ClosureNote] nvarchar(1000) NULL,
        [ReturnCount] int NOT NULL,
        [EscalationLevel] int NOT NULL,
        [RemindedAt] datetime2 NULL,
        CONSTRAINT [PK_Tasks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tasks_TaskCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [TaskCategories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Tasks_Tasks_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Tasks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Tasks_Users_AssignedById] FOREIGN KEY ([AssignedById]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Tasks_Users_ClosureAuthorityId] FOREIGN KEY ([ClosureAuthorityId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Tasks_Users_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Documents] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(40) NOT NULL,
        [Subject] nvarchar(300) NOT NULL,
        [DocumentTypeId] int NOT NULL,
        [OriginOrgUnitId] int NULL,
        [Classification] nvarchar(24) NOT NULL,
        [TaskId] int NULL,
        [ExternalRef] nvarchar(120) NULL,
        [Notes] nvarchar(4000) NULL,
        [InitiatorId] nvarchar(48) NOT NULL,
        [Status] nvarchar(24) NOT NULL,
        [ApprovalState] nvarchar(24) NOT NULL,
        [SignatureState] nvarchar(24) NOT NULL,
        [ActiveVersionId] int NULL,
        [TemplateId] int NULL,
        [TemplateVersion] int NULL,
        [RouteSnapshotJson] nvarchar(max) NULL,
        [ReturnCount] int NOT NULL,
        [Outcome] nvarchar(24) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [SubmittedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Documents_DocumentTypes_DocumentTypeId] FOREIGN KEY ([DocumentTypeId]) REFERENCES [DocumentTypes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Documents_OrgUnits_OriginOrgUnitId] FOREIGN KEY ([OriginOrgUnitId]) REFERENCES [OrgUnits] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Documents_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Documents_Users_InitiatorId] FOREIGN KEY ([InitiatorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [TaskActivities] (
        [Id] bigint NOT NULL IDENTITY,
        [TaskId] int NOT NULL,
        [At] datetime2 NOT NULL,
        [ActorId] nvarchar(48) NOT NULL,
        [ActingForId] nvarchar(48) NULL,
        [Event] nvarchar(24) NOT NULL,
        [Stage] nvarchar(24) NOT NULL,
        [OldValue] nvarchar(1000) NULL,
        [NewValue] nvarchar(1000) NULL,
        [Comment] nvarchar(4000) NULL,
        [ReplyToId] bigint NULL,
        [FileId] uniqueidentifier NULL,
        [Mentions] nvarchar(400) NULL,
        CONSTRAINT [PK_TaskActivities] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskActivities_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [TaskAttachments] (
        [Id] int NOT NULL IDENTITY,
        [TaskId] int NOT NULL,
        [FileId] uniqueidentifier NOT NULL,
        [Kind] nvarchar(16) NOT NULL,
        [Note] nvarchar(400) NULL,
        [Stage] nvarchar(24) NOT NULL,
        CONSTRAINT [PK_TaskAttachments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TaskAttachments_Files_FileId] FOREIGN KEY ([FileId]) REFERENCES [Files] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TaskAttachments_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [TaskDependencies] (
        [TaskId] int NOT NULL,
        [DependsOnId] int NOT NULL,
        CONSTRAINT [PK_TaskDependencies] PRIMARY KEY ([TaskId], [DependsOnId]),
        CONSTRAINT [FK_TaskDependencies_Tasks_DependsOnId] FOREIGN KEY ([DependsOnId]) REFERENCES [Tasks] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TaskDependencies_Tasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [Tasks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [DocumentVersions] (
        [Id] int NOT NULL IDENTITY,
        [DocumentId] int NOT NULL,
        [No] int NOT NULL,
        [FileId] uniqueidentifier NOT NULL,
        [State] nvarchar(24) NOT NULL,
        [Note] nvarchar(600) NULL,
        [CreatedById] nvarchar(48) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_DocumentVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocumentVersions_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DocumentVersions_Files_FileId] FOREIGN KEY ([FileId]) REFERENCES [Files] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [Movements] (
        [Id] bigint NOT NULL IDENTITY,
        [DocumentId] int NOT NULL,
        [At] datetime2 NOT NULL,
        [FromUserId] nvarchar(48) NULL,
        [ToUserId] nvarchar(48) NULL,
        [ActingForId] nvarchar(48) NULL,
        [RouteStageId] int NULL,
        [StageName] nvarchar(120) NULL,
        [Action] nvarchar(60) NOT NULL,
        [ActionRequested] nvarchar(80) NULL,
        [Comment] nvarchar(4000) NULL,
        [VersionNo] int NULL,
        [HeldHours] float NULL,
        CONSTRAINT [PK_Movements] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Movements_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [RouteAmendments] (
        [Id] int NOT NULL IDENTITY,
        [DocumentId] int NOT NULL,
        [RequestedById] nvarchar(48) NOT NULL,
        [RequestedAt] datetime2 NOT NULL,
        [Reason] nvarchar(1000) NOT NULL,
        [Status] nvarchar(12) NOT NULL,
        [ProposalJson] nvarchar(max) NOT NULL,
        [BeforeJson] nvarchar(max) NULL,
        [AfterJson] nvarchar(max) NULL,
        [DecidedById] nvarchar(48) NULL,
        [DecidedAt] datetime2 NULL,
        [DecisionNote] nvarchar(1000) NULL,
        CONSTRAINT [PK_RouteAmendments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RouteAmendments_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [RouteStages] (
        [Id] int NOT NULL IDENTITY,
        [DocumentId] int NOT NULL,
        [Step] int NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Type] nvarchar(24) NOT NULL,
        [Mandatory] bit NOT NULL,
        [TemplateStageId] int NULL,
        [UserId] nvarchar(48) NULL,
        [AdHoc] bit NOT NULL,
        [Status] nvarchar(24) NOT NULL,
        [ServiceHours] int NULL,
        [ReceivedAt] datetime2 NULL,
        [DueAt] datetime2 NULL,
        [ActedAt] datetime2 NULL,
        [ActedById] nvarchar(48) NULL,
        [ActingForId] nvarchar(48) NULL,
        [Outcome] nvarchar(24) NULL,
        [Comment] nvarchar(4000) NULL,
        [VersionId] int NULL,
        [EscalationLevel] int NOT NULL,
        [RemindedAt] datetime2 NULL,
        CONSTRAINT [PK_RouteStages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RouteStages_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RouteStages_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE TABLE [SignatureFields] (
        [Id] int NOT NULL IDENTITY,
        [DocumentId] int NOT NULL,
        [RouteStageId] int NULL,
        [SignerUserId] nvarchar(48) NOT NULL,
        [Order] int NOT NULL,
        [Page] int NOT NULL,
        [X] float NOT NULL,
        [Y] float NOT NULL,
        [W] float NOT NULL,
        [H] float NOT NULL,
        [ShowName] bit NOT NULL,
        [ShowDate] bit NOT NULL,
        [Status] nvarchar(24) NOT NULL,
        [VersionId] int NULL,
        [DocumentHash] nvarchar(64) NULL,
        [SignedAt] datetime2 NULL,
        [SignedById] nvarchar(48) NULL,
        [ActingForId] nvarchar(48) NULL,
        [AuthMethod] nvarchar(60) NULL,
        [SignatureCode] nvarchar(16) NULL,
        [SignatureMac] nvarchar(64) NULL,
        [SignedFileId] uniqueidentifier NULL,
        [InvalidatedReason] nvarchar(600) NULL,
        CONSTRAINT [PK_SignatureFields] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SignatureFields_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_ActorId] ON [AuditLogs] ([ActorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_At] ON [AuditLogs] ([At] DESC);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_AuthorityRules_DesignationId] ON [AuthorityRules] ([DesignationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_AuthorityRules_DocumentTypeId] ON [AuthorityRules] ([DocumentTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Delegations_FromUserId] ON [Delegations] ([FromUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Delegations_ToUserId_EndsOn] ON [Delegations] ([ToUserId], [EndsOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Designations_Code] ON [Designations] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Documents_DocumentTypeId] ON [Documents] ([DocumentTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Documents_InitiatorId_Status] ON [Documents] ([InitiatorId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Documents_Number] ON [Documents] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Documents_OriginOrgUnitId] ON [Documents] ([OriginOrgUnitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Documents_TaskId] ON [Documents] ([TaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DocumentTypes_Code] ON [DocumentTypes] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DocumentVersions_DocumentId_No] ON [DocumentVersions] ([DocumentId], [No]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_DocumentVersions_FileId] ON [DocumentVersions] ([FileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_EscalationLogs_At] ON [EscalationLogs] ([At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Movements_At] ON [Movements] ([At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Movements_DocumentId] ON [Movements] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_ReadAt] ON [Notifications] ([UserId], [ReadAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrgUnits_Code] ON [OrgUnits] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_OrgUnits_ParentId] ON [OrgUnits] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_RouteAmendments_DocumentId] ON [RouteAmendments] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_RouteStages_DocumentId] ON [RouteStages] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_RouteStages_UserId_Status] ON [RouteStages] ([UserId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_SignatureFields_DocumentId] ON [SignatureFields] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_SignatureFields_SignatureCode] ON [SignatureFields] ([SignatureCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_TaskActivities_TaskId] ON [TaskActivities] ([TaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_TaskAttachments_FileId] ON [TaskAttachments] ([FileId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_TaskAttachments_TaskId] ON [TaskAttachments] ([TaskId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_TaskDependencies_DependsOnId] ON [TaskDependencies] ([DependsOnId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Tasks_AssignedById] ON [Tasks] ([AssignedById]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Tasks_CategoryId] ON [Tasks] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Tasks_ClosureAuthorityId] ON [Tasks] ([ClosureAuthorityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Tasks_DueOn] ON [Tasks] ([DueOn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tasks_Number] ON [Tasks] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Tasks_OwnerId_Status] ON [Tasks] ([OwnerId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Tasks_ParentId] ON [Tasks] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Users_DesignationId] ON [Users] ([DesignationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]) WHERE [Email] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Users_OrgUnitId] ON [Users] ([OrgUnitId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Users_ReportsToId] ON [Users] ([ReportsToId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_Users_RoleKey] ON [Users] ([RoleKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkflowTemplates_Code_Version] ON [WorkflowTemplates] ([Code], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_WorkflowTemplates_DocumentTypeId] ON [WorkflowTemplates] ([DocumentTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    CREATE INDEX [IX_WorkflowTemplateStages_TemplateId] ON [WorkflowTemplateStages] ([TemplateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261029121308_Initial'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261029121308_Initial', N'9.0.7');
END;

COMMIT;
GO

