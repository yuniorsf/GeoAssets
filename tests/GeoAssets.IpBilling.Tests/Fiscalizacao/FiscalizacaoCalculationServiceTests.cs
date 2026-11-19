using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.Tests.Calculation;
using GeoAssets.IpBilling.Workflow;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Fiscalizacao;

public class FiscalizacaoCalculationServiceTests
{
    private const string IbgeCode = "3550308";
    private static readonly Guid MunicipioId = Guid.NewGuid();

    private sealed class FakeServiceOrderRepository : IServiceOrderRepository
    {
        private readonly Dictionary<string, ServiceOrder> _orders = [];
        public readonly List<OrderActionLog> AppendedActions = [];

        /// <summary>Runs before an action is recorded — lets a test observe or fail the transition.</summary>
        public Action<OrderActionLog>? OnAppend { get; set; }

        public void Seed(ServiceOrder order) => _orders[order.Id] = order;

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IServiceOrder?>(_orders.GetValueOrDefault(id));

        public Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            _orders[order.Id] = (ServiceOrder)order;
            return Task.CompletedTask;
        }

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            OnAppend?.Invoke(entry);
            AppendedActions.Add(entry);
            if (entry.ResultingStatus is not null) _orders[orderId].Status = entry.ResultingStatus;
            return Task.CompletedTask;
        }

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();

        // Explicit no-op accessors (not field-like events) — this fake doesn't support
        // subscribing to/observing these events by design; nothing in these tests needs to.
        public event EventHandler<IServiceOrder>? OrderAdded { add { } remove { } }
        public event EventHandler<IServiceOrder>? OrderUpdated { add { } remove { } }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add { } remove { } }
        public event EventHandler<string>? OrderDeleted { add { } remove { } }
    }

    private sealed class FakeMunicipioRepository(Municipio municipio) : IMunicipioRepository
    {
        public Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Municipio?>(municipio);
        public Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default) =>
            Task.FromResult(string.Equals(ibgeCode, municipio.IbgeCode, StringComparison.Ordinal) ? municipio : null);
        public Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Municipio>>([municipio]);
        public Task AddAsync(Municipio m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Municipio m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeFiscalizacaoRepository : IFiscalizacaoRepository
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

        /// <summary>Makes the next <see cref="MarkValidatedAsync"/> fail once, simulating a crash.</summary>
        public bool FailNextMarkValidated { get; set; }

        public Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default)
        {
            if (FailNextMarkValidated)
            {
                FailNextMarkValidated = false;
                throw new IOException("simulated crash while marking the header validated");
            }

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

    private static ServiceOrder NewFiscalizacaoOrder(string id, string competencia) => new()
    {
        Id = id,
        OrderTypeId = "ip-fiscalizacao",
        Status = "Carregado",
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["municipioIbgeCode"] = IbgeCode,
            ["competencia"] = competencia,
        },
    };

    private static (FiscalizacaoCalculationService Service, FakeServiceOrderRepository OrderRepo, FakeFiscalizacaoRepository FiscalizacaoRepo) NewService()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var fiscalizacaoRepo = new FakeFiscalizacaoRepository();
        var service = new FiscalizacaoCalculationService(
            orderRepo, new FakeMunicipioRepository(municipio), fiscalizacaoRepo, new InspectionPostingHarness().CreatePoster(), TimeProvider.System);
        return (service, orderRepo, fiscalizacaoRepo);
    }

    [Fact]
    public async Task UploadAsync_PersistsLinesWithoutComputingTotals()
    {
        var (service, orderRepo, fiscalizacaoRepo) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);

        var count = await service.UploadAsync("order-1",
        [
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
        ]);

        count.Should().Be(1);
        var lines = await fiscalizacaoRepo.GetLinesAsync("order-1");
        lines.Should().ContainSingle();
        lines[0].Agregado.Should().Be(0, "UploadAsync must not compute totals — CalculateAsync does");
    }

    [Fact]
    public async Task CalculateAsync_AllFourGroupsPresent_ComputesTotalsAndTransitionsToCalculado()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);

        var rows = new[]
        {
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 8),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Novos24hIrregulares, 4),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Regularizacao24h, 3),
        };

        var result = await service.CalculateAsync("order-1", rows);

        result.Status.Should().Be("Calculado");
        result.TotalAgregado.Should().Be(3);
        result.TotalRetroativo.Should().Be(2);
        result.Lines.Should().ContainSingle();
        order.Status.Should().Be("Calculado");
    }

    [Fact]
    public async Task CalculateAsync_NegativeAgregadoAcrossMultipleLines_SumsCorrectly_NotClamped()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);

        var rows = new[]
        {
            // INST-001: Agregado = 2 - 20 = -18.
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 2),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 20),
            // INST-002: Agregado = 5.
            new FiscalizacaoWorkbookRow("INST-002", FiscalizacaoOperationGroup.Fiscalizacao, 5),
        };

        var result = await service.CalculateAsync("order-1", rows);

        result.TotalAgregado.Should().Be(-13);
    }

    [Fact]
    public async Task CalculateStoredAsync_RederivesRowsFromPersistedLines()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);
        await service.UploadAsync("order-1",
        [
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 4),
        ]);

        var result = await service.CalculateStoredAsync("order-1");

        result.TotalAgregado.Should().Be(6);
        result.TotalRetroativo.Should().Be(6);
    }

    [Fact]
    public async Task ValidateAsync_MarksHeaderValidated_AndTransitionsOrder()
    {
        var (service, orderRepo, fiscalizacaoRepo) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        orderRepo.Seed(order);
        await service.CalculateAsync("order-1", [new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10)]);

        var result = await service.ValidateAsync("order-1");

        result.Status.Should().Be("Validado");
        result.TotalAgregado.Should().Be(10);
        order.Status.Should().Be("Validado");
        (await fiscalizacaoRepo.GetHeaderAsync("order-1"))!.IsValidated.Should().BeTrue();
    }

    [Fact]
    public Task CalculateAsync_WrongStatus_Throws()
    {
        var (service, orderRepo, _) = NewService();
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        order.Status = "Validado";
        orderRepo.Seed(order);

        var act = () => service.CalculateAsync("order-1", []);

        return act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }

    // ── XD01-301: Validate posts the points→kWh delta to the Faturamento ledger ──────────
    // Each test names the contract's guard/branch ID (VAL-*). They run the real
    // InspectionLedgerPoster + FaturamentoLedgerService over in-memory repositories.

    private static readonly DateTimeOffset ValidatedAt = new(2027, 2, 3, 10, 0, 0, TimeSpan.Zero);

    // 2027-01 has 31 days; the standard chart bills VS 100 W at 117 W.
    // 10 × 117 × 11.8667 × 31 ÷ 1000 = 430.4052.
    private const decimal TenPointsJanuaryKwh = 430.4052m;

    private sealed record Sut(
        FiscalizacaoCalculationService Service,
        FakeServiceOrderRepository OrderRepo,
        FakeFiscalizacaoRepository FiscalizacaoRepo,
        InspectionPostingHarness Posting,
        Municipio Municipio);

    private static Sut NewSut()
    {
        var municipio = new Municipio { Id = MunicipioId, IbgeCode = IbgeCode, Name = "Teste", Uf = "SP", DistribuidoraOrganizationId = Guid.NewGuid() };
        var orderRepo = new FakeServiceOrderRepository();
        var fiscalizacaoRepo = new FakeFiscalizacaoRepository();
        var posting = new InspectionPostingHarness();
        var service = new FiscalizacaoCalculationService(
            orderRepo, new FakeMunicipioRepository(municipio), fiscalizacaoRepo, posting.CreatePoster(), new FixedTimeProvider(ValidatedAt));
        return new Sut(service, orderRepo, fiscalizacaoRepo, posting, municipio);
    }

    private static async Task<ServiceOrder> SeedCalculatedAsync(Sut sut, params FiscalizacaoWorkbookRow[] rows)
    {
        var order = NewFiscalizacaoOrder("order-1", "2027-01");
        sut.OrderRepo.Seed(order);
        await sut.Service.CalculateAsync("order-1", rows);
        sut.OrderRepo.AppendedActions.Clear();
        return order;
    }

    private static FiscalizacaoWorkbookRow ExactRow(int count = 10) =>
        new("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, count, LampTypeCode.VS, 100);

    private static FiscalizacaoWorkbookRow LamplessRow(int count = 10) =>
        new("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, count);

    [Fact]
    public async Task ValidateAsync_VAL_B1_ExactLines_PostsToLedger_ThenValidates_AndLogsTheExactConversion()
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(sut, ExactRow());

        var result = await sut.Service.ValidateAsync("order-1", "ana");

        result.Status.Should().Be("Validado");
        result.DeltaKwh.Should().Be(TenPointsJanuaryKwh);
        result.IsProvisional.Should().BeFalse();
        result.ExactPoints.Should().Be(10);
        result.ProvisionalPoints.Should().Be(0);
        result.Faturamento.Should().NotBeNull();
        result.Faturamento!.KwhFaturado.Should().Be(TenPointsJanuaryKwh);
        result.Faturamento.MunicipioId.Should().Be(MunicipioId);
        result.Faturamento.Competencia.Should().Be("2027-01");

        var posting = sut.Posting.Faturamentos.Postings.Should().ContainSingle().Which;
        posting.Mode.Should().Be(FaturamentoPostingMode.Accumulate);
        posting.IsProvisional.Should().BeFalse();
        posting.PostedAt.Should().Be(ValidatedAt);
        sut.Posting.Faturamentos.SaveCalls.Should().Be(1);

        order.Status.Should().Be("Validado");
        (await sut.FiscalizacaoRepo.GetHeaderAsync("order-1"))!.IsValidated.Should().BeTrue();
        var action = sut.OrderRepo.AppendedActions.Should().ContainSingle().Which;
        action.Action.Should().Be(IpBillingActions.Validate);
        action.PerformedBy.Should().Be("ana");
        action.ResultingStatus.Should().Be("Validado");
        action.Comment.Should().Contain("Validated Fiscalização (Agregado=10, Retroativo=10)")
            .And.Contain("posted (Accumulate) 430.4052 kWh to Faturamento 2027-01 — exact conversion from lamp detail");
    }

    [Fact]
    public async Task ValidateAsync_VAL_B1_LedgerIsWrittenBeforeTheOrderTransitions()
    {
        var sut = NewSut();
        await SeedCalculatedAsync(sut, ExactRow());
        var postingsSeenAtTransition = -1;
        sut.OrderRepo.OnAppend = _ => postingsSeenAtTransition = sut.Posting.Faturamentos.SaveCalls;

        await sut.Service.ValidateAsync("order-1");

        postingsSeenAtTransition.Should().Be(1, "the ledger posting is saved before the order is transitioned");
    }

    [Fact]
    public async Task ValidateAsync_VAL_B2_LamplessPoints_PostProvisionally_AndSayItLoudly()
    {
        var sut = NewSut();
        await SeedCalculatedAsync(sut, LamplessRow());

        var result = await sut.Service.ValidateAsync("order-1");

        // The only standard-chart row bills 117 W, so the mean watts-per-point is 117 W too.
        result.DeltaKwh.Should().Be(TenPointsJanuaryKwh);
        result.IsProvisional.Should().BeTrue();
        result.ProvisionalPoints.Should().Be(10);
        result.ExactPoints.Should().Be(0);
        sut.Posting.Faturamentos.Postings.Should().ContainSingle().Which.IsProvisional.Should().BeTrue();
        sut.OrderRepo.AppendedActions.Should().ContainSingle().Which.Comment.Should()
            .Contain("PROVISIONAL conversion — 10 point(s) without lamp detail")
            .And.Contain("pending recalculation");
    }

    [Fact]
    public async Task ValidateAsync_VAL_B3_CrashAfterLedgerWriteBeforeOrderTransition_IsSafeToRetry()
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(sut, ExactRow());
        sut.OrderRepo.OnAppend = _ => throw new IOException("simulated crash while transitioning the order");

        await sut.Service.Invoking(s => s.ValidateAsync("order-1")).Should().ThrowAsync<IOException>();

        order.Status.Should().Be("Calculado", "the transition never happened");
        sut.Posting.Faturamentos.Postings.Should().ContainSingle("the ledger write already committed");

        sut.OrderRepo.OnAppend = null;
        var retry = await sut.Service.ValidateAsync("order-1");

        retry.Status.Should().Be("Validado");
        retry.DeltaKwh.Should().Be(TenPointsJanuaryKwh);
        retry.Faturamento!.KwhFaturado.Should().Be(TenPointsJanuaryKwh, "the retry must not accumulate the delta twice");
        sut.Posting.Faturamentos.Postings.Should().ContainSingle();
        sut.Posting.Faturamentos.SaveCalls.Should().Be(1, "the retry found the existing posting and wrote nothing");
        order.Status.Should().Be("Validado");
    }

    [Fact]
    public async Task ValidateAsync_VAL_B3_CrashWhileMarkingTheHeaderValidated_IsSafeToRetry()
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(sut, LamplessRow());
        sut.FiscalizacaoRepo.FailNextMarkValidated = true;

        await sut.Service.Invoking(s => s.ValidateAsync("order-1")).Should().ThrowAsync<IOException>();
        var retry = await sut.Service.ValidateAsync("order-1");

        retry.IsProvisional.Should().BeTrue("the stored marker is reused on the retry");
        retry.Faturamento!.KwhFaturado.Should().Be(TenPointsJanuaryKwh);
        sut.Posting.Faturamentos.Postings.Should().ContainSingle();
        order.Status.Should().Be("Validado");
    }

    [Fact]
    public async Task ValidateAsync_VAL_B4_AlreadyValidado_ReportsTheExistingPosting_WithoutPostingOrLogging()
    {
        var sut = NewSut();
        await SeedCalculatedAsync(sut, LamplessRow());
        var first = await sut.Service.ValidateAsync("order-1");
        sut.OrderRepo.AppendedActions.Clear();
        var postCalls = sut.Posting.Faturamentos.PostCalls;

        var again = await sut.Service.ValidateAsync("order-1");

        again.Should().BeEquivalentTo(first);
        again.Faturamento.Should().NotBeNull();
        sut.Posting.Faturamentos.PostCalls.Should().Be(postCalls, "PostAsync is never called for a Validado order");
        sut.Posting.Faturamentos.Postings.Should().ContainSingle();
        sut.OrderRepo.AppendedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_VAL_B4_ValidadoOrderFromBeforeKwhPosting_HasNoFaturamento()
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(sut, ExactRow());
        order.Status = "Validado"; // validated before XD01-301: transitioned, but nothing was ever posted

        var result = await sut.Service.ValidateAsync("order-1");

        result.Status.Should().Be("Validado");
        result.TotalAgregado.Should().Be(10);
        result.Faturamento.Should().BeNull();
        result.DeltaKwh.Should().Be(0m);
        result.IsProvisional.Should().BeFalse();
        sut.Posting.Faturamentos.Postings.Should().BeEmpty();
        sut.Posting.Faturamentos.PostCalls.Should().Be(0);
        sut.OrderRepo.AppendedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_VAL_B5_LampInNoChart_Throws_AndLeavesOrderHeaderAndLedgerUntouched()
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(
            sut, new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10, LampTypeCode.MV, 400));

        var act = () => sut.Service.ValidateAsync("order-1");

        var ex = (await act.Should().ThrowAsync<InspectionLampNotInChartException>()).Which;
        ex.Code.Should().Be(LampTypeCode.MV);
        ex.WattageW.Should().Be(400);
        order.Status.Should().Be("Calculado");
        (await sut.FiscalizacaoRepo.GetHeaderAsync("order-1"))!.IsValidated.Should().BeFalse();
        sut.Posting.Faturamentos.Postings.Should().BeEmpty();
        sut.Posting.Faturamentos.SaveCalls.Should().Be(0);
        sut.OrderRepo.AppendedActions.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_VAL_B6_MunicipioMissing_ThrowsKeyNotFound()
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(sut, ExactRow());
        order.Attributes["municipioIbgeCode"] = "9999999";

        await sut.Service.Invoking(s => s.ValidateAsync("order-1"))
            .Should().ThrowAsync<KeyNotFoundException>().WithMessage("*9999999*");
        sut.Posting.Faturamentos.Postings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("municipioIbgeCode", null)]
    [InlineData("municipioIbgeCode", "  ")]
    [InlineData("competencia", null)]
    [InlineData("competencia", "2027-13")]
    [InlineData("competencia", "janeiro")]
    public async Task ValidateAsync_VAL_B6_MissingOrInvalidBillingAttribute_ThrowsInvalidOperation(string attribute, string? value)
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(sut, ExactRow());
        if (value is null) order.Attributes.Remove(attribute);
        else order.Attributes[attribute] = value;

        await sut.Service.Invoking(s => s.ValidateAsync("order-1"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{attribute}*");
        order.Status.Should().Be("Calculado");
        sut.Posting.Faturamentos.Postings.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_VAL_B7_WrongStatus_ThrowsInvalidTransition()
    {
        var sut = NewSut();
        sut.OrderRepo.Seed(NewFiscalizacaoOrder("order-1", "2027-01")); // still Carregado

        await sut.Service.Invoking(s => s.ValidateAsync("order-1"))
            .Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        sut.Posting.Faturamentos.Postings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FiscalizacaoCalculationService.AttrAgregado)]
    [InlineData(FiscalizacaoCalculationService.AttrRetroativo)]
    public async Task ValidateAsync_VAL_B7_MissingCalculatedTotals_ThrowsInvalidOperation(string attribute)
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(sut, ExactRow());
        order.Attributes.Remove(attribute);

        await sut.Service.Invoking(s => s.ValidateAsync("order-1"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing a calculated*");
        sut.Posting.Faturamentos.Postings.Should().BeEmpty();
    }

    [Fact]
    public Task ValidateAsync_UnknownOrder_ThrowsKeyNotFound()
    {
        var sut = NewSut();

        return sut.Service.Invoking(s => s.ValidateAsync("nope")).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ValidateAsync_InconsistentStoredLine_ThrowsInvalidOperation_AndPostsNothing()
    {
        var sut = NewSut();
        var order = await SeedCalculatedAsync(sut, ExactRow());
        var line = (await sut.FiscalizacaoRepo.GetLinesAsync("order-1")).Single();
        line.LampDetails[0].Quantity = 11; // more lamps than the group counts (MAP-G2)

        await sut.Service.Invoking(s => s.ValidateAsync("order-1"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*INST-001*");
        order.Status.Should().Be("Calculado");
        sut.Posting.Faturamentos.Postings.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateStoredAsync_VAL_B8_PreservesLampDetailsThroughRecalculation()
    {
        var sut = NewSut();
        sut.OrderRepo.Seed(NewFiscalizacaoOrder("order-1", "2027-01"));
        await sut.Service.UploadAsync("order-1",
        [
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 6, LampTypeCode.VS, 100),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 4),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 3, LampTypeCode.LD, 60),
        ]);

        var first = await sut.Service.CalculateStoredAsync("order-1");
        var second = await sut.Service.CalculateStoredAsync("order-1"); // recompute while already Calculado

        first.TotalAgregado.Should().Be(7);
        second.TotalAgregado.Should().Be(7);
        var stored = (await sut.FiscalizacaoRepo.GetLinesAsync("order-1")).Single();
        stored.LampDetails.Select(l => (l.OperationGroup, l.Code, l.WattageW, l.Quantity)).Should().Equal(
            (FiscalizacaoOperationGroup.Fiscalizacao, LampTypeCode.VS, 100, 6),
            (FiscalizacaoOperationGroup.UltimoLevantamento, LampTypeCode.LD, 60, 3));
        stored.LampDetails.Should().OnlyContain(l => l.LineId == stored.Id);
        FiscalizacaoKwhEntryMapper.CountWithoutLampDetail(stored, FiscalizacaoOperationGroup.Fiscalizacao).Should().Be(4);
    }
}
