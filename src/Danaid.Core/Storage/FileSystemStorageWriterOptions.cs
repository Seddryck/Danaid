namespace Danaid.Core.Storage;

public sealed class FileSystemStorageWriterOptions
{
    public string BasePath { get; init; } = Path.Combine(AppContext.BaseDirectory, "capture");
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public string FailureErrorCode { get; init; } = "storage.write_failed";
    public int MaxHeaderValueLength { get; init; } = 1024;
    public IReadOnlyCollection<string> ExcludedHeaderKeys { get; init; } =
    [
        "x-death",
        "x-first-death-exchange",
        "x-first-death-queue",
        "x-first-death-reason"
    ];
}
