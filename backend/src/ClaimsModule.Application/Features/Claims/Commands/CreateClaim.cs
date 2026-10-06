using AutoMapper;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Rules;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Features.Claims.Commands;

public sealed record PartyInput(
    PartyRole PartyRole, PartyType PartyType, string? FirstName = null, string? LastName = null,
    string? CompanyName = null, string? Email = null, string? Phone = null, string? Notes = null);

public sealed record RiskObjectInput(
    AssetType AssetType, string AssetDescription, string? DamageDescription = null, string? AssetReference = null);

public sealed record InitialReserveInput(ReserveComponentType Component, decimal Amount, string? ChangeReason = null);

/// <summary>FNOL intake: creates the claim, loss event, parties, risk objects and optional initial reserve atomically.</summary>
public sealed record CreateClaimCommand(
    Guid? PolicyId,
    DateTimeOffset LossDate,
    string LossDescription,
    string CauseOfLossCode,
    string? LossLocation = null,
    decimal? EstimatedLossAmount = null,
    string? PoliceReportNumber = null,
    ClaimSeverity? Severity = null,
    IReadOnlyList<PartyInput>? Parties = null,
    IReadOnlyList<RiskObjectInput>? RiskObjects = null,
    InitialReserveInput? InitialReserve = null) : ICommand<ClaimCreatedDto>;

public sealed class PartyInputValidator : AbstractValidator<PartyInput>
{
    public PartyInputValidator()
    {
        RuleFor(p => p.PartyRole).IsInEnum().WithMessage("Invalid party role.");
        RuleFor(p => p.PartyType).IsInEnum().WithMessage("Invalid party type.");
        When(p => p.PartyType == PartyType.Person, () =>
        {
            RuleFor(p => p.FirstName).NotEmpty().WithMessage("First name is required.").MaximumLength(100);
            RuleFor(p => p.LastName).NotEmpty().WithMessage("Last name is required.").MaximumLength(100);
        });
        When(p => p.PartyType == PartyType.Company, () =>
            RuleFor(p => p.CompanyName).NotEmpty().WithMessage("Company name is required.").MaximumLength(255));
        RuleFor(p => p.Email).EmailAddress().MaximumLength(255).When(p => !string.IsNullOrWhiteSpace(p.Email));
        RuleFor(p => p.Phone).MaximumLength(50);
    }
}

public sealed class RiskObjectInputValidator : AbstractValidator<RiskObjectInput>
{
    public RiskObjectInputValidator()
    {
        RuleFor(r => r.AssetType).IsInEnum().WithMessage("Invalid asset type.");
        RuleFor(r => r.AssetDescription).NotEmpty().WithMessage("Asset description is required.").MaximumLength(500);
        RuleFor(r => r.AssetReference).MaximumLength(255);
    }
}

public static class ReserveRules
{
    /// <summary>BR-R-01: amounts are positive, except SubrogationRecoverable which may be negative (never zero).</summary>
    public static bool IsValidAmount(ReserveComponentType component, decimal amount) =>
        ReserveAuthorityPolicy.AllowsNegativeBalance(component) ? amount != 0 : amount > 0;
}

public sealed class CreateClaimCommandValidator : AbstractValidator<CreateClaimCommand>
{
    public CreateClaimCommandValidator(IReferenceDataRepository reference, TimeProvider time)
    {
        RuleFor(c => c.LossDate)
            .Must(d => d != default).WithMessage(ValidationMessages.LossDateRequired)
            .DependentRules(() => RuleFor(c => c.LossDate)
                .Must(d => d <= time.GetUtcNow()).WithMessage(ValidationMessages.LossDateInFuture));

        RuleFor(c => c.LossDescription)
            .Must(d => !string.IsNullOrWhiteSpace(d) && d.Trim().Length >= LossEvent.MinDescriptionLength)
            .WithMessage(ValidationMessages.LossDescription);

        RuleFor(c => c.CauseOfLossCode)
            .NotEmpty().WithMessage(ValidationMessages.CauseOfLossInvalid)
            .MustAsync(async (code, ct) => await reference.GetActiveCauseOfLossCodeAsync(code, ct) is not null)
            .WithMessage(ValidationMessages.CauseOfLossInvalid)
            .When(c => !string.IsNullOrWhiteSpace(c.CauseOfLossCode));

        RuleFor(c => c.LossLocation).MaximumLength(500);
        RuleFor(c => c.PoliceReportNumber).MaximumLength(100);
        RuleFor(c => c.EstimatedLossAmount).GreaterThanOrEqualTo(0).When(c => c.EstimatedLossAmount.HasValue)
            .WithMessage("Estimated loss amount cannot be negative.");
        RuleFor(c => c.Severity).IsInEnum().When(c => c.Severity.HasValue);

        RuleForEach(c => c.Parties).SetValidator(new PartyInputValidator());
        RuleForEach(c => c.RiskObjects).SetValidator(new RiskObjectInputValidator());

        When(c => c.InitialReserve is not null, () =>
        {
            RuleFor(c => c.PolicyId).NotNull().WithMessage(ValidationMessages.NoPolicy);
            RuleFor(c => c.InitialReserve!.Component).IsInEnum().WithMessage(ValidationMessages.ReserveComponent);
            RuleFor(c => c.InitialReserve!).Must(r => ReserveRules.IsValidAmount(r.Component, r.Amount))
                .WithMessage(ValidationMessages.ReserveAmount).WithName("InitialReserve.Amount");
        });
    }
}

