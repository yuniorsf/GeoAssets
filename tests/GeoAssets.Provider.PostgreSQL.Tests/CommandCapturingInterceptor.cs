using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Records the SQL text of every command EF Core sends to Postgres, so a test can assert on
/// *how many* round-trips a query made and what each one looked like — proof a paged query
/// stayed server-side instead of materializing rows into .NET first.
/// </summary>
internal sealed class CommandCapturingInterceptor : DbCommandInterceptor
{
    public List<string> CommandTexts { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CommandTexts.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        CommandTexts.Add(command.CommandText);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
