using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.FieldService.Server;
using GeoAssets.Workflow.Contractors.Governance;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

public class HttpContractorGovernancePortalClientTests
{
    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static IConfiguration Config(string? pushUrl = null, string? balanceBaseUrl = null)
    {
        var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (pushUrl is not null) dict["ContractorGovernancePortal:MeasurementSheetPushUrl"] = pushUrl;
        if (balanceBaseUrl is not null) dict["ContractorGovernancePortal:ContractBalanceQueryBaseUrl"] = balanceBaseUrl;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static MeasurementSheetPortalPayload SamplePayload() => new(
        ContractId: "contract-1",
        ServiceOrderId: "order-1",
        ContractorOrganizationId: Guid.NewGuid(),
        ContractorName: "Empreiteira Teste",
        Items: [],
        TotalBrl: 30m,
        RetentionBrl: 3m,
        NetPayableBrl: 27m,
        DossierSignatureBase64: "c2lnbmF0dXJl");

    [Fact]
    public async Task PushMeasurementSheetAsync_ConfiguredUrlAndSuccessResponse_PostsToConfiguredUrl()
    {
        const string url = "https://portal.example.test/measurement-sheets";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpContractorGovernancePortalClient(new FakeHttpClientFactory(handler), Config(pushUrl: url));

        await client.PushMeasurementSheetAsync(SamplePayload());

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri.Should().Be(new Uri(url));
    }

    [Fact]
    public async Task PushMeasurementSheetAsync_NoUrlConfigured_ThrowsWithoutAttemptingHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpContractorGovernancePortalClient(new FakeHttpClientFactory(handler), Config());

        var act = async () => await client.PushMeasurementSheetAsync(SamplePayload());

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public Task PushMeasurementSheetAsync_NonSuccessStatusCode_Throws()
    {
        const string url = "https://portal.example.test/measurement-sheets";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new HttpContractorGovernancePortalClient(new FakeHttpClientFactory(handler), Config(pushUrl: url));

        var act = async () => await client.PushMeasurementSheetAsync(SamplePayload());

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task QueryContractBalanceAsync_ConfiguredBaseUrl_QueriesContractSpecificPath()
    {
        const string baseUrl = "https://portal.example.test/contracts";
        var balance = new ContractBalanceQueryResult("contract-1", 5_000m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(balance),
        });
        var client = new HttpContractorGovernancePortalClient(new FakeHttpClientFactory(handler), Config(balanceBaseUrl: baseUrl));

        var result = await client.QueryContractBalanceAsync("contract-1");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[0].RequestUri.Should().Be(new Uri($"{baseUrl}/contract-1/balance"));
        result.RemainingBalanceBrl.Should().Be(5_000m);
    }

    [Fact]
    public async Task QueryContractBalanceAsync_NoBaseUrlConfigured_ThrowsWithoutAttemptingHttpCall()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpContractorGovernancePortalClient(new FakeHttpClientFactory(handler), Config());

        var act = async () => await client.QueryContractBalanceAsync("contract-1");

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Requests.Should().BeEmpty();
    }
}
