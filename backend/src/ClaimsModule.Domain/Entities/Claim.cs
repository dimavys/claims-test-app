using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Events;
using ClaimsModule.Domain.Rules;

namespace ClaimsModule.Domain.Entities;

public sealed record ReserveSubmissionResult(ReserveHistory Transaction, IReadOnlyList<string> Warnings);

/// <summary>
/// Claim aggregate root: owns the loss event, parties, risk objects, validation issues, reserves and documents,
/// and enforces the lifecycle and reserve-authority business rules (FRS §4–§7).
/// </summary>
public class Claim : AggregateRoot
{
    private readonly List<ClaimParty> _parties = new();
    private readonly List<ClaimRiskObject> _riskObjects = new();
    private readonly List<ClaimReserveComponent> _reserveComponents = new();
    private readonly List<ClaimDocument> _documents = new();
    private readonly List<ClaimValidationIssue> _validationIssues = new();

    private Claim() { }

    public string ClaimNumber { get; private set; } = string.Empty;
    public Guid? PolicyId { get; private set; }
    public string? PolicyNumber { get; private set; }
    public string ClientName { get; private set; } = string.Empty;
    public ClaimStatus Status { get; private set; }
    public ClaimSeverity Severity { get; private set; }
    public string? ClaimType { get; private set; }
    public DateTimeOffset ReportedDate { get; private set; }
    public Guid? AssignedHandlerId { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosureReason { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Set by a Manager to allow total reserves above the aggregate limit (BR-R-05).</summary>
    public bool ManagerOverride { get; private set; }

    public LossEvent LossEvent { get; private set; } = null!;
    public IReadOnlyCollection<ClaimParty> Parties => _parties;
    public IReadOnlyCollection<ClaimRiskObject> RiskObjects => _riskObjects;
    public IReadOnlyCollection<ClaimReserveComponent> ReserveComponents => _reserveComponents;
    public IReadOnlyCollection<ClaimDocument> Documents => _documents;
    public IReadOnlyCollection<ClaimValidationIssue> ValidationIssues => _validationIssues;

    public decimal TotalReserves => _reserveComponents.Sum(c => c.CurrentAmount);

    private IEnumerable<ClaimParty> ActiveClaimants =>
        _parties.Where(p => p.IsActive && p.PartyRole == PartyRole.Claimant);

    // ------------------------------------------------------------------ creation

    public static Claim Create(
        string claimNumber,
        Policy? policy,
        string clientName,
        ClaimSeverity severity,
        DateTimeOffset reportedDate,
        LossDetails loss,
        DateTimeOffset now,
        Guid? actorId = null,
        Guid? assignedHandlerId = null,
        string? claimType = null,
        string? notes = null,
        IEnumerable<ClaimParty>? parties = null,
        IEnumerable<ClaimRiskObject>? riskObjects = null)
    {
        if (string.IsNullOrWhiteSpace(claimNumber))
        {
            throw new DomainException("Claim number is required.", field: "ClaimNumber");
        }

        if (string.IsNullOrWhiteSpace(clientName))
        {
            throw new DomainException("Client name is required.", field: "ClientName");
        }

        var claim = new Claim
        {
            ClaimNumber = claimNumber,
            PolicyId = policy?.Id,
            PolicyNumber = policy?.PolicyNumber,
            ClientName = clientName.Trim(),
            Status = ClaimStatus.Draft,
            Severity = severity,
            ClaimType = claimType,
            ReportedDate = reportedDate,
            AssignedHandlerId = assignedHandlerId,
            Notes = notes,
        };

        claim.LossEvent = LossEvent.Create(claim.Id, loss, reportedDate, now);

        if (policy is null)
        {
            claim.RaiseIssue(ValidationSeverity.Warning, ValidationIssueCodes.NoPolicy, "PolicyId",
                ValidationMessages.NoPolicy, false);
        }
        else if (!policy.Covers(loss.LossDate))
        {
            claim.RaiseIssue(ValidationSeverity.Warning, ValidationIssueCodes.LossDateOutsidePolicy, "LossDate",
                ValidationMessages.LossDateOutsidePolicy, true);
        }

        // Initial parties/risk objects are attached before the structural issues are evaluated, so an intake that
        // supplies a claimant never records (and immediately resolves) a "no claimant" issue.
        foreach (var party in parties ?? Enumerable.Empty<ClaimParty>())
        {
            claim.AttachParty(party);
        }

        foreach (var riskObject in riskObjects ?? Enumerable.Empty<ClaimRiskObject>())
        {
            claim.AttachRiskObject(riskObject);
        }

        claim.SyncStructuralIssues(now);
        claim.Raise(new ClaimCreatedDomainEvent(claim.Id, claimNumber, claim.PolicyId, claim.LossEvent.CauseOfLossCode, actorId));
        return claim;
    }

    // ------------------------------------------------------------------ parties & risk objects

    public ClaimParty AddParty(ClaimParty party, DateTimeOffset now)
    {
        AttachParty(party);
        SyncStructuralIssues(now);
        return party;
    }

    private void AttachParty(ClaimParty party)
    {
        party.Attach(Id);
        _parties.Add(party);
        Raise(new PartyAddedDomainEvent(Id, party.Id, party.PartyRole, party.DisplayName));
    }

    /// <summary>
    /// Soft-removes a party. The last active Claimant cannot be removed (FRS §10.1, BR-P-01: a claim keeps at least one Claimant).
    /// </summary>
    public void RemoveParty(Guid partyId, DateTimeOffset now)
    {
        var party = _parties.FirstOrDefault(p => p.Id == partyId && p.IsActive)
            ?? throw DomainException.NotFound("Party not found on this claim.");

        if (party.PartyRole == PartyRole.Claimant && ActiveClaimants.Count() == 1)
        {
            throw new DomainException("The last Claimant cannot be removed from the claim.", field: "ClaimParties");
        }

        party.Deactivate();
        SyncStructuralIssues(now);
        Raise(new PartyRemovedDomainEvent(Id, party.Id, party.PartyRole, party.DisplayName));
    }

    public ClaimRiskObject AddRiskObject(ClaimRiskObject riskObject, DateTimeOffset now)
    {
        AttachRiskObject(riskObject);
        SyncStructuralIssues(now);
        return riskObject;
    }

    private void AttachRiskObject(ClaimRiskObject riskObject)
    {
        riskObject.Attach(Id, isPrimary: _riskObjects.Count == 0);
        _riskObjects.Add(riskObject);
    }

    public void UpdateNotes(string? notes) => Notes = notes;

    public void AssignHandler(Guid handlerId) => AssignedHandlerId = handlerId;

    // ------------------------------------------------------------------ validation issues

    /// <summary>Acknowledges every outstanding warning that requires acknowledgement (BR-C-02).</summary>
    public void AcknowledgeWarnings(DateTimeOffset now, Guid? userId)
    {
        foreach (var issue in OutstandingAcknowledgeable())
        {
            issue.Acknowledge(now, userId);
        }
    }

    private IEnumerable<ClaimValidationIssue> OutstandingAcknowledgeable() =>
        _validationIssues.Where(i => i.IsOutstanding && i.Severity == ValidationSeverity.Warning && i.RequiresAcknowledgement);

    private void RaiseIssue(ValidationSeverity severity, string code, string? field, string message, bool requiresAck)
    {
        if (_validationIssues.Any(i => i.IsOutstanding && i.Code == code))
        {
            return;
        }

        var issue = ClaimValidationIssue.Create(Id, severity, code, field, message, requiresAck);
        _validationIssues.Add(issue);
        Raise(new ValidationIssueAddedDomainEvent(Id, issue.Id, severity, code, message));
    }

    private void ResolveIssues(string code, DateTimeOffset now)
    {
        foreach (var issue in _validationIssues.Where(i => i.IsOutstanding && i.Code == code))
        {
            issue.Resolve(now);
        }
    }

    /// <summary>
    /// Keeps the claimant (Critical) and risk-object (Warning) issues in step with the claim's contents.
    /// FRS BR-C-03 / BR-P-01: having no Claimant is a Critical issue that blocks Draft → Open.
    /// </summary>
    private void SyncStructuralIssues(DateTimeOffset now)
    {
        if (ActiveClaimants.Any())
        {
            ResolveIssues(ValidationIssueCodes.NoClaimant, now);
        }
        else
        {
            RaiseIssue(ValidationSeverity.Critical, ValidationIssueCodes.NoClaimant, "ClaimParties",
                ValidationMessages.NoClaimant, false);
        }

        if (_riskObjects.Count > 0)
        {
            ResolveIssues(ValidationIssueCodes.NoRiskObjects, now);
        }
        else
        {
            RaiseIssue(ValidationSeverity.Warning, ValidationIssueCodes.NoRiskObjects, "RiskObjects",
                ValidationMessages.NoRiskObjects, false);
        }
    }

    // ------------------------------------------------------------------ status transitions

    public void TransitionTo(
        ClaimStatus target,
        UserRole actorRole,
        DateTimeOffset now,
        Guid? actorId = null,
        string? reason = null,
        bool acknowledgeWarnings = false,
        string? closureJustification = null)
    {
        var from = Status;
        var rule = ClaimStateMachine.Find(from, target)
            ?? throw ClaimTransitionException.NotPermitted(from, target, ClaimStateMachine.ValidNextStatuses(from));

        if (actorRole < rule.MinimumRole)
        {
            throw DomainException.Authority($"The {rule.MinimumRole} role is required to move a claim from {from} to {target}.");
        }

        var blocking = new List<string>();

        switch (target)
        {
            case ClaimStatus.Open when from == ClaimStatus.Draft:
                // FRS BR-ST-02 / BR-C-03: no Critical issues and at least one Claimant; BR-C-02: warnings cleared or acknowledged.
                if (_validationIssues.Any(i => i.IsOutstanding && i.Severity == ValidationSeverity.Critical))
                {
                    blocking.Add("Unresolved Critical validation issues remain.");
                }

                if (!ActiveClaimants.Any())
                {
                    blocking.Add(ValidationMessages.NoClaimant);
                }

                if (!acknowledgeWarnings && OutstandingAcknowledgeable().Any())
                {
                    blocking.Add("Warnings must be cleared or acknowledged before opening: " +
                        string.Join(" ", OutstandingAcknowledgeable().Select(i => i.Message)));
                }

                break;

            case ClaimStatus.PendingPayment:
                if (!_reserveComponents.Any(c => c.History.Any(h => h.IsApproved)))
                {
                    blocking.Add("At least one approved reserve is required.");
                }

                break;

            case ClaimStatus.Closed:
                blocking.AddRange(ClosureBlockers(closureJustification));
                break;

            case ClaimStatus.Withdrawn:
                if (string.IsNullOrWhiteSpace(reason))
                {
                    blocking.Add("A withdrawal reason is required.");
                }

                break;

            case ClaimStatus.Reopened:
                if (string.IsNullOrWhiteSpace(reason))
                {
                    blocking.Add("A reopen reason is required.");
                }

                break;
        }

        if (blocking.Count > 0)
        {
            throw ClaimTransitionException.Blocked(from, target, blocking);
        }

        if (acknowledgeWarnings)
        {
            AcknowledgeWarnings(now, actorId);
        }

        Status = target;
        Raise(new ClaimStatusChangedDomainEvent(Id, from, target, reason, actorId));

        switch (target)
        {
            case ClaimStatus.Open when from == ClaimStatus.Draft:
                AssignedHandlerId ??= actorId;
                break;

            case ClaimStatus.Closed:
                ClosedAt = now;
                ClosureReason = string.IsNullOrWhiteSpace(reason) ? closureJustification : reason;
                break;

            case ClaimStatus.Withdrawn:
                ClosureReason = reason;
                break;

            case ClaimStatus.Reopened:
                // Reopened is transient: the claim immediately becomes Open again (BR-ST-04).
                ClosedAt = null;
                ClosureReason = null;
                Status = ClaimStatus.Open;
                Raise(new ClaimStatusChangedDomainEvent(Id, ClaimStatus.Reopened, ClaimStatus.Open, reason, actorId));
                break;
        }
    }

    /// <summary>
    /// Closure pre-flight: FRS §4.3 conditions CC-01..CC-04, enforced by BR-ST-03. Empty means the claim can be closed.
    /// </summary>
    public IReadOnlyList<string> ClosureBlockers(string? closureJustification = null)
    {
        var blockers = new List<string>();

        if (_reserveComponents.Any(c => c.History.Any(h => h.IsPending)))
        {
            blockers.Add("Reserve transactions pending approval remain.");
        }

        if (_validationIssues.Any(i => i.IsOutstanding && i.Severity == ValidationSeverity.Critical))
        {
            blockers.Add("Unresolved Critical validation issues remain.");
        }

        if (!ActiveClaimants.Any())
        {
            blockers.Add("At least one Claimant party is required.");
        }

        var openBalance = _reserveComponents.Where(c => c.CurrentAmount > 0).Sum(c => c.CurrentAmount);
        if (openBalance > 0 && string.IsNullOrWhiteSpace(closureJustification))
        {
            blockers.Add($"Open reserves of {openBalance:N2} remain; a justification note is required to close with open reserves.");
        }

        return blockers;
    }

    // ------------------------------------------------------------------ reserves

    public ReserveSubmissionResult SubmitReserve(
        ReserveComponentType componentType,
        ReserveTransactionType transactionType,
        decimal amount,
        string changeReason,
        DateTimeOffset now,
        Guid? submitterId = null)
    {
        if (PolicyId is null)
        {
            throw new DomainException(
                ValidationMessages.NoPolicy, field: "PolicyId");
        }

        if (Status is ClaimStatus.Closed or ClaimStatus.Withdrawn)
        {
            throw new DomainException($"Reserves cannot be changed on a {Status} claim.", field: "Status");
        }

        if (!Enum.IsDefined(componentType))
        {
            throw new DomainException(ValidationMessages.ReserveComponent, field: "ReserveComponent");
        }

        if (string.IsNullOrWhiteSpace(changeReason))
        {
            throw new DomainException("A change reason is required.", field: "ChangeReason");
        }

        var allowsNegative = ReserveAuthorityPolicy.AllowsNegativeBalance(componentType);
        if (amount == 0 || (amount < 0 && !allowsNegative))
        {
            throw new DomainException(ValidationMessages.ReserveAmount, field: "ReserveAmount");
        }

        // The request amount is a magnitude; the transaction type decides the direction of the delta.
        var delta = transactionType == ReserveTransactionType.Reverse ? -amount : amount;

        var component = _reserveComponents.FirstOrDefault(c => c.Component == componentType);
        if (component is null)
        {
            component = new ClaimReserveComponent(Id, componentType);
            _reserveComponents.Add(component);
        }

        if (!allowsNegative && component.CurrentAmount + delta < 0)
        {
            throw new DomainException("The reserve balance for this component cannot go negative.", field: "ReserveAmount");
        }

        var warnings = new List<string>();
        var exceedsAggregate = delta > 0 && TotalReserves + delta > ReserveAuthorityPolicy.AggregateLimit;
        if (exceedsAggregate && !ManagerOverride)
        {
            const string message = ValidationMessages.AggregateLimit;
            warnings.Add(message);
            RaiseIssue(ValidationSeverity.Warning, ValidationIssueCodes.AggregateReserveLimit, "ReserveAmount", message, false);
        }

        var txn = component.AddTransaction(transactionType, delta, changeReason, submitterId);
        var autoApprove = ReserveAuthorityPolicy.RequiredLevel(delta) == AuthorityLevel.Auto && !(exceedsAggregate && !ManagerOverride);

        if (autoApprove)
        {
            txn.AutoApprove(now);
            component.ApplyApproved(txn);
        }

        Raise(new ReserveSubmittedDomainEvent(Id, component.Id, txn.Id, componentType, delta, txn.ApprovalStatus, submitterId));
        if (autoApprove)
        {
            Raise(new ReserveApprovedDomainEvent(Id, component.Id, txn.Id, componentType, delta, AutoApproved: true, txn.IdempotencyKey, submitterId));
        }

        return new ReserveSubmissionResult(txn, warnings);
    }

    public ReserveHistory ApproveReserve(Guid transactionId, Guid approverId, UserRole approverRole, DateTimeOffset now)
    {
        var (component, txn) = FindTransaction(transactionId);

        if (!txn.IsPending)
        {
            throw new DomainException("Only reserves pending approval can be approved.", DomainErrorKind.Conflict, "ReserveApproval");
        }

        if (!ReserveAuthorityPolicy.CanApprove(approverRole, txn.Amount))
        {
            throw DomainException.Authority(ValidationMessages.ApproverAuthority);
        }

        // FRS BR-R-03: a user may never approve their own reserve, even when their role covers the amount.
        if (txn.SubmittedByUserId == approverId)
        {
            throw new DomainException(ValidationMessages.SelfApproval, field: "ReserveApproval");
        }

        if (!component.AllowsNegativeBalance && component.CurrentAmount + txn.Amount < 0)
        {
            throw new DomainException("The reserve balance for this component cannot go negative.", field: "ReserveAmount");
        }

        // FRS BR-R-05 (Brief BR-R-07): beyond $10,000,000 in total, approval needs the Manager override flag.
        if (txn.Amount > 0 && TotalReserves + txn.Amount > ReserveAuthorityPolicy.AggregateLimit && !ManagerOverride)
        {
            throw new DomainException(
                ValidationMessages.AggregateLimit, DomainErrorKind.Validation, "ReserveAmount");
        }

        txn.Approve(approverId, now);
        component.ApplyApproved(txn);
        Raise(new ReserveApprovedDomainEvent(Id, component.Id, txn.Id, component.Component, txn.Amount, AutoApproved: false, txn.IdempotencyKey, approverId));
        return txn;
    }

    /// <summary>
    /// Rejects a pending reserve (supervisor/manager). FRS BR-R-04: the rejected record stays in the history with its reason,
    /// and the submitter may submit a new transaction with a revised amount.
    /// </summary>
    public ReserveHistory RejectReserve(Guid transactionId, Guid rejecterId, UserRole rejecterRole, string reason, DateTimeOffset now)
    {
        var (component, txn) = FindTransaction(transactionId);

        if (!txn.IsPending)
        {
            throw new DomainException("Only reserves pending approval can be rejected.", DomainErrorKind.Conflict, "ReserveApproval");
        }

        if (!ReserveAuthorityPolicy.CanApprove(rejecterRole, txn.Amount))
        {
            throw DomainException.Authority("Your role does not have authority to reject this reserve amount.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection reason is required.", field: "RejectionReason");
        }

        txn.Reject(rejecterId, reason, now);
        Raise(new ReserveRejectedDomainEvent(Id, component.Id, txn.Id, txn.Amount, reason, rejecterId));
        return txn;
    }

    /// <summary>The submitter withdraws their own pending reserve (status → Cancelled) before approval.</summary>
    public ReserveHistory RetractReserve(Guid transactionId, Guid userId)
    {
        var (component, txn) = FindTransaction(transactionId);

        if (!txn.IsPending)
        {
            throw new DomainException("Only reserves pending approval can be retracted.", DomainErrorKind.Conflict, "ReserveApproval");
        }

        if (txn.SubmittedByUserId != userId)
        {
            throw DomainException.Authority("Only the submitter can retract a pending reserve.");
        }

        txn.Retract();
        Raise(new ReserveRetractedDomainEvent(Id, component.Id, txn.Id, txn.Amount, userId));
        return txn;
    }

    public ReserveHistory RetryGlPosting(Guid transactionId)
    {
        var (component, txn) = FindTransaction(transactionId);
        txn.RequestPostingRetry();
        Raise(new GlPostingRetryRequestedDomainEvent(Id, component.Id, txn.Id, txn.IdempotencyKey));
        return txn;
    }

    public void SetManagerOverride(UserRole role, bool value = true)
    {
        if (role < UserRole.Manager)
        {
            throw DomainException.Authority("Only a Manager can set the reserve override flag.");
        }

        ManagerOverride = value;
    }

    private (ClaimReserveComponent Component, ReserveHistory Transaction) FindTransaction(Guid transactionId)
    {
        foreach (var component in _reserveComponents)
        {
            var txn = component.History.FirstOrDefault(h => h.Id == transactionId);
            if (txn is not null)
            {
                return (component, txn);
            }
        }

        throw DomainException.NotFound("Reserve transaction not found on this claim.");
    }

    // ------------------------------------------------------------------ documents

    public ClaimDocument AddDocument(
        string documentType, string documentName, string blobPath, string contentType, long fileSizeBytes,
        DateTimeOffset now, Guid? uploadedBy = null, string? notes = null)
    {
        var document = ClaimDocument.Create(Id, documentType, documentName, blobPath, contentType, fileSizeBytes, uploadedBy, notes, now);
        _documents.Add(document);
        Raise(new DocumentUploadedDomainEvent(Id, document.Id, document.DocumentName, uploadedBy));
        return document;
    }
}
