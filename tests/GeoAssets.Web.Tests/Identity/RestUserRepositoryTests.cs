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

public class RestUserRepositoryTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestUserRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: _opts) };

    [Fact]
    public async Task GetByIdAsync_Found_MapsAllFields()
    {
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var dto = new UserDetailDto(
            Id: Guid.NewGuid(), Email: "user@example.com", DisplayName: "Test User", IsActive: true,
            OrganizationId: orgId, CreatedAt: new DateTime(2026, 1, 1), LastLoginAt: new DateTime(2026, 2, 1),
            RoleIds: [roleId]);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(dto));
        var sut = Sut(handler);

        var user = await sut.GetByIdAsync(dto.Id);

        user.Should().NotBeNull();
        user!.Id.Should().Be(dto.Id);
        user.Email.Should().Be("user@example.com");
        user.DisplayName.Should().Be("Test User");
        user.IsActive.Should().BeTrue();
        user.OrganizationId.Should().Be(orgId);
        user.CreatedAt.Should().Be(dto.CreatedAt);
        user.LastLoginAt.Should().Be(dto.LastLoginAt);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/users/{dto.Id}");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var user = await sut.GetByIdAsync(Guid.NewGuid());

        user.Should().BeNull();
    }

    [Fact]
    public async Task GetByExternalObjectIdAsync_Found_MapsAllFields()
    {
        var orgId = Guid.NewGuid();
        var dto = new UserDetailDto(
            Id: Guid.NewGuid(), Email: "user@example.com", DisplayName: "Test User", IsActive: true,
            OrganizationId: orgId, CreatedAt: new DateTime(2026, 1, 1), LastLoginAt: null, RoleIds: []);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(dto));
        var sut = Sut(handler);

        var user = await sut.GetByExternalObjectIdAsync("oid-abc-123");

        user.Should().NotBeNull();
        user!.Id.Should().Be(dto.Id);
        user.OrganizationId.Should().Be(orgId);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/users/by-external-id/oid-abc-123");
    }

    [Fact]
    public async Task GetByExternalObjectIdAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var user = await sut.GetByExternalObjectIdAsync("no-such-oid");

        user.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_MapsSummaries_WithDefaultCreatedAt()
    {
        var summary = new UserSummaryDto(Guid.NewGuid(), "user@example.com", "Test User", true, null);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<UserSummaryDto> { summary }));
        var sut = Sut(handler);

        var users = await sut.GetAllAsync();

        var user = users.Should().ContainSingle().Subject;
        user.Id.Should().Be(summary.Id);
        user.Email.Should().Be("user@example.com");
        // UserSummaryDto carries no CreatedAt — mapping must not fabricate one.
        user.CreatedAt.Should().Be(default);
    }

    [Fact]
    public async Task UpdateAsync_SendsUpdateDtoToCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var user = new AppUser
        {
            Id = Guid.NewGuid(), DisplayName = "New Name", IsActive = false, OrganizationId = orgId,
            CreatedAt = default,
        };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.UpdateAsync(user);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsolutePath.Should().Be($"/users/{user.Id}");
        var sentDto = await request.Content!.ReadFromJsonAsync<UserUpdateDto>();
        sentDto.Should().Be(new UserUpdateDto("New Name", false, orgId));
    }

    [Fact]
    public async Task SaveChangesAsync_MakesNoHttpCall()
    {
        // Writes are already persisted server-side by the time their HTTP call returns
        // (the server calls its own SaveChangesAsync internally per request).
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called."));
        var sut = Sut(handler);

        await sut.SaveChangesAsync();

        handler.Requests.Should().BeEmpty();
    }

    public static IEnumerable<object[]> UnsupportedMethods()
    {
        yield return [Call((IUserRepository r) => r.GetByEmailAsync("user@example.com"))];
        yield return [Call((IUserRepository r) => r.GetByRoleAsync("Administrator"))];
        yield return [Call((IUserRepository r) => r.GetByOrganizationAsync(Guid.NewGuid()))];
        yield return [Call((IUserRepository r) => r.GetRolesAsync(Guid.NewGuid()))];
        yield return [Call((IUserRepository r) => r.GetEffectivePermissionsAsync(Guid.NewGuid()))];
        yield return [Call((IUserRepository r) => r.AddAsync(new AppUser { CreatedAt = default }))];
        yield return [Call((IUserRepository r) => r.AssignRoleAsync(Guid.NewGuid(), Guid.NewGuid()))];
        yield return [Call((IUserRepository r) => r.RemoveRoleAsync(Guid.NewGuid(), Guid.NewGuid()))];

        static Func<IUserRepository, Task> Call(Func<IUserRepository, Task> f) => f;
    }

    [Theory]
    [MemberData(nameof(UnsupportedMethods))]
    public async Task Method_WithNoServerEndpoint_ThrowsNotSupportedException(Func<IUserRepository, Task> call)
    {
        var sut = Sut(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called.")));

        var act = () => call(sut);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
