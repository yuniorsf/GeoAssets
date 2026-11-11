using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.IpBilling.AssetTypes;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.FaturamentoLedger;
using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Oficio;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Resolution;
using GeoAssets.IpBilling.Server;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Provider.Xlsx;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.Rest;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

public sealed class IpBillingServerIntegrationTests
{
    [Fact]
    public async Task GetPolesForTransformer_ResolvesIndexLocallyAndQueriesCoreServerOverHttp()
    {
        var (provider, transformerId, pole1Id, pole2Id) = SeedTestNetwork();

        using var coreServer = await BuildCoreServerAsync(provider);
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var transformerResolver = new StubTransformerResolver(
            new Dictionary<(string, string), Guid>
            {
                [("3550308", "INST-001")] = transformerId,
            });

        using var ipBillingServer = await BuildIpBillingServerAsync(coreHttpClient, transformerResolver);
        using var ipBillingClient = ipBillingServer.CreateClient();

        var okResponse = await ipBillingClient.GetAsync(
            new Uri("/api/ip-billing/transformers/3550308/INST-001/poles", UriKind.Relative));
        okResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var poles = await okResponse.Content.ReadFromJsonAsync<List<Guid>>();
        poles.Should().NotBeNull();
        poles.Should().BeEquivalentTo([pole1Id, pole2Id]);

        var missingResponse = await ipBillingClient.GetAsync(
            new Uri("/api/ip-billing/transformers/3550308/UNKNOWN/poles", UriKind.Relative));
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CalculateAndValidateOrders_QueriesCoreServerAndPostsSetAndAccumulateIdempotently()
    {
        var (provider, transformerId, _, _) = SeedTestNetwork();

        using var coreServer = await BuildCoreServerAsync(provider);
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var transformerResolver = new StubTransformerResolver(
            new Dictionary<(string, string), Guid>
            {
                [("3550308", "INST-001")] = transformerId,
            });

        using var ipBillingServer = await BuildIpBillingServerAsync(coreHttpClient, transformerResolver);
        using var ipBillingClient = ipBillingServer.CreateClient();

        var levVal = await RunOrderCalculationAndValidationAsync(
            ipBillingClient,
            IpBillingOrderTypes.LevantamentoId,
            quantity: 10);

        levVal.Status.Should().Be(IpBillingWorkflowGraph.Validado);
        levVal.PostingMode.Should().Be(FaturamentoPostingMode.Set);
        var baselineKwh = levVal.Faturamento.KwhFaturado;
        baselineKwh.Should().BeGreaterThan(0m);

        var fiscVal = await RunOrderCalculationAndValidationAsync(
            ipBillingClient,
            IpBillingOrderTypes.FiscalizacaoId,
            quantity: 2);

        fiscVal.PostingMode.Should().Be(FaturamentoPostingMode.Accumulate);
        fiscVal.Faturamento.KwhFaturado.Should().BeGreaterThan(baselineKwh);

        var idempotentRetry = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/orders/{fiscVal.OrderId}/validate", UriKind.Relative),
            new IpBillingRestApiExtensions.ValidateOrderRequest("auditor"));
        idempotentRetry.EnsureSuccessStatusCode();
        var retryBody = await idempotentRetry.Content.ReadFromJsonAsync<IpBillingValidationResult>();
        retryBody!.Faturamento.KwhFaturado.Should().Be(fiscVal.Faturamento.KwhFaturado);
    }

    [Fact]
    public async Task LevantamentoUploadCalculateValidate_HappyPath_SetsLedgerBaselineAndMemoriaCalculo()
    {
        var (provider, transformerId, _, _) = SeedTestNetwork();

        using var coreServer = await BuildCoreServerAsync(provider);
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var transformerResolver = new StubTransformerResolver(
            new Dictionary<(string, string), Guid> { [("3550308", "INST-001")] = transformerId });

        var rawRows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Código de Instalação", "Tipo de Lâmpada", "Potência (W)", "Quantidade" },
            new string?[] { "INST-001", "LD", "100", "10" },
        };

        using var ipBillingServer = await BuildIpBillingServerAsync(coreHttpClient, transformerResolver, new StubWorkbookReader(rawRows));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/orders", UriKind.Relative),
            new IpBillingRestApiExtensions.CreateOrderRequest(IpBillingOrderTypes.LevantamentoId, "Levantamento", "3550308", "2026-03", null, "tester"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createResp.Content.ReadFromJsonAsync<ServiceOrder>();

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent([0x01, 0x02]), "file", "levantamento.xlsx");
        var uploadResp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/levantamento/{order!.Id}/upload", UriKind.Relative), uploadContent);
        uploadResp.EnsureSuccessStatusCode();

