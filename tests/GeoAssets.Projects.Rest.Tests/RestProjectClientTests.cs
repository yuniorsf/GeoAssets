using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Projects.Rest.Tests;

public class RestProjectClientTests
{
    private static readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static RestProjectClient Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object? body = null)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
            response.Content = JsonContent.Create(body, options: _opts);
        return response;
    }

    private static Project SampleProject(Guid? id = null) => new()
    {
        Id   = id ?? Guid.NewGuid(),
        Kind = ProjectKind.General,
        Name = "Test Project",
    };

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsProject()
    {
        var project = SampleProject();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, project));
        var sut = Sut(handler);

        var result = await sut.GetByIdAsync(project.Id);

        result!.Id.Should().Be(project.Id);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/{project.Id}");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        (await sut.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_Forbidden_ThrowsUnauthorizedAccessExceptionWithReason()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Forbidden, new { reason = "Not authorized to read this project." }));
        var sut = Sut(handler);

        var act = () => sut.GetByIdAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .WithMessage("Not authorized to read this project.");
    }

    // ── GetByOrganizationAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetByOrganizationAsync_ReturnsProjects()
    {
        var orgId = Guid.NewGuid();
        var projects = new[] { SampleProject(), SampleProject() };
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, projects));
        var sut = Sut(handler);

        var result = await sut.GetByOrganizationAsync(orgId);

        result.Should().HaveCount(2);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/organization/{orgId}");
    }

    [Fact]
    public async Task GetByOrganizationAsync_EmptyBody_ReturnsEmptyList()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, Array.Empty<Project>()));
        var sut = Sut(handler);

        var result = await sut.GetByOrganizationAsync(Guid.NewGuid());

        result.Should().BeEmpty();
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Success_ReturnsCreatedProjectAndPostsToRoot()
    {
        var parentId = Guid.NewGuid();
        var created = new Project { Id = Guid.NewGuid(), Kind = ProjectKind.User, ParentProjectId = parentId, Name = "My Copy" };
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Created, created));
        var sut = Sut(handler);

        var result = await sut.CreateAsync(new Project { Name = "My Copy", ParentProjectId = parentId });

        result.Id.Should().Be(created.Id);
        result.Kind.Should().Be(ProjectKind.User);
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/");
    }

    [Fact]
    public async Task CreateAsync_Forbidden_ThrowsUnauthorizedAccessException()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Forbidden, new { reason = "Not authorized to fork this project." }));
        var sut = Sut(handler);

        var act = () => sut.CreateAsync(new Project { Name = "X", ParentProjectId = Guid.NewGuid() });

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ── UpdateProvidersAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProvidersAsync_Success_ReturnsResolvedTarget()
    {
        var id = Guid.NewGuid();
        var returned = SampleProject(id);
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, returned));
        var sut = Sut(handler);

        var result = await sut.UpdateProvidersAsync(id, []);

        result.Id.Should().Be(id);
        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsolutePath.Should().Be($"/{id}/providers");
    }

    [Fact]
    public async Task UpdateProvidersAsync_CopyOnWriteRedirect_ReturnsDifferentForkId()
    {
        var generalId = Guid.NewGuid();
        var forkId = Guid.NewGuid();
        var fork = new Project { Id = forkId, Kind = ProjectKind.User, ParentProjectId = generalId };
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, fork));
        var sut = Sut(handler);

        var result = await sut.UpdateProvidersAsync(generalId, []);

        result.Id.Should().Be(forkId);
        result.Kind.Should().Be(ProjectKind.User);
    }

    [Fact]
    public async Task UpdateProvidersAsync_NotFound_ThrowsKeyNotFoundException()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var act = () => sut.UpdateProvidersAsync(id, []);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateProvidersAsync_Forbidden_ThrowsUnauthorizedAccessException()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.Forbidden, new { reason = "Not authorized (projects:manage-providers) on this project." }));
        var sut = Sut(handler);

        var act = () => sut.UpdateProvidersAsync(Guid.NewGuid(), []);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ── UpdateAssetTypeScopeAsync / UpdateLayerScopeAsync / UpdateViewStateAsync ─

    [Fact]
    public async Task UpdateAssetTypeScopeAsync_BuildsExpectedPath()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SampleProject(id)));
        var sut = Sut(handler);

        await sut.UpdateAssetTypeScopeAsync(id, new ProjectAssetTypeScope());

        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/{id}/asset-types");
    }

    [Fact]
    public async Task UpdateLayerScopeAsync_BuildsExpectedPath()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SampleProject(id)));
        var sut = Sut(handler);

        await sut.UpdateLayerScopeAsync(id, new ProjectLayerScope());

        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/{id}/layers");
    }

    [Fact]
    public async Task UpdateViewStateAsync_BuildsExpectedPath()
    {
        var id = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, SampleProject(id)));
        var sut = Sut(handler);

        await sut.UpdateViewStateAsync(id, new ProjectViewState());

        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be($"/{id}/view");
    }
}
