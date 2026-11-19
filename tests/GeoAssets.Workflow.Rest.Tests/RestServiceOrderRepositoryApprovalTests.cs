using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Services;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// <see cref="RestServiceOrderRepository.AppendActionAsync"/> on the conflicts an approval can
/// meet (XD01-318): the occupancy problem comes back as <see cref="OccupancyConflictException"/>
/// naming the poles; any other conflict is still the order's own.
/// </summary>
public sealed class RestServiceOrderRepositoryApprovalTests
{
    private static readonly OrderActionLog Approve = new(OrderActionType.Approve, "approver-1", new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc));

    private static RestServiceOrderRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage Response(HttpStatusCode status, object? body = null)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
            response.Content = JsonContent.Create(body, options: GeoJsonSerializer.Options);
        return response;
    }

    [Fact]
    public async Task AppendActionAsync_OccupancyConcurrentModification_ThrowsOccupancyConflictException_NamingThePoles()
    {
        var handler = new FakeHttpMessageHandler(_ => Response(HttpStatusCode.Conflict, new
        {
            type = $"urn:geoassets:problem:{OccupancyProblemCodes.ConcurrentModification}",
            title = "The occupancy of a pole changed after its changes were staged.",
            status = 409,
            code = OccupancyProblemCodes.ConcurrentModification,
            poleFeatureIds = new[] { "pole-1", "pole-3" },
        }));

        var act = () => Sut(handler).AppendActionAsync("ord-1", Approve);

        (await act.Should().ThrowAsync<OccupancyConflictException>()).Which.PoleFeatureIds.Should().Equal("pole-1", "pole-3");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppendActionAsync_AnyOtherConflict_ThrowsServiceOrderConcurrencyException(bool withAnotherProblem)
    {
        var handler = new FakeHttpMessageHandler(_ => withAnotherProblem
            ? Response(HttpStatusCode.Conflict, new { code = "targets.concurrent-modification", title = "t", status = 409 })
            : Response(HttpStatusCode.Conflict));

        var act = () => Sut(handler).AppendActionAsync("ord-1", Approve);

        (await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be("ord-1");
    }
}
