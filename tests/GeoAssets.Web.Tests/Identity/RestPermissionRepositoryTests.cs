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

public class RestPermissionRepositoryTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestPermissionRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    [Fact]
    public async Task GetAllAsync_MapsPermissions()
    {
        var dto = new PermissionDto(Guid.NewGuid(), "reports:export", "reports", "export", "Exportar reportes");
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new List<PermissionDto> { dto }, options: _opts) });
        var sut = Sut(handler);

        var permissions = await sut.GetAllAsync();

        var permission = permissions.Should().ContainSingle().Subject;
        permission.Id.Should().Be(dto.Id);
        permission.Code.Should().Be("reports:export");
        permission.Resource.Should().Be("reports");
        permission.Action.Should().Be("export");
        permission.Description.Should().Be("Exportar reportes");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/permissions");
    }

    public static IEnumerable<object[]> UnsupportedMethods()
    {
        yield return [Call((IPermissionRepository r) => r.GetByIdAsync(Guid.NewGuid()))];
        yield return [Call((IPermissionRepository r) => r.GetByCodeAsync("features:read"))];
        yield return [Call((IPermissionRepository r) => r.GetByResourceAsync("features"))];
        yield return [Call((IPermissionRepository r) => r.AddAsync(new AppPermission()))];
        yield return [Call((IPermissionRepository r) => r.UpdateAsync(new AppPermission()))];
        yield return [Call((IPermissionRepository r) => r.DeleteAsync(Guid.NewGuid()))];
        yield return [Call((IPermissionRepository r) => r.SaveChangesAsync())];

        static Func<IPermissionRepository, Task> Call(Func<IPermissionRepository, Task> f) => f;
    }

    [Theory]
    [MemberData(nameof(UnsupportedMethods))]
    public async Task Method_WithNoServerEndpoint_ThrowsNotSupportedException(Func<IPermissionRepository, Task> call)
    {
        var sut = Sut(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called.")));

        var act = () => call(sut);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
