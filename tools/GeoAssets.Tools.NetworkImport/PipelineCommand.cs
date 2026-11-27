using System.Globalization;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Ingestion.Pipeline;
using GeoAssets.Ingestion.Profiles;
using GeoAssets.Ingestion.Proj;
using GeoAssets.Ingestion.Shapefile;
using GeoAssets.Ingestion.Sources;
using GeoAssets.Provider.Gdal.VectorFiles;

namespace GeoAssets.Tools.NetworkImport;

/// <summary>
/// <c>plan</c> and <c>import</c>: inventory the delivery, run the pipeline into <c>features.ndjson</c> (plan) or the
/// kernel (import), write <c>report.json</c> and print a summary. Chunks never exceed the kernel's
/// <see cref="FeatureImportLimits.MaxChunkSize"/>. <c>plan --export</c> then streams <c>features.ndjson</c> into
/// <c>features.fgb</c> or <c>features.parquet</c> through <see cref="NetworkImportDependencies.VectorFileWriter"/>
/// (XD01-466), or into the PMTiles archive <c>features.pmtiles</c> — one MVT layer per asset class — through
/// <see cref="NetworkImportDependencies.VectorTileArchiveWriter"/> (XD01-472).
/// </summary>
internal sealed class PipelineCommand(NetworkImportOptions options, NetworkImportDependencies dependencies, TextWriter stdout)
{
    public const string FeaturesFile = "features.ndjson";
    public const string ReportFile = "report.json";
    public const string ExportFileName = "features";

