using System.Net;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// <see cref="RestVerticalRegistrationClient"/> (XD01-382): the two idempotent PUTs a vertical makes to the kernel at bootstrap — its order-type
/// descriptors and its permissions — with the vertical escaped into the path, the body the kernel's endpoints read, and the service principal's bearer.
/// </summary>
public sealed class RestVerticalRegistrationClientTests
{
    private static readonly OrderTypeDescriptor PoleWork = new()
    {
        VerticalId = "neutral-operator",
        OrderTypeId = "shared-use-pole-work",
        DisplayNameKey = "orderTypes.sharedUsePoleWork",
        ApiBaseRoute = "/api/workflow/service-orders",
        FeatureActions = [new FeatureActionDescriptor("occupiable-support", "create", "featureActions.createSharedUseOrder", "shared-use-pole-work", "/api/workflow/service-orders")],
        TargetPolicy = new OrderTargetPolicy { TargetAssetTypeRoles = ["occupiable-support"], SelectionLimit = 5000 },
        TerminalStatuses = ["Approved", "Cancelled"],
    };

    private static readonly ModulePermissionSet Permissions = new()
    {
        Permissions = [new PermissionDefinition("neutral:read", "neutral", "read", "permissions.neutral.read")],
        RoleBundles = [new RoleBundleContribution(new Guid("10000000-0000-0000-0000-000000000005"), "IspTenant", "Tenant", ["serviceorders:read"])],
    };

