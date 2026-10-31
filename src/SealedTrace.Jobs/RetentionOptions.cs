namespace Arovyx.SealedTrace.Jobs;

public sealed class RetentionOptions
{
    public int DefaultMaxAgeDays { get; set; } = 30;

    public long DefaultMaxEvents { get; set; } = 1_000_000;
}
