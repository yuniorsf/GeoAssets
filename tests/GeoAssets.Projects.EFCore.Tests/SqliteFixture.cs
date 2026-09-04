using GeoAssets.Projects.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Projects.EFCore.Tests;

/// <summary>
/// Real SQLite database backing a single held-open connection — SQLite drops a
/// "Data Source=:memory:" database once the last connection to it closes, so this keeps one
/// alive for the fixture's lifetime. Real SQLite (not the <c>Microsoft.EntityFrameworkCore.InMemory</c>
/// provider) is used so query translation — including <c>ProjectRowConfiguration</c>'s
/// <c>HasQueryFilter</c> soft-delete filter — is actually exercised, matching
/// <c>GeoAssets.Workflow.EFCore.Tests</c>'s choice of provider for the same reason.
/// </summary>
public sealed class SqliteFixture : IDisposable
{
    private readonly SqliteConnection _connection;

    public ProjectDbContext Context { get; }

    public SqliteFixture()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        Context = NewContext();
        Context.Database.EnsureCreated();
    }

    /// <summary>A second <see cref="ProjectDbContext"/> bound to the same connection/schema —
    /// its own change tracker is independent of <see cref="Context"/>'s.</summary>
    public ProjectDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ProjectDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ProjectDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
