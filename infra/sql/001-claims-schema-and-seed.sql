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
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [CauseOfLossCodes] (
        [CauseOfLossCodeId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(255) NOT NULL,
        [PerilCategory] nvarchar(50) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [SortOrder] int NOT NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_CauseOfLossCodes] PRIMARY KEY ([CauseOfLossCodeId]),
        CONSTRAINT [AK_CauseOfLossCodes_Code] UNIQUE ([Code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ClaimNumberSequences] (
        [ClaimNumberSequenceId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [Year] int NOT NULL,
        [LastValue] int NOT NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimNumberSequences] PRIMARY KEY ([ClaimNumberSequenceId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ClaimStatusTransitions] (
        [ClaimStatusTransitionId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [FromStatus] nvarchar(50) NOT NULL,
        [ToStatus] nvarchar(50) NOT NULL,
        [RequiredPermission] nvarchar(50) NOT NULL,
        [Description] nvarchar(500) NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimStatusTransitions] PRIMARY KEY ([ClaimStatusTransitionId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [Policies] (
        [PolicyId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [PolicyNumber] nvarchar(50) NOT NULL,
        [ClientName] nvarchar(255) NOT NULL,
        [EffectiveDate] date NOT NULL,
        [ExpirationDate] date NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [CoverageTypes] nvarchar(500) NOT NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_Policies] PRIMARY KEY ([PolicyId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [Claims] (
        [ClaimId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimNumber] nvarchar(50) NOT NULL,
        [PolicyId] uniqueidentifier NULL,
        [PolicyNumber] nvarchar(50) NULL,
        [ClientName] nvarchar(255) NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [Severity] nvarchar(50) NOT NULL,
        [ClaimType] nvarchar(50) NULL,
        [ReportedDate] datetimeoffset NOT NULL,
        [AssignedHandlerId] uniqueidentifier NULL,
        [ClosedAt] datetimeoffset NULL,
        [ClosureReason] nvarchar(500) NULL,
        [Notes] nvarchar(max) NULL,
        [ManagerOverride] bit NOT NULL DEFAULT CAST(0 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        [RowVer] rowversion NOT NULL,
        CONSTRAINT [PK_Claims] PRIMARY KEY ([ClaimId]),
        CONSTRAINT [FK_Claims_Policies_PolicyId] FOREIGN KEY ([PolicyId]) REFERENCES [Policies] ([PolicyId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ClaimAuditLog] (
        [AuditLogId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [OrganisationId] uniqueidentifier NOT NULL,
        [ClaimId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [OldValue] nvarchar(max) NULL,
        [NewValue] nvarchar(max) NULL,
        [RelatedEntityId] uniqueidentifier NULL,
        [RelatedEntityType] nvarchar(100) NULL,
        [CorrelationId] uniqueidentifier NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [CreatedByUserId] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimAuditLog] PRIMARY KEY ([AuditLogId]),
        CONSTRAINT [FK_ClaimAuditLog_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ClaimDocuments] (
        [ClaimDocumentId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [DocumentType] nvarchar(100) NOT NULL,
        [DocumentName] nvarchar(255) NOT NULL,
        [BlobPath] nvarchar(500) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [FileSizeBytes] bigint NOT NULL,
        [UploadedAt] datetimeoffset NOT NULL,
        [UploadedByUserId] uniqueidentifier NULL,
        [Notes] nvarchar(500) NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimDocuments] PRIMARY KEY ([ClaimDocumentId]),
        CONSTRAINT [FK_ClaimDocuments_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ClaimParties] (
        [ClaimPartyId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [PartyRole] nvarchar(50) NOT NULL,
        [PartyType] nvarchar(20) NOT NULL,
        [FirstName] nvarchar(100) NULL,
        [LastName] nvarchar(100) NULL,
        [CompanyName] nvarchar(255) NULL,
        [Email] nvarchar(255) NULL,
        [Phone] nvarchar(50) NULL,
        [Notes] nvarchar(max) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimParties] PRIMARY KEY ([ClaimPartyId]),
        CONSTRAINT [FK_ClaimParties_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ClaimReserveComponents] (
        [ReserveComponentId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [Component] nvarchar(50) NOT NULL,
        [CurrentAmount] decimal(19,4) NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [Notes] nvarchar(max) NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        [RowVer] rowversion NOT NULL,
        CONSTRAINT [PK_ClaimReserveComponents] PRIMARY KEY ([ReserveComponentId]),
        CONSTRAINT [FK_ClaimReserveComponents_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ClaimRiskObjects] (
        [ClaimRiskObjectId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [AssetType] nvarchar(50) NOT NULL,
        [AssetDescription] nvarchar(500) NOT NULL,
        [DamageDescription] nvarchar(max) NULL,
        [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
        [AssetReference] nvarchar(255) NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimRiskObjects] PRIMARY KEY ([ClaimRiskObjectId]),
        CONSTRAINT [FK_ClaimRiskObjects_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ClaimValidationIssues] (
        [ClaimValidationIssueId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [Severity] nvarchar(50) NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Field] nvarchar(100) NULL,
        [Message] nvarchar(1000) NOT NULL,
        [RequiresAcknowledgement] bit NOT NULL DEFAULT CAST(0 AS bit),
        [IsResolved] bit NOT NULL DEFAULT CAST(0 AS bit),
        [ResolvedAt] datetimeoffset NULL,
        [IsAcknowledged] bit NOT NULL DEFAULT CAST(0 AS bit),
        [AcknowledgedAt] datetimeoffset NULL,
        [AcknowledgedByUserId] uniqueidentifier NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ClaimValidationIssues] PRIMARY KEY ([ClaimValidationIssueId]),
        CONSTRAINT [FK_ClaimValidationIssues_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [LossEvents] (
        [LossEventId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ClaimId] uniqueidentifier NOT NULL,
        [LossDate] datetimeoffset NOT NULL,
        [LossDescription] nvarchar(max) NOT NULL,
        [LossLocation] nvarchar(500) NULL,
        [CauseOfLossCode] nvarchar(50) NOT NULL,
        [EstimatedLossAmount] decimal(19,4) NULL,
        [ReportDate] datetimeoffset NOT NULL,
        [PoliceReportNumber] nvarchar(100) NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_LossEvents] PRIMARY KEY ([LossEventId]),
        CONSTRAINT [FK_LossEvents_CauseOfLossCodes_CauseOfLossCode] FOREIGN KEY ([CauseOfLossCode]) REFERENCES [CauseOfLossCodes] ([Code]) ON DELETE NO ACTION,
        CONSTRAINT [FK_LossEvents_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE TABLE [ReserveHistory] (
        [ReserveHistoryId] uniqueidentifier NOT NULL DEFAULT (NEWSEQUENTIALID()),
        [ReserveComponentId] uniqueidentifier NOT NULL,
        [ClaimId] uniqueidentifier NOT NULL,
        [TransactionType] nvarchar(50) NOT NULL,
        [Amount] decimal(19,4) NOT NULL,
        [PreviousBalance] decimal(19,4) NOT NULL,
        [NewBalance] decimal(19,4) NOT NULL,
        [ApprovalStatus] nvarchar(50) NOT NULL,
        [ApprovedByUserId] uniqueidentifier NULL,
        [ApprovedAt] datetimeoffset NULL,
        [RejectedByUserId] uniqueidentifier NULL,
        [RejectedAt] datetimeoffset NULL,
        [RejectionReason] nvarchar(max) NULL,
        [ChangeReason] nvarchar(500) NOT NULL,
        [PostingStatus] nvarchar(50) NOT NULL,
        [PostingJobId] nvarchar(100) NULL,
        [IdempotencyKey] nvarchar(200) NOT NULL,
        [ChangeSequence] int NOT NULL,
        [SubmittedByUserId] uniqueidentifier NULL,
        [OrganisationId] uniqueidentifier NOT NULL,
        [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
        [DeletedAt] datetimeoffset NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        [UpdatedAt] datetimeoffset NULL,
        [UserCreated] uniqueidentifier NULL,
        [UserModified] uniqueidentifier NULL,
        CONSTRAINT [PK_ReserveHistory] PRIMARY KEY ([ReserveHistoryId]),
        CONSTRAINT [FK_ReserveHistory_ClaimReserveComponents_ReserveComponentId] FOREIGN KEY ([ReserveComponentId]) REFERENCES [ClaimReserveComponents] ([ReserveComponentId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ReserveHistory_Claims_ClaimId] FOREIGN KEY ([ClaimId]) REFERENCES [Claims] ([ClaimId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CauseOfLossCodeId', N'Code', N'CreatedAt', N'DeletedAt', N'IsActive', N'Name', N'OrganisationId', N'PerilCategory', N'SortOrder', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[CauseOfLossCodes]'))
        SET IDENTITY_INSERT [CauseOfLossCodes] ON;
    EXEC(N'INSERT INTO [CauseOfLossCodes] ([CauseOfLossCodeId], [Code], [CreatedAt], [DeletedAt], [IsActive], [Name], [OrganisationId], [PerilCategory], [SortOrder], [UpdatedAt], [UserCreated], [UserModified])
    VALUES (''18102cc4-c2d0-8a26-c369-2c02108fcee9'', N''COL-FIRE'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Fire'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Property'', 1, NULL, NULL, NULL),
    (''2e5894b5-1293-534f-bcb9-60f7a83b9e31'', N''COL-EQUIP'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Equipment Breakdown'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Equipment'', 7, NULL, NULL, NULL),
    (''4dbb8c03-3e7d-c55c-bee2-4a10843145ad'', N''COL-VEH-COL'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Vehicle Collision'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Auto'', 4, NULL, NULL, NULL),
    (''8d937f3f-71ad-a675-efed-c3105dfc8f1c'', N''COL-FLOOD'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Flood'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Weather'', 2, NULL, NULL, NULL),
    (''9563c816-25a5-59c1-27a8-127f70f06ef9'', N''COL-VEH-COMP'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Vehicle Comprehensive'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Auto'', 5, NULL, NULL, NULL),
    (''9d54bfbb-a427-13d6-8e45-b7f2dfa640bd'', N''COL-INJURY'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Bodily Injury'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Liability'', 9, NULL, NULL, NULL),
    (''a8ae26f8-05ac-b158-d1c2-f873e8b7ec47'', N''COL-THEFT'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Theft'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Crime'', 3, NULL, NULL, NULL),
    (''c0a95872-1fc8-d514-80b4-2ed383a9ccb6'', N''COL-WIND'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Wind / Storm'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Weather'', 8, NULL, NULL, NULL),
    (''ea1258b5-258a-1258-949b-b61f415f8f96'', N''COL-OTHER'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Other / Unknown'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''General'', 10, NULL, NULL, NULL),
    (''ef95f3a2-cd86-d08d-d224-a9fc626fa3ab'', N''COL-LIAB'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, CAST(1 AS bit), N''Third Party Liability'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Liability'', 6, NULL, NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'CauseOfLossCodeId', N'Code', N'CreatedAt', N'DeletedAt', N'IsActive', N'Name', N'OrganisationId', N'PerilCategory', N'SortOrder', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[CauseOfLossCodes]'))
        SET IDENTITY_INSERT [CauseOfLossCodes] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'CreatedAt', N'DeletedAt', N'Description', N'FromStatus', N'OrganisationId', N'RequiredPermission', N'ToStatus', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] ON;
    EXEC(N'INSERT INTO [ClaimStatusTransitions] ([ClaimStatusTransitionId], [CreatedAt], [DeletedAt], [Description], [FromStatus], [OrganisationId], [RequiredPermission], [ToStatus], [UpdatedAt], [UserCreated], [UserModified])
    VALUES (''054e0711-2787-bb9c-837f-f890f8b696fc'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''No critical validation issues remain (or all waived); at least one claimant party exists'', N''Draft'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''Open'', NULL, NULL, NULL),
    (''10465c5f-be4f-7b77-dd45-04e8da12ada4'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''All closure conditions satisfied'', N''PendingPayment'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''Closed'', NULL, NULL, NULL),
    (''118e273e-0eec-749b-fdb9-be52e26917d5'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''At least one approved reserve exists'', N''Open'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''PendingPayment'', NULL, NULL, NULL),
    (''139046cd-5853-9801-79b5-b2bc67097636'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''Handler or supervisor; no additional conditions'', N''Open'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''UnderInvestigation'', NULL, NULL, NULL),
    (''244cabc2-9e14-fe26-e0e5-ee5194e4d55a'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''Withdrawal reason provided'', N''UnderInvestigation'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''Withdrawn'', NULL, NULL, NULL),
    (''4438334c-026c-75b1-cbdb-225b96e6d678'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''All closure conditions satisfied'', N''UnderInvestigation'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''Closed'', NULL, NULL, NULL),
    (''923a77cc-de38-7134-5b75-0ef5eac005d8'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''Immediately on reopen'', N''Reopened'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''Open'', NULL, NULL, NULL),
    (''9d46e463-7829-f0c1-fbe4-cd33eff0b535'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''Withdrawal reason provided'', N''Open'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''Withdrawn'', NULL, NULL, NULL),
    (''9e275b0a-cb8f-8dae-617a-929349237f32'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''Reopen reason provided; supervisor role required'', N''Closed'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Supervisor'', N''Reopened'', NULL, NULL, NULL),
    (''bd560587-943e-8dba-869d-365d039a7c17'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''At least one approved reserve; liability determined'', N''UnderInvestigation'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''PendingPayment'', NULL, NULL, NULL),
    (''ce79691d-7a6b-18de-a44b-88834df1de37'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''Handler manually reverts'', N''UnderInvestigation'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''Open'', NULL, NULL, NULL),
    (''fef9d49e-b420-c973-bf5f-204954b5984d'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, N''All closure conditions satisfied'', N''Open'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''Handler'', N''Closed'', NULL, NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ClaimStatusTransitionId', N'CreatedAt', N'DeletedAt', N'Description', N'FromStatus', N'OrganisationId', N'RequiredPermission', N'ToStatus', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[ClaimStatusTransitions]'))
        SET IDENTITY_INSERT [ClaimStatusTransitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'PolicyId', N'ClientName', N'CoverageTypes', N'CreatedAt', N'DeletedAt', N'EffectiveDate', N'ExpirationDate', N'OrganisationId', N'PolicyNumber', N'Status', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[Policies]'))
        SET IDENTITY_INSERT [Policies] ON;
    EXEC(N'INSERT INTO [Policies] ([PolicyId], [ClientName], [CoverageTypes], [CreatedAt], [DeletedAt], [EffectiveDate], [ExpirationDate], [OrganisationId], [PolicyNumber], [Status], [UpdatedAt], [UserCreated], [UserModified])
    VALUES (''0cf86c04-2a25-343e-2c4f-4fa6c7e0a02b'', N''Coastal Builders Group'', N''Property,Equipment'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2025-03-01'', ''2027-02-28'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''POL-2025-002001'', N''Active'', NULL, NULL, NULL),
    (''127e03a8-ef89-cce5-1928-fe92094dfa60'', N''Stanton Medical Group'', N''Liability,Vehicle'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2025-01-01'', ''2026-12-31'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''POL-2025-002002'', N''Active'', NULL, NULL, NULL),
    (''1947fcaf-f596-9f66-7d69-c4c8429c82df'', N''Harborview Properties Inc'', N''Property,Liability'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2024-06-01'', ''2026-05-31'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''POL-2024-001002'', N''Expired'', NULL, NULL, NULL),
    (''1e71f5e6-5993-9788-964e-5c080745dd72'', N''Meridian Transport LLC'', N''Vehicle,Cargo'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2024-01-01'', ''2026-12-31'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''POL-2024-001001'', N''Active'', NULL, NULL, NULL),
    (''e39a965c-f3de-4b7d-be17-a46cffe0827e'', N''Archived Corp'', N''Property'', ''2026-01-01T00:00:00.0000000+00:00'', NULL, ''2020-01-01'', ''2021-12-31'', ''0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001'', N''POL-2023-000099'', N''Expired'', NULL, NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'PolicyId', N'ClientName', N'CoverageTypes', N'CreatedAt', N'DeletedAt', N'EffectiveDate', N'ExpirationDate', N'OrganisationId', N'PolicyNumber', N'Status', N'UpdatedAt', N'UserCreated', N'UserModified') AND [object_id] = OBJECT_ID(N'[Policies]'))
        SET IDENTITY_INSERT [Policies] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CauseOfLossCodes_OrganisationId] ON [CauseOfLossCodes] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimAuditLog_ClaimId_CreatedAt] ON [ClaimAuditLog] ([ClaimId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimAuditLog_ClaimId_EventType_CreatedAt] ON [ClaimAuditLog] ([ClaimId], [EventType], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimAuditLog_OrganisationId] ON [ClaimAuditLog] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimDocuments_ClaimId] ON [ClaimDocuments] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimDocuments_OrganisationId] ON [ClaimDocuments] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimNumberSequences_OrganisationId] ON [ClaimNumberSequences] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClaimNumberSequences_OrganisationId_Year] ON [ClaimNumberSequences] ([OrganisationId], [Year]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimParties_ClaimId_PartyRole] ON [ClaimParties] ([ClaimId], [PartyRole]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimParties_OrganisationId] ON [ClaimParties] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClaimReserveComponents_ClaimId_Component] ON [ClaimReserveComponents] ([ClaimId], [Component]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimReserveComponents_OrganisationId] ON [ClaimReserveComponents] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimRiskObjects_ClaimId] ON [ClaimRiskObjects] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimRiskObjects_OrganisationId] ON [ClaimRiskObjects] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Claims_AssignedHandlerId] ON [Claims] ([AssignedHandlerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Claims_OrganisationId] ON [Claims] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Claims_OrganisationId_ClaimNumber] ON [Claims] ([OrganisationId], [ClaimNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Claims_PolicyId] ON [Claims] ([PolicyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Claims_ReportedDate] ON [Claims] ([ReportedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Claims_Status] ON [Claims] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimStatusTransitions_OrganisationId] ON [ClaimStatusTransitions] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ClaimStatusTransitions_OrganisationId_FromStatus_ToStatus] ON [ClaimStatusTransitions] ([OrganisationId], [FromStatus], [ToStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimValidationIssues_ClaimId_IsResolved] ON [ClaimValidationIssues] ([ClaimId], [IsResolved]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ClaimValidationIssues_OrganisationId] ON [ClaimValidationIssues] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LossEvents_CauseOfLossCode] ON [LossEvents] ([CauseOfLossCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LossEvents_ClaimId] ON [LossEvents] ([ClaimId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_LossEvents_OrganisationId] ON [LossEvents] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Policies_ClientName] ON [Policies] ([ClientName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Policies_OrganisationId] ON [Policies] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Policies_PolicyNumber] ON [Policies] ([PolicyNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReserveHistory_ClaimId_ApprovalStatus] ON [ReserveHistory] ([ClaimId], [ApprovalStatus]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReserveHistory_IdempotencyKey] ON [ReserveHistory] ([IdempotencyKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReserveHistory_OrganisationId] ON [ReserveHistory] ([OrganisationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ReserveHistory_ReserveComponentId_ChangeSequence] ON [ReserveHistory] ([ReserveComponentId], [ChangeSequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006105812_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006105812_InitialCreate', N'9.0.0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006110252_ReserveHistoryConcurrencyTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006110252_ReserveHistoryConcurrencyTokens', N'9.0.0');
END;

COMMIT;
GO

