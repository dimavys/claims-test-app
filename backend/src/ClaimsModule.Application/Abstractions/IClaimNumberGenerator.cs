namespace ClaimsModule.Application.Abstractions;

public interface IClaimNumberGenerator
{
    /// <summary>
    /// Returns the next CLM-{YYYY}-{0000000} number. Must be called inside the claim-creation transaction so a
    /// rollback also releases the number (no gaps) while concurrent callers are serialised (no duplicates).
    /// </summary>
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
