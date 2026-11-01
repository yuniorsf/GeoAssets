namespace GeoAssets.Shared.Services.Offline;

/// <summary>Local staging store for evidence photos pending upload (XD01-243).</summary>
public interface ILocalMediaStagingStore
{
    Task StageAsync(LocalMediaStagingEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<LocalMediaStagingEntry>> GetPendingUploadsAsync(CancellationToken ct = default);

    Task MarkUploadedAsync(Guid entryId, CancellationToken ct = default);

    Task MarkUploadFailedAsync(Guid entryId, CancellationToken ct = default);
}
