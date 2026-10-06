using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Application.Features.Claims.Dtos;

public sealed record ClaimSummaryDto
{
    public Guid Id { get; init; }
    public string ClaimNumber { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string? PolicyNumber { get; init; }
    public DateTimeOffset LossDate { get; init; }
    public string CauseOfLossCode { get; init; } = string.Empty;
    public string? CauseOfLossName { get; init; }
    public ClaimStatus Status { get; init; }
    public decimal TotalReserves { get; init; }
    public Guid? AssignedHandlerId { get; init; }
    public string? AssignedHandlerName { get; init; }
    public DateTimeOffset ReportedDate { get; init; }
}

public sealed record LossEventDto
{
    public Guid Id { get; init; }
    public DateTimeOffset LossDate { get; init; }
    public string LossDescription { get; init; } = string.Empty;
    public string? LossLocation { get; init; }
    public string CauseOfLossCode { get; init; } = string.Empty;
    public string? CauseOfLossName { get; init; }
    public decimal? EstimatedLossAmount { get; init; }
    public DateTimeOffset ReportDate { get; init; }
    public string? PoliceReportNumber { get; init; }
}

public sealed record ClaimPartyDto
{
    public Guid Id { get; init; }
    public PartyRole PartyRole { get; init; }
    public PartyType PartyType { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? CompanyName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
}

public sealed record ClaimRiskObjectDto
{
    public Guid Id { get; init; }
    public AssetType AssetType { get; init; }
    public string AssetDescription { get; init; } = string.Empty;
    public string? DamageDescription { get; init; }
    public bool IsPrimary { get; init; }
    public string? AssetReference { get; init; }
}

public sealed record ValidationIssueDto
{
    public Guid Id { get; init; }
    public ValidationSeverity Severity { get; init; }
    public string Code { get; init; } = string.Empty;
    public string? Field { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool RequiresAcknowledgement { get; init; }
    public bool IsResolved { get; init; }
    public bool IsAcknowledged { get; init; }
    public bool IsOutstanding { get; init; }
}

public sealed record DocumentDto
{
    public Guid Id { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTimeOffset UploadedAt { get; init; }
    public Guid? UploadedByUserId { get; init; }
    public string? UploadedByName { get; init; }
    public string? Notes { get; init; }

    /// <summary>Short-lived (1 hour) download URL; only populated by the documents listing.</summary>
    public string? DownloadUrl { get; set; }
    public DateTimeOffset? DownloadUrlExpiresAt { get; set; }
}

public sealed record AuditEntryDto
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public Guid? RelatedEntityId { get; init; }
    public string? RelatedEntityType { get; init; }
    public Guid? CorrelationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByName { get; init; }
}

public sealed record NextStatusDto(ClaimStatus Status, UserRole MinimumRole, string Conditions);

public sealed record ClaimDetailDto
{
    public Guid Id { get; init; }
    public string ClaimNumber { get; init; } = string.Empty;
    public Guid? PolicyId { get; init; }
    public string? PolicyNumber { get; init; }
    public string ClientName { get; init; } = string.Empty;
    public ClaimStatus Status { get; init; }
    public ClaimSeverity Severity { get; init; }
    public string? ClaimType { get; init; }
    public DateTimeOffset ReportedDate { get; init; }
    public Guid? AssignedHandlerId { get; init; }
    public string? AssignedHandlerName { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public string? ClosureReason { get; init; }
    public string? Notes { get; init; }
    public bool ManagerOverride { get; init; }
    public LossEventDto LossEvent { get; set; } = null!;
    public IReadOnlyList<ClaimPartyDto> Parties { get; init; } = Array.Empty<ClaimPartyDto>();
    public IReadOnlyList<ClaimRiskObjectDto> RiskObjects { get; init; } = Array.Empty<ClaimRiskObjectDto>();
    public IReadOnlyList<ValidationIssueDto> ValidationIssues { get; init; } = Array.Empty<ValidationIssueDto>();
    public IReadOnlyList<DocumentDto> Documents { get; init; } = Array.Empty<DocumentDto>();
    public ReserveSummaryDto ReserveSummary { get; set; } = null!;
    public IReadOnlyList<AuditEntryDto> RecentAudit { get; set; } = Array.Empty<AuditEntryDto>();
    public IReadOnlyList<NextStatusDto> ValidNextStatuses { get; set; } = Array.Empty<NextStatusDto>();
}

public sealed record ClaimCreatedDto(
    Guid Id,
    string ClaimNumber,
    ClaimStatus Status,
    IReadOnlyList<ValidationIssueDto> ValidationIssues,
    ReserveSubmissionDto? InitialReserve);

public sealed record ClaimStatusChangedDto(Guid Id, ClaimStatus PreviousStatus, ClaimStatus Status, IReadOnlyList<NextStatusDto> ValidNextStatuses);

public sealed record ClosurePreflightDto(bool CanClose, IReadOnlyList<string> Blockers, decimal OpenReserveBalance, bool RequiresJustification);

public sealed record ValidationReportDto(IReadOnlyList<string> Critical, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Critical.Count == 0;
}

// ----- reserves

public sealed record ReserveComponentDto
{
    public Guid Id { get; init; }
    public ReserveComponentType Component { get; init; }
    public decimal CurrentAmount { get; init; }
    public decimal PendingAmount { get; init; }
    public ReserveComponentStatus Status { get; init; }
}

public sealed record ReserveSummaryDto(IReadOnlyList<ReserveComponentDto> Components, decimal TotalReserves, decimal TotalPending, bool ManagerOverride);

public sealed record ReserveTransactionDto
{
    public Guid Id { get; init; }
    public Guid ReserveComponentId { get; init; }
    public ReserveComponentType Component { get; set; }
    public ReserveTransactionType TransactionType { get; init; }
    public decimal Amount { get; init; }
    public decimal PreviousBalance { get; init; }
    public decimal NewBalance { get; init; }
    public ReserveApprovalStatus ApprovalStatus { get; init; }
    public ReservePostingStatus PostingStatus { get; init; }
    public string? PostingJobId { get; init; }
    public string ChangeReason { get; init; } = string.Empty;
    public int ChangeSequence { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public Guid? SubmittedByUserId { get; init; }
    public string? SubmittedByName { get; init; }
    public Guid? ApprovedByUserId { get; init; }
    public string? ApprovedByName { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public Guid? RejectedByUserId { get; init; }
    public DateTimeOffset? RejectedAt { get; init; }
    public string? RejectionReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record ReserveSubmissionDto(ReserveTransactionDto Transaction, IReadOnlyList<string> Warnings, string RequiredAuthority, bool RequiresApproval);

public sealed record ReservesDto(ReserveSummaryDto Summary, IReadOnlyList<ReserveTransactionDto> Transactions);

// ----- reference data

public sealed record PolicyDto
{
    public Guid Id { get; init; }
    public string PolicyNumber { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public DateOnly EffectiveDate { get; init; }
    public DateOnly ExpirationDate { get; init; }
    public PolicyStatus Status { get; init; }
    public IReadOnlyList<string> CoverageTypes { get; set; } = Array.Empty<string>();
}

public sealed record CauseOfLossCodeDto(string Code, string Name, PerilCategory PerilCategory);

public sealed record ClaimStatusInfoDto(ClaimStatus Status, IReadOnlyList<NextStatusDto> ValidNextStatuses);
