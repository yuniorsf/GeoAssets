using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Server.Tests.FeatureImport;
using Xunit;

namespace GeoAssets.Server.Tests.MembershipOrganization;

/// <summary>
/// XD01-471: a caller whose token carries no organization reads the features of the organization its identity record
/// names — through the feature routes and WMS — when <c>GeoAssets:Tenancy:OrganizationFromMembership</c> is on, and
/// nothing changes when it is off or the record names no active membership.
/// </summary>
[Collection(MembershipOrganizationHost.CollectionName)]
public sealed class MembershipOrganizationEndpointTests
{
    private const double Lon = -38.476;
    private const double Lat = -12.932;

    [Fact]
    public async Task Member_ReadsItsOrganizationsFeature()
    {
        var host = await MembershipOrganizationHost.GetEnabledAsync();
        var (organization, featureId) = await SeedOrganizationFeatureAsync(host);
        var caller = Caller(organization);

        using var byId = await GetAsync(host, $"/api/geoassets/features/{featureId}", caller);
        var featureInfo = await GetFeatureInfoIdsAsync(host, caller);

        byId.StatusCode.Should().Be(HttpStatusCode.OK);
        featureInfo.Should().Contain(featureId);
    }

    [Fact]
    public async Task CallerWithoutAnOrganization_SeesOnlySharedFeatures_AsBefore()
    {
        var host = await MembershipOrganizationHost.GetEnabledAsync();
        var (_, featureId) = await SeedOrganizationFeatureAsync(host);
        var caller = Caller(organizationId: null);

        using var byId = await GetAsync(host, $"/api/geoassets/features/{featureId}", caller);
        var featureInfo = await GetFeatureInfoIdsAsync(host, caller);

        byId.StatusCode.Should().Be(HttpStatusCode.NotFound);
        featureInfo.Should().NotContain(featureId);
    }

    [Fact]
    public async Task InactiveMember_GetsNoOrganization()
    {
        var host = await MembershipOrganizationHost.GetEnabledAsync();
        var (organization, featureId) = await SeedOrganizationFeatureAsync(host);
        var caller = Caller(organization, isActive: false);

        using var byId = await GetAsync(host, $"/api/geoassets/features/{featureId}", caller);

        byId.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MemberOfAnotherOrganization_DoesNotSeeTheFeature()
    {
        var host = await MembershipOrganizationHost.GetEnabledAsync();
        var (_, featureId) = await SeedOrganizationFeatureAsync(host);
        var caller = Caller(Guid.NewGuid());

        using var byId = await GetAsync(host, $"/api/geoassets/features/{featureId}", caller);
        var featureInfo = await GetFeatureInfoIdsAsync(host, caller);

        byId.StatusCode.Should().Be(HttpStatusCode.NotFound);
        featureInfo.Should().NotContain(featureId);
    }

    [Fact]
    public async Task SettingOff_Member_SeesOnlySharedFeatures_AsBefore()
    {
        var host = await MembershipOrganizationHost.GetDisabledAsync();
        var (organization, featureId) = await SeedOrganizationFeatureAsync(host);
        var caller = Caller(organization);

        using var byId = await GetAsync(host, $"/api/geoassets/features/{featureId}", caller);
        var featureInfo = await GetFeatureInfoIdsAsync(host, caller);

        byId.StatusCode.Should().Be(HttpStatusCode.NotFound);
        featureInfo.Should().NotContain(featureId);
    }

    [Fact]
    public async Task ServiceCaller_IsNeverGivenAMembershipOrganization()
    {
        var host = await MembershipOrganizationHost.GetEnabledAsync();
        var (organization, featureId) = await SeedOrganizationFeatureAsync(host);
        var caller = Caller(organization);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/geoassets/features/{featureId}", UriKind.Relative));
        request.Headers.Add(MembershipAuthenticationHandler.CallerHeader, caller);
        request.Headers.Add(MembershipAuthenticationHandler.RolesHeader, FeatureImportRequests.ServiceCaller);
        using var byId = await host.Client.SendAsync(request);

        byId.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ServiceCallerWithoutMembership_StillImportsIntoTheOrganizationInThePath()
    {
        var host = await MembershipOrganizationHost.GetEnabledAsync();

        var (organization, _) = await SeedOrganizationFeatureAsync(host);

        (await FeatureImportDatabase.CountEntitiesAsync(host.ConnectionString, organization)).Should().Be(1);
    }

    private static string Caller(Guid? organizationId, bool isActive = true)
    {
        var caller = "membership-" + Guid.NewGuid().ToString("N");
        MembershipAuthorizationService.Users[caller] = (organizationId, isActive);
        return caller;
    }

    private static async Task<(Guid Organization, string FeatureId)> SeedOrganizationFeatureAsync(MembershipOrganizationHostContext host)
    {
        var organization = Guid.NewGuid();
        var type = await FeatureImportDatabase.SeedAssetTypeAsync(host.ConnectionString, organization, GeometryType.Point);
        var featureId = FeatureImportRequests.NewId();
        var importer = Caller(organizationId: null);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(FeatureImportRequests.ChunkUrl(organization, "membership-run", "membership/POSTE"), UriKind.Relative))
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(FeatureImportRequests.Ndjson(
                [FeatureImportRequests.Feature(featureId, type, new GeoPoint(Lon, Lat))]))),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        request.Headers.Add(MembershipAuthenticationHandler.CallerHeader, importer);
        request.Headers.Add(MembershipAuthenticationHandler.RolesHeader, FeatureImportRequests.ServiceCaller);
        using var response = await host.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (organization, featureId);
    }

    private static async Task<HttpResponseMessage> GetAsync(MembershipOrganizationHostContext host, string url, string caller)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        request.Headers.Add(MembershipAuthenticationHandler.CallerHeader, caller);
        return await host.Client.SendAsync(request);
    }

    private static async Task<IReadOnlyList<string>> GetFeatureInfoIdsAsync(MembershipOrganizationHostContext host, string caller)
    {
        const double half = 0.001;
        var bbox = FormattableString.Invariant($"{Lon - half},{Lat - half},{Lon + half},{Lat + half}");
        using var response = await GetAsync(host,
            $"/wms?SERVICE=WMS&VERSION=1.1.1&REQUEST=GetFeatureInfo&LAYERS=geoassets:feature&QUERY_LAYERS=geoassets:feature&SRS=EPSG:4326&BBOX={bbox}&WIDTH=100&HEIGHT=100&I=50&J=50&FEATURE_COUNT=50&INFO_FORMAT=application/json",
            caller);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("features").EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToList();
    }
}
