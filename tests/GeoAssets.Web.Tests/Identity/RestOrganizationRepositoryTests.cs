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

public class RestOrganizationRepositoryTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestOrganizationRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: _opts) };

    [Fact]
    public async Task GetByIdAsync_Found_MapsFields()
    {
        var dto = new OrganizationDto(Guid.NewGuid(), "Empresa Eléctrica", "een", "desc", true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(dto));
        var sut = Sut(handler);

        var org = await sut.GetByIdAsync(dto.Id);

        org.Should().NotBeNull();
        org!.Id.Should().Be(dto.Id);
        org.Name.Should().Be("Empresa Eléctrica");
        org.Slug.Should().Be("een");
        org.Description.Should().Be("desc");
        org.IsActive.Should().BeTrue();
        org.CreatedAt.Should().Be(dto.CreatedAt);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/organizations/{dto.Id}");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var org = await sut.GetByIdAsync(Guid.NewGuid());

        org.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_MapsOrganizations()
    {
        var dto = new OrganizationDto(Guid.NewGuid(), "Empresa Test", "test", null, true, DateTime.UtcNow);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<OrganizationDto> { dto }));
        var sut = Sut(handler);

        var orgs = await sut.GetAllAsync();

        var org = orgs.Should().ContainSingle().Subject;
        org.Id.Should().Be(dto.Id);
        org.Slug.Should().Be("test");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/organizations");
    }

    [Fact]
    public async Task GetUsersAsync_MapsUserSummaries()
    {
        var orgId = Guid.NewGuid();
        var userDto = new UserSummaryDto(Guid.NewGuid(), "user@example.com", "Test User", true, orgId);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(new List<UserSummaryDto> { userDto }));
        var sut = Sut(handler);

        var users = await sut.GetUsersAsync(orgId);

        var user = users.Should().ContainSingle().Subject;
        user.Id.Should().Be(userDto.Id);
        user.Email.Should().Be("user@example.com");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/organizations/{orgId}/users");
    }

    [Fact]
    public async Task AddAsync_ReplacesLocalIdWithServerAssignedIdFromLocationHeader()
    {
        var serverAssignedId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri($"/api/identity/organizations/{serverAssignedId}", UriKind.Relative);
            return response;
        });
        var sut = Sut(handler);
        var org = new Organization { Id = Guid.NewGuid(), Name = "Empresa Test", Slug = "test", IsActive = true, CreatedAt = DateTime.UtcNow };
        var callerSuppliedId = org.Id;

        await sut.AddAsync(org);

        org.Id.Should().Be(serverAssignedId);
        org.Id.Should().NotBe(callerSuppliedId);
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/organizations");
        var sentDto = await request.Content!.ReadFromJsonAsync<OrganizationWriteDto>();
        sentDto.Should().Be(new OrganizationWriteDto("Empresa Test", "test", null, true));
    }

    [Fact]
    public async Task UpdateAsync_SendsWriteDtoToCorrectUrl()
    {
        var org = new Organization { Id = Guid.NewGuid(), Name = "Renamed", Slug = "renamed", Description = "d", IsActive = false, CreatedAt = DateTime.UtcNow };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.UpdateAsync(org);

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsolutePath.Should().Be($"/organizations/{org.Id}");
        var sentDto = await request.Content!.ReadFromJsonAsync<OrganizationWriteDto>();
        sentDto.Should().Be(new OrganizationWriteDto("Renamed", "renamed", "d", false));
    }

    [Fact]
    public async Task SaveChangesAsync_MakesNoHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called."));
        var sut = Sut(handler);

        await sut.SaveChangesAsync();

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetBySlugAsync_ThrowsNotSupportedException()
    {
        var sut = Sut(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not be called.")));

        var act = () => sut.GetBySlugAsync("test");

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
