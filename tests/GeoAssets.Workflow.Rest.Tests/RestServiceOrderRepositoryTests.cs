using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

public class RestServiceOrderRepositoryTests
{
    private static RestServiceOrderRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object? body = null)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
            response.Content = JsonContent.Create(body, options: GeoJsonSerializer.GetOptions());
        return response;
    }

    private static ServiceOrder Order(string id = "a", string status = ServiceOrderStatus.Draft) => new()
    {
        Id = id,
        OrderTypeId = "inspection",
        Status = status,
    };

    // ── Reads ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsOrder()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, Order("a")));
        var sut = Sut(handler);

        var order = await sut.GetByIdAsync("a");

        order!.Id.Should().Be("a");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/service-orders/a");
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        (await sut.GetByIdAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_DeserializesArray()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, new[] { Order("a"), Order("b") }));
        var sut = Sut(handler);

        var orders = await sut.GetAllAsync();

        orders.Should().HaveCount(2);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/service-orders");
    }

    [Fact]
    public async Task GetParentAsync_NotFound_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        (await sut.GetParentAsync("child")).Should().BeNull();
    }

    [Fact]
    public async Task GetParentAsync_Found_ReturnsOrder()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, Order("parent")));
        var sut = Sut(handler);

        (await sut.GetParentAsync("child"))!.Id.Should().Be("parent");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/service-orders/child/parent");
    }

    [Fact]
    public async Task GetChildrenAsync_BuildsExpectedPath()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, Array.Empty<ServiceOrder>()));
        var sut = Sut(handler);

        await sut.GetChildrenAsync("parent-1");

        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/service-orders/parent-1/children");
    }

    [Fact]
    public async Task GetByDateRangeAsync_BuildsIsoQueryString()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, Array.Empty<ServiceOrder>()));
        var sut = Sut(handler);
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to   = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await sut.GetByDateRangeAsync(from, to);

        var query = handler.Requests.Single().RequestUri!.Query;
        query.Should().Contain(Uri.EscapeDataString(from.ToString("O")));
        query.Should().Contain(Uri.EscapeDataString(to.ToString("O")));
    }

    [Fact]
    public async Task GetDispatchedToAsync_BuildsTargetTypeQueryString()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, Array.Empty<ServiceOrder>()));
        var sut = Sut(handler);

        await sut.GetDispatchedToAsync("org-1", DispatchTargetType.Organization);

        var uri = handler.Requests.Single().RequestUri!;
        uri.AbsolutePath.Should().Be("/service-orders/dispatched-to/org-1");
        uri.Query.Should().Contain("targetType=Organization");
    }

    // ── AddAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_Success_RaisesOrderAdded()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Created));
        var sut = Sut(handler);
        IServiceOrder? raised = null;
        sut.OrderAdded += (_, o) => raised = o;

        var order = Order("a");
        await sut.AddAsync(order);

        raised.Should().BeSameAs(order);
        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/service-orders");
    }

    [Fact]
    public async Task AddAsync_AttributeValidationFailure_ThrowsWithoutRaisingEvent()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.BadRequest, new { orderTypeId = "inspection", errors = new[] { "severity is required" } }));
        var sut = Sut(handler);
        var raised = false;
        sut.OrderAdded += (_, _) => raised = true;

        var act = () => sut.AddAsync(Order("a"));

        var thrown = (await act.Should().ThrowAsync<ServiceOrderAttributeValidationException>()).Which;
        thrown.OrderTypeId.Should().Be("inspection");
        thrown.Errors.Should().ContainSingle("severity is required");
        raised.Should().BeFalse();
    }

    [Fact]
    public void AddAsync_NonServiceOrderImplementation_ThrowsArgumentException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Created));
        var sut = Sut(handler);

        var act = () => sut.AddAsync(new FakeServiceOrder());

        act.Should().ThrowAsync<ArgumentException>();
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Success_RaisesOrderUpdatedOnly()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NoContent));
        var sut = Sut(handler);
        var updatedCount = 0;
        var statusChangedCount = 0;
        sut.OrderUpdated      += (_, _) => updatedCount++;
        sut.OrderStatusChanged += (_, _) => statusChangedCount++;

        await sut.UpdateAsync(Order("a", ServiceOrderStatus.InProgress));

        updatedCount.Should().Be(1);
        statusChangedCount.Should().Be(0);
        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Put);
        req.RequestUri!.AbsolutePath.Should().Be("/service-orders/a");
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ThrowsKeyNotFoundException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var act = () => sut.UpdateAsync(Order("missing"));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_Conflict_ThrowsServiceOrderConcurrencyException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Conflict));
        var sut = Sut(handler);

        var act = () => sut.UpdateAsync(Order("a"));

        var thrown = (await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()).Which;
        thrown.OrderId.Should().Be("a");
    }

    [Fact]
    public async Task UpdateAsync_InvalidTransition_ThrowsWithFromAndTo()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.BadRequest, new { from = ServiceOrderStatus.Completed, to = ServiceOrderStatus.Draft }));
        var sut = Sut(handler);

        var act = () => sut.UpdateAsync(Order("a", ServiceOrderStatus.Draft));

        var thrown = (await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which;
        thrown.From.Should().Be(ServiceOrderStatus.Completed);
        thrown.To.Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task UpdateAsync_AttributeValidationFailure_ThrowsServiceOrderAttributeValidationException()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            JsonResponse(HttpStatusCode.BadRequest, new { orderTypeId = "inspection", errors = new[] { "bad" } }));
        var sut = Sut(handler);

        var act = () => sut.UpdateAsync(Order("a"));

        await act.Should().ThrowAsync<ServiceOrderAttributeValidationException>();
    }

    [Fact]
    public async Task UpdateAsync_UnrecognizedBadRequestShape_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.BadRequest, new { message = "nope" }));
        var sut = Sut(handler);

        var act = () => sut.UpdateAsync(Order("a"));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── AppendDispatchAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task AppendDispatchAsync_Success_FetchesUpdatedOrderAndRaisesOrderUpdated()
    {
        var handler = new FakeHttpMessageHandler(req =>
            req.Method == HttpMethod.Post
                ? JsonResponse(HttpStatusCode.NoContent)
                : JsonResponse(HttpStatusCode.OK, Order("a")));
        var sut = Sut(handler);
        IServiceOrder? raised = null;
        sut.OrderUpdated += (_, o) => raised = o;

        await sut.AppendDispatchAsync("a", new OrderDispatch("target-1", DispatchTargetType.User, "dispatcher-1", DateTime.UtcNow));

        raised!.Id.Should().Be("a");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/service-orders/a/dispatch");
    }

    [Fact]
    public async Task AppendDispatchAsync_NotFound_ThrowsKeyNotFoundException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var act = () => sut.AppendDispatchAsync("missing", new OrderDispatch("t", DispatchTargetType.User, "d", DateTime.UtcNow));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── AppendActionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AppendActionAsync_NoResultingStatus_SkipsPreFetchAndDoesNotRaiseStatusChanged()
    {
        var handler = new FakeHttpMessageHandler(req =>
            req.Method == HttpMethod.Post
                ? JsonResponse(HttpStatusCode.NoContent)
                : JsonResponse(HttpStatusCode.OK, Order("a")));
        var sut = Sut(handler);
        var statusChanged = false;
        sut.OrderStatusChanged += (_, _) => statusChanged = true;

        await sut.AppendActionAsync("a", new OrderActionLog(OrderActionType.Approve, "user-1", DateTime.UtcNow));

        statusChanged.Should().BeFalse();
        // Only POST + the post-write GET — no pre-fetch GET since there's no status to compare.
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task AppendActionAsync_ResultingStatusChanges_RaisesOrderStatusChangedWithPrevious()
    {
        var callIndex = 0;
        var handler = new FakeHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post) return JsonResponse(HttpStatusCode.NoContent);
            callIndex++;
            // First GET (pre-fetch) sees the old status; second GET (post-write) sees the new one.
            return JsonResponse(HttpStatusCode.OK, Order("a", callIndex == 1 ? ServiceOrderStatus.Pending : ServiceOrderStatus.InProgress));
        });
        var sut = Sut(handler);
        (IServiceOrder Order, string Previous)? raised = null;
        sut.OrderStatusChanged += (_, e) => raised = e;

        await sut.AppendActionAsync("a", new OrderActionLog(OrderActionType.Execute, "user-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.InProgress));

        raised.Should().NotBeNull();
        raised!.Value.Previous.Should().Be(ServiceOrderStatus.Pending);
        raised.Value.Order.Status.Should().Be(ServiceOrderStatus.InProgress);
        handler.Requests.Should().HaveCount(3); // pre-fetch GET, POST, post-write GET
    }

    [Fact]
    public async Task AppendActionAsync_ResultingStatusSameAsPrevious_DoesNotRaiseStatusChanged()
    {
        var handler = new FakeHttpMessageHandler(req =>
            req.Method == HttpMethod.Post
                ? JsonResponse(HttpStatusCode.NoContent)
                : JsonResponse(HttpStatusCode.OK, Order("a", ServiceOrderStatus.Draft)));
        var sut = Sut(handler);
        var statusChanged = false;
        sut.OrderStatusChanged += (_, _) => statusChanged = true;

        await sut.AppendActionAsync("a", new OrderActionLog(OrderActionType.Approve, "user-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Draft));

        statusChanged.Should().BeFalse();
    }

    [Fact]
    public async Task AppendActionAsync_InvalidTransition_ThrowsWithFromAndTo()
    {
        var handler = new FakeHttpMessageHandler(req =>
            req.Method == HttpMethod.Get
                ? JsonResponse(HttpStatusCode.OK, Order("a", ServiceOrderStatus.Completed))
                : JsonResponse(HttpStatusCode.BadRequest, new { from = ServiceOrderStatus.Completed, to = ServiceOrderStatus.Draft }));
        var sut = Sut(handler);

        var act = () => sut.AppendActionAsync("a", new OrderActionLog(OrderActionType.Dispatch, "user-1", DateTime.UtcNow, ResultingStatus: ServiceOrderStatus.Draft));

        var thrown = (await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>()).Which;
        thrown.From.Should().Be(ServiceOrderStatus.Completed);
        thrown.To.Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task AppendActionAsync_NotFound_ThrowsKeyNotFoundException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NotFound));
        var sut = Sut(handler);

        var act = () => sut.AppendActionAsync("missing", new OrderActionLog(OrderActionType.Approve, "user-1", DateTime.UtcNow));

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Success_RaisesOrderDeleted()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NoContent));
        var sut = Sut(handler);
        string? raised = null;
        sut.OrderDeleted += (_, id) => raised = id;

        await sut.DeleteAsync("a");

        raised.Should().Be("a");
        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Delete);
        req.RequestUri!.AbsolutePath.Should().Be("/service-orders/a");
    }

    // ── Test double ───────────────────────────────────────────────────────────

    private sealed class FakeServiceOrder : IServiceOrder
    {
        public string Id => "fake";
        public string Title => "";
        public string Description => "";
        public string OrderTypeId => "inspection";
        public string Status => ServiceOrderStatus.Draft;
        public ServiceOrderPriority Priority => ServiceOrderPriority.Normal;
        public string CreatedBy => "";
        public string? AssignedTo => null;
        public DateTime CreatedAt => DateTime.UtcNow;
        public DateTime? UpdatedAt => null;
        public DateTime? ScheduledAt => null;
        public DateTime? CompletedAt => null;
        public IReadOnlyDictionary<string, string> Attributes => new Dictionary<string, string>();
        public IReadOnlyList<Core.Models.GeoFeature> Features => [];
        public GeoAssets.Workflow.Selection.FeatureSelectionSpec? SelectionSpec => null;
        public string? ParentOrderId => null;
        public IReadOnlyList<string> ChildOrderIds => [];
        public IReadOnlyList<OrderDispatch> Dispatches => [];
        public IReadOnlyList<OrderActionLog> ActionLog => [];
    }
}
