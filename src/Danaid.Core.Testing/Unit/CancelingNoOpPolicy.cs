using System.Threading;
using System.Threading.Tasks;
using Polly;

namespace Danaid.Core.Testing.Unit;

internal sealed class CancelingNoOpPolicy : AsyncPolicy
{
    private readonly CancellationTokenSource cancellationTokenSource;

    public bool WasExecuted { get; private set; }

    public CancelingNoOpPolicy(CancellationTokenSource cancellationTokenSource)
    {
        this.cancellationTokenSource = cancellationTokenSource;
    }

    protected override Task ImplementationAsync(
        Func<Context, CancellationToken, Task> action,
        Context context,
        CancellationToken cancellationToken,
        bool continueOnCapturedContext)
    {
        WasExecuted = true;
        if (!cancellationTokenSource.IsCancellationRequested)
            cancellationTokenSource.Cancel();

        return Task.FromCanceled(cancellationTokenSource.Token);
    }

    protected override Task<TResult> ImplementationAsync<TResult>(
        Func<Context, CancellationToken, Task<TResult>> action,
        Context context,
        CancellationToken cancellationToken,
        bool continueOnCapturedContext)
    {
        WasExecuted = true;
        if (!cancellationTokenSource.IsCancellationRequested)
            cancellationTokenSource.Cancel();

        return Task.FromCanceled<TResult>(cancellationTokenSource.Token);
    }
}
