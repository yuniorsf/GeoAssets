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
using GeoAssets.IpBilling.Levantamento;
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
        IWorkbookReader? workbookReader = null)
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
                        new ValidatingServiceOrderRepository(
                            new InMemoryOrderRepository(),
                            sp.GetRequiredService<OrderTypeRegistry>()));
                    services.AddSingleton<IMunicipioRepository>(new InMemoryMunicipioRepository());
                    services.AddSingleton<ILampTypeRepository>(new InMemoryLampTypeRepository());
                    services.AddSingleton<IFaturamentoRepository>(new InMemoryFaturamentoRepository());
                    services.AddSingleton<IGeoCoreClient>(new RestGeoCoreClient(coreHttpClient));
                    services.AddSingleton(transformerResolver);
                    services.AddScoped<IPoleResolver, HttpPoleResolver>();
                    services.AddScoped<IIpBillingCalculationService, IpBillingCalculationService>();
                    services.AddSingleton<ILevantamentoRepository>(new InMemoryLevantamentoRepository());
                    services.AddSingleton<IInterveningOperationLookup, NullInterveningOperationLookup>();
                    services.AddScoped<ILevantamentoCalculationService, LevantamentoCalculationService>();
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
                faturamento.KwhFaturado = mode == FaturamentoPostingMode.Set
                    ? rounded
                    : IpEnergyMath.Round(faturamento.KwhFaturado + rounded);
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
    }
}
