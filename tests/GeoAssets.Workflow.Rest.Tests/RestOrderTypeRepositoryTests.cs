using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

public class RestOrderTypeRepositoryTests
{
    private static RestOrderTypeRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/") });

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object? body = null)
    {
        var response = new HttpResponseMessage(status);
        if (body is not null)
            response.Content = JsonContent.Create(body, options: GeoJsonSerializer.GetOptions());
        return response;
    }

    private static OrderType Type(string id = "inspection") => new()
    {
        Id = id,
        DisplayName = "Inspección",
    };

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsOrderType()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, Type()));
        var sut = Sut(handler);

        (await sut.GetByIdAsync("inspection"))!.Id.Should().Be("inspection");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/order-types/inspection");
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
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK, new[] { Type("inspection"), Type("maintenance") }));
        var sut = Sut(handler);

        var types = await sut.GetAllAsync();

        types.Should().HaveCount(2);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/order-types");
    }

    [Fact]
    public async Task AddAsync_PostsToOrderTypes()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.Created));
        var sut = Sut(handler);

        await sut.AddAsync(Type());

        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.RequestUri!.AbsolutePath.Should().Be("/order-types");
    }

    [Fact]
    public async Task AddAsync_Failure_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.BadRequest));
        var sut = Sut(handler);

        var act = () => sut.AddAsync(Type());

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task UpdateAsync_PutsToOrderTypeId()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.UpdateAsync(Type("inspection"));

        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Put);
        req.RequestUri!.AbsolutePath.Should().Be("/order-types/inspection");
    }

    [Fact]
    public async Task DeleteAsync_DeletesOrderTypeId()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.DeleteAsync("inspection");

        var req = handler.Requests.Single();
        req.Method.Should().Be(HttpMethod.Delete);
        req.RequestUri!.AbsolutePath.Should().Be("/order-types/inspection");
    }

    [Fact]
    public async Task SaveChangesAsync_IsNoOp()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Should not make an HTTP call."));
        var sut = Sut(handler);

        var act = () => sut.SaveChangesAsync();

        await act.Should().NotThrowAsync();
        handler.Requests.Should().BeEmpty();
    }
}
