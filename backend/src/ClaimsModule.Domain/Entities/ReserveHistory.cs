using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Entities;

/// <summary>
/// One reserve transaction. Reserves are event-sourced: transactions are inserted, never updated in value;
/// the component balance is the sum of approved transactions (FRS §6.6). Only the approval/posting lifecycle
/// fields change after creation.
/// </summary>
public class ReserveHistory : BaseEntity
{
    private ReserveHistory() { }

    public Guid ReserveComponentId { get; private set; }
    public Guid ClaimId { get; private set; }
    public ReserveTransactionType TransactionType { get; private set; }

    /// <summary>Signed delta applied to the component balance when approved.</summary>
    public decimal Amount { get; private set; }

    public decimal PreviousBalance { get; private set; }
    public decimal NewBalance { get; private set; }
    public ReserveApprovalStatus ApprovalStatus { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public Guid? RejectedByUserId { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public string ChangeReason { get; private set; } = string.Empty;
    public ReservePostingStatus PostingStatus { get; private set; }
    public string? PostingJobId { get; private set; }

    /// <summary>Reserve:{ReserveComponentId}:Change:{ChangeSequence}</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public int ChangeSequence { get; private set; }
    public Guid? SubmittedByUserId { get; private set; }

    public bool IsApproved => ApprovalStatus is ReserveApprovalStatus.Approved or ReserveApprovalStatus.AutoApproved;
    public bool IsPending => ApprovalStatus == ReserveApprovalStatus.PendingApproval;

    public static string BuildIdempotencyKey(Guid reserveComponentId, int changeSequence) =>
        $"Reserve:{reserveComponentId}:Change:{changeSequence}";

    internal static ReserveHistory Create(
        ClaimReserveComponent component, ReserveTransactionType type, decimal delta, string changeReason,
        Guid? submittedBy, int changeSequence) => new()
    {
        ReserveComponentId = component.Id,
        ClaimId = component.ClaimId,
        TransactionType = type,
        Amount = delta,
        PreviousBalance = component.CurrentAmount,
        NewBalance = component.CurrentAmount + delta,
        ApprovalStatus = ReserveApprovalStatus.PendingApproval,
        ChangeReason = changeReason.Trim(),
        PostingStatus = ReservePostingStatus.Pending,
        ChangeSequence = changeSequence,
        IdempotencyKey = BuildIdempotencyKey(component.Id, changeSequence),
        SubmittedByUserId = submittedBy,
    };

    internal void AutoApprove(DateTimeOffset now)
    {
        ApprovalStatus = ReserveApprovalStatus.AutoApproved;
        ApprovedAt = now;
    }

    internal void Approve(Guid approverId, DateTimeOffset now)
    {
        ApprovalStatus = ReserveApprovalStatus.Approved;
        ApprovedByUserId = approverId;
        ApprovedAt = now;
    }

    internal void Reject(Guid rejecterId, string reason, DateTimeOffset now)
    {
        ApprovalStatus = ReserveApprovalStatus.Rejected;
        RejectedByUserId = rejecterId;
        RejectedAt = now;
        RejectionReason = reason.Trim();
        PostingStatus = ReservePostingStatus.Cancelled;
    }

    internal void Retract()
    {
        ApprovalStatus = ReserveApprovalStatus.Cancelled;
        PostingStatus = ReservePostingStatus.Cancelled;
    }

    /// <summary>Balances are (re)computed at approval time because other approvals may have landed since submission.</summary>
    internal void SetBalances(decimal previous, decimal next)
    {
        PreviousBalance = previous;
        NewBalance = next;
    }

    /// <summary>
    /// Idempotent GL posting acknowledgement. Returns false when the transaction was already posted, so a retried
    /// or duplicated job writes nothing twice.
    /// </summary>
    public bool TryMarkPosted(string? jobId)
    {
        if (PostingStatus == ReservePostingStatus.Posted)
        {
            return false;
        }

        if (!IsApproved)
        {
            throw new DomainException("Only approved reserve transactions can be posted to the GL.", DomainErrorKind.Conflict);
        }

        PostingStatus = ReservePostingStatus.Posted;
        PostingJobId = jobId ?? PostingJobId;
        return true;
    }

    /// <summary>Called once Hangfire has exhausted its retries.</summary>
    public void MarkPostingFailed()
    {
        if (PostingStatus != ReservePostingStatus.Posted)
        {
            PostingStatus = ReservePostingStatus.Failed;
        }
    }

    /// <summary>Records the Hangfire job id of the latest posting attempt.</summary>
    public void SetPostingJob(string jobId) => PostingJobId = jobId;

    /// <summary>Failed → Pending so the GL job can be queued again from the UI's retry button.</summary>
    internal void RequestPostingRetry()
    {
        if (PostingStatus != ReservePostingStatus.Failed)
        {
            throw new DomainException("Only failed GL postings can be retried.", DomainErrorKind.Conflict, "PostingStatus");
        }

        PostingStatus = ReservePostingStatus.Pending;
    }
}
