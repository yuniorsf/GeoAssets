using FluentAssertions;
using GeoAssets.IpBilling.Oficio;
using GeoAssets.IpBilling.Persistence;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

public class EFOficioRepositoryTests
{
    private static OficioHeader Header(string orderId, Guid municipioId, string competencia, string? numero = null) => new()
    {
        ServiceOrderId = orderId,
        MunicipioId = municipioId,
        Competencia = competencia,
        NumeroPrefeitura = numero,
    };

    private static OficioLine Line(string orderId, string codigo)
    {
        var line = new OficioLine
        {
            ServiceOrderId = orderId,
            CodigoInstalacao = codigo,
            Agregado = 3,
            Retroativo = 2,
        };
        line.OperationGroups.Add(new OficioOperationGroupDetail
        {
            LineId = line.Id,
            OperationGroup = OficioOperationGroup.Inclusao,
            Count = 10,
        });
        line.OperationGroups.Add(new OficioOperationGroupDetail
        {
            LineId = line.Id,
            OperationGroup = OficioOperationGroup.Retirada,
            Count = 8,
        });
        return line;
    }

    [Fact]
    public async Task AddHeaderAsync_ThenGetHeaderAsync_RoundTrips()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOficioRepository(fixture.Context);
        var municipioId = Guid.NewGuid();

        await repo.AddHeaderAsync(Header("order-1", municipioId, "2027-01", "OF-001"));
        await repo.SaveChangesAsync();

        var loaded = await repo.GetHeaderAsync("order-1");
        loaded.Should().NotBeNull();
        loaded!.MunicipioId.Should().Be(municipioId);
        loaded.Competencia.Should().Be("2027-01");
        loaded.NumeroPrefeitura.Should().Be("OF-001");
        loaded.IsValidated.Should().BeFalse();
    }

    [Fact]
    public async Task MarkValidatedAsync_SetsIsValidated()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOficioRepository(fixture.Context);
        await repo.AddHeaderAsync(Header("order-1", Guid.NewGuid(), "2027-01"));
        await repo.SaveChangesAsync();

        await repo.MarkValidatedAsync("order-1");
        await repo.SaveChangesAsync();

        var loaded = await new EFOficioRepository(fixture.NewContext()).GetHeaderAsync("order-1");
        loaded!.IsValidated.Should().BeTrue();
    }

    [Fact]
    public async Task GetHeaderByNumeroAsync_FindsActiveClaim_ScopedToMunicipio()
    {
        using var fixture = new SqliteFixture();
        var municipioId = Guid.NewGuid();
        var otherMunicipioId = Guid.NewGuid();
        var repo = new EFOficioRepository(fixture.Context);
        await repo.AddHeaderAsync(Header("order-1", municipioId, "2027-01", "OF-001"));
        await repo.AddHeaderAsync(Header("order-2", otherMunicipioId, "2027-01", "OF-001"));
        await repo.SaveChangesAsync();

        var found = await new EFOficioRepository(fixture.NewContext()).GetHeaderByNumeroAsync(municipioId, "OF-001");

        found.Should().NotBeNull();
        found!.ServiceOrderId.Should().Be("order-1");
    }

    [Fact]
    public async Task GetHeaderByNumeroAsync_NoClaim_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOficioRepository(fixture.Context);

        var found = await repo.GetHeaderByNumeroAsync(Guid.NewGuid(), "OF-999");

        found.Should().BeNull();
    }

    [Fact]
    public async Task ClearNumeroPrefeituraAsync_ReleasesTheClaim_SoAnotherHeaderCanReuseIt()
    {
        using var fixture = new SqliteFixture();
        var municipioId = Guid.NewGuid();
        var repo = new EFOficioRepository(fixture.Context);
        await repo.AddHeaderAsync(Header("order-1", municipioId, "2027-01", "OF-001"));
        await repo.SaveChangesAsync();

        var clearContext = fixture.NewContext();
        var clearRepo = new EFOficioRepository(clearContext);
        await clearRepo.ClearNumeroPrefeituraAsync("order-1");
        await clearRepo.SaveChangesAsync();

        var releasedHeader = await new EFOficioRepository(fixture.NewContext()).GetHeaderAsync("order-1");
        releasedHeader!.NumeroPrefeitura.Should().BeNull();

        // The partial unique index must now admit a second header claiming the same número.
        var repo2 = new EFOficioRepository(fixture.NewContext());
        await repo2.AddHeaderAsync(Header("order-2", municipioId, "2027-01", "OF-001"));
        var act = async () => await repo2.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReplaceLinesAsync_RoundTripsLinesAndOperationGroupDetails()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOficioRepository(fixture.Context);

        await repo.ReplaceLinesAsync("order-1", [Line("order-1", "INST-001")]);
        await repo.SaveChangesAsync();

        var lines = await new EFOficioRepository(fixture.NewContext()).GetLinesAsync("order-1");
        lines.Should().ContainSingle();
        var line = lines[0];
        line.CodigoInstalacao.Should().Be("INST-001");
        line.Agregado.Should().Be(3);
        line.Retroativo.Should().Be(2);
        line.OperationGroups.Should().HaveCount(2);
        line.OperationGroups.Should().ContainSingle(d => d.OperationGroup == OficioOperationGroup.Inclusao && d.Count == 10);
    }

    [Fact]
    public async Task ReplaceLinesAsync_CalledAgain_RemovesPriorLinesAndDetails()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOficioRepository(fixture.Context);

        await repo.ReplaceLinesAsync("order-1", [Line("order-1", "INST-001")]);
        await repo.SaveChangesAsync();

        await repo.ReplaceLinesAsync("order-1", [Line("order-1", "INST-002")]);
        await repo.SaveChangesAsync();

        var lines = await new EFOficioRepository(fixture.NewContext()).GetLinesAsync("order-1");
        lines.Should().ContainSingle(l => l.CodigoInstalacao == "INST-002");
    }

    [Fact]
    public async Task GetLinesAsync_NoLines_ReturnsEmpty()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOficioRepository(fixture.Context);

        var lines = await repo.GetLinesAsync("order-missing");

        lines.Should().BeEmpty();
    }
}
