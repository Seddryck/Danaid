namespace Danaid.Core.Storage;

public sealed record StorageWriteResult(bool Success, string? Location, string? Error)
{
    public static StorageWriteResult SuccessResult(string location) => new(true, location, null);
    public static StorageWriteResult FailureResult(string error) => new(false, null, error);
}
