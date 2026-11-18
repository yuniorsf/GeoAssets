using GeoAssets.Provider.Rest;

namespace GeoAssets.Server.Tests.Architecture.Fixtures;

internal sealed class FieldReferenceFixture
{
    private readonly GeoCoreFederationOptions _options = new();

    public string? Read() => _options.DefaultCatalogInstanceId;
}
