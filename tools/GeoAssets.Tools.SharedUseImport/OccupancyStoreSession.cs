using GeoAssets.NeutralOperator.Domain.Occupancy;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>The occupancy ports one run writes through, and whatever must be released after it.</summary>
internal sealed class OccupancyStoreSession(IPoleOccupancyReader reader, IPoleOccupancyWriter writer, IAsyncDisposable? resource = null)
    : IAsyncDisposable
{
    public IPoleOccupancyReader Reader { get; } = reader ?? throw new ArgumentNullException(nameof(reader));

    public IPoleOccupancyWriter Writer { get; } = writer ?? throw new ArgumentNullException(nameof(writer));

    public ValueTask DisposeAsync() => resource?.DisposeAsync() ?? ValueTask.CompletedTask;
}
