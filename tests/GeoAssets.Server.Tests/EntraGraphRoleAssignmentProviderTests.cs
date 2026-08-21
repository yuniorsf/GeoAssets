using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using Xunit;

namespace GeoAssets.Server.Tests;

public class EntraGraphRoleAssignmentProviderTests
{
    private const string WebClientId               = "web-app-client-id";
    private const string ServerClientId             = "server-app-client-id";
    private const string WebServicePrincipalId      = "web-sp-id";
    private const string ServerServicePrincipalId   = "server-sp-id";

    private sealed class StubGraphAccessTokenProvider(string token = "test-token") : IGraphAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(token);
    }

    /// <summary>
    /// A minimal, in-memory stand-in for the subset of Microsoft Graph
    /// (applications/servicePrincipals/appRoleAssignedTo) this provider talks to — lets tests
    /// assert on server-side state after a call rather than just on request payloads.
    /// </summary>
    private sealed class FakeGraphServer
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, JsonArray> _appRoles = new();
        private readonly Dictionary<string, string> _servicePrincipalIds = new();
        private readonly Dictionary<string, JsonArray> _assignments = new();

        public FakeGraphServer WithServicePrincipal(string clientId, string servicePrincipalId)
        {
            _servicePrincipalIds[clientId] = servicePrincipalId;
            _assignments.TryAdd(servicePrincipalId, new JsonArray());
            return this;
        }

        public FakeGraphServer WithAppRole(string clientId, Guid id, string name, string description = "", bool isEnabled = true)
        {
            _appRoles.TryAdd(clientId, new JsonArray());
            _appRoles[clientId].Add(new JsonObject
            {
                ["id"]                 = id.ToString(),
                ["displayName"]        = name,
                ["description"]        = description,
                ["value"]              = name,
                ["isEnabled"]          = isEnabled,
                ["allowedMemberTypes"] = new JsonArray("User"),
            });
            return this;
        }

        public FakeGraphServer WithAssignment(string servicePrincipalId, string assignmentId, string principalId, Guid appRoleId)
        {
            _assignments.TryAdd(servicePrincipalId, new JsonArray());
            _assignments[servicePrincipalId].Add(new JsonObject
            {
                ["id"]          = assignmentId,
                ["principalId"] = principalId,
                ["resourceId"]  = servicePrincipalId,
                ["appRoleId"]   = appRoleId.ToString(),
            });
            return this;
        }

        public IReadOnlyList<JsonNode> AppRolesFor(string clientId)
        {
            lock (_gate)
                return _appRoles.TryGetValue(clientId, out var arr) ? arr.Select(n => n!).ToList() : [];
        }

        public IReadOnlyList<JsonNode> AssignmentsFor(string servicePrincipalId)
        {
            lock (_gate)
                return _assignments.TryGetValue(servicePrincipalId, out var arr) ? arr.Select(n => n!).ToList() : [];
        }

        public HttpMessageHandler Handler => new FakeHttpMessageHandler(Respond);

        private HttpResponseMessage Respond(HttpRequestMessage request)
        {
            lock (_gate)
            {
                var url = request.RequestUri!.ToString();

                var appMatch = Regex.Match(url, @"applications\(appId='([^']+)'\)");
                if (appMatch.Success)
                {
                    var clientId = appMatch.Groups[1].Value;
                    if (request.Method == HttpMethod.Get)
                    {
                        var roles = _appRoles.TryGetValue(clientId, out var arr) ? arr : new JsonArray();
                        return JsonResponse(new JsonObject { ["appRoles"] = roles.DeepClone() });
                    }
                    if (request.Method == HttpMethod.Patch)
                    {
                        var body = ReadBody(request);
                        _appRoles[clientId] = (JsonArray)body["appRoles"]!.DeepClone();
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    }
                }

                var spMatch = Regex.Match(url, @"servicePrincipals\(appId='([^']+)'\)");
                if (spMatch.Success && request.Method == HttpMethod.Get)
                    return JsonResponse(new JsonObject { ["id"] = _servicePrincipalIds[spMatch.Groups[1].Value] });

                var assignMatch = Regex.Match(url, @"servicePrincipals/([^/]+)/appRoleAssignedTo(?:/(.+))?$");
                if (assignMatch.Success)
                {
                    var spId = assignMatch.Groups[1].Value;
                    var assignmentId = assignMatch.Groups[2].Success ? assignMatch.Groups[2].Value : null;
                    _assignments.TryAdd(spId, new JsonArray());

                    if (request.Method == HttpMethod.Get && assignmentId is null)
                        return JsonResponse(new JsonObject { ["value"] = _assignments[spId].DeepClone() });

                    if (request.Method == HttpMethod.Post && assignmentId is null)
                    {
                        var body = ReadBody(request);
                        _assignments[spId].Add(new JsonObject
                        {
                            ["id"]          = Guid.NewGuid().ToString(),
                            ["principalId"] = body["principalId"]!.DeepClone(),
                            ["resourceId"]  = body["resourceId"]!.DeepClone(),
                            ["appRoleId"]   = body["appRoleId"]!.DeepClone(),
                        });
                        return new HttpResponseMessage(HttpStatusCode.Created);
                    }

                    if (request.Method == HttpMethod.Delete && assignmentId is not null)
                    {
                        var toRemove = _assignments[spId].FirstOrDefault(a => a!["id"]!.GetValue<string>() == assignmentId);
                        if (toRemove is not null) _assignments[spId].Remove(toRemove);
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    }
                }

                throw new InvalidOperationException($"Unhandled fake Graph request: {request.Method} {url}");
            }
        }

        private static JsonObject ReadBody(HttpRequestMessage request)
        {
            var text = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return (JsonObject)JsonNode.Parse(text)!;
        }

        private static HttpResponseMessage JsonResponse(JsonNode node) =>
            new(HttpStatusCode.OK) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    private static EntraGraphRoleAssignmentProvider BuildSut(FakeGraphServer server, string[] targetClientIds) =>
        new(
            new HttpClient(server.Handler) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") },
            new StubGraphAccessTokenProvider(),
            new RoleSyncOptions { TargetApplicationClientIds = targetClientIds });

    // ── RegisterRoleAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RegisterRoleAsync_NewRole_AddsItToEveryTargetApplication()
    {
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithServicePrincipal(ServerClientId, ServerServicePrincipalId);
        var sut = BuildSut(server, [WebClientId, ServerClientId]);
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Supervisor", Description = "desc" };

        await sut.RegisterRoleAsync(role);

        server.AppRolesFor(WebClientId).Should().ContainSingle(r => r["value"]!.GetValue<string>() == "Supervisor");
        server.AppRolesFor(ServerClientId).Should().ContainSingle(r => r["value"]!.GetValue<string>() == "Supervisor");
    }

    [Fact]
    public async Task RegisterRoleAsync_RoleAlreadyRegistered_UpdatesInPlaceRatherThanDuplicating()
    {
        var role = new AppRole { Id = Guid.NewGuid(), Name = "Supervisor", Description = "old" };
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithAppRole(WebClientId, role.Id, "Supervisor", "old")
            .WithAppRole(WebClientId, Guid.NewGuid(), "OtherRole");
        var sut = BuildSut(server, [WebClientId]);

        role.Description = "new description";
        await sut.RegisterRoleAsync(role);

        var roles = server.AppRolesFor(WebClientId);
        roles.Should().HaveCount(2);
        roles.Should().ContainSingle(r =>
            r["value"]!.GetValue<string>() == "Supervisor" && r["description"]!.GetValue<string>() == "new description");
    }

    [Fact]
    public async Task RegisterRoleAsync_ConcurrentDifferentRoles_BothSurviveInAppRoles()
    {
        // Proves the per-application SemaphoreSlim actually prevents the appRoles
        // whole-collection-replace race: without it, two concurrent GET-modify-PATCH cycles
        // against the same app could each read the same starting state and the second PATCH
        // would silently overwrite the first admin's addition.
        var server = new FakeGraphServer().WithServicePrincipal(WebClientId, WebServicePrincipalId);
        var sut = BuildSut(server, [WebClientId]);
        var role1 = new AppRole { Id = Guid.NewGuid(), Name = "RoleA" };
        var role2 = new AppRole { Id = Guid.NewGuid(), Name = "RoleB" };

        await Task.WhenAll(sut.RegisterRoleAsync(role1), sut.RegisterRoleAsync(role2));

        server.AppRolesFor(WebClientId).Should().HaveCount(2);
    }

    // ── UnregisterRoleAsync ────────────────────────────────────────────────

    [Fact]
    public async Task UnregisterRoleAsync_RegisteredRole_RemovesItFromAppRoles()
    {
        var roleId = Guid.NewGuid();
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithAppRole(WebClientId, roleId, "Supervisor");
        var sut = BuildSut(server, [WebClientId]);

        await sut.UnregisterRoleAsync(roleId);

        server.AppRolesFor(WebClientId).Should().BeEmpty();
    }

    [Fact]
    public async Task UnregisterRoleAsync_NeverRegistered_DoesNotThrowOrModifyAppRoles()
    {
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithAppRole(WebClientId, Guid.NewGuid(), "UnrelatedRole");
        var sut = BuildSut(server, [WebClientId]);

        var act = () => sut.UnregisterRoleAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
        server.AppRolesFor(WebClientId).Should().ContainSingle();
    }

    // ── AssignRoleAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task AssignRoleAsync_RegisteredRole_CreatesAssignment()
    {
        var roleId = Guid.NewGuid();
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithAppRole(WebClientId, roleId, "Supervisor");
        var sut = BuildSut(server, [WebClientId]);

        await sut.AssignRoleAsync("user-1", "Supervisor");

        server.AssignmentsFor(WebServicePrincipalId).Should().ContainSingle(a =>
            a["principalId"]!.GetValue<string>() == "user-1" && a["appRoleId"]!.GetValue<Guid>() == roleId);
    }

    [Fact]
    public async Task AssignRoleAsync_AlreadyAssigned_DoesNotCreateADuplicateAssignment()
    {
        var roleId = Guid.NewGuid();
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithAppRole(WebClientId, roleId, "Supervisor")
            .WithAssignment(WebServicePrincipalId, "existing-assignment", "user-1", roleId);
        var sut = BuildSut(server, [WebClientId]);

        await sut.AssignRoleAsync("user-1", "Supervisor");

        server.AssignmentsFor(WebServicePrincipalId).Should().ContainSingle();
    }

    [Fact]
    public async Task AssignRoleAsync_RoleNeverRegistered_ThrowsInvalidOperationException()
    {
        var server = new FakeGraphServer().WithServicePrincipal(WebClientId, WebServicePrincipalId);
        var sut = BuildSut(server, [WebClientId]);

        var act = () => sut.AssignRoleAsync("user-1", "GhostRole");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── RevokeRoleAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeRoleAsync_ExistingAssignment_RemovesIt()
    {
        var roleId = Guid.NewGuid();
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithAppRole(WebClientId, roleId, "Supervisor")
            .WithAssignment(WebServicePrincipalId, "existing-assignment", "user-1", roleId);
        var sut = BuildSut(server, [WebClientId]);

        await sut.RevokeRoleAsync("user-1", "Supervisor");

        server.AssignmentsFor(WebServicePrincipalId).Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeRoleAsync_NotCurrentlyAssigned_DoesNotThrow()
    {
        var roleId = Guid.NewGuid();
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithAppRole(WebClientId, roleId, "Supervisor");
        var sut = BuildSut(server, [WebClientId]);

        var act = () => sut.RevokeRoleAsync("user-1", "Supervisor");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeRoleAsync_RoleNeverRegistered_DoesNotThrow()
    {
        var server = new FakeGraphServer().WithServicePrincipal(WebClientId, WebServicePrincipalId);
        var sut = BuildSut(server, [WebClientId]);

        var act = () => sut.RevokeRoleAsync("user-1", "GhostRole");

        await act.Should().NotThrowAsync();
    }

    // ── GetAssignedRoleNamesAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetAssignedRoleNamesAsync_UnionsAndDedupesAcrossAllTargetApplications()
    {
        var roleId = Guid.NewGuid();
        var server = new FakeGraphServer()
            .WithServicePrincipal(WebClientId, WebServicePrincipalId)
            .WithServicePrincipal(ServerClientId, ServerServicePrincipalId)
            .WithAppRole(WebClientId, roleId, "Supervisor")
            .WithAppRole(ServerClientId, roleId, "Supervisor")
            .WithAssignment(WebServicePrincipalId, "a1", "user-1", roleId)
            .WithAssignment(ServerServicePrincipalId, "a2", "user-1", roleId);
        var sut = BuildSut(server, [WebClientId, ServerClientId]);

        var names = await sut.GetAssignedRoleNamesAsync("user-1");

        names.Should().BeEquivalentTo(["Supervisor"]);
    }

    [Fact]
    public async Task GetAssignedRoleNamesAsync_NoAssignments_ReturnsEmpty()
    {
        var server = new FakeGraphServer().WithServicePrincipal(WebClientId, WebServicePrincipalId);
        var sut = BuildSut(server, [WebClientId]);

        var names = await sut.GetAssignedRoleNamesAsync("user-1");

        names.Should().BeEmpty();
    }
}
