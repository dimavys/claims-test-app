namespace ClaimsModule.Domain.Common;

/// <summary>
/// Generates GUIDs that sort in SQL Server's uniqueidentifier order (same algorithm as EF Core's
/// SequentialGuidValueGenerator). IDs are assigned in the domain so that aggregates can reference each other
/// (audit entries, idempotency keys) before SaveChanges; the column still carries DEFAULT NEWSEQUENTIALID().
/// </summary>
public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    public static Guid Next()
    {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var counterBytes = BitConverter.GetBytes(Interlocked.Increment(ref _counter));
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        guidBytes[08] = counterBytes[1];
        guidBytes[09] = counterBytes[0];
        guidBytes[10] = counterBytes[7];
        guidBytes[11] = counterBytes[6];
        guidBytes[12] = counterBytes[5];
        guidBytes[13] = counterBytes[4];
        guidBytes[14] = counterBytes[3];
        guidBytes[15] = counterBytes[2];
        return new Guid(guidBytes);
    }
}
