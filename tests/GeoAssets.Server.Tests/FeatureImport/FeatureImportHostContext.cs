namespace GeoAssets.Server.Tests.FeatureImport;

internal sealed record FeatureImportHostContext(HttpClient Client, string ConnectionString);
