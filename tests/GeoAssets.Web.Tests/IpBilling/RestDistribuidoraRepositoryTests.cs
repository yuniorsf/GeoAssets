using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.Web.Services.IpBilling.Rest;
using GeoAssets.Web.Tests.Identity;
using Xunit;

namespace GeoAssets.Web.Tests.IpBilling;

/// <summary>REST contract of <see cref="RestDistribuidoraRepository"/> (XD01-301).</summary>
public class RestDistribuidoraRepositoryTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static RestDistribuidoraRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/api/ip-billing/") });

    private static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: Web) };

    [Fact]
    public async Task GetPageAsync_SendsPageAndPageSize_AndUnwrapsTheEnvelope()
    {
        var enel = new Distribuidora { OrganizationId = Guid.NewGuid(), Name = "Enel SP" };
        var handler = new FakeHttpMessageHandler(_ => Json(new { items = new[] { enel }, totalCount = 7, page = 2, pageSize = 3 }));

        var page = await Sut(handler).GetPageAsync(2, 3);

        handler.Requests.Single().RequestUri!.PathAndQuery.Should().Be("/api/ip-billing/distribuidoras?page=2&pageSize=3");
        page.TotalCount.Should().Be(7);
        page.Items.Should().ContainSingle().Which.Should().BeEquivalentTo(enel);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public async Task GetPageAsync_PageOrPageSizeBelowOne_Throws_WithoutCallingTheServer(int page, int pageSize)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Sut(handler).Invoking(s => s.GetPageAsync(page, pageSize)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsIt_AndNotFound_ReturnsNull()
    {
        var enel = new Distribuidora { OrganizationId = Guid.NewGuid(), Name = "Enel SP" };
        var found = await Sut(new FakeHttpMessageHandler(_ => Json(enel))).GetByIdAsync(enel.Id);
        var missing = await Sut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))).GetByIdAsync(Guid.NewGuid());

        found.Should().BeEquivalentTo(enel);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_PostsTheWriteBody_AndAdoptsTheServerAssignedId()
    {
        var serverId = Guid.NewGuid();
        var local = new Distribuidora { OrganizationId = Guid.NewGuid(), Name = "Enel SP" };
        var handler = new FakeHttpMessageHandler(_ =>
            Json(new Distribuidora { Id = serverId, OrganizationId = local.OrganizationId, Name = local.Name }, HttpStatusCode.Created));

        await Sut(handler).AddAsync(local);
        var sentBody = await handler.Requests.Single().Content!.ReadAsStringAsync();

        handler.Requests.Single().Method.Should().Be(HttpMethod.Post);
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/api/ip-billing/distribuidoras");
        sentBody.Should().Contain("\"name\":\"Enel SP\"").And.Contain(local.OrganizationId.ToString());
        local.Id.Should().Be(serverId);
    }

    [Fact]
    public async Task UpdateAsync_PutsToTheIdRoute_AndDeleteAsync_DeletesIt()
    {
        var enel = new Distribuidora { OrganizationId = Guid.NewGuid(), Name = "Enel SP" };
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.UpdateAsync(enel);
        await sut.DeleteAsync(enel.Id);
        await sut.SaveChangesAsync();

        handler.Requests.Select(r => (r.Method, r.RequestUri!.AbsolutePath)).Should().Equal(
            (HttpMethod.Put, $"/api/ip-billing/distribuidoras/{enel.Id}"),
            (HttpMethod.Delete, $"/api/ip-billing/distribuidoras/{enel.Id}"));
    }

    [Fact]
    public async Task Writes_ServerError_Throws()
    {
        var sut = Sut(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        await sut.Invoking(s => s.UpdateAsync(new Distribuidora { OrganizationId = Guid.NewGuid(), Name = "x" }))
            .Should().ThrowAsync<HttpRequestException>();
        await sut.Invoking(s => s.DeleteAsync(Guid.NewGuid())).Should().ThrowAsync<HttpRequestException>();
        await sut.Invoking(s => s.AddAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }
}