        var calcResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/levantamento/{order.Id}/calculate", UriKind.Relative),
            new IpBillingLevantamentoRestApiExtensions.LevantamentoCalculateRequest(new DateOnly(2026, 3, 31), "tester"));
        calcResp.EnsureSuccessStatusCode();
        var calcResult = await calcResp.Content.ReadFromJsonAsync<LevantamentoCalculationResult>();
        calcResult!.Status.Should().Be(IpBillingWorkflowGraph.Calculado);
        calcResult.TransformerLines.Should().ContainSingle();
        calcResult.UnresolvedCodigosInstalacao.Should().BeEmpty();
        calcResult.NormalKwh.Should().BeGreaterThan(0m);

        // Re-running Calculate while already Calculado is a same-status no-op recompute.
        var recalcResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/levantamento/{order.Id}/calculate", UriKind.Relative),
            new IpBillingLevantamentoRestApiExtensions.LevantamentoCalculateRequest(new DateOnly(2026, 3, 31), "tester"));
        recalcResp.EnsureSuccessStatusCode();

        var valResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/levantamento/{order.Id}/validate", UriKind.Relative),
            new IpBillingLevantamentoRestApiExtensions.LevantamentoValidateRequest("auditor"));
        valResp.EnsureSuccessStatusCode();
        var valResult = await valResp.Content.ReadFromJsonAsync<LevantamentoValidationResult>();
        valResult!.Status.Should().Be(IpBillingWorkflowGraph.Validado);
        valResult.Faturamento.KwhFaturado.Should().Be(calcResult.TotalCalculatedKwh);

        var memResp = await ipBillingClient.GetAsync(
            new Uri($"/api/ip-billing/levantamento/{order.Id}/memoria-calculo", UriKind.Relative));
        memResp.EnsureSuccessStatusCode();
        var mem = await memResp.Content.ReadFromJsonAsync<IpBillingLevantamentoRestApiExtensions.LevantamentoMemoriaCalculoResponse>();
        mem!.OrderStatus.Should().Be(IpBillingWorkflowGraph.Validado);
        mem.TotalCalculatedKwh.Should().Be(calcResult.TotalCalculatedKwh);
        mem.TransformerLines.Should().ContainSingle();
    }

    /// <summary>
    /// XD01-188's rollback/idempotency-safety acceptance criterion. <see cref="LevantamentoCalculationService.ValidateAsync"/>
    /// posts to the Faturamento ledger and <c>SaveChangesAsync</c>s FIRST, then transitions the
    /// ServiceOrder to Validado via a separate <c>AppendActionAsync</c> call afterward — a
    /// deliberate ordering (see <see cref="GeoAssets.IpBilling.FaturamentoLedger.FaturamentoLedgerService"/>'s
    /// doc comment for the full reasoning) that never leaves a "Validado with no posted money"
    /// state, at the cost of a transient window where the ledger is posted but the order hasn't
    /// caught up. This test forces a crash in exactly that window (inside the order-transition
    /// call, after the ledger write has already committed) and proves: (1) the ledger was
    /// genuinely posted despite the order failing to transition — the real, non-rolled-back
    /// state — and (2) retrying the same validate call is idempotency-safe: it does not
    /// double-post, and it completes the transition the second time.
    /// </summary>
    [Fact]
    public async Task LevantamentoValidate_CrashAfterLedgerWriteBeforeOrderTransition_IsSafeToRetry()
    {
        var (provider, transformerId, _, _) = SeedTestNetwork();

        using var coreServer = await BuildCoreServerAsync(provider);
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var transformerResolver = new StubTransformerResolver(
            new Dictionary<(string, string), Guid> { [("3550308", "INST-001")] = transformerId });

        var rawRows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Código de Instalação", "Tipo de Lâmpada", "Potência (W)", "Quantidade" },
            new string?[] { "INST-001", "LD", "100", "10" },
        };

        FaultInjectingOrderRepository? faultRepo = null;
        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient,
            transformerResolver,
            new StubWorkbookReader(rawRows),
            decorateOrderRepository: real =>
            {
                faultRepo = new FaultInjectingOrderRepository(real);
                return faultRepo;
            });
        using var ipBillingClient = ipBillingServer.CreateClient();

        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/orders", UriKind.Relative),
            new IpBillingRestApiExtensions.CreateOrderRequest(IpBillingOrderTypes.LevantamentoId, "Levantamento", "3550308", "2026-03", null, "tester"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createResp.Content.ReadFromJsonAsync<ServiceOrder>();

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent([0x01, 0x02]), "file", "levantamento.xlsx");
        (await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/levantamento/{order!.Id}/upload", UriKind.Relative), uploadContent))
            .EnsureSuccessStatusCode();

        var calcResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/levantamento/{order.Id}/calculate", UriKind.Relative),
            new IpBillingLevantamentoRestApiExtensions.LevantamentoCalculateRequest(new DateOnly(2026, 3, 31), "tester"));
        calcResp.EnsureSuccessStatusCode();
        var calcResult = await calcResp.Content.ReadFromJsonAsync<LevantamentoCalculationResult>();

        // Arm the fault, then validate — the ledger write happens, then the order-transition
        // call throws an InvalidOperationException, which the endpoint's own catch block
        // (apps/GeoAssets.IpBilling.Server/IpBillingLevantamentoRestApiExtensions.cs) converts
        // to a 400 rather than letting it surface as an unhandled 500 — either way, the point
        // is the ledger write already committed by the time this failure happens.
        faultRepo!.FaultArmed = true;
        var failedValResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/levantamento/{order.Id}/validate", UriKind.Relative),
            new IpBillingLevantamentoRestApiExtensions.LevantamentoValidateRequest("auditor"));
        failedValResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The real, non-rolled-back state: the ledger WAS posted even though the order never
        // transitioned to Validado (still Calculado) — proving this is genuinely NOT a two-phase
        // rollback, exactly as documented.
        var orderAfterCrash = await ipBillingClient.GetAsync(new Uri($"/api/ip-billing/orders/{order.Id}", UriKind.Relative));
        orderAfterCrash.EnsureSuccessStatusCode();
        var orderStateAfterCrash = await orderAfterCrash.Content.ReadFromJsonAsync<ServiceOrder>();
        orderStateAfterCrash!.Status.Should().Be(IpBillingWorkflowGraph.Calculado,
            "the fault threw inside the order-transition call, after the ledger write had already committed");

        // Disarm the fault and retry the identical validate call — idempotency-safe: no
        // double-post, and the transition now completes.
        faultRepo.FaultArmed = false;
        var retryValResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/levantamento/{order.Id}/validate", UriKind.Relative),
            new IpBillingLevantamentoRestApiExtensions.LevantamentoValidateRequest("auditor"));
        retryValResp.EnsureSuccessStatusCode();
        var retryResult = await retryValResp.Content.ReadFromJsonAsync<LevantamentoValidationResult>();
        retryResult!.Status.Should().Be(IpBillingWorkflowGraph.Validado);
        retryResult.Faturamento.KwhFaturado.Should().Be(calcResult!.TotalCalculatedKwh,
            "the retry must not double-post the already-committed ledger entry");
    }

    [Fact]
    // Renamed from "...AccumulatesIntoLedger" during XD01-188's investigation: this test never
    // actually asserted anything about the Faturamento ledger, and per that ticket's findings,
    // Fiscalização validation does NOT post to the ledger (no point-to-kWh conversion rule
    // exists) — the old name was aspirational/inaccurate, not a description of real behavior.
    public async Task FiscalizacaoUploadCalculateValidate_HappyPath_ComputesAgregadoAndRetroativo()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var rawRows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Código de Instalação", "Grupo de Operação", "Quantidade" },
            new string?[] { "INST-001", "1", "10" },
            new string?[] { "INST-001", "2", "3" },
            new string?[] { "INST-001", "3", "2" },
            new string?[] { "INST-001", "4", "1" },
        };

        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient, new StubTransformerResolver(new Dictionary<(string, string), Guid>()), new StubWorkbookReader(rawRows));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/orders", UriKind.Relative),
            new IpBillingRestApiExtensions.CreateOrderRequest(IpBillingOrderTypes.FiscalizacaoId, "Fiscalização", "3550308", "2026-03", null, "tester"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createResp.Content.ReadFromJsonAsync<ServiceOrder>();

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent([0x01, 0x02]), "file", "fiscalizacao.xlsx");
        var uploadResp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/fiscalizacao/{order!.Id}/upload", UriKind.Relative), uploadContent);
        uploadResp.EnsureSuccessStatusCode();

        var calcResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/fiscalizacao/{order.Id}/calculate", UriKind.Relative),
            new IpBillingFiscalizacaoRestApiExtensions.FiscalizacaoCalculateRequest("tester"));
        calcResp.EnsureSuccessStatusCode();
        var calcResult = await calcResp.Content.ReadFromJsonAsync<FiscalizacaoCalculationResult>();
        calcResult!.Status.Should().Be(IpBillingWorkflowGraph.Calculado);
        calcResult.Lines.Should().ContainSingle();
        // Agregado = 10 - 3 + 2 - 1 = 8; Retroativo = 10 - 3 = 7 (ops 3/4 excluded).
        calcResult.TotalAgregado.Should().Be(8);
        calcResult.TotalRetroativo.Should().Be(7);

        var valResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/fiscalizacao/{order.Id}/validate", UriKind.Relative),
            new IpBillingFiscalizacaoRestApiExtensions.FiscalizacaoValidateRequest("auditor"));
        valResp.EnsureSuccessStatusCode();
        var valResult = await valResp.Content.ReadFromJsonAsync<FiscalizacaoValidationResult>();
        valResult!.Status.Should().Be(IpBillingWorkflowGraph.Validado);
        valResult.TotalAgregado.Should().Be(8);
        valResult.TotalRetroativo.Should().Be(7);
    }

    [Fact]
    public async Task FiscalizacaoUpload_MalformedRows_RejectedWithStructuredRowErrors()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var rawRows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Código de Instalação", "Grupo de Operação", "Quantidade" },
            new string?[] { "INST-001", "9", "10" },  // unrecognized operation group
            new string?[] { null, "1", "10" },        // missing código
        };

        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient, new StubTransformerResolver(new Dictionary<(string, string), Guid>()), new StubWorkbookReader(rawRows));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/orders", UriKind.Relative),
            new IpBillingRestApiExtensions.CreateOrderRequest(IpBillingOrderTypes.FiscalizacaoId, "Fiscalização", "3550308", "2026-03", null, "tester"));
        var order = await createResp.Content.ReadFromJsonAsync<ServiceOrder>();

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent([0x01]), "file", "fiscalizacao.xlsx");
        var uploadResp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/fiscalizacao/{order!.Id}/upload", UriKind.Relative), uploadContent);

        uploadResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await uploadResp.Content.ReadAsStringAsync();
        body.Should().Contain("Grupo de Operação");
        body.Should().Contain("Código de Instalação");
    }

    [Fact]
    public async Task OficioUploadCalculateValidate_HappyPath_ComputesAgregadoAndRetroativo()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var rawRows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Código de Instalação", "Grupo de Operação", "Quantidade" },
            new string?[] { "INST-001", "1", "10" },
            new string?[] { "INST-001", "2", "3" },
            new string?[] { "INST-001", "3", "2" },
            new string?[] { "INST-001", "4", "1" },
        };

        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient, new StubTransformerResolver(new Dictionary<(string, string), Guid>()), new StubWorkbookReader(rawRows));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/orders", UriKind.Relative),
            new IpBillingRestApiExtensions.CreateOrderRequest(IpBillingOrderTypes.OficioId, "Ofício", "3550308", "2026-03", null, "tester"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var order = await createResp.Content.ReadFromJsonAsync<ServiceOrder>();

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent([0x01, 0x02]), "file", "oficio.xlsx");
        var uploadResp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/oficio/{order!.Id}/upload?numeroPrefeitura=OF-2026-001", UriKind.Relative), uploadContent);
        uploadResp.EnsureSuccessStatusCode();

        var calcResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/oficio/{order.Id}/calculate", UriKind.Relative),
            new IpBillingOficioRestApiExtensions.OficioCalculateRequest("tester"));
        calcResp.EnsureSuccessStatusCode();
        var calcResult = await calcResp.Content.ReadFromJsonAsync<OficioCalculationResult>();
        calcResult!.Status.Should().Be(IpBillingWorkflowGraph.Calculado);
        calcResult.NumeroPrefeitura.Should().Be("OF-2026-001");
        calcResult.Lines.Should().ContainSingle();
        // Agregado = 10 - 3 + 2 - 1 = 8 (same formula as Fiscalização).
        calcResult.TotalAgregado.Should().Be(8);
        // Retroativo under the default symmetric policy = 10 - 3 + 2 - 1 = 8 (flagged business-rule ambiguity, see XD01-185).
        calcResult.TotalRetroativo.Should().Be(8);
        calcResult.Swaps.Should().ContainSingle(s => s.CodigoInstalacao == "INST-001" && s.IsSwap);

        var valResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/oficio/{order.Id}/validate", UriKind.Relative),
            new IpBillingOficioRestApiExtensions.OficioValidateRequest("auditor"));
        valResp.EnsureSuccessStatusCode();
        var valResult = await valResp.Content.ReadFromJsonAsync<OficioValidationResult>();
        valResult!.Status.Should().Be(IpBillingWorkflowGraph.Validado);
        valResult.TotalAgregado.Should().Be(8);
        valResult.TotalRetroativo.Should().Be(8);
    }

    [Fact]
    public async Task OficioUpload_DuplicateNumero_RejectedUntilSuperseded()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var rawRows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Código de Instalação", "Grupo de Operação", "Quantidade" },
            new string?[] { "INST-001", "1", "5" },
        };

        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient, new StubTransformerResolver(new Dictionary<(string, string), Guid>()), new StubWorkbookReader(rawRows));
        using var ipBillingClient = ipBillingServer.CreateClient();

        async Task<ServiceOrder> CreateOrderAsync()
        {
            var resp = await ipBillingClient.PostAsJsonAsync(
                new Uri("/api/ip-billing/orders", UriKind.Relative),
                new IpBillingRestApiExtensions.CreateOrderRequest(IpBillingOrderTypes.OficioId, "Ofício", "3550308", "2026-03", null, "tester"));
            return (await resp.Content.ReadFromJsonAsync<ServiceOrder>())!;
        }

        var order1 = await CreateOrderAsync();
        using var upload1 = new MultipartFormDataContent { { new ByteArrayContent([0x01]), "file", "oficio1.xlsx" } };
        var upload1Resp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/oficio/{order1.Id}/upload?numeroPrefeitura=OF-2026-002", UriKind.Relative), upload1);
        upload1Resp.EnsureSuccessStatusCode();

        var order2 = await CreateOrderAsync();
        using var upload2 = new MultipartFormDataContent { { new ByteArrayContent([0x01]), "file", "oficio2.xlsx" } };
        var upload2Resp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/oficio/{order2.Id}/upload?numeroPrefeitura=OF-2026-002", UriKind.Relative), upload2);
        upload2Resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictBody = await upload2Resp.Content.ReadFromJsonAsync<IpBillingOficioRestApiExtensions.DuplicateNumeroResponse>();
        conflictBody!.NumeroPrefeitura.Should().Be("OF-2026-002");
        conflictBody.ExistingServiceOrderId.Should().Be(order1.Id);

        var supersedeResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/oficio/{order1.Id}/supersede", UriKind.Relative),
            new IpBillingOficioRestApiExtensions.OficioSupersedeRequest("auditor"));
        supersedeResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var upload2Retry = new MultipartFormDataContent { { new ByteArrayContent([0x01]), "file", "oficio2.xlsx" } };
        var upload2RetryResp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/oficio/{order2.Id}/upload?numeroPrefeitura=OF-2026-002", UriKind.Relative), upload2Retry);
        upload2RetryResp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task LevantamentoCalculate_UnresolvedTransformerCode_ReportsTogetherInResponseWithoutBlockingBilling()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var transformerResolver = new StubTransformerResolver(new Dictionary<(string, string), Guid>());

        var rawRows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Código de Instalação", "Tipo de Lâmpada", "Potência (W)", "Quantidade" },
            new string?[] { "INST-UNKNOWN-1", "LD", "100", "10" },
            new string?[] { "INST-UNKNOWN-2", "LD", "100", "5" },
        };

        using var ipBillingServer = await BuildIpBillingServerAsync(coreHttpClient, transformerResolver, new StubWorkbookReader(rawRows));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/orders", UriKind.Relative),
            new IpBillingRestApiExtensions.CreateOrderRequest(IpBillingOrderTypes.LevantamentoId, "Levantamento", "3550308", "2026-03", null, "tester"));
        var order = await createResp.Content.ReadFromJsonAsync<ServiceOrder>();

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent([0x01]), "file", "levantamento.xlsx");
        var uploadResp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/levantamento/{order!.Id}/upload", UriKind.Relative), uploadContent);
        uploadResp.EnsureSuccessStatusCode();

        var calcResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/levantamento/{order.Id}/calculate", UriKind.Relative),
            new IpBillingLevantamentoRestApiExtensions.LevantamentoCalculateRequest(new DateOnly(2026, 3, 31), "tester"));
        calcResp.EnsureSuccessStatusCode();
        var calcResult = await calcResp.Content.ReadFromJsonAsync<LevantamentoCalculationResult>();

        calcResult!.UnresolvedCodigosInstalacao.Should().BeEquivalentTo(["INST-UNKNOWN-1", "INST-UNKNOWN-2"]);
        // Billing still proceeds for unresolved transformers — a map/GIS gap doesn't lose kWh.
        calcResult.NormalKwh.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task LevantamentoUpload_MalformedRows_RejectedWithStructuredRowErrors()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");

        var rawRows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Código de Instalação", "Tipo de Lâmpada", "Potência (W)", "Quantidade" },
            new string?[] { "INST-001", "NOPE", "100", "10" },  // bad lamp type
            new string?[] { null, "LD", "100", "10" },          // missing código
        };

        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient, new StubTransformerResolver(new Dictionary<(string, string), Guid>()), new StubWorkbookReader(rawRows));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/orders", UriKind.Relative),
            new IpBillingRestApiExtensions.CreateOrderRequest(IpBillingOrderTypes.LevantamentoId, "Levantamento", "3550308", "2026-03", null, "tester"));
        var order = await createResp.Content.ReadFromJsonAsync<ServiceOrder>();

        using var uploadContent = new MultipartFormDataContent();
        uploadContent.Add(new ByteArrayContent([0x01]), "file", "levantamento.xlsx");
        var uploadResp = await ipBillingClient.PostAsync(
            new Uri($"/api/ip-billing/levantamento/{order!.Id}/upload", UriKind.Relative), uploadContent);

        uploadResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await uploadResp.Content.ReadAsStringAsync();
        body.Should().Contain("Tipo de Lâmpada");
        body.Should().Contain("Código de Instalação");
    }

    [Fact]
    public async Task FaturamentoManualEntry_NewPair_CreatesThenRejectsOnRetry()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");
        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient, new StubTransformerResolver(new Dictionary<(string, string), Guid>()));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var municipioId = Guid.NewGuid();
        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/faturamentos/manual-entry", UriKind.Relative),
            new IpBillingFaturamentoRestApiExtensions.ManualEntryRequest(municipioId, "2026-03", 500m, "operator-1"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<Faturamento>();
        created!.KwhFaturado.Should().Be(500m);

        // Manual entry is insert-only — a second call for the same (município, competência)
        // must be rejected, not silently overwrite the first entry.
        var retryResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/faturamentos/manual-entry", UriKind.Relative),
            new IpBillingFaturamentoRestApiExtensions.ManualEntryRequest(municipioId, "2026-03", 999m, "operator-2"));
        retryResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflict = await retryResp.Content.ReadFromJsonAsync<IpBillingFaturamentoRestApiExtensions.FaturamentoAlreadyPostedResponse>();
        conflict!.MunicipioId.Should().Be(municipioId);
        conflict.Competencia.Should().Be("2026-03");
    }

    [Fact]
    public async Task FaturamentoBatchLoad_ExistingAndNewPairs_InsertsNewAndSkipsExistingWithLegacyMessage()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");
        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient, new StubTransformerResolver(new Dictionary<(string, string), Guid>()));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var existingMunicipio = Guid.NewGuid();
        (await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/faturamentos/manual-entry", UriKind.Relative),
            new IpBillingFaturamentoRestApiExtensions.ManualEntryRequest(existingMunicipio, "2026-03", 100m, "seed")))
            .EnsureSuccessStatusCode();

        var newMunicipio = Guid.NewGuid();
        var batchResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/faturamentos/batch-load", UriKind.Relative),
            new IpBillingFaturamentoRestApiExtensions.BatchRequest(
                [
                    new FaturamentoBatchItem(newMunicipio, "2026-03", 200m),
                    new FaturamentoBatchItem(existingMunicipio, "2026-03", 999m),
                ],
                "batch-operator"));
        batchResp.EnsureSuccessStatusCode();
        var result = await batchResp.Content.ReadFromJsonAsync<FaturamentoBatchResult>();

        result!.Applied.Should().ContainSingle(f => f.MunicipioId == newMunicipio && f.KwhFaturado == 200m);
        result.Skipped.Should().ContainSingle(s => s.MunicipioId == existingMunicipio && s.Reason == "Faturamento já cadastrado");
    }

    [Fact]
    public async Task FaturamentoRecarga_ExistingAndMissingPairs_UpdatesExistingAndSkipsMissingWithLegacyMessage()
    {
        using var coreServer = await BuildCoreServerAsync(new TestAssetProvider());
        using var coreHttpClient = coreServer.CreateClient();
        coreHttpClient.BaseAddress = new Uri(coreServer.BaseAddress, "api/geoassets/");
        using var ipBillingServer = await BuildIpBillingServerAsync(
            coreHttpClient, new StubTransformerResolver(new Dictionary<(string, string), Guid>()));
        using var ipBillingClient = ipBillingServer.CreateClient();

        var existingMunicipio = Guid.NewGuid();
        (await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/faturamentos/manual-entry", UriKind.Relative),
            new IpBillingFaturamentoRestApiExtensions.ManualEntryRequest(existingMunicipio, "2026-03", 100m, "seed")))
            .EnsureSuccessStatusCode();

        var missingMunicipio = Guid.NewGuid();
        var recargaResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/faturamentos/recarga", UriKind.Relative),
            new IpBillingFaturamentoRestApiExtensions.BatchRequest(
                [
                    new FaturamentoBatchItem(existingMunicipio, "2026-03", 777m),
                    new FaturamentoBatchItem(missingMunicipio, "2026-03", 200m),
                ],
                "recarga-operator"));
        recargaResp.EnsureSuccessStatusCode();
        var result = await recargaResp.Content.ReadFromJsonAsync<FaturamentoBatchResult>();

        result!.Applied.Should().ContainSingle(f => f.MunicipioId == existingMunicipio && f.KwhFaturado == 777m);
        result.Skipped.Should().ContainSingle(s => s.MunicipioId == missingMunicipio && s.Reason == "Faturamento inexistente");

        // Query/consulta endpoint: by município+competência should reflect the update.
        var queryResp = await ipBillingClient.GetAsync(
            new Uri($"/api/ip-billing/faturamentos/{existingMunicipio}/consulta?competencia=2026-03", UriKind.Relative));
        queryResp.EnsureSuccessStatusCode();
        var queried = await queryResp.Content.ReadFromJsonAsync<List<Faturamento>>();
        queried.Should().ContainSingle(f => f.KwhFaturado == 777m);
    }

    private static async Task<IpBillingValidationResult> RunOrderCalculationAndValidationAsync(
        HttpClient ipBillingClient,
        string orderTypeId,
        int quantity)
    {
        var createResp = await ipBillingClient.PostAsJsonAsync(
            new Uri("/api/ip-billing/orders", UriKind.Relative),
            new IpBillingRestApiExtensions.CreateOrderRequest(orderTypeId, $"Order {orderTypeId}", "3550308", "2026-03", "INST-001", "tester"));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var createdOrder = await createResp.Content.ReadFromJsonAsync<ServiceOrder>();
        createdOrder.Should().NotBeNull();

        var calcResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/orders/{createdOrder!.Id}/calculate", UriKind.Relative),
            new IpBillingCalculationRequest
            {
                CodigoInstalacao = "INST-001",
                NormalLamps      = [new IpLampLineItem(LampTypeCode.LD, 50, quantity)],
                ReferenceDate    = new DateOnly(2026, 3, 31),
                PerformedBy      = "tester",
            });
        calcResp.EnsureSuccessStatusCode();

        var calcResult = await calcResp.Content.ReadFromJsonAsync<IpBillingCalculationResult>();
        calcResult!.Status.Should().Be(IpBillingWorkflowGraph.Calculado);
        calcResult.ResolvedPoleCount.Should().Be(2);
        calcResult.Irregular24hKwh.Should().BeGreaterThan(0m);

        var valResp = await ipBillingClient.PostAsJsonAsync(
            new Uri($"/api/ip-billing/orders/{createdOrder.Id}/validate", UriKind.Relative),
            new IpBillingRestApiExtensions.ValidateOrderRequest("auditor"));
        valResp.EnsureSuccessStatusCode();

        return (await valResp.Content.ReadFromJsonAsync<IpBillingValidationResult>())!;
    }

    private static (IAssetProvider Provider, Guid TransformerId, Guid Pole1Id, Guid Pole2Id) SeedTestNetwork()
    {
        var provider = new TestAssetProvider();
        provider.AddAssetType(IpBillingAssetTypes.Transformador);
        provider.AddAssetType(IpBillingAssetTypes.Poste);
        provider.AddAssetType(IpBillingAssetTypes.LampGroup24h);

        var transformerId = Guid.NewGuid();
        var pole1Id = Guid.NewGuid();
        var pole2Id = Guid.NewGuid();

        provider.Add(new GeoFeature
        {
            Id = transformerId.ToString(),
            Geometry = new GeoPoint(-46.63, -23.55),
            Properties = new GeoFeatureProperties
            {
                Name = "TR-SP-01",
                AssetTypeId = IpBillingAssetTypes.Transformador.Id.ToString(),
            },
            Topology =
            [
                new TopoEdge { TargetId = pole1Id.ToString(), Kind = "feeds" },
                new TopoEdge { TargetId = pole2Id.ToString(), Kind = "feeds" },
            ],
        });
        provider.Add(new GeoFeature
        {
            Id = pole1Id.ToString(),
            Geometry = new GeoPoint(-46.631, -23.551),
            Properties = new GeoFeatureProperties { Name = "P-SP-01", AssetTypeId = IpBillingAssetTypes.Poste.Id.ToString() },
        });
        provider.Add(new GeoFeature
        {
            Id = pole2Id.ToString(),
            Geometry = new GeoPoint(-46.632, -23.552),
            Properties = new GeoFeatureProperties { Name = "P-SP-02", AssetTypeId = IpBillingAssetTypes.Poste.Id.ToString() },
        });
        provider.Add(new GeoFeature
        {
            Id = Guid.NewGuid().ToString(),
            Geometry = new GeoPoint(-46.633, -23.553),
            Properties = new GeoFeatureProperties
            {
                Name = "LG24-SP-01",
                AssetTypeId = IpBillingAssetTypes.LampGroup24h.Id.ToString(),
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["scopeId"]   = "3550308",
                    ["lampType"]  = "VS",
                    ["wattageW"]  = "100",
                    ["quantity"]  = "2",
                    ["eventDate"] = "2026-01-15",
                },
            },
        });

        return (provider, transformerId, pole1Id, pole2Id);
    }

    private static async Task<TestServer> BuildCoreServerAsync(IAssetProvider provider)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, NoOpAuthenticationHandler>("Test", _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                    services.AddSingleton<IGeoAuthorizationService>(new AllowAllAuthorizationService());
                    services.AddSingleton<IOrganizationGrantRepository, NoOpGrantRepository>();
                    services.AddSingleton(provider);
                    services.AddSingleton<IDbContextFactory<GeoAssetsDbContext>, NoOpDbContextFactory>();
                    services.AddSingleton<WmsPostGisRenderer>();
                });
                webHost.Configure(app =>
                {
                    app.Use(async (ctx, next) =>
                    {
                        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim(ClaimTypes.NameIdentifier, "test-user")], "TestScheme"));
                        await next();
                    });
                    app.UseRouting();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapGeoAssetsApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static async Task<TestServer> BuildIpBillingServerAsync(
        HttpClient coreHttpClient,
        ITransformerResolver transformerResolver,
        IWorkbookReader? workbookReader = null,
        Func<IServiceOrderRepository, IServiceOrderRepository>? decorateOrderRepository = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(TimeProvider.System);
                    services.AddOrderTypeRegistry(IpBillingOrderTypes.RegisterAll);
                    services.AddSingleton<IServiceOrderRepository>(sp =>
                    {
                        IServiceOrderRepository repo = new ValidatingServiceOrderRepository(
                            new InMemoryOrderRepository(),
                            sp.GetRequiredService<OrderTypeRegistry>());
                        return decorateOrderRepository?.Invoke(repo) ?? repo;
                    });
                    services.AddSingleton<IMunicipioRepository>(new InMemoryMunicipioRepository());
                    services.AddSingleton<ILampTypeRepository>(new InMemoryLampTypeRepository());
                    services.AddSingleton<IFaturamentoRepository>(new InMemoryFaturamentoRepository());
                    services.AddSingleton<IFaturamentoLedgerService, FaturamentoLedgerService>();
                    services.AddSingleton<IFaturamentoManualEntryService, FaturamentoManualEntryService>();
                    services.AddSingleton<IGeoCoreClient>(new RestGeoCoreClient(coreHttpClient));
                    services.AddSingleton(transformerResolver);
                    services.AddScoped<IPoleResolver, HttpPoleResolver>();
                    services.AddScoped<IIpBillingCalculationService, IpBillingCalculationService>();
                    services.AddSingleton<ILevantamentoRepository>(new InMemoryLevantamentoRepository());
                    services.AddSingleton<IInterveningOperationLookup, NullInterveningOperationLookup>();
                    services.AddScoped<ILevantamentoCalculationService, LevantamentoCalculationService>();
                    services.AddSingleton<IFiscalizacaoRepository>(new InMemoryFiscalizacaoRepository());
                    services.AddScoped<IFiscalizacaoCalculationService, FiscalizacaoCalculationService>();
                    services.AddSingleton<IOficioRepository>(new InMemoryOficioRepository());
                    services.AddScoped<IOficioCalculationService, OficioCalculationService>();
                    if (workbookReader is not null) services.AddSingleton(workbookReader);
                    else services.AddGeoAssetsXlsx();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapIpBillingApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    /// <summary>
    /// Returns pre-configured raw rows regardless of the uploaded bytes — TestHost-level
    /// routing tests exercise <see cref="IpBillingLevantamentoRestApiExtensions"/>'s multipart
    /// handling and <see cref="LevantamentoWorkbookRowParser"/> wiring, not ClosedXML's real
    /// .xlsx parsing (covered separately, at the provider level).
    /// </summary>
    private sealed class StubWorkbookReader(IReadOnlyList<IReadOnlyList<string?>> rows) : IWorkbookReader
    {
        public IReadOnlyList<IReadOnlyList<string?>> ReadRows(Stream workbookStream, string? worksheetName = null) => rows;
    }

    private sealed class StubTransformerResolver(
        IReadOnlyDictionary<(string IbgeCode, string CodigoInstalacao), Guid> mappings)
        : ITransformerResolver
    {
        public Task<Guid?> ResolveTransformerFeatureIdAsync(
            string municipioIbgeCode, string codigoInstalacao, CancellationToken ct = default) =>
            Task.FromResult(
                mappings.TryGetValue((municipioIbgeCode, codigoInstalacao), out var id)
                    ? (Guid?)id
                    : null);
    }

    private sealed class InMemoryMunicipioRepository : IMunicipioRepository
    {
        private readonly Municipio _sp = new()
        {
            Id                          = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            IbgeCode                    = "3550308",
            Name                        = "São Paulo",
            Uf                          = "SP",
            DistribuidoraOrganizationId = Guid.NewGuid(),
        };

        public Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == _sp.Id ? _sp : null);
        public Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default) =>
            Task.FromResult(string.Equals(ibgeCode, _sp.IbgeCode, StringComparison.Ordinal) ? _sp : null);
        public Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Municipio>>([_sp]);
        public Task AddAsync(Municipio municipio, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Municipio municipio, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryLampTypeRepository : ILampTypeRepository
    {
        private static readonly IReadOnlyList<LampType> Catalog =
        [
            new LampType
            {
                Code = LampTypeCode.LD,
                Name = "LED",
                WattageOptions = [new LampWattageOption { WattageW = 50, BallastLossW = 0m }],
            },
            new LampType
            {
                Code = LampTypeCode.VS,
                Name = "Vapor de Sódio",
                WattageOptions = [new LampWattageOption { WattageW = 100, BallastLossW = 10m }],
            },
        ];

        public Task<LampType?> GetByCodeAsync(LampTypeCode code, CancellationToken ct = default) =>
            Task.FromResult(Catalog.FirstOrDefault(c => c.Code == code));
        public Task<IReadOnlyList<LampType>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(Catalog);
    }

    private sealed class InMemoryFaturamentoRepository : IFaturamentoRepository
    {
        private readonly Dictionary<(Guid MunicipioId, string Competencia), Faturamento> _ledger = [];
        private readonly List<FaturamentoPosting> _postings = [];

        public Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(Guid municipioId, string competencia, CancellationToken ct = default) =>
            Task.FromResult(_ledger.TryGetValue((municipioId, competencia), out var f) ? f : null);

        public Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Faturamento>>(_ledger.Values.Where(f => f.MunicipioId == municipioId).ToList());

        public Task AddAsync(Faturamento faturamento, CancellationToken ct = default)
        {
            _ledger[(faturamento.MunicipioId, faturamento.Competencia)] = faturamento;
            return Task.CompletedTask;
        }

        public Task<Faturamento> PostOrderCalculationAsync(
            string serviceOrderId,
            Guid municipioId,
            string competencia,
            decimal kwhDeltaOrBaseline,
            FaturamentoPostingMode mode,
            DateTimeOffset postedAt,
            CancellationToken ct = default)
        {
            if (_postings.Any(p => string.Equals(p.ServiceOrderId, serviceOrderId, StringComparison.Ordinal) &&
                                   string.Equals(p.Competencia, competencia, StringComparison.Ordinal)) &&
                _ledger.TryGetValue((municipioId, competencia), out var existing))
            {
                return Task.FromResult(existing);
            }

            var rounded = IpEnergyMath.Round(kwhDeltaOrBaseline);
            if (!_ledger.TryGetValue((municipioId, competencia), out var faturamento))
            {
                faturamento = new Faturamento
                {
                    MunicipioId = municipioId,
                    Competencia = competencia,
                    KwhFaturado = rounded,
                    CreatedAt   = postedAt,
                };
                _ledger[(municipioId, competencia)] = faturamento;
            }
            else
            {
                faturamento.KwhFaturado = mode == FaturamentoPostingMode.Accumulate
                    ? IpEnergyMath.Round(faturamento.KwhFaturado + rounded)
                    : rounded;
            }

            _postings.Add(new FaturamentoPosting
            {
                ServiceOrderId     = serviceOrderId,
                MunicipioId        = municipioId,
                Competencia        = competencia,
                Mode               = mode,
                DeltaOrBaselineKwh = rounded,
                ResultingKwh       = faturamento.KwhFaturado,
                PostedAt           = postedAt,
            });

            return Task.FromResult(faturamento);
        }

        public Task<Faturamento> OverwriteManualEntryAsync(
            Guid municipioId,
            string competencia,
            decimal kwhFaturado,
            string enteredByReference,
            DateTimeOffset postedAt,
            CancellationToken ct = default)
        {
            var rounded = IpEnergyMath.Round(kwhFaturado);
            if (!_ledger.TryGetValue((municipioId, competencia), out var faturamento))
            {
                faturamento = new Faturamento
                {
                    MunicipioId = municipioId,
                    Competencia = competencia,
                    KwhFaturado = rounded,
                    CreatedAt   = postedAt,
                };
                _ledger[(municipioId, competencia)] = faturamento;
            }
            else
            {
                faturamento.KwhFaturado = rounded;
            }

            _postings.Add(new FaturamentoPosting
            {
                ServiceOrderId     = enteredByReference,
                MunicipioId        = municipioId,
                Competencia        = competencia,
                Mode               = FaturamentoPostingMode.Overwrite,
                DeltaOrBaselineKwh = rounded,
                ResultingKwh       = faturamento.KwhFaturado,
                PostedAt           = postedAt,
            });

            return Task.FromResult(faturamento);
        }

        public Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FaturamentoPosting>>(
                _postings.Where(p => string.Equals(p.ServiceOrderId, serviceOrderId, StringComparison.Ordinal)).ToList());

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryLevantamentoRepository : ILevantamentoRepository
    {
        private readonly Dictionary<string, LevantamentoHeader> _headers = [];
        private readonly Dictionary<string, List<LevantamentoTransformerLine>> _lines = [];
        private readonly Dictionary<string, List<Levantamento24hLine>> _lines24h = [];

        public Task<LevantamentoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult(_headers.GetValueOrDefault(serviceOrderId));

        public Task AddHeaderAsync(LevantamentoHeader header, CancellationToken ct = default)
        {
            _headers[header.ServiceOrderId] = header;
            return Task.CompletedTask;
        }

        public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
        {
            _headers[serviceOrderId].IsValidated = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LevantamentoTransformerLine>> GetTransformerLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LevantamentoTransformerLine>>(_lines.GetValueOrDefault(serviceOrderId) ?? []);

        public Task ReplaceTransformerLinesAsync(string serviceOrderId, IReadOnlyList<LevantamentoTransformerLine> lines, CancellationToken ct = default)
        {
            _lines[serviceOrderId] = [.. lines];
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Levantamento24hLine>> Get24hLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Levantamento24hLine>>(_lines24h.GetValueOrDefault(serviceOrderId) ?? []);

        public Task Replace24hLinesAsync(string serviceOrderId, IReadOnlyList<Levantamento24hLine> lines, CancellationToken ct = default)
        {
            _lines24h[serviceOrderId] = [.. lines];
            return Task.CompletedTask;
        }

        public Task<LevantamentoHeader?> GetMostRecentValidatedAsync(Guid municipioId, string beforeCompetencia, CancellationToken ct = default) =>
            Task.FromResult(_headers.Values
                .Where(h => h.MunicipioId == municipioId && h.IsValidated &&
                            string.Compare(h.Competencia, beforeCompetencia, StringComparison.Ordinal) < 0)
                .OrderByDescending(h => h.Competencia, StringComparer.Ordinal)
                .FirstOrDefault());

        public Task<LevantamentoHeader?> GetFirstValidatedAsync(Guid municipioId, CancellationToken ct = default) =>
            Task.FromResult(_headers.Values
                .Where(h => h.MunicipioId == municipioId && h.IsValidated)
                .OrderBy(h => h.Competencia, StringComparer.Ordinal)
                .FirstOrDefault());

        public Task<IReadOnlySet<string>> GetAllValidatedCodigosInstalacaoAsync(Guid municipioId, CancellationToken ct = default)
        {
            var validatedOrderIds = _headers.Values
                .Where(h => h.MunicipioId == municipioId && h.IsValidated)
                .Select(h => h.ServiceOrderId)
                .ToHashSet(StringComparer.Ordinal);

            var codigos = _lines
                .Where(kvp => validatedOrderIds.Contains(kvp.Key))
                .SelectMany(kvp => kvp.Value)
                .Select(l => l.CodigoInstalacao)
                .ToHashSet(StringComparer.Ordinal);

            return Task.FromResult<IReadOnlySet<string>>(codigos);
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryFiscalizacaoRepository : IFiscalizacaoRepository
    {
        private readonly Dictionary<string, FiscalizacaoHeader> _headers = [];
        private readonly Dictionary<string, List<FiscalizacaoLine>> _lines = [];

        public Task<FiscalizacaoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult(_headers.GetValueOrDefault(serviceOrderId));

        public Task AddHeaderAsync(FiscalizacaoHeader header, CancellationToken ct = default)
        {
            _headers[header.ServiceOrderId] = header;
            return Task.CompletedTask;
        }

        public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
        {
            _headers[serviceOrderId].IsValidated = true;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FiscalizacaoLine>> GetLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FiscalizacaoLine>>(_lines.GetValueOrDefault(serviceOrderId) ?? []);

        public Task ReplaceLinesAsync(string serviceOrderId, IReadOnlyList<FiscalizacaoLine> lines, CancellationToken ct = default)
        {
            _lines[serviceOrderId] = [.. lines];
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryOficioRepository : IOficioRepository
    {
        private readonly Dictionary<string, OficioHeader> _headers = [];
        private readonly Dictionary<string, List<OficioLine>> _lines = [];

        public Task<OficioHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult(_headers.GetValueOrDefault(serviceOrderId));

        public Task<OficioHeader?> GetHeaderByNumeroAsync(Guid municipioId, string numeroPrefeitura, CancellationToken ct = default) =>
            Task.FromResult(_headers.Values.FirstOrDefault(h =>
                h.MunicipioId == municipioId &&
                string.Equals(h.NumeroPrefeitura, numeroPrefeitura, StringComparison.Ordinal)));

        public Task AddHeaderAsync(OficioHeader header, CancellationToken ct = default)
        {
            _headers[header.ServiceOrderId] = header;
            return Task.CompletedTask;
        }

        public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
        {
            _headers[serviceOrderId].IsValidated = true;
            return Task.CompletedTask;
        }

        public Task ClearNumeroPrefeituraAsync(string serviceOrderId, CancellationToken ct = default)
        {
            if (_headers.TryGetValue(serviceOrderId, out var header)) header.NumeroPrefeitura = null;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OficioLine>> GetLinesAsync(string serviceOrderId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OficioLine>>(_lines.GetValueOrDefault(serviceOrderId) ?? []);

        public Task ReplaceLinesAsync(string serviceOrderId, IReadOnlyList<OficioLine> lines, CancellationToken ct = default)
        {
            _lines[serviceOrderId] = [.. lines];
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InMemoryOrderRepository : IServiceOrderRepository
    {
        private readonly Dictionary<string, ServiceOrder> _orders = new(StringComparer.Ordinal);

        public event EventHandler<IServiceOrder>? OrderAdded;
        public event EventHandler<IServiceOrder>? OrderUpdated;
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged;
        public event EventHandler<string>? OrderDeleted;

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_orders.TryGetValue(id, out var o) ? (IServiceOrder?)o : null);
        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([.. _orders.Values]);
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IServiceOrder>>([]);
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) =>
            Task.FromResult<IServiceOrder?>(null);
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => GetAllAsync(ct);

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default)
        {
            var so = (ServiceOrder)order;
            _orders[so.Id] = so;
            OrderAdded?.Invoke(this, so);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            var so = (ServiceOrder)order;
            _orders[so.Id] = so;
            OrderUpdated?.Invoke(this, so);
            return Task.CompletedTask;
        }

        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            var order = _orders[orderId];
            var prev  = order.Status;
            order.ActionLog.Add(entry);
            if (entry.ResultingStatus is not null)
            {
                order.Status    = entry.ResultingStatus;
                order.UpdatedAt = entry.PerformedAt;
                OrderStatusChanged?.Invoke(this, (order, prev));
            }
            OrderUpdated?.Invoke(this, order);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            if (_orders.Remove(id)) OrderDeleted?.Invoke(this, id);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Decorates a real <see cref="IServiceOrderRepository"/>, forwarding every call unchanged
    /// except <see cref="AppendActionAsync"/>, where it throws exactly once (then behaves
    /// normally) — simulating a crash between the Faturamento ledger write (which, per
    /// <c>LevantamentoCalculationService.ValidateAsync</c>'s established ordering, already
    /// committed by the time this call happens) and the ServiceOrder's own Validado transition
    /// (which this call is what actually performs, per <see cref="OrderActionLog.ResultingStatus"/>).
    /// Used by XD01-188's rollback/idempotency-safety test.
    /// </summary>
    private sealed class FaultInjectingOrderRepository(IServiceOrderRepository inner) : IServiceOrderRepository
    {
        private bool _hasThrown;

        public bool FaultArmed { get; set; }

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => inner.GetRootsAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => inner.GetChildrenAsync(parentId, ct);
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => inner.GetParentAsync(childId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => inner.GetByStatusAsync(status, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => inner.GetByAssigneeAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => inner.GetByCreatorAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => inner.GetByOrderTypeAsync(orderTypeId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => inner.GetByDateRangeAsync(from, toExclusive, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => inner.GetDispatchedToAsync(targetId, targetType, ct);

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => inner.AddAsync(order, ct);
        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default) => inner.UpdateAsync(order, ct);
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => inner.AppendDispatchAsync(orderId, dispatch, ct);
        public Task DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            if (FaultArmed && !_hasThrown && entry.Action == OrderActionType.Validate)
            {
                _hasThrown = true;
                throw new InvalidOperationException("Simulated crash after the Faturamento ledger write, before the ServiceOrder's Validado transition.");
            }

            return inner.AppendActionAsync(orderId, entry, ct);
        }

        public event EventHandler<IServiceOrder>? OrderAdded { add => inner.OrderAdded += value; remove => inner.OrderAdded -= value; }
        public event EventHandler<IServiceOrder>? OrderUpdated { add => inner.OrderUpdated += value; remove => inner.OrderUpdated -= value; }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add => inner.OrderStatusChanged += value; remove => inner.OrderStatusChanged -= value; }
        public event EventHandler<string>? OrderDeleted { add => inner.OrderDeleted += value; remove => inner.OrderDeleted -= value; }
    }

    private sealed class AllowAllAuthorizationService : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoOpDbContextFactory : IDbContextFactory<GeoAssetsDbContext>
    {
        public GeoAssetsDbContext CreateDbContext() => throw new NotSupportedException();
        public Task<GeoAssetsDbContext> CreateDbContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoOpGrantRepository : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
