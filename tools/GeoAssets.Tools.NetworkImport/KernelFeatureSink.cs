using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Ingestion.Model;
using GeoAssets.Ingestion.Pipeline;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// Writes the pipeline's chunks to the kernel's bulk feature import. Asset types are reused when the catalog has one
/// with the class's id, role or one of its reuse roles, else created. Each chunk goes to its scope's route (gzip
/// NDJSON); a checkpoint is saved after each acknowledged chunk and chunks a previous run of the same import already
/// wrote (sequence ≤ the checkpoint's) are skipped. Completion completes every scope seen, merging or replacing it.
/// </summary>
internal sealed class KernelFeatureSink(
    KernelClient client,
    Guid organizationId,
    string importId,
    FeatureImportCompletionMode mode,
    ImportCheckpointStore? checkpoints,
    int lastAcknowledgedSequence) : IFeatureSink
{
    private readonly Dictionary<string, string> _assetTypeIds = new(StringComparer.Ordinal);
    private readonly List<string> _scopes = [];

    public int ChunksSkipped { get; private set; }

    public async Task EnsureAssetTypesAsync(IReadOnlyList<AssetClassDefinition> classes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classes);
        var catalog = await client.GetAssetTypesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var assetClass in classes)
        {
            var existing = catalog.FirstOrDefault(type => type.Roles.Contains(assetClass.Role, StringComparer.Ordinal))
                ?? catalog.FirstOrDefault(type => type.Id == assetClass.TypeId)
                ?? catalog.FirstOrDefault(type => assetClass.ReuseRoles.Any(role => type.Roles.Contains(role, StringComparer.Ordinal)));
            if (existing is null)
            {
                existing = new AssetType
                {
                    Id = assetClass.TypeId,
                    Name = assetClass.DisplayName,
                    Color = assetClass.Color,
                    AllowedGeometryType = assetClass.Geometry,
                    Roles = [assetClass.Role],
                };
                await client.CreateAssetTypeAsync(existing, cancellationToken).ConfigureAwait(false);
            }

            _assetTypeIds[assetClass.Name] = existing.Id.ToString();
        }
    }

    public string AssetTypeIdFor(string assetClass) =>
        _assetTypeIds.TryGetValue(assetClass, out var id)
            ? id
            : throw new InvalidOperationException($"No asset type was ensured for the asset class '{assetClass}'.");

    public async Task<SinkChunkResult> WriteChunkAsync(int sequence, IReadOnlyList<GeoFeature> features, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (features.Count == 0)
            return new SinkChunkResult(0, []);

        var scope = features[0].Properties.CustomAttributes.GetValueOrDefault(FeatureImportAttributes.Scope)
            ?? throw new InvalidOperationException($"Chunk {sequence} has no {FeatureImportAttributes.Scope}.");
        if (!_scopes.Contains(scope, StringComparer.Ordinal))
            _scopes.Add(scope);

        if (sequence <= lastAcknowledgedSequence)
        {
            ChunksSkipped++;
            return new SinkChunkResult(features.Count, []);
        }

        var body = await NdjsonEncoding.GzipAsync(features, cancellationToken).ConfigureAwait(false);
        var result = await client.PostChunkAsync(organizationId, importId, scope, body, cancellationToken).ConfigureAwait(false);
        if (checkpoints is not null)
            await checkpoints.SaveAsync(new ImportCheckpoint(importId, sequence), cancellationToken).ConfigureAwait(false);

        var rejections = result.Rejected
            .Select(rejection => new ImportIssue(IssueSeverity.Error, ImportIssueCodes.Rejected, rejection.FeatureId, $"The kernel rejected the feature: {rejection.Reason}."))
            .ToList();
        return new SinkChunkResult(result.Inserted + result.Updated, rejections);
    }

    public async Task CompleteAsync(CancellationToken cancellationToken)
    {
        foreach (var scope in _scopes)
            await client.CompleteAsync(organizationId, importId, scope, mode, cancellationToken).ConfigureAwait(false);
    }
}
