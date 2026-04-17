namespace Danaid.Core.Storage;

public sealed class FileSystemStorageWriterOptions
{
    public string BasePath { get; init; } = Path.Combine(AppContext.BaseDirectory, "capture");
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
}
