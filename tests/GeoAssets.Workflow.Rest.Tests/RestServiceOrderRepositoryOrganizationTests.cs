using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// XD01-332 — what the REST-backed repository makes of the server's organization answers, so a
/// client sees the same failures it would see against the store directly.
/// </summary>
public class RestServiceOrderRepositoryOrganizationTests
{
    private static RestServiceOrderRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) =>
        new(status) { Content = JsonContent.Create(body, options: GeoJsonSerializer.Options) };

    private static ServiceOrder Order() => new() { Id = "a", OrderTypeId = "inspection", Status = ServiceOrderStatus.Draft };

    [Fact]
    public async Task XD01_332_UpdateAsync_ServerRefusesAChangedOrganization_ThrowsServiceOrderOrganizationException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(
            HttpStatusCode.BadRequest,
            new { code = ServiceOrderOrganization.ChangeRefusedCode, reason = "A service order's organization cannot be changed after the order is created." }));

        var act = () => Sut(handler).UpdateAsync(Order());

        var thrown = (await act.Should().ThrowAsync<ServiceOrderOrganizationException>()).Which;
        thrown.OrderId.Should().Be("a");
        thrown.Message.Should().Contain("cannot be changed");
    }

    [Theory]
    [InlineData("update")]
    [InlineData("dispatch")]
    public async Task XD01_332_Writes_OnAnOrderTheCallerIsOutsideTheOrganizationOf_SurfaceAsNotFound_LikeAnUnknownOrder(string write)
    {
        // The server answers a caller outside the order's organization with the 404 of an
        // unknown order; the client cannot tell them apart either.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        Func<Task> act = string.Equals(write, "update", StringComparison.Ordinal)
            ? () => sut.UpdateAsync(Order())
            : () => sut.AppendDispatchAsync("a", new OrderDispatch("crew", DispatchTargetType.Group, "me", new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc)));

        var thrown = (await act.Should().ThrowAsync<KeyNotFoundException>()).Which;
        thrown.Message.Should().Contain("'a'");
    }

    [Fact]
    public async Task XD01_332_GetByIdAsync_OrderTheCallerIsOutsideTheOrganizationOf_ReadsAsAbsent()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await Sut(handler).GetByIdAsync("a")).Should().BeNull();
    }
}
