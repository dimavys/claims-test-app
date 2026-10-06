using AutoMapper;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Features.Claims.Commands;

internal static class ClaimLoader
{
    public static async Task<Claim> LoadForUpdateAsync(this IClaimRepository claims, Guid id, CancellationToken ct, ClaimIncludes includes = ClaimIncludes.ForUpdate) =>
        await claims.GetByIdAsync(id, includes, track: true, ct) ?? throw new NotFoundException(nameof(Claim), id);
}

// ------------------------------------------------------------------ status transition

public sealed record TransitionClaimStatusCommand(
    Guid ClaimId,
    ClaimStatus TargetStatus,
    string? Reason = null,
    bool AcknowledgeWarnings = false,
    string? ClosureJustification = null) : ICommand<ClaimStatusChangedDto>;

public sealed class TransitionClaimStatusValidator : AbstractValidator<TransitionClaimStatusCommand>
{
    public TransitionClaimStatusValidator()
    {
        RuleFor(c => c.TargetStatus).IsInEnum().WithMessage("Unknown target status.");
        RuleFor(c => c.Reason).MaximumLength(500);
        RuleFor(c => c.ClosureJustification).MaximumLength(1000);
    }
}

public sealed class TransitionClaimStatusHandler(IClaimRepository claims, ICurrentUserService currentUser, TimeProvider time)
    : IRequestHandler<TransitionClaimStatusCommand, ClaimStatusChangedDto>
{
    public async Task<ClaimStatusChangedDto> Handle(TransitionClaimStatusCommand cmd, CancellationToken ct)
    {
        var (userId, role) = currentUser.Require();
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct);
        var previous = claim.Status;

        claim.TransitionTo(cmd.TargetStatus, role, time.GetUtcNow(), userId, cmd.Reason, cmd.AcknowledgeWarnings, cmd.ClosureJustification);

        return new ClaimStatusChangedDto(claim.Id, previous, claim.Status, ClaimDtoFactory.NextStatuses(claim.Status));
    }
}

// ------------------------------------------------------------------ parties

public sealed record AddClaimPartyCommand(Guid ClaimId, PartyInput Party) : ICommand<ClaimPartyDto>;

public sealed class AddClaimPartyValidator : AbstractValidator<AddClaimPartyCommand>
{
    public AddClaimPartyValidator() => RuleFor(c => c.Party).NotNull().SetValidator(new PartyInputValidator());
}

public sealed class AddClaimPartyHandler(IClaimRepository claims, TimeProvider time, IMapper mapper)
    : IRequestHandler<AddClaimPartyCommand, ClaimPartyDto>
{
    public async Task<ClaimPartyDto> Handle(AddClaimPartyCommand cmd, CancellationToken ct)
    {
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct);
        var p = cmd.Party;
        var party = claim.AddParty(
            ClaimParty.Create(p.PartyRole, p.PartyType, p.FirstName, p.LastName, p.CompanyName, p.Email, p.Phone, p.Notes),
            time.GetUtcNow());
        return mapper.Map<ClaimPartyDto>(party);
    }
}

public sealed record RemoveClaimPartyCommand(Guid ClaimId, Guid PartyId) : ICommand<Unit>;

public sealed class RemoveClaimPartyHandler(IClaimRepository claims, TimeProvider time)
    : IRequestHandler<RemoveClaimPartyCommand, Unit>
{
    public async Task<Unit> Handle(RemoveClaimPartyCommand cmd, CancellationToken ct)
    {
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct);
        claim.RemoveParty(cmd.PartyId, time.GetUtcNow());
        return Unit.Value;
    }
}

// ------------------------------------------------------------------ notes & override

public sealed record UpdateClaimNotesCommand(Guid ClaimId, string? Notes) : ICommand<Unit>;

public sealed class UpdateClaimNotesHandler(IClaimRepository claims) : IRequestHandler<UpdateClaimNotesCommand, Unit>
{
    public async Task<Unit> Handle(UpdateClaimNotesCommand cmd, CancellationToken ct)
    {
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct, ClaimIncludes.None);
        claim.UpdateNotes(cmd.Notes);
        return Unit.Value;
    }
}

/// <summary>Manager sets (or clears) the flag that allows total reserves above $10,000,000 (BR-R-05).</summary>
public sealed record SetManagerOverrideCommand(Guid ClaimId, bool Value = true) : ICommand<Unit>;

public sealed class SetManagerOverrideHandler(IClaimRepository claims, ICurrentUserService currentUser)
    : IRequestHandler<SetManagerOverrideCommand, Unit>
{
    public async Task<Unit> Handle(SetManagerOverrideCommand cmd, CancellationToken ct)
    {
        var (_, role) = currentUser.Require();
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct, ClaimIncludes.None);
        claim.SetManagerOverride(role, cmd.Value);
        return Unit.Value;
    }
}
