namespace Danaid.Core.Capture;

public sealed class CaptureBatchBufferOptions
{
    public int Capacity { get; init; } = 10_000;
    public int MaxCount { get; init; } = 500;
    public long MaxBytes { get; init; } = 4 * 1024 * 1024;
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(2);
}
