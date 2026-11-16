using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.Web.Services.IpBilling.Rest;
using GeoAssets.Web.Tests.Identity;
using Xunit;

namespace GeoAssets.Web.Tests.IpBilling;

/// <summary>REST contract of <see cref="RestDistribuidoraLampWattageRepository"/> (XD01-301).</summary>
public class RestDistribuidoraLampWattageRepositoryTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid DistribuidoraId = Guid.NewGuid();

    private static RestDistribuidoraLampWattageRepository Sut(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://test/api/ip-billing/") });

    private static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: Web) };

    private static DistribuidoraLampWattage Row(LampTypeCode code, int wattageW, decimal ballastLossW) => new()
    {
        DistribuidoraId = DistribuidoraId,
        Code = code,
        WattageW = wattageW,
        BallastLossW = ballastLossW,
    };

    /// <summary>Serves <paramref name="rows"/> through the paged list route, honouring page/pageSize.</summary>
    private static FakeHttpMessageHandler PagedChart(List<DistribuidoraLampWattage> rows) => new(req =>
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query);
        var page = int.Parse(query["page"]!, System.Globalization.CultureInfo.InvariantCulture);
        var pageSize = int.Parse(query["pageSize"]!, System.Globalization.CultureInfo.InvariantCulture);
        return Json(new { items = rows.Skip((page - 1) * pageSize).Take(pageSize), totalCount = rows.Count, page, pageSize });
    });

    [Fact]
    public async Task GetPageAsync_SendsPageAndPageSize_AndUnwrapsTheEnvelope()
    {
        var handler = PagedChart([Row(LampTypeCode.VS, 70, 14m), Row(LampTypeCode.VS, 100, 17m), Row(LampTypeCode.LD, 60, 0m)]);

        var page = await Sut(handler).GetPageAsync(DistribuidoraId, 2, 2);

        handler.Requests.Single().RequestUri!.PathAndQuery.Should().Be(
            $"/api/ip-billing/distribuidoras/{DistribuidoraId}/lamp-wattages?page=2&pageSize=2");
        page.TotalCount.Should().Be(3);
        page.Items.Should().ContainSingle().Which.Code.Should().Be(LampTypeCode.LD);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public async Task GetPageAsync_PageOrPageSizeBelowOne_Throws_WithoutCallingTheServer(int page, int pageSize)
    {
        var handler = PagedChart([]);

        await Sut(handler).Invoking(s => s.GetPageAsync(DistribuidoraId, page, pageSize))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FindAsync_WalksEveryPage_UntilTheRowIsFoundOrTheChartEnds()
    {
        var rows = Enumerable.Range(1, 250).Select(w => Row(LampTypeCode.VS, w, 1m)).ToList();
        var handler = PagedChart(rows);
        var sut = Sut(handler);

        var found = await sut.FindAsync(DistribuidoraId, LampTypeCode.VS, 240);
        var missing = await sut.FindAsync(DistribuidoraId, LampTypeCode.LD, 240);

        found!.WattageW.Should().Be(240);
        missing.Should().BeNull();
        handler.Requests.Should().HaveCount(6, "250 rows at 100 per page is three requests per walk");
    }

    [Fact]
    public async Task GetMeanTotalBilledWAsync_EmptyChart_ReturnsNull_OtherwiseTheMean()
    {
        (await Sut(PagedChart([])).GetMeanTotalBilledWAsync(DistribuidoraId)).Should().BeNull();
        (await Sut(PagedChart([Row(LampTypeCode.VS, 70, 14m), Row(LampTypeCode.VS, 100, 17m)]))
            .GetMeanTotalBilledWAsync(DistribuidoraId)).Should().Be(100.5m);
    }

    [Fact]
    public async Task UpsertAsync_PutsTheRow_AndReturnsTheStoredOne()
    {
        var stored = Row(LampTypeCode.VS, 100, 17m);
        var handler = new FakeHttpMessageHandler(_ => Json(stored));

        var result = await Sut(handler).UpsertAsync(Row(LampTypeCode.VS, 100, 17m));

        var request = handler.Requests.Single();
        var sentBody = await request.Content!.ReadAsStringAsync();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsolutePath.Should().Be($"/api/ip-billing/distribuidoras/{DistribuidoraId}/lamp-wattages");
        sentBody.Should().Contain("\"wattageW\":100").And.Contain("\"ballastLossW\":17");
        result.Id.Should().Be(stored.Id);
    }

    [Fact]
    public Task UpsertAsync_ServerRejection_SurfacesTheServersMessage()
    {
        var handler = new FakeHttpMessageHandler(_ => Json(new { message = "'wattageW' must be positive." }, HttpStatusCode.BadRequest));

        return Sut(handler).Invoking(s => s.UpsertAsync(Row(LampTypeCode.VS, 0, 0m)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("'wattageW' must be positive.");
    }

    [Fact]
    public async Task DeleteAsync_DeletesByRowId()
    {
        var rowId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Sut(handler);

        await sut.DeleteAsync(rowId);
        await sut.SaveChangesAsync();

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.AbsolutePath.Should().Be($"/api/ip-billing/distribuidoras/lamp-wattages/{rowId}");
    }
}
