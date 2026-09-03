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

public class RestGroupRepositoryTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestGroupRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: _opts) };

    [Fact]
    public async Task GetByIdAsync_Found_MapsFields()
    {
        var orgId = Guid.NewGuid();
        var dto = new GroupDto(Guid.NewGuid(), "Cuadrilla Norte", "desc", orgId, true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(dto));
        var sut = Sut(handler);

        var group = await sut.GetByIdAsync(dto.Id);

        group.Should().NotBeNull();
        group!.Id.Should().Be(dto.Id);
        group.Name.Should().Be("Cuadrilla Norte");
        group.OrganizationId.Should().Be(orgId);
        group.IsActive.Should().BeTrue();
        group.CreatedAt.Should().Be(dto.CreatedAt);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/groups/{dto.Id}");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var group = await sut.GetByIdAsync(Guid.NewGuid());

        group.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_MapsGroups()
    {
        var dto = new GroupDto(Guid.NewGuid(), "Cuadrilla Sur", null, null, true, DateTime.UtcNow);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<GroupDto> { dto }));
        var sut = Sut(handler);

        var groups = await sut.GetAllAsync();

        var group = groups.Should().ContainSingle().Subject;
        group.Id.Should().Be(dto.Id);
        group.Name.Should().Be("Cuadrilla Sur");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/groups");
    }

    [Fact]
    public async Task GetMembersAsync_MapsUserSummaries()
    {
        var groupId = Guid.NewGuid();
        var userDto = new UserSummaryDto(Guid.NewGuid(), "member@example.com", "Member", true, null);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<UserSummaryDto> { userDto }));
        var sut = Sut(handler);

        var members = await sut.GetMembersAsync(groupId);

        var member = members.Should().ContainSingle().Subject;
        member.Id.Should().Be(userDto.Id);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/groups/{groupId}/members");
    }

    [Fact]
    public async Task AddAsync_ReplacesLocalIdWithServerAssignedIdFromLocationHeader()
    {
        var serverAssignedId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri($"/api/identity/groups/{serverAssignedId}", UriKind.Relative);
            return response;
        });
        var sut = Sut(handler);
        var group = new AppGroup { Id = Guid.NewGuid(), Name = "Cuadrilla Norte", IsActive = true, CreatedAt = DateTime.UtcNow };
        var callerSuppliedId = group.Id;

        await sut.AddAsync(group);

        group.Id.Should().Be(serverAssignedId);
        group.Id.Should().NotBe(callerSuppliedId);
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/groups");
        var sentDto = await request.Content!.ReadFromJsonAsync<GroupWriteDto>();
        sentDto.Should().Be(new GroupWriteDto("Cuadrilla Norte", null, null, true));
    }

    [Fact]
    public async Task UpdateAsync_SendsWriteDtoToCorrectUrl()
    {
        var orgId = Guid.NewGuid();
        var group = new AppGroup { Id = Guid.NewGuid(), Name = "Renamed", Description = "d", OrganizationId = orgId, IsActive = false, CreatedAt = DateTime.UtcNow };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.UpdateAsync(group);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsolutePath.Should().Be($"/groups/{group.Id}");
        var sentDto = await request.Content!.ReadFromJsonAsync<GroupWriteDto>();
        sentDto.Should().Be(new GroupWriteDto("Renamed", "d", orgId, false));
    }

    [Fact]
    public async Task AddMemberAsync_PostsToCorrectUrl()
    {
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.AddMemberAsync(groupId, userId);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be($"/groups/{groupId}/members/{userId}");
    }

    [Fact]
    public async Task RemoveMemberAsync_DeletesCorrectUrl()
    {
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.RemoveMemberAsync(groupId, userId);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be($"/groups/{groupId}/members/{userId}");
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
        yield return [Call((IGroupRepository r) => r.GetByOrganizationAsync(Guid.NewGuid()))];
        yield return [Call((IGroupRepository r) => r.GetGroupsForUserAsync(Guid.NewGuid()))];

        static Func<IGroupRepository, Task> Call(Func<IGroupRepository, Task> f) => f;
    }

    [Theory]
    [MemberData(nameof(UnsupportedMethods))]
    public async Task Method_WithNoServerEndpoint_ThrowsNotSupportedException(Func<IGroupRepository, Task> call)
    {
        var sut = Sut(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called.")));

        var act = () => call(sut);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
