using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Repositories;
using GeoAssets.Shared.Components.IpBilling;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.IpBilling;

/// <summary>
/// <see cref="MunicipioForm.LoadAllDistribuidorasAsync"/> (XD01-301): the Distribuidora picker
/// has to walk a paged list endpoint to offer every Distribuidora.
/// </summary>
public class MunicipioFormTests
{
    private sealed class PagedReader(int total) : IDistribuidoraReader
    {
        private readonly List<Distribuidora> _all = Enumerable.Range(1, total)
            .Select(i => new Distribuidora { OrganizationId = Guid.NewGuid(), Name = $"D{i:000}" })
            .ToList();

        public List<(int Page, int PageSize)> Requests { get; } = [];

        /// <summary>When set, the reported total is this instead of the real count.</summary>
        public int? ReportedTotal { get; init; }

        public Task<Distribuidora?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_all.FirstOrDefault(d => d.Id == id));

        public Task<PagedResult<Distribuidora>> GetPageAsync(int page, int pageSize, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add((page, pageSize));
            return Task.FromResult(new PagedResult<Distribuidora>
            {
                Items = _all.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = ReportedTotal ?? _all.Count,
            });
        }
    }

    [Fact]
    public async Task LoadAllDistribuidorasAsync_NoDistribuidoras_ReturnsEmptyAfterOneRequest()
    {
        var reader = new PagedReader(0);

        var all = await MunicipioForm.LoadAllDistribuidorasAsync(reader);

        all.Should().BeEmpty();
        reader.Requests.Should().Equal((1, MunicipioForm.DistribuidoraPickerPageSize));
    }

    [Fact]
    public async Task LoadAllDistribuidorasAsync_SinglePage_StopsAtTheReportedTotal()
    {
        var reader = new PagedReader(3);

        var all = await MunicipioForm.LoadAllDistribuidorasAsync(reader);

        all.Select(d => d.Name).Should().Equal("D001", "D002", "D003");
        reader.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task LoadAllDistribuidorasAsync_SeveralPages_WalksThemInOrder()
    {
        var reader = new PagedReader(250);

        var all = await MunicipioForm.LoadAllDistribuidorasAsync(reader);

        all.Should().HaveCount(250);
        all.Select(d => d.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
        reader.Requests.Select(r => r.Page).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task LoadAllDistribuidorasAsync_TotalOverstated_StopsOnTheFirstEmptyPage()
    {
        var reader = new PagedReader(100) { ReportedTotal = 500 };

        var all = await MunicipioForm.LoadAllDistribuidorasAsync(reader);

        all.Should().HaveCount(100);
        reader.Requests.Select(r => r.Page).Should().Equal(1, 2);
    }

    [Fact]
    public async Task LoadAllDistribuidorasAsync_ForwardsTheCancellationToken_AndRejectsANullReader()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions.Awaiting(() => MunicipioForm.LoadAllDistribuidorasAsync(new PagedReader(3), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => MunicipioForm.LoadAllDistribuidorasAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }
}
