using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClaimsModule.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CauseOfLossCodes",
                columns: table => new
                {
                    CauseOfLossCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PerilCategory = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CauseOfLossCodes", x => x.CauseOfLossCodeId);
                    table.UniqueConstraint("AK_CauseOfLossCodes_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ClaimNumberSequences",
                columns: table => new
                {
                    ClaimNumberSequenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    LastValue = table.Column<int>(type: "int", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimNumberSequences", x => x.ClaimNumberSequenceId);
                });

            migrationBuilder.CreateTable(
                name: "ClaimStatusTransitions",
                columns: table => new
                {
                    ClaimStatusTransitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    FromStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RequiredPermission = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimStatusTransitions", x => x.ClaimStatusTransitionId);
                });

            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    PolicyNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CoverageTypes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.PolicyId);
                });

            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PolicyNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ClientName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReportedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AssignedHandlerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManagerOverride = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVer = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.ClaimId);
                    table.ForeignKey(
                        name: "FK_Claims_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "PolicyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimAuditLog",
                columns: table => new
                {
                    AuditLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimAuditLog", x => x.AuditLogId);
                    table.ForeignKey(
                        name: "FK_ClaimAuditLog_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimDocuments",
                columns: table => new
                {
                    ClaimDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimDocuments", x => x.ClaimDocumentId);
                    table.ForeignKey(
                        name: "FK_ClaimDocuments_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimParties",
                columns: table => new
                {
                    ClaimPartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PartyType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimParties", x => x.ClaimPartyId);
                    table.ForeignKey(
                        name: "FK_ClaimParties_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimReserveComponents",
                columns: table => new
                {
                    ReserveComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Component = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVer = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimReserveComponents", x => x.ReserveComponentId);
                    table.ForeignKey(
                        name: "FK_ClaimReserveComponents_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimRiskObjects",
                columns: table => new
                {
                    ClaimRiskObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AssetDescription = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DamageDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AssetReference = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimRiskObjects", x => x.ClaimRiskObjectId);
                    table.ForeignKey(
                        name: "FK_ClaimRiskObjects_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimValidationIssues",
                columns: table => new
                {
                    ClaimValidationIssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequiresAcknowledgement = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsAcknowledged = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimValidationIssues", x => x.ClaimValidationIssueId);
                    table.ForeignKey(
                        name: "FK_ClaimValidationIssues_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LossEvents",
                columns: table => new
                {
                    LossEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LossDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LossDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LossLocation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CauseOfLossCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EstimatedLossAmount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    ReportDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PoliceReportNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LossEvents", x => x.LossEventId);
                    table.ForeignKey(
                        name: "FK_LossEvents_CauseOfLossCodes_CauseOfLossCode",
                        column: x => x.CauseOfLossCode,
                        principalTable: "CauseOfLossCodes",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LossEvents_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReserveHistory",
                columns: table => new
                {
                    ReserveHistoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ReserveComponentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PreviousBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    NewBalance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PostingStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PostingJobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ChangeSequence = table.Column<int>(type: "int", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UserCreated = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserModified = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReserveHistory", x => x.ReserveHistoryId);
                    table.ForeignKey(
                        name: "FK_ReserveHistory_ClaimReserveComponents_ReserveComponentId",
                        column: x => x.ReserveComponentId,
                        principalTable: "ClaimReserveComponents",
                        principalColumn: "ReserveComponentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReserveHistory_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "ClaimId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "CauseOfLossCodes",
                columns: new[] { "CauseOfLossCodeId", "Code", "CreatedAt", "DeletedAt", "IsActive", "Name", "OrganisationId", "PerilCategory", "SortOrder", "UpdatedAt", "UserCreated", "UserModified" },
                values: new object[,]
                {
                    { new Guid("18102cc4-c2d0-8a26-c369-2c02108fcee9"), "COL-FIRE", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Fire", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Property", 1, null, null, null },
                    { new Guid("2e5894b5-1293-534f-bcb9-60f7a83b9e31"), "COL-EQUIP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Equipment Breakdown", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Equipment", 7, null, null, null },
                    { new Guid("4dbb8c03-3e7d-c55c-bee2-4a10843145ad"), "COL-VEH-COL", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Vehicle Collision", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Auto", 4, null, null, null },
                    { new Guid("8d937f3f-71ad-a675-efed-c3105dfc8f1c"), "COL-FLOOD", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Flood", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Weather", 2, null, null, null },
                    { new Guid("9563c816-25a5-59c1-27a8-127f70f06ef9"), "COL-VEH-COMP", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Vehicle Comprehensive", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Auto", 5, null, null, null },
                    { new Guid("9d54bfbb-a427-13d6-8e45-b7f2dfa640bd"), "COL-INJURY", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Bodily Injury", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Liability", 9, null, null, null },
                    { new Guid("a8ae26f8-05ac-b158-d1c2-f873e8b7ec47"), "COL-THEFT", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Theft", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Crime", 3, null, null, null },
                    { new Guid("c0a95872-1fc8-d514-80b4-2ed383a9ccb6"), "COL-WIND", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Wind / Storm", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Weather", 8, null, null, null },
                    { new Guid("ea1258b5-258a-1258-949b-b61f415f8f96"), "COL-OTHER", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Other / Unknown", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "General", 10, null, null, null },
                    { new Guid("ef95f3a2-cd86-d08d-d224-a9fc626fa3ab"), "COL-LIAB", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Third Party Liability", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Liability", 6, null, null, null }
                });

            migrationBuilder.InsertData(
                table: "ClaimStatusTransitions",
                columns: new[] { "ClaimStatusTransitionId", "CreatedAt", "DeletedAt", "Description", "FromStatus", "OrganisationId", "RequiredPermission", "ToStatus", "UpdatedAt", "UserCreated", "UserModified" },
                values: new object[,]
                {
                    { new Guid("054e0711-2787-bb9c-837f-f890f8b696fc"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "No critical validation issues remain (or all waived); at least one claimant party exists", "Draft", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "Open", null, null, null },
                    { new Guid("10465c5f-be4f-7b77-dd45-04e8da12ada4"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "All closure conditions satisfied", "PendingPayment", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "Closed", null, null, null },
                    { new Guid("118e273e-0eec-749b-fdb9-be52e26917d5"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "At least one approved reserve exists", "Open", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "PendingPayment", null, null, null },
                    { new Guid("139046cd-5853-9801-79b5-b2bc67097636"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Handler or supervisor; no additional conditions", "Open", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "UnderInvestigation", null, null, null },
                    { new Guid("244cabc2-9e14-fe26-e0e5-ee5194e4d55a"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Withdrawal reason provided", "UnderInvestigation", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "Withdrawn", null, null, null },
                    { new Guid("4438334c-026c-75b1-cbdb-225b96e6d678"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "All closure conditions satisfied", "UnderInvestigation", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "Closed", null, null, null },
                    { new Guid("923a77cc-de38-7134-5b75-0ef5eac005d8"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Immediately on reopen", "Reopened", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "Open", null, null, null },
                    { new Guid("9d46e463-7829-f0c1-fbe4-cd33eff0b535"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Withdrawal reason provided", "Open", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "Withdrawn", null, null, null },
                    { new Guid("9e275b0a-cb8f-8dae-617a-929349237f32"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Reopen reason provided; supervisor role required", "Closed", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Supervisor", "Reopened", null, null, null },
                    { new Guid("bd560587-943e-8dba-869d-365d039a7c17"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "At least one approved reserve; liability determined", "UnderInvestigation", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "PendingPayment", null, null, null },
                    { new Guid("ce79691d-7a6b-18de-a44b-88834df1de37"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Handler manually reverts", "UnderInvestigation", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "Open", null, null, null },
                    { new Guid("fef9d49e-b420-c973-bf5f-204954b5984d"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "All closure conditions satisfied", "Open", new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "Handler", "Closed", null, null, null }
                });

            migrationBuilder.InsertData(
                table: "Policies",
                columns: new[] { "PolicyId", "ClientName", "CoverageTypes", "CreatedAt", "DeletedAt", "EffectiveDate", "ExpirationDate", "OrganisationId", "PolicyNumber", "Status", "UpdatedAt", "UserCreated", "UserModified" },
                values: new object[,]
                {
                    { new Guid("0cf86c04-2a25-343e-2c4f-4fa6c7e0a02b"), "Coastal Builders Group", "Property,Equipment", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2025, 3, 1), new DateOnly(2027, 2, 28), new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "POL-2025-002001", "Active", null, null, null },
                    { new Guid("127e03a8-ef89-cce5-1928-fe92094dfa60"), "Stanton Medical Group", "Liability,Vehicle", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "POL-2025-002002", "Active", null, null, null },
                    { new Guid("1947fcaf-f596-9f66-7d69-c4c8429c82df"), "Harborview Properties Inc", "Property,Liability", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2024, 6, 1), new DateOnly(2026, 5, 31), new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "POL-2024-001002", "Expired", null, null, null },
                    { new Guid("1e71f5e6-5993-9788-964e-5c080745dd72"), "Meridian Transport LLC", "Vehicle,Cargo", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "POL-2024-001001", "Active", null, null, null },
                    { new Guid("e39a965c-f3de-4b7d-be17-a46cffe0827e"), "Archived Corp", "Property", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31), new Guid("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001"), "POL-2023-000099", "Expired", null, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CauseOfLossCodes_OrganisationId",
                table: "CauseOfLossCodes",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAuditLog_ClaimId_CreatedAt",
                table: "ClaimAuditLog",
                columns: new[] { "ClaimId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAuditLog_ClaimId_EventType_CreatedAt",
                table: "ClaimAuditLog",
                columns: new[] { "ClaimId", "EventType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimAuditLog_OrganisationId",
                table: "ClaimAuditLog",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimDocuments_ClaimId",
                table: "ClaimDocuments",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimDocuments_OrganisationId",
                table: "ClaimDocuments",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimNumberSequences_OrganisationId",
                table: "ClaimNumberSequences",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimNumberSequences_OrganisationId_Year",
                table: "ClaimNumberSequences",
                columns: new[] { "OrganisationId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimParties_ClaimId_PartyRole",
                table: "ClaimParties",
                columns: new[] { "ClaimId", "PartyRole" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimParties_OrganisationId",
                table: "ClaimParties",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimReserveComponents_ClaimId_Component",
                table: "ClaimReserveComponents",
                columns: new[] { "ClaimId", "Component" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimReserveComponents_OrganisationId",
                table: "ClaimReserveComponents",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimRiskObjects_ClaimId",
                table: "ClaimRiskObjects",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimRiskObjects_OrganisationId",
                table: "ClaimRiskObjects",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_AssignedHandlerId",
                table: "Claims",
                column: "AssignedHandlerId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_OrganisationId",
                table: "Claims",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_OrganisationId_ClaimNumber",
                table: "Claims",
                columns: new[] { "OrganisationId", "ClaimNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_PolicyId",
                table: "Claims",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ReportedDate",
                table: "Claims",
                column: "ReportedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_Status",
                table: "Claims",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimStatusTransitions_OrganisationId",
                table: "ClaimStatusTransitions",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimStatusTransitions_OrganisationId_FromStatus_ToStatus",
                table: "ClaimStatusTransitions",
                columns: new[] { "OrganisationId", "FromStatus", "ToStatus" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClaimValidationIssues_ClaimId_IsResolved",
                table: "ClaimValidationIssues",
                columns: new[] { "ClaimId", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimValidationIssues_OrganisationId",
                table: "ClaimValidationIssues",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_LossEvents_CauseOfLossCode",
                table: "LossEvents",
                column: "CauseOfLossCode");

            migrationBuilder.CreateIndex(
                name: "IX_LossEvents_ClaimId",
                table: "LossEvents",
                column: "ClaimId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LossEvents_OrganisationId",
                table: "LossEvents",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_ClientName",
                table: "Policies",
                column: "ClientName");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_OrganisationId",
                table: "Policies",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_PolicyNumber",
                table: "Policies",
                column: "PolicyNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReserveHistory_ClaimId_ApprovalStatus",
                table: "ReserveHistory",
                columns: new[] { "ClaimId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ReserveHistory_IdempotencyKey",
                table: "ReserveHistory",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReserveHistory_OrganisationId",
                table: "ReserveHistory",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReserveHistory_ReserveComponentId_ChangeSequence",
                table: "ReserveHistory",
                columns: new[] { "ReserveComponentId", "ChangeSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimAuditLog");

            migrationBuilder.DropTable(
                name: "ClaimDocuments");

            migrationBuilder.DropTable(
                name: "ClaimNumberSequences");

            migrationBuilder.DropTable(
                name: "ClaimParties");

            migrationBuilder.DropTable(
                name: "ClaimRiskObjects");

            migrationBuilder.DropTable(
                name: "ClaimStatusTransitions");

            migrationBuilder.DropTable(
                name: "ClaimValidationIssues");

            migrationBuilder.DropTable(
                name: "LossEvents");

            migrationBuilder.DropTable(
                name: "ReserveHistory");

            migrationBuilder.DropTable(
                name: "CauseOfLossCodes");

            migrationBuilder.DropTable(
                name: "ClaimReserveComponents");

            migrationBuilder.DropTable(
                name: "Claims");

            migrationBuilder.DropTable(
                name: "Policies");
        }
    }
}
