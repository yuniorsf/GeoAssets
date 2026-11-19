namespace GeoAssets.Server.Tests.Catalogs;

/// <summary>A capability port only <see cref="CatalogTestProvider"/> offers, for <c>ICatalogPool.GetCapability</c>.</summary>
internal interface ITestCatalogCapability
{
    string Origin { get; }
}
