using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// <c>GET /order-types</c> and <c>GET /order-types/{id}</c> serve the clients the whole catalog (XD01-378): the types
/// registered in code (the shared-use ones the NeutralOperator module registers) overlaid by the persisted ones, which
/// replace a registered type of the same id. Same <see cref="OrderTargetsApiHost"/> identities as the other order routes.
/// </summary>
public sealed class OrderTypesCatalogEndpointTests
{
    private static readonly Uri OrderTypesUri = new("order-types", UriKind.Relative);

    private static async Task<OrderType[]> GetAllAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<OrderType[]>(OrderTypesUri, GeoJsonSerializer.Options))!;

    [Fact]
    public async Task GetAll_ReturnsTheCodeRegisteredTypes_MergedWithThePersistedOnes()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        var repository = host.Server.Services.GetRequiredService<IOrderTypeRepository>();
        await repository.AddAsync(new OrderType { Id = "persisted-only", DisplayName = "Persisted only" });
        using var client = host.ClientFor("supervisor-a");

        var all = await GetAllAsync(client);

        all.Select(t => t.Id).Should().Contain([SharedUseOrderTypes.PoleWorkId, SharedUseOrderTypes.OccupantNoticeId, "persisted-only"]);
        all.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        all.Single(t => string.Equals(t.Id, SharedUseOrderTypes.PoleWorkId, StringComparison.Ordinal)).TargetPolicy!.FanOut.Should().NotBeNull("the registered type travels whole");
    }

    [Theory]
    [InlineData("shared-use-pole-work")]
    [InlineData("SHARED-USE-POLE-WORK")]
    public async Task GetAll_APersistedTypeReplacesTheRegisteredOneOfTheSameId_ComparedCaseInsensitively(string persistedId)
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        var repository = host.Server.Services.GetRequiredService<IOrderTypeRepository>();
        await repository.AddAsync(new OrderType { Id = persistedId, DisplayName = "Persisted pole work" });
        using var client = host.ClientFor("supervisor-a");

        var all = await GetAllAsync(client);

        all.Where(t => string.Equals(t.Id, SharedUseOrderTypes.PoleWorkId, StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle().Which.DisplayName.Should().Be("Persisted pole work");
    }

    [Fact]
    public async Task GetAll_IsOrderedByIdOrdinal()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        var repository = host.Server.Services.GetRequiredService<IOrderTypeRepository>();
        await repository.AddAsync(new OrderType { Id = "Zulu-persisted", DisplayName = "Zulu" });
        await repository.AddAsync(new OrderType { Id = "alpha-persisted", DisplayName = "Alpha" });
        using var client = host.ClientFor("supervisor-a");

        var ids = (await GetAllAsync(client)).Select(t => t.Id).ToList();

        ids.Should().Equal(ids.Order(StringComparer.Ordinal));
        ids.IndexOf("Zulu-persisted").Should().BeLessThan(ids.IndexOf("alpha-persisted"), "ordinal puts upper case before lower case");
    }

    [Fact]
    public async Task GetById_FallsBackToTheRegistry_WhenNothingIsPersisted()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        using var client = host.ClientFor("supervisor-a");

        var response = await client.GetAsync(new Uri($"order-types/{SharedUseOrderTypes.PoleWorkId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var type = await response.Content.ReadFromJsonAsync<OrderType>(GeoJsonSerializer.Options);
        type!.Id.Should().Be(SharedUseOrderTypes.PoleWorkId);
        type.TargetPolicy!.FanOut.Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_PrefersThePersistedType_OverTheRegisteredOne()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        var repository = host.Server.Services.GetRequiredService<IOrderTypeRepository>();
        await repository.AddAsync(new OrderType { Id = SharedUseOrderTypes.PoleWorkId, DisplayName = "Persisted pole work" });
        using var client = host.ClientFor("supervisor-a");

        var type = await client.GetFromJsonAsync<OrderType>(
            new Uri($"order-types/{SharedUseOrderTypes.PoleWorkId}", UriKind.Relative), GeoJsonSerializer.Options);

        type!.DisplayName.Should().Be("Persisted pole work");
    }

    [Fact]
    public async Task GetById_AnIdNeitherPersistedNorRegistered_Returns404()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        using var client = host.ClientFor("supervisor-a");

        var response = await client.GetAsync(new Uri("order-types/no-such-type", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
