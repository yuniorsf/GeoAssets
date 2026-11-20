using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Security.Tests;

/// <summary>The kernel's <see cref="IAssetMutationBatchApplier"/>, between the consumer and the real one, failing on command.</summary>
internal sealed class FaultInjectingApplier(IAssetMutationBatchApplier inner) : IAssetMutationBatchApplier
{
    private int _calls;
    private int _applies;

    public KernelFault Mode { get; set; }

    /// <summary>Calls the consumer made.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Calls that reached the real applier.</summary>
    public int Applies => Volatile.Read(ref _applies);

    public async Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        if (Mode == KernelFault.DownBeforeApply)
            throw new TimeoutException("The kernel's catalog is unreachable.");

        var applied = await inner.ApplyAsync(batch, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _applies);
        if (Mode == KernelFault.CrashAfterApplyOnce)
        {
            Mode = KernelFault.None;
            throw new TimeoutException("The kernel died after applying the batch and before answering it.");
        }

        return applied;
    }
}