    private static RestVerticalRegistrationClient Sut(RecordingKernelHandler handler, FakeServiceAuthTokenProvider? tokens = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://kernel.test/") }, tokens);

    private static RecordingKernelHandler NoContent() => new(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

    [Fact]
    public async Task PublishOrderTypesAsync_PutsTheWholeDescriptorSet_ToTheVerticalsRegistryRoute()
    {
        var handler = NoContent();

        await Sut(handler).PublishOrderTypesAsync("neutral-operator", [PoleWork], CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.Path.Should().Be("/api/registry/verticals/neutral-operator/order-types");
        var descriptor = request.Json.EnumerateArray().Should().ContainSingle().Subject;
        descriptor.GetProperty("verticalId").GetString().Should().Be("neutral-operator");
        descriptor.GetProperty("orderTypeId").GetString().Should().Be("shared-use-pole-work");
        descriptor.GetProperty("displayNameKey").GetString().Should().Be("orderTypes.sharedUsePoleWork");
        descriptor.GetProperty("apiBaseRoute").GetString().Should().Be("/api/workflow/service-orders");
        descriptor.GetProperty("featureActions").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("role").GetString().Should().Be("occupiable-support");
        descriptor.GetProperty("targetPolicy").GetProperty("selectionLimit").GetInt32().Should().Be(5000);
        descriptor.GetProperty("terminalStatuses").EnumerateArray().Select(e => e.GetString()).Should().Equal("Approved", "Cancelled");
    }

    [Fact]
    public async Task PublishOrderTypesAsync_WithNoDescriptors_PutsAnEmptyArray_WhichReplacesTheVerticalsSet()
    {
        var handler = NoContent();

        await Sut(handler).PublishOrderTypesAsync("neutral-operator", [], CancellationToken.None);

        handler.Requests.Single().Body.Should().Be("[]");
    }

    [Fact]
    public async Task PublishPermissionsAsync_PutsThePermissionsAndRoleBundles_ToTheVerticalsIdentityRoute()
    {
        var handler = NoContent();

        await Sut(handler).PublishPermissionsAsync("neutral-operator", Permissions, CancellationToken.None);

        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Put);
        request.Path.Should().Be("/api/identity/verticals/neutral-operator/permissions");
        request.Json.GetProperty("permissions").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("code").GetString().Should().Be("neutral:read");
        var bundle = request.Json.GetProperty("roleBundles").EnumerateArray().Should().ContainSingle().Subject;
        bundle.GetProperty("roleName").GetString().Should().Be("IspTenant");
        bundle.GetProperty("roleId").GetGuid().Should().Be(new Guid("10000000-0000-0000-0000-000000000005"));
        bundle.GetProperty("permissionCodes").EnumerateArray().Select(e => e.GetString()).Should().Equal("serviceorders:read");
    }

    [Theory]
    [InlineData("neutral operator", "neutral%20operator")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("../identity", "..%2Fidentity")]
    public async Task BothRoutes_EscapeTheVerticalIdIntoOnePathSegment(string verticalId, string escaped)
    {
        var handler = NoContent();
        var sut = Sut(handler);

        await sut.PublishOrderTypesAsync(verticalId, [], CancellationToken.None);
        await sut.PublishPermissionsAsync(verticalId, Permissions, CancellationToken.None);

        handler.Requests.Select(r => r.Original).Should().Equal(
            $"http://kernel.test/api/registry/verticals/{escaped}/order-types", $"http://kernel.test/api/identity/verticals/{escaped}/permissions");
    }

    [Fact]
    public async Task BothCalls_CarryAFreshServiceBearerToken()
    {
        var handler = NoContent();
        var sut = Sut(handler, new FakeServiceAuthTokenProvider());

        await sut.PublishOrderTypesAsync("neutral-operator", [PoleWork], CancellationToken.None);
        await sut.PublishPermissionsAsync("neutral-operator", Permissions, CancellationToken.None);

        handler.Requests.Select(r => r.Authorization).Should().Equal("Bearer service-token-1", "Bearer service-token-2");
    }

    [Fact]
    public async Task WithoutATokenProvider_NoAuthorizationIsSent()
    {
        var handler = NoContent();

        await Sut(handler).PublishOrderTypesAsync("neutral-operator", [PoleWork], CancellationToken.None);

        handler.Requests.Single().Authorization.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task BothCalls_WhenTheKernelRefuses_ThrowHttpRequestException_WithTheStatus(HttpStatusCode status)
    {
        var sut = Sut(new RecordingKernelHandler(_ => new HttpResponseMessage(status)));

        var orderTypes = async () => await sut.PublishOrderTypesAsync("neutral-operator", [PoleWork], CancellationToken.None);
        var permissions = async () => await sut.PublishPermissionsAsync("neutral-operator", Permissions, CancellationToken.None);

        (await orderTypes.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
        (await permissions.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(status);
    }

    [Fact]
    public Task BothCalls_WhenTheConnectionFails_ThrowHttpRequestException()
    {
        var sut = Sut(new RecordingKernelHandler(_ => throw new HttpRequestException("connection refused")));

        var orderTypes = async () => await sut.PublishOrderTypesAsync("neutral-operator", [PoleWork], CancellationToken.None);

        return orderTypes.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task BothCalls_RefuseBlankOrMissingArguments_BeforeAnyRequest()
    {
        var handler = NoContent();
        var sut = Sut(handler);

        var blankVertical = async () => await sut.PublishOrderTypesAsync(" ", [], CancellationToken.None);
        var noDescriptors = async () => await sut.PublishOrderTypesAsync("neutral-operator", null!, CancellationToken.None);
        var blankPermissionsVertical = async () => await sut.PublishPermissionsAsync("", Permissions, CancellationToken.None);
        var noPermissions = async () => await sut.PublishPermissionsAsync("neutral-operator", null!, CancellationToken.None);

        await blankVertical.Should().ThrowAsync<ArgumentException>();
        await noDescriptors.Should().ThrowAsync<ArgumentNullException>();
        await blankPermissionsVertical.Should().ThrowAsync<ArgumentException>();
        await noPermissions.Should().ThrowAsync<ArgumentNullException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BothCalls_WhenTheCallerCancels_ThrowTheCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = async () => await Sut(NoContent()).PublishOrderTypesAsync("neutral-operator", [PoleWork], cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
