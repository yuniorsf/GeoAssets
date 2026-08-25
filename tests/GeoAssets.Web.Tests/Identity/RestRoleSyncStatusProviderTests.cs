using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class RestRoleSyncStatusProviderTests
{
    private static RestRoleSyncStatusProvider Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    [Fact]
    public async Task IsEnabledAsync_ServerReportsTrue_ReturnsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new RoleSyncStatusDto(true)) });
        var sut = Sut(handler);

        var enabled = await sut.IsEnabledAsync();

        enabled.Should().BeTrue();
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/rolesync/status");
    }

    [Fact]
    public async Task IsEnabledAsync_ServerReportsFalse_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new RoleSyncStatusDto(false)) });
        var sut = Sut(handler);

        var enabled = await sut.IsEnabledAsync();

        enabled.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_NullResponseBody_ReturnsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") });
        var sut = Sut(handler);

        var enabled = await sut.IsEnabledAsync();

        enabled.Should().BeFalse();
    }
}
