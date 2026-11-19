namespace HogwartsRP.Resources;

/// <summary>
/// Defines a bounded retry schedule for Rust worker recovery. This policy is opt-in;
/// a zero retry count preserves fail-fast behavior.
/// </summary>
public sealed record RustWorkerRestartPolicy
{
    public int MaxRestarts { get; }
    public TimeSpan InitialDelay { get; }
    public TimeSpan MaxDelay { get; }

    public RustWorkerRestartPolicy(int maxRestarts = 0, TimeSpan? initialDelay = null, TimeSpan? maxDelay = null)
    {
        if (maxRestarts is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(maxRestarts), "MaxRestarts must be between 0 and 10.");
        var initial = initialDelay ?? TimeSpan.FromSeconds(1);
        var maximum = maxDelay ?? TimeSpan.FromSeconds(30);
        if (initial < TimeSpan.Zero || initial > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(initialDelay), "Initial delay must be between zero and 30 seconds.");
        if (maximum < initial || maximum > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "Maximum delay must be at least the initial delay and no more than five minutes.");
        MaxRestarts = maxRestarts;
        InitialDelay = initial;
        MaxDelay = maximum;
    }

    /// <summary>Returns the capped exponential delay for a one-based retry attempt.</summary>
    public TimeSpan GetDelay(int attempt)
    {
        if (attempt < 1 || attempt > MaxRestarts)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be within the configured retry budget.");
        var multiplier = Math.Pow(2, Math.Min(attempt - 1, 10));
        return TimeSpan.FromMilliseconds(Math.Min(MaxDelay.TotalMilliseconds, InitialDelay.TotalMilliseconds * multiplier));
    }
}
