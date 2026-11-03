using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Models;
using GeoAssets.IpBilling.Persistence.Entities;
using GeoAssets.IpBilling.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>EF Core implementation of <see cref="IFaturamentoRepository"/>.</summary>
public sealed class EFFaturamentoRepository(IpBillingDbContext db) : IFaturamentoRepository
{
    public async Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(
        Guid municipioId, string competencia, CancellationToken ct = default)
    {
        var record = await db.Faturamentos.FirstOrDefaultAsync(
            f => f.MunicipioId == municipioId && f.Competencia == competencia, ct).ConfigureAwait(false);

        return record is null ? null : IpBillingMapper.ToDomain(record);
    }

    public async Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(
        Guid municipioId, CancellationToken ct = default)
    {
        var records = await db.Faturamentos
            .Where(f => f.MunicipioId == municipioId)
            .OrderBy(f => f.Competencia)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.Select(IpBillingMapper.ToDomain).ToList();
    }

    public async Task AddAsync(Faturamento faturamento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(faturamento);
        await db.Faturamentos.AddAsync(IpBillingMapper.ToRecord(faturamento), ct).ConfigureAwait(false);
    }

    public async Task<Faturamento> PostOrderCalculationAsync(
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        decimal kwhDeltaOrBaseline,
        FaturamentoPostingMode mode,
        DateTimeOffset postedAt,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(competencia);

        var existingPosting = await db.FaturamentoPostings.FirstOrDefaultAsync(
            p => p.ServiceOrderId == serviceOrderId && p.Competencia == competencia, ct).ConfigureAwait(false);

        var ledger = await db.Faturamentos.FirstOrDefaultAsync(
            f => f.MunicipioId == municipioId && f.Competencia == competencia, ct).ConfigureAwait(false);

        if (existingPosting is not null && ledger is not null)
        {
            return IpBillingMapper.ToDomain(ledger);
        }

        var roundedInput = IpEnergyMath.Round(kwhDeltaOrBaseline);

        if (ledger is null)
        {
            ledger = new FaturamentoRecord
            {
                Id          = Guid.NewGuid(),
                MunicipioId = municipioId,
                Competencia = competencia,
                KwhFaturado = roundedInput,
                CreatedAt   = postedAt,
            };
            await db.Faturamentos.AddAsync(ledger, ct).ConfigureAwait(false);
        }
        else
        {
            ledger.KwhFaturado = mode == FaturamentoPostingMode.Accumulate
                ? IpEnergyMath.Round(ledger.KwhFaturado + roundedInput)
                : roundedInput;
        }

        await db.FaturamentoPostings.AddAsync(new FaturamentoPostingRecord
        {
            Id                 = Guid.NewGuid(),
            ServiceOrderId     = serviceOrderId,
            MunicipioId        = municipioId,
            Competencia        = competencia,
            Mode               = mode,
            DeltaOrBaselineKwh = roundedInput,
            ResultingKwh       = ledger.KwhFaturado,
            PostedAt           = postedAt,
        }, ct).ConfigureAwait(false);

        return IpBillingMapper.ToDomain(ledger);
    }

    public async Task<Faturamento> OverwriteManualEntryAsync(
        Guid municipioId,
        string competencia,
        decimal kwhFaturado,
        string enteredByReference,
        DateTimeOffset postedAt,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(competencia);
        ArgumentException.ThrowIfNullOrWhiteSpace(enteredByReference);

        var roundedInput = IpEnergyMath.Round(kwhFaturado);

        var ledger = await db.Faturamentos.FirstOrDefaultAsync(
            f => f.MunicipioId == municipioId && f.Competencia == competencia, ct).ConfigureAwait(false);

        if (ledger is null)
        {
            ledger = new FaturamentoRecord
            {
                Id          = Guid.NewGuid(),
                MunicipioId = municipioId,
                Competencia = competencia,
                KwhFaturado = roundedInput,
                CreatedAt   = postedAt,
            };
            await db.Faturamentos.AddAsync(ledger, ct).ConfigureAwait(false);
        }
        else
        {
            ledger.KwhFaturado = roundedInput;
        }

        // No idempotency dedup here (unlike PostOrderCalculationAsync): a manual entry is an
        // intentional, human-triggered correction, not an automated retry-prone path — each call
        // is a deliberate new overwrite, identified for audit purposes by the caller-supplied
        // enteredByReference rather than a ServiceOrderId (manual entries have no ServiceOrder).
        await db.FaturamentoPostings.AddAsync(new FaturamentoPostingRecord
        {
            Id                 = Guid.NewGuid(),
            ServiceOrderId     = enteredByReference,
            MunicipioId        = municipioId,
            Competencia        = competencia,
            Mode               = FaturamentoPostingMode.Overwrite,
            DeltaOrBaselineKwh = roundedInput,
            ResultingKwh       = ledger.KwhFaturado,
            PostedAt           = postedAt,
        }, ct).ConfigureAwait(false);

        return IpBillingMapper.ToDomain(ledger);
    }

    public async Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(
        string serviceOrderId, CancellationToken ct = default)
    {
        var records = await db.FaturamentoPostings
            .Where(p => p.ServiceOrderId == serviceOrderId)
            .OrderBy(p => p.Competencia)
            .ToListAsync(ct).ConfigureAwait(false);

        return records.OrderBy(p => p.PostedAt).Select(IpBillingMapper.ToDomain).ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
