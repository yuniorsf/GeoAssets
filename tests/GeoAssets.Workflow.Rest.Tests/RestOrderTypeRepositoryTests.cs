using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
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
            response.Content = JsonContent.Create(body, options: GeoJsonSerializer.Options);
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
    public Task AddAsync_Failure_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(HttpStatusCode.BadRequest));
        var sut = Sut(handler);

        var act = () => sut.AddAsync(Type());

        return act.Should().ThrowAsync<HttpRequestException>();
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

    // ── TargetPolicy / AttributesSchemaJson over the wire (XD01-304) ──────────

    private static OrderType TypeWithFullPolicy() => new()
    {
        Id = "pole-inspection",
        DisplayName = "Pole inspection",
        AttributesSchemaJson = """{"type":"object"}""",
        TargetPolicy = new OrderTargetPolicy
        {
            TargetAssetTypeIds = ["pole", "manhole"],
            AliasAttributeKey = "tag",
            SelectionLimit = 500,
            Activities = [new("inspect", "Inspect"), new("replace", "Replace")],
            Exclusivity = OrderTargetExclusivity.BlockOnOverlap,
            FanOut = new OrderTargetFanOutPolicy("pole-inspection-notice", OrderTargetFanOutPartyKind.OccupantOrganization),
        },
    };

    /// <summary>
    /// Echo server: keeps whatever body the client last wrote and serves it back on reads, so a
    /// write followed by a read exercises the client's own serialization in both directions.
    /// </summary>
    private static FakeHttpMessageHandler EchoHandler(bool wrapReadsInArray = false)
    {
        string? stored = null;
        return new FakeHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                var payload = wrapReadsInArray ? $"[{stored}]" : stored!;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
                };
            }

            using var reader = new StreamReader(request.Content!.ReadAsStream());
            stored = reader.ReadToEnd();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
    }

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsTargetPolicyAndAttributesSchemaJson()
    {
        var sut = Sut(EchoHandler());

        await sut.AddAsync(TypeWithFullPolicy());
        var loaded = await sut.GetByIdAsync("pole-inspection");

        loaded.Should().BeEquivalentTo(TypeWithFullPolicy());
        loaded!.TargetPolicy!.Exclusivity.Should().Be(OrderTargetExclusivity.BlockOnOverlap);
        loaded.TargetPolicy.FanOut.Should().Be(new OrderTargetFanOutPolicy("pole-inspection-notice"));
    }

    [Fact]
    public async Task UpdateAsync_ThenGetAllAsync_RoundTripsTargetPolicy()
    {
        var sut = Sut(EchoHandler(wrapReadsInArray: true));

        await sut.UpdateAsync(TypeWithFullPolicy());
        var loaded = await sut.GetAllAsync();

        loaded.Should().ContainSingle().Which.Should().BeEquivalentTo(TypeWithFullPolicy());
    }

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_TypeWithoutPolicy_KeepsTargetPolicyNull()
    {
        var sut = Sut(EchoHandler());

        await sut.AddAsync(Type());
        var loaded = await sut.GetByIdAsync("inspection");

        loaded.Should().BeEquivalentTo(Type());
        loaded!.TargetPolicy.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_PayloadWithoutTargetPolicyProperty_LeavesTargetPolicyNull()
    {
        // The shape every server produced before XD01-304.
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"inspection","displayName":"Inspección"}""", System.Text.Encoding.UTF8, "application/json"),
        });

        var loaded = await Sut(handler).GetByIdAsync("inspection");

        loaded!.Id.Should().Be("inspection");
        loaded.TargetPolicy.Should().BeNull();
        loaded.AttributesSchemaJson.Should().BeNull();
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
