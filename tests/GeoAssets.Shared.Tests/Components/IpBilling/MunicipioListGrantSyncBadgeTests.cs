using System.Text.Json;
using Bunit;
using FluentAssertions;
using GeoAssets.Core.Localization;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.IpBilling.UI.Components;
using GeoAssets.Shared.Tests.Components.ServiceOrders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.IpBilling;

/// <summary>XD01-395: the Prefeitura grant-sync badge of <see cref="MunicipioList"/>, per state, and its text keys.</summary>
public sealed class MunicipioListGrantSyncBadgeTests : BunitContext
{
    private static readonly Guid Distribuidora = new("d0d0d0d0-0000-4000-8000-000000000395");

    private sealed class FixedMunicipioRepository(IReadOnlyList<Municipio> municipios) : IMunicipioRepository
    {
        public Task<Municipio?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Municipio?> GetByIbgeCodeAsync(string ibgeCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Municipio>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(municipios);
        public Task AddAsync(Municipio municipio, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Municipio municipio, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static Municipio Municipio(MunicipioGrantSyncState state, string? error = null, DateTimeOffset? syncedAt = null) => new()
    {
        IbgeCode = "3550308",
        Name = "São Paulo",
        Uf = "SP",
        DistribuidoraOrganizationId = Distribuidora,
        GrantSyncState = state,
        GrantSyncError = error,
        GrantSyncedAt = syncedAt,
    };

    private IRenderedComponent<MunicipioList> Render(params Municipio[] municipios)
    {
        Services.AddSingleton<IMunicipioRepository>(new FixedMunicipioRepository(municipios));
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        return Render<MunicipioList>();
    }

    [Fact]
    public void NoBadge_WhenTheMunicipioHasNoGrantSync()
    {
        var cut = Render(Municipio(MunicipioGrantSyncState.None));

        cut.FindAll(".asset-list-item .badge").Should().BeEmpty();
    }

    [Theory]
    [InlineData(MunicipioGrantSyncState.Pending, "admin.ipBilling.municipios.grantSync.pending", "--warning")]
    [InlineData(MunicipioGrantSyncState.Synced, "admin.ipBilling.municipios.grantSync.synced", "--success")]
    [InlineData(MunicipioGrantSyncState.Failed, "admin.ipBilling.municipios.grantSync.failed", "--danger")]
    public void Badge_ShowsTheStatesTextKey_InTheStatesTheme_Color(MunicipioGrantSyncState state, string key, string cssVariable)
    {
        var cut = Render(Municipio(state));

        var badge = cut.Find(".asset-list-item .badge");
        badge.TextContent.Should().Be(key);
        badge.GetAttribute("style").Should().Contain($"var({cssVariable})").And.Contain("var(--on-accent)");
    }

    [Fact]
    public void Badge_OfASyncedMunicipio_TitlesTheSyncTime()
    {
        var cut = Render(Municipio(MunicipioGrantSyncState.Synced, syncedAt: new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)));

        cut.Find(".badge").GetAttribute("title").Should().Be("admin.ipBilling.municipios.grantSync.syncedAt");
    }

    [Fact]
    public void Badge_OfAPendingMunicipio_TitlesTheLastError_WhenThereIsOne()
    {
        var cut = Render(Municipio(MunicipioGrantSyncState.Pending, error: "HttpRequestException: kernel down"));

        cut.Find(".badge").GetAttribute("title").Should().Be("HttpRequestException: kernel down");
    }

    [Fact]
    public void Badge_OfAPendingMunicipio_WithoutAnError_TitlesTheQueuedHint()
    {
        var cut = Render(Municipio(MunicipioGrantSyncState.Pending));

        cut.Find(".badge").GetAttribute("title").Should().Be("admin.ipBilling.municipios.grantSync.pendingHint");
    }

    [Fact]
    public void Badge_OfAFailedMunicipio_TitlesTheError()
    {
        var cut = Render(Municipio(MunicipioGrantSyncState.Failed, error: "InvalidOperationException: refused"));

        cut.Find(".badge").GetAttribute("title").Should().Be("InvalidOperationException: refused");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("es")]
    public void EveryGrantSyncKey_IsTranslated(string language)
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "GeoAssets.sln"))) root = Path.GetDirectoryName(root);
        root.Should().NotBeNull("the repository root holds GeoAssets.sln");

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, "apps", "GeoAssets.Shared", "wwwroot", "i18n", $"{language}.json")));

        foreach (var key in new[] { "pending", "synced", "failed", "syncedAt", "pendingHint" })
        {
            document.RootElement.TryGetProperty($"admin.ipBilling.municipios.grantSync.{key}", out var value).Should().BeTrue($"{language} lacks grantSync.{key}");
            value.GetString().Should().NotBeNullOrWhiteSpace();
        }
    }
}
