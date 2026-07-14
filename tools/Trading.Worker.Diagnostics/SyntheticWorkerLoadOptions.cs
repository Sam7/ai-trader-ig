namespace Trading.Worker.Diagnostics;

/// <summary>Controls a local-only allocation profile for reproducing worker memory behavior without IG traffic.</summary>
public sealed class SyntheticWorkerLoadOptions
{
    public const string SectionName = "SyntheticWorkerLoad";

    public bool Enabled { get; init; } = true;

    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan AllocationInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    public int RetainedMegabytes { get; init; } = 64;

    public int ChurnMegabytesPerInterval { get; init; } = 4;

    public int BurstMegabytes { get; init; } = 32;

    public TimeSpan BurstInterval { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan BurstHold { get; init; } = TimeSpan.FromMilliseconds(500);

    public string ResultPath { get; init; } = Path.Combine("artifacts", "diagnostics-lab", "synthetic-memory-lab.json");

    public void Validate()
    {
        if (Duration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Synthetic worker load duration must be greater than zero.");
        }

        if (AllocationInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Synthetic worker load allocation interval must be greater than zero.");
        }

        if (RetainedMegabytes < 0 || ChurnMegabytesPerInterval < 0 || BurstMegabytes < 0)
        {
            throw new InvalidOperationException("Synthetic worker load megabyte settings cannot be negative.");
        }

        if (BurstMegabytes > 0 && BurstInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Synthetic worker load burst interval must be greater than zero when bursts are enabled.");
        }

        if (BurstHold < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Synthetic worker load burst hold cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(ResultPath))
        {
            throw new InvalidOperationException("Synthetic worker load result path is required.");
        }
    }
}
