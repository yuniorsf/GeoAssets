using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Server.Tests.FeatureImport;

[Collection(FeatureImportHost.CollectionName)]
public sealed class SeededElectricAssetTypeRolesTests
{
    private static readonly Guid Pole = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Transformer = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid LowTensionWire = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid Breaker = Guid.Parse("00000000-0000-0000-0000-000000000008");

    private static async Task<AssetType[]> ListAssetTypesAsync()
    {
        var host = await FeatureImportHost.GetAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/geoassets/asset-types", UriKind.Relative));
        request.Headers.Add(HeaderRolesAuthenticationHandler.RolesHeader, FeatureImportRequests.ServiceCaller);
        using var response = await host.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonSerializer.Deserialize<AssetType[]>(await response.Content.ReadAsStringAsync(), GeoJsonSerializer.Options)!;
    }

    [Fact]
    public async Task AssetTypes_Pole_CarriesTheElectricPoleAndOccupiableSupportRoles()
    {
        var types = await ListAssetTypesAsync();

        var pole = types.Single(t => t.Id == Pole);
        pole.Roles.Should().Contain("electric:pole").And.Contain("occupiable-support");
    }

    [Fact]
    public async Task AssetTypes_Pole_KeepsItsExistingIspPoleRole()
    {
        var types = await ListAssetTypesAsync();

        types.Single(t => t.Id == Pole).Roles.Should().Contain("isp:pole");
    }

    [Fact]
    public async Task AssetTypes_Transformer_CarriesTheElectricTransformerRole()
    {
        (await ListAssetTypesAsync()).Single(t => t.Id == Transformer).Roles.Should().Contain("electric:transformer");
    }

    [Fact]
    public async Task AssetTypes_LowTensionWire_CarriesTheElectricLvSegmentRole()
    {
        (await ListAssetTypesAsync()).Single(t => t.Id == LowTensionWire).Roles.Should().Contain("electric:lv-segment");
    }

    [Fact]
    public async Task AssetTypes_Breaker_CarriesTheElectricBreakerRole()
    {
        (await ListAssetTypesAsync()).Single(t => t.Id == Breaker).Roles.Should().Contain("electric:breaker");
    }

    [Fact]
    public async Task AssetTypes_RolesInTheDatabaseMatchWhatTheEndpointServes()
    {
        var host = await FeatureImportHost.GetAsync();

        var roles = await FeatureImportDatabase.GetRolesAsync(host.ConnectionString, Pole);

        roles.Should().Contain("electric:pole").And.Contain("occupiable-support");
    }
}