public sealed class CreateClaimHandler(
    IClaimRepository claims,
    IReferenceDataRepository reference,
    IClaimNumberGenerator numbers,
    ICurrentUserService currentUser,
    TimeProvider time,
    IMapper mapper) : IRequestHandler<CreateClaimCommand, ClaimCreatedDto>
{
    public async Task<ClaimCreatedDto> Handle(CreateClaimCommand cmd, CancellationToken ct)
    {
        var (userId, _) = currentUser.Require();
        var now = time.GetUtcNow();

        Policy? policy = null;
        if (cmd.PolicyId is { } policyId)
        {
            policy = await reference.GetPolicyAsync(policyId, ct)
                ?? throw new RequestValidationException(nameof(cmd.PolicyId), "The selected policy was not found.");
        }

        var cause = await reference.GetActiveCauseOfLossCodeAsync(cmd.CauseOfLossCode, ct)
            ?? throw new RequestValidationException(nameof(cmd.CauseOfLossCode), ValidationMessages.CauseOfLossInvalid);

        var parties = (cmd.Parties ?? Array.Empty<PartyInput>())
            .Select(p => ClaimParty.Create(p.PartyRole, p.PartyType, p.FirstName, p.LastName, p.CompanyName, p.Email, p.Phone, p.Notes))
            .ToList();
        var riskObjects = (cmd.RiskObjects ?? Array.Empty<RiskObjectInput>())
            .Select(r => ClaimRiskObject.Create(r.AssetType, r.AssetDescription, r.DamageDescription, r.AssetReference))
            .ToList();

        // Without a policy the client name falls back to the claimant so list/search screens stay meaningful.
        var clientName = policy?.ClientName
            ?? (parties.FirstOrDefault(p => p.PartyRole == PartyRole.Claimant) ?? parties.FirstOrDefault())?.DisplayName
            ?? "Unknown policyholder";

        // Runs inside the command transaction: the counter row stays locked until commit, so numbers are gap-free.
        var claimNumber = await numbers.NextAsync(ct);

        var claim = Claim.Create(
            claimNumber, policy, clientName, cmd.Severity ?? ClaimSeverity.Standard, now,
            new LossDetails(cmd.LossDate, cmd.LossDescription, cause.Code, cmd.LossLocation, cmd.EstimatedLossAmount, cmd.PoliceReportNumber),
            now, userId, assignedHandlerId: userId, claimType: cause.PerilCategory.ToString(),
            parties: parties, riskObjects: riskObjects);

        ReserveSubmissionDto? reserve = null;
        if (cmd.InitialReserve is { } initial)
        {
            var result = claim.SubmitReserve(
                initial.Component, ReserveTransactionType.Add, initial.Amount,
                string.IsNullOrWhiteSpace(initial.ChangeReason) ? "Initial reserve at FNOL" : initial.ChangeReason,
                now, userId);
            reserve = ClaimDtoFactory.ToSubmissionDto(claim, result, mapper);
        }

        await claims.AddAsync(claim, ct);

        return new ClaimCreatedDto(
            claim.Id, claim.ClaimNumber, claim.Status,
            claim.ValidationIssues.Select(mapper.Map<ValidationIssueDto>).ToList(),
            reserve);
    }
}
