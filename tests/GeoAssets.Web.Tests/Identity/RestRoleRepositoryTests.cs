using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Xunit;

namespace GeoAssets.Web.Tests.Identity;

public class RestRoleRepositoryTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestRoleRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: _opts) };

    [Fact]
    public async Task GetByIdAsync_Found_MapsFields()
    {
        var permissionId = Guid.NewGuid();
        var dto = new RoleDetailDto(Guid.NewGuid(), "Auditor", "desc", false, [permissionId]);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(dto));
        var sut = Sut(handler);

        var role = await sut.GetByIdAsync(dto.Id);

        role.Should().NotBeNull();
        role!.Id.Should().Be(dto.Id);
        role.Name.Should().Be("Auditor");
        role.Description.Should().Be("desc");
        role.IsBuiltIn.Should().BeFalse();
        // RoleDetailDto.PermissionIds has no direct AppRole property — the identity admin UI
        // (XD01-58) reads a role's current permissions via RolePermissions instead.
        role.RolePermissions.Should().ContainSingle(rp => rp.PermissionId == permissionId && rp.RoleId == dto.Id);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/roles/{dto.Id}");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var role = await sut.GetByIdAsync(Guid.NewGuid());

        role.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_MapsSummaries()
    {
        var summary = new RoleSummaryDto(Guid.NewGuid(), "Supervisor", "desc", true);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<RoleSummaryDto> { summary }));
        var sut = Sut(handler);

        var roles = await sut.GetAllAsync();

        var role = roles.Should().ContainSingle().Subject;
        role.Id.Should().Be(summary.Id);
        role.Name.Should().Be("Supervisor");
        role.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public async Task AddAsync_ReplacesLocalIdWithServerAssignedIdFromLocationHeader()
    {
        // Regression guard: the server always mints its own Id (RoleWriteDto carries no Id
        // field) and returns it only via the Location header — without re-parsing that header,
        // role.Id would stay stuck at whatever the caller happened to set it to beforehand.
        var serverAssignedId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri($"/api/identity/roles/{serverAssignedId}", UriKind.Relative);
            return response;
        });
        var sut = Sut(handler);
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Auditor", Description = "desc", IsBuiltIn = false };
        var callerSuppliedId = role.Id;

        await sut.AddAsync(role);

        role.Id.Should().Be(serverAssignedId);
        role.Id.Should().NotBe(callerSuppliedId);
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/roles");
        var sentDto = await request.Content!.ReadFromJsonAsync<RoleWriteDto>();
        sentDto.Should().Be(new RoleWriteDto("Auditor", "desc"));
    }

    [Fact]
    public async Task AddAsync_AlsoHandlesAbsoluteLocationHeader()
    {
        var serverAssignedId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri($"http://test/api/identity/roles/{serverAssignedId}");
            return response;
        });
        var sut = Sut(handler);
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Auditor", Description = "desc", IsBuiltIn = false };

        await sut.AddAsync(role);

        role.Id.Should().Be(serverAssignedId);
    }

    [Fact]
    public async Task UpdateAsync_SendsWriteDtoToCorrectUrl()
    {
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Renamed", Description = "new desc", IsBuiltIn = false };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.UpdateAsync(role);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsolutePath.Should().Be($"/roles/{role.Id}");
        var sentDto = await request.Content!.ReadFromJsonAsync<RoleWriteDto>();
        sentDto.Should().Be(new RoleWriteDto("Renamed", "new desc"));
    }

    [Fact]
    public async Task DeleteAsync_SendsDeleteToCorrectUrl()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.DeleteAsync(id);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be($"/roles/{id}");
    }

    [Fact]
    public async Task GrantPermissionAsync_PostsToCorrectUrl()
    {
        var roleId = Guid.NewGuid();
        var permId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.GrantPermissionAsync(roleId, permId);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be($"/roles/{roleId}/permissions/{permId}");
    }

    [Fact]
    public async Task RevokePermissionAsync_DeletesCorrectUrl()
    {
        var roleId = Guid.NewGuid();
        var permId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.RevokePermissionAsync(roleId, permId);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be($"/roles/{roleId}/permissions/{permId}");
    }

    [Fact]
    public async Task SaveChangesAsync_MakesNoHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called."));
        var sut = Sut(handler);

        await sut.SaveChangesAsync();

        handler.Requests.Should().BeEmpty();
    }

    public static IEnumerable<object[]> UnsupportedMethods()
    {
        yield return [Call((IRoleRepository r) => r.GetByNameAsync("Administrator"))];
        yield return [Call((IRoleRepository r) => r.GetPermissionsAsync(Guid.NewGuid()))];

        static Func<IRoleRepository, Task> Call(Func<IRoleRepository, Task> f) => f;
    }

    [Theory]
    [MemberData(nameof(UnsupportedMethods))]
    public async Task Method_WithNoServerEndpoint_ThrowsNotSupportedException(Func<IRoleRepository, Task> call)
    {
        var sut = Sut(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called.")));

        var act = () => call(sut);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
