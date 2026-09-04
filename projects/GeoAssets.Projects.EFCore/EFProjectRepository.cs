using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Projects.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Projects.Persistence;

/// <summary>EF Core implementation of <see cref="IProjectRepository"/>.</summary>
public sealed class EFProjectRepository : IProjectRepository, IAsyncDisposable
{
    private readonly ProjectDbContext _db;

    public EFProjectRepository(ProjectDbContext db) => _db = db;

    // ── Events ────────────────────────────────────────────────────────────────

    public event EventHandler<Project>? ProjectSaved;
    public event EventHandler<Guid>?    ProjectDeleted;

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        return row is null ? null : ProjectMapper.ToDomain(row);
    }

    public Task<IReadOnlyList<Project>> GetAllAsync(CancellationToken ct = default)
        => LoadAllAsync(_db.Projects, ct);

    public Task<IReadOnlyList<Project>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => LoadAllAsync(_db.Projects.Where(p => p.OrganizationId == organizationId), ct);

    public Task<IReadOnlyList<Project>> GetForksOfAsync(Guid parentProjectId, CancellationToken ct = default)
        => LoadAllAsync(_db.Projects.Where(p => p.ParentProjectId == parentProjectId), ct);

    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task AddAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Add(ProjectMapper.ToRow(project));
        await _db.SaveChangesAsync(ct);

        ProjectSaved?.Invoke(this, project);
    }

    public async Task UpdateAsync(Project project, CancellationToken ct = default)
    {
        var existing = await _db.Projects.FirstOrDefaultAsync(p => p.Id == project.Id, ct)
            ?? throw new KeyNotFoundException($"Project '{project.Id}' not found.");

        var updated = ProjectMapper.ToRow(project);
        existing.Name               = updated.Name;
        existing.Description        = updated.Description;
        existing.UpdatedAt          = updated.UpdatedAt;
        existing.SchemaVersion      = updated.SchemaVersion;
        existing.ProvidersJson      = updated.ProvidersJson;
        existing.AssetTypeScopeJson = updated.AssetTypeScopeJson;
        existing.LayerScopeJson     = updated.LayerScopeJson;
        existing.ViewStateJson      = updated.ViewStateJson;

        await _db.SaveChangesAsync(ct);

        ProjectSaved?.Invoke(this, project);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (row is null) return;

        row.IsDeleted = true;
        row.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        ProjectDeleted?.Invoke(this, id);
    }

    // ── Async disposal ────────────────────────────────────────────────────────

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<Project>> LoadAllAsync(
        IQueryable<ProjectRow> query, CancellationToken ct)
    {
        var rows = await query.OrderBy(p => p.CreatedAt).ToListAsync(ct);
        return rows.Select(ProjectMapper.ToDomain).ToList();
    }
}
