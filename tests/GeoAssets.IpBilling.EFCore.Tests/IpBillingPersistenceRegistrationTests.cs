using FluentAssertions;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence;
using GeoAssets.IpBilling.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// <c>AddIpBillingPersistence</c> after XD01-301: the points→kWh poster and the Distribuidora
/// ports resolve from the container, and the resolved poster works end to end against the real
/// EF repositories (chart lookup, mean, provisional marker, idempotent retry).
/// </summary>
public sealed class IpBillingPersistenceRegistrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _provider;

    public IpBillingPersistenceRegistrationTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddIpBillingPersistence(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IpBillingDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void Resolves_ThePosterAndTheDistribuidoraPorts_SharingOneInstancePerScope()
    {
        using var scope = _provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IInspectionLedgerPoster>().Should().BeOfType<InspectionLedgerPoster>();
        sp.GetRequiredService<IDistribuidoraRepository>().Should().BeOfType<EFDistribuidoraRepository>();
        sp.GetRequiredService<IDistribuidoraReader>().Should().BeSameAs(sp.GetRequiredService<IDistribuidoraRepository>());
        sp.GetRequiredService<IDistribuidoraLampWattageRepository>().Should().BeOfType<EFDistribuidoraLampWattageRepository>();
        sp.GetRequiredService<IDistribuidoraLampWattageReader>().Should().BeSameAs(sp.GetRequiredService<IDistribuidoraLampWattageRepository>());
    }

    [Fact]
    public void Poster_IsScoped_NotSharedAcrossScopes()
    {
        using var first = _provider.CreateScope();
        using var second = _provider.CreateScope();

        first.ServiceProvider.GetRequiredService<IInspectionLedgerPoster>()
            .Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<IInspectionLedgerPoster>());
    }

    [Fact]
    public async Task ResolvedPoster_ConvertsWithTheStoredDistribuidoraChart_AndARetryInANewScopePostsNothingNew()
    {
        Municipio municipio;
        using (var seed = _provider.CreateScope())
        {
            var distribuidoras = seed.ServiceProvider.GetRequiredService<IDistribuidoraRepository>();
            var distribuidora = new Distribuidora { OrganizationId = Guid.NewGuid(), Name = "Enel SP" };
            await distribuidoras.AddAsync(distribuidora);
            await distribuidoras.SaveChangesAsync();

            var chart = seed.ServiceProvider.GetRequiredService<IDistribuidoraLampWattageRepository>();
            await chart.UpsertAsync(new DistribuidoraLampWattage { DistribuidoraId = distribuidora.Id, Code = LampTypeCode.VS, WattageW = 100, BallastLossW = 30m });
            await chart.SaveChangesAsync();

            municipio = new Municipio
            {
                IbgeCode = "3550308",
                Name = "São Paulo",
                Uf = "SP",
                DistribuidoraOrganizationId = Guid.NewGuid(),
                DistribuidoraId = distribuidora.Id,
            };
            var municipios = seed.ServiceProvider.GetRequiredService<IMunicipioRepository>();
            await municipios.AddAsync(municipio);
            await municipios.SaveChangesAsync();
        }

        // 10 exact points + 2 lamp-less points, all at the chart's 130 W (its only row is also its mean):
        // 12 × 130 × 11.8667 × 31 ÷ 1000 = 573.8736.
        var request = new InspectionLedgerPostingRequest(
            "order-fisc-1",
            municipio,
            "2026-03",
            [new InspectionPointEntry(10, 1, false, true, LampTypeCode.VS, 100), new InspectionPointEntry(2, 1, false, true)],
            new DateTimeOffset(2026, 4, 2, 9, 0, 0, TimeSpan.Zero));

        InspectionLedgerPosting first;
        using (var scope = _provider.CreateScope())
        {
            first = await scope.ServiceProvider.GetRequiredService<IInspectionLedgerPoster>().PostAsync(request);
        }

        InspectionLedgerPosting retry;
        using (var scope = _provider.CreateScope())
        {
            retry = await scope.ServiceProvider.GetRequiredService<IInspectionLedgerPoster>().PostAsync(request);
        }

        first.WasAlreadyPosted.Should().BeFalse();
        first.DeltaKwh.Should().Be(573.8736m);
        first.IsProvisional.Should().BeTrue();
        first.ExactPoints.Should().Be(10);
        first.ProvisionalPoints.Should().Be(2);
        retry.WasAlreadyPosted.Should().BeTrue();
        retry.DeltaKwh.Should().Be(573.8736m);
        retry.IsProvisional.Should().BeTrue();

        using var verify = _provider.CreateScope();
        var faturamentos = verify.ServiceProvider.GetRequiredService<IFaturamentoRepository>();
        (await faturamentos.GetByMunicipioAndCompetenciaAsync(municipio.Id, "2026-03"))!.KwhFaturado.Should().Be(573.8736m);
        var posting = (await faturamentos.GetPostingsByOrderAsync("order-fisc-1")).Should().ContainSingle().Which;
        posting.IsProvisional.Should().BeTrue();
        posting.Mode.Should().Be(FaturamentoPostingMode.Accumulate);
    }
}
