namespace ClaimsModule.Domain.Rules;

/// <summary>CLM-{YYYY}-{7-digit zero padded sequence} (BR-C-04).</summary>
public static class ClaimNumberFormatter
{
    public const int MaxSequence = 9_999_999;

    public static string Format(int year, int sequence)
    {
        if (year is < 1000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (sequence is < 1 or > MaxSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Claim sequence exhausted for the year.");
        }

        return $"CLM-{year:D4}-{sequence:D7}";
    }
}