    public async Task<int> PlanAsync(CancellationToken cancellationToken)
    {
        var exportFormat = NetworkImportInputs.ExportFormat(options.Export);
        var tileExport = NetworkImportInputs.TileExport(options.Export, options.MinZoom, options.MaxZoom);
        var (profile, inventory, pipeline) = await PrepareAsync(cancellationToken).ConfigureAwait(false);
        var featuresPath = Path.Combine(options.OutputDirectory, FeaturesFile);
        ImportReport report;
        NdjsonFileFeatureSink sink;
        var stream = File.Create(featuresPath);
        await using (stream.ConfigureAwait(false))
        {
            var request = Request(profile, inventory, Guid.Empty);
            sink = new NdjsonFileFeatureSink(stream);
            report = await pipeline.RunAsync(request, sink, cancellationToken).ConfigureAwait(false);
        }

        var headline = $"Plan written to {featuresPath}";
        if (exportFormat is { } format)
        {
            var exportPath = Path.Combine(options.OutputDirectory, ExportFileName + OgrVectorFileWriter.Extension(format));
            var written = await dependencies.VectorFileWriter.WriteAsync(
                exportPath,
                new VectorFileWriteRequest(format) { Attributes = sink.AttributeKeys },
                NdjsonEncoding.ReadAsync(featuresPath, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            headline += string.Create(CultureInfo.InvariantCulture, $" and exported as {format} to {exportPath} ({written.FeaturesWritten} features)");
        }

        if (tileExport is { } zooms)
        {
            var exportPath = Path.Combine(options.OutputDirectory, ExportFileName + OgrPMTilesArchiveWriter.Extension);
            var written = await dependencies.VectorTileArchiveWriter.WriteAsync(
                exportPath,
                new VectorTileArchiveWriteRequest(zooms.MinZoom, zooms.MaxZoom) { LayerNames = sink.AssetClassNames },
                NdjsonEncoding.ReadAsync(featuresPath, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            var skipped = written.FeaturesSkipped == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $", {written.FeaturesSkipped} without geometry skipped");
            headline += string.Create(
                CultureInfo.InvariantCulture,
                $" and exported as PMTiles (zooms {written.MinZoom}-{written.MaxZoom}, layers {string.Join(", ", written.Layers)}) to {exportPath} ({written.FeaturesWritten} features{skipped})");
        }

        return await FinishAsync(report, headline, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ImportAsync(IAccessTokenSource tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var organizationId = NetworkImportInputs.Organization(options.Organization);
        var kernelUrl = NetworkImportInputs.KernelUrl(options.KernelUrl);
        var profileId = options.Profile ?? string.Empty;
        var importId = NetworkImportInputs.ImportId(options.ImportId
            ?? string.Create(CultureInfo.InvariantCulture, $"{profileId}-{dependencies.TimeProvider.GetUtcNow():yyyyMMddHHmmss}"));
        var (profile, inventory, pipeline) = await PrepareAsync(cancellationToken).ConfigureAwait(false);

        var checkpoints = options.CheckpointPath is null ? null : new ImportCheckpointStore(options.CheckpointPath);
        var lastSequence = checkpoints is null ? -1 : await checkpoints.LastSequenceAsync(importId, cancellationToken).ConfigureAwait(false);
        using var http = new HttpClient(dependencies.Handler, disposeHandler: false) { BaseAddress = kernelUrl, Timeout = TimeSpan.FromMinutes(10) };
        var client = new KernelClient(http, tokens, dependencies.TimeProvider, options.TenantDomain);
        var mode = options.ReplaceScope ? FeatureImportCompletionMode.ReplaceScope : FeatureImportCompletionMode.Merge;
        var sink = new KernelFeatureSink(client, organizationId, importId, mode, checkpoints, lastSequence);

        var report = await pipeline.RunAsync(Request(profile, inventory, organizationId), sink, cancellationToken).ConfigureAwait(false);
        var skipped = sink.ChunksSkipped == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" ({sink.ChunksSkipped} chunks already acknowledged were skipped)");
        return await FinishAsync(report, $"Import {importId} into organization {organizationId:D} completed{skipped}", cancellationToken).ConfigureAwait(false);
    }

    private async Task<(ISourceProfile Profile, DeliveryInventory Inventory, IImportPipeline Pipeline)> PrepareAsync(CancellationToken cancellationToken)
    {
        var profile = NetworkImportInputs.Profile(options.Profile);
        var root = NetworkImportInputs.Root(options.Root);
        var projData = NetworkImportInputs.ProjDataDirectory(options.ProjDataDirectory);
        var inventory = await new ShapefileDeliveryInventory().InspectAsync(root, profile, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(options.OutputDirectory);
        var pipeline = new ImportPipeline(new ShapefileFeatureSource(), new ProjCoordinateTransformerFactory(new ProjOptions(projData)));
        return (profile, inventory, pipeline);
    }

    private ImportRequest Request(ISourceProfile profile, DeliveryInventory inventory, Guid organizationId) => new()
    {
        Inventory = inventory,
        Profile = profile,
        ScopePrefix = options.ScopePrefix ?? string.Empty,
        OrganizationId = organizationId,
        Regions = options.Regions,
        Layers = options.Layers,
        IncludeGeometryOnly = options.IncludeGeometryOnly,
        ChunkSize = FeatureImportLimits.MaxChunkSize,
    };

    private async Task<int> FinishAsync(ImportReport report, string headline, CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(options.OutputDirectory, ReportFile);
        await NetworkImportJson.WriteFileAsync(reportPath, report, cancellationToken).ConfigureAwait(false);
        await stdout.LineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{headline}: {report.FeaturesWritten} features in {report.ChunksWritten} chunks, {report.UnresolvedReferences} unresolved references, " +
            $"position accuracy {report.PositionAccuracy}. Report: {reportPath}"), cancellationToken).ConfigureAwait(false);
        foreach (var (assetClass, count) in report.ElementsByAssetClass.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            await stdout.LineAsync(string.Create(CultureInfo.InvariantCulture, $"  {assetClass}: {count}"), cancellationToken).ConfigureAwait(false);
        foreach (var (code, count) in report.IssueCounts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            await stdout.LineAsync(string.Create(CultureInfo.InvariantCulture, $"  issue {code}: {count}"), cancellationToken).ConfigureAwait(false);
        return NetworkImportExitCodes.Succeeded;
    }
}
