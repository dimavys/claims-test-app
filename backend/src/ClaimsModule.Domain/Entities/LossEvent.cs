using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Entities;

public sealed record LossDetails(
    DateTimeOffset LossDate,
    string LossDescription,
    string CauseOfLossCode,
    string? LossLocation = null,
    decimal? EstimatedLossAmount = null,
    string? PoliceReportNumber = null);

public class LossEvent : BaseEntity
{
    public const int MinDescriptionLength = 20;

    private LossEvent() { }

    public Guid ClaimId { get; private set; }
    public DateTimeOffset LossDate { get; private set; }
    public string LossDescription { get; private set; } = string.Empty;
    public string? LossLocation { get; private set; }
    public string CauseOfLossCode { get; private set; } = string.Empty;
    public decimal? EstimatedLossAmount { get; private set; }
    public DateTimeOffset ReportDate { get; private set; }
    public string? PoliceReportNumber { get; private set; }

    internal static LossEvent Create(Guid claimId, LossDetails d, DateTimeOffset reportDate, DateTimeOffset now)
    {
        // Domain guards (BR-C-01, BR-C-07). Application validators report these as Critical issues before we get here.
        if (d.LossDate > now)
        {
            throw new DomainException(Rules.ValidationMessages.LossDateInFuture, field: "LossDate");
        }

        if (string.IsNullOrWhiteSpace(d.LossDescription) || d.LossDescription.Trim().Length < MinDescriptionLength)
        {
            throw new DomainException(Rules.ValidationMessages.LossDescription, field: "LossDescription");
        }

        if (string.IsNullOrWhiteSpace(d.CauseOfLossCode))
        {
            throw new DomainException(Rules.ValidationMessages.CauseOfLossInvalid, field: "CauseOfLossCode");
        }

        if (d.EstimatedLossAmount is < 0)
        {
            throw new DomainException("Estimated loss amount cannot be negative.", field: "EstimatedLossAmount");
        }

        return new LossEvent
        {
            ClaimId = claimId,
            LossDate = d.LossDate,
            LossDescription = d.LossDescription.Trim(),
            LossLocation = d.LossLocation?.Trim(),
            CauseOfLossCode = d.CauseOfLossCode.Trim(),
            EstimatedLossAmount = d.EstimatedLossAmount,
            ReportDate = reportDate,
            PoliceReportNumber = d.PoliceReportNumber?.Trim(),
        };
    }
}
