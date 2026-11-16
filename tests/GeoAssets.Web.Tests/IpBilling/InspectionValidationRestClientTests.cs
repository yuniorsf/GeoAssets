using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.IpBilling.Models;
using GeoAssets.Web.Services.IpBilling.Rest;
using GeoAssets.Web.Tests.Identity;
using Xunit;

namespace GeoAssets.Web.Tests.IpBilling;

/// <summary>
/// The Fiscalização/Ofício REST clients' <c>ValidateAsync</c> after XD01-301: the extended
/// validation result deserializes, and a 4xx carrying a message surfaces that message.
/// </summary>
public class InspectionValidationRestClientTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static HttpClient Client(FakeHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://test/api/ip-billing/") };

    private static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: Web) };

    private static object ValidatedBody(Guid municipioId) => new
    {
        serviceOrderId = "order-1",
        status = "Validado",
        totalAgregado = 8,
        totalRetroativo = 7,
        faturamento = new { id = Guid.NewGuid(), municipioId, competencia = "2026-03", kwhFaturado = 236.0965m, createdAt = DateTimeOffset.UnixEpoch },
        deltaKwh = 236.0965m,
        isProvisional = true,
        exactPoints = 4,
        provisionalPoints = 12,
    };

    [Fact]
    public async Task Fiscalizacao_ValidateAsync_DeserializesTheExtendedResult()
    {
        var municipioId = Guid.NewGuid();
        var handler = new FakeHttpMessageHandler(_ => Json(ValidatedBody(municipioId)));

        var result = await new FiscalizacaoRestClient(Client(handler)).ValidateAsync("order-1", "ana");

        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/api/ip-billing/fiscalizacao/order-1/validate");
        result.DeltaKwh.Should().Be(236.0965m);
        result.IsProvisional.Should().BeTrue();
        result.ExactPoints.Should().Be(4);
        result.ProvisionalPoints.Should().Be(12);
        result.Faturamento!.KwhFaturado.Should().Be(236.0965m);
        result.Faturamento.MunicipioId.Should().Be(municipioId);
    }

    [Fact]
    public async Task Oficio_ValidateAsync_DeserializesTheExtendedResult()
    {
        var handler = new FakeHttpMessageHandler(_ => Json(ValidatedBody(Guid.NewGuid())));

        var result = await new OficioRestClient(Client(handler)).ValidateAsync("order-1", "ana");

        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Be("/api/ip-billing/oficio/order-1/validate");
        result.DeltaKwh.Should().Be(236.0965m);
        result.IsProvisional.Should().BeTrue();
        result.ProvisionalPoints.Should().Be(12);
        result.Faturamento.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateAsync_LegacyBodyWithoutPostingFields_DefaultsToNoPosting()
    {
        var handler = new FakeHttpMessageHandler(_ => Json(new { serviceOrderId = "order-1", status = "Validado", totalAgregado = 8, totalRetroativo = 7 }));

        var result = await new FiscalizacaoRestClient(Client(handler)).ValidateAsync("order-1", null);

        result.Faturamento.Should().BeNull();
        result.DeltaKwh.Should().Be(0m);
        result.IsProvisional.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ValidateAsync_ClientErrorWithMessage_ThrowsWithTheServersMessage(HttpStatusCode status)
    {
        const string message = "Lamp 'MV' at 400 W is not in the Distribuidora lamp wattage chart nor in the standard chart; cannot convert points to kWh.";
        var problem = new { code = "lamp-not-in-chart", message, lampCode = nameof(LampTypeCode.MV), wattageW = 400 };

        var fiscalizacao = new FiscalizacaoRestClient(Client(new FakeHttpMessageHandler(_ => Json(problem, status))));
        var oficio = new OficioRestClient(Client(new FakeHttpMessageHandler(_ => Json(problem, status))));

        await fiscalizacao.Invoking(c => c.ValidateAsync("order-1", null)).Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        await oficio.Invoking(c => c.ValidateAsync("order-1", null)).Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public async Task ValidateAsync_ClientErrorWithoutAMessage_FallsBackToTheStatusCode()
    {
        // The invalid-transition 400 body is { from, to } — no message to surface.
        var noMessage = new FiscalizacaoRestClient(Client(new FakeHttpMessageHandler(_ => Json(new { from = "Carregado", to = "Validado" }, HttpStatusCode.BadRequest))));
        var notJson = new FiscalizacaoRestClient(Client(new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("<html>", System.Text.Encoding.UTF8, "application/json") })));

        await noMessage.Invoking(c => c.ValidateAsync("order-1", null)).Should().ThrowAsync<HttpRequestException>();
        await notJson.Invoking(c => c.ValidateAsync("order-1", null)).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public Task ValidateAsync_ServerError_IsNotMistakenForAValidationMessage()
    {
        var client = new FiscalizacaoRestClient(Client(new FakeHttpMessageHandler(_ => Json(new { message = "boom" }, HttpStatusCode.InternalServerError))));

        return client.Invoking(c => c.ValidateAsync("order-1", null)).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task RestMunicipioRepository_Writes_CarryTheDistribuidoraId()
    {
        var distribuidoraId = Guid.NewGuid();
        var municipio = new Municipio
        {
            IbgeCode = "3550308",
            Name = "São Paulo",
            Uf = "SP",
            DistribuidoraOrganizationId = Guid.NewGuid(),
            DistribuidoraId = distribuidoraId,
        };
        var handler = new FakeHttpMessageHandler(_ => Json(municipio, HttpStatusCode.Created));
        var sut = new RestMunicipioRepository(Client(handler));

        await sut.AddAsync(municipio);
        municipio.DistribuidoraId = null;
        await sut.UpdateAsync(municipio);
        var bodies = new List<string>();
        foreach (var request in handler.Requests) bodies.Add(await request.Content!.ReadAsStringAsync());

        bodies[0].Should().Contain($"\"distribuidoraId\":\"{distribuidoraId}\"");
        bodies[1].Should().Contain("\"distribuidoraId\":null");
    }
}
