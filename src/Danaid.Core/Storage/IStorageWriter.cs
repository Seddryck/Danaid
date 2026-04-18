using Danaid.Core.Capture;

namespace Danaid.Core.Storage;

public interface IStorageWriter
{
    Task<StorageWriteResult> WriteAsync(CaptureBatch batch, CancellationToken cancellationToken);
}
