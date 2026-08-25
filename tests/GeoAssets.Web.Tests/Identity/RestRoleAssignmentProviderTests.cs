using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class RestRoleAssignmentProviderTests
{
    private static RestRoleAssignmentProvider Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    [Fact]
    public async Task RegisterRoleAsync_PostsToCorrectUrl()
    {
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Supervisor" };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.RegisterRoleAsync(role);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be($"/rolesync/roles/{role.Id}");
    }

    [Fact]
    public async Task UnregisterRoleAsync_ThrowsNotSupportedException()
    {
        // No server endpoint exists for this — XD01-63's admin UI only exposes a "Register"
        // action, matching the other Rest* repos' NotSupportedException idiom for operations
        // their server surface doesn't expose.
        var sut = Sut(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called.")));

        var act = () => sut.UnregisterRoleAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task AssignRoleAsync_PostsToCorrectEscapedUrl()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.AssignRoleAsync("ext oid/1", "Field Technician");

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/rolesync/users/ext%20oid%2F1/roles/Field%20Technician");
    }

    [Fact]
    public async Task RevokeRoleAsync_DeletesCorrectEscapedUrl()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.RevokeRoleAsync("ext-oid-1", "Supervisor");

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be("/rolesync/users/ext-oid-1/roles/Supervisor");
    }

    [Fact]
    public async Task GetAssignedRoleNamesAsync_ReturnsNamesFromServer()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<string> { "Supervisor", "Auditor" }) });
        var sut = Sut(handler);

        var names = await sut.GetAssignedRoleNamesAsync("ext-oid-1");

        names.Should().BeEquivalentTo(["Supervisor", "Auditor"]);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/rolesync/users/ext-oid-1/roles");
    }

    [Fact]
    public async Task GetAssignedRoleNamesAsync_NullResponseBody_ReturnsEmptyList()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") });
        var sut = Sut(handler);

        var names = await sut.GetAssignedRoleNamesAsync("ext-oid-1");

        names.Should().BeEmpty();
    }
}
