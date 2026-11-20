using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// The tables that move, in foreign-key order (a table's rows are copied after every table they reference, and removed
/// before), with their columns and keys read from the two EF models the data belongs to — the kernel's
/// <see cref="ServiceOrderDbContext"/> and the vertical's <see cref="NeutralOperatorDbContext"/>. A table or column the
/// tool relies on that a model no longer maps fails here, at startup; <see cref="SchemaPreflight"/> then checks every
/// table's columns against both databases' catalogs, so a column neither model nor tool knows is never silently left out.
/// <para>Not moved, on purpose: order types, states, transitions, permissions and policies (NeutralOperator.Server builds
/// its order types from the module's code and overlays its own database's rows), the outbox's delivered history, the
/// inbox, and the organization-grant mirror and cursor.</para>
/// </summary>
internal static class MovedTableCatalog
{
    /// <summary>The schema of the kernel's workflow tables.</summary>
    public const string KernelSchema = "public";

    public const string ServiceOrders = "ServiceOrders";
    public const string ServiceOrderTargets = "ServiceOrderTargets";
    public const string OrderTypes = "OrderTypes";
    public const string OccupancyChangeSets = "ServiceOrderOccupancyChangeSets";
    public const string OccupancyApplySagas = "occupancy_apply_sagas";

    public const string IdColumn = "Id";
    public const string OrderTypeIdColumn = "OrderTypeId";
    public const string ParentOrderIdColumn = "ParentOrderId";
    public const string ServiceOrderIdColumn = "ServiceOrderId";
    public const string VerticalIdColumn = "VerticalId";
    public const string KernelOrderIdColumn = "KernelOrderId";
    public const string ExclusivityColumn = "Exclusivity";
    public const string PartitionKeyColumn = "PartitionKey";
    public const string MessageTypeColumn = "MessageType";
    public const string StatusColumn = "Status";
    public const string TargetIdColumn = "TargetId";

    /// <summary>The outbox's delivery-state columns: each relay's own bookkeeping, not part of the message.</summary>
    private static readonly string[] OutboxDeliveryColumns =
        [StatusColumn, "AttemptCount", "NextRetryAtUtc", "LockedUntilUtc", "LockedBy", "DispatchedAtUtc", "LastError"];

    /// <summary>The vertical's schema (<see cref="NeutralOperatorDbContext.Schema"/>), moved whole.</summary>
    public static string VerticalSchema => NeutralOperatorDbContext.Schema;

    /// <summary>The vertical chain's history table, in <see cref="VerticalSchema"/> — the one table there that does not move.</summary>
    public static string VerticalHistoryTable => NeutralOperatorDbContext.MigrationsHistoryTableName;

    public static IReadOnlyList<MovedTable> Build()
    {
        using var kernel = new ServiceOrderDbContext(new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql().Options);
        using var vertical = new NeutralOperatorDbContext(new DbContextOptionsBuilder<NeutralOperatorDbContext>().UseNpgsql().Options);
        var schema = VerticalSchema;
        var kernelModel = kernel.GetService<IDesignTimeModel>().Model;
        var verticalModel = vertical.GetService<IDesignTimeModel>().Model;

        return
        [
            Map(kernelModel, KernelSchema, ServiceOrders, MovedRowScope.OrderTree, [IdColumn, OrderTypeIdColumn, ParentOrderIdColumn]),
            Map(kernelModel, KernelSchema, "OrderDispatches", MovedRowScope.OrderSequence, [IdColumn, ServiceOrderIdColumn]),
            Map(kernelModel, KernelSchema, "OrderActionLogs", MovedRowScope.OrderSequence, [IdColumn, ServiceOrderIdColumn]),
            Map(kernelModel, KernelSchema, ServiceOrderTargets, MovedRowScope.OrderTargets,
                [IdColumn, ServiceOrderIdColumn, VerticalIdColumn, KernelOrderIdColumn, OrderTypeIdColumn, ExclusivityColumn]),
            Map(kernelModel, KernelSchema, "WorkflowOutboxMessages", MovedRowScope.PendingOutbox,
                [IdColumn, PartitionKeyColumn, MessageTypeColumn, .. OutboxDeliveryColumns]),
            Map(verticalModel, schema, "field_pole_occupancies", MovedRowScope.WholeTable, []),
            Map(verticalModel, schema, "field_pole_occupancy_entries", MovedRowScope.WholeTable, []),
            Map(verticalModel, schema, OccupancyApplySagas, MovedRowScope.WholeTable, [ServiceOrderIdColumn]),
            Map(verticalModel, schema, "occupancy_apply_saga_poles", MovedRowScope.WholeTable, []),
            Map(verticalModel, schema, OccupancyChangeSets, MovedRowScope.WholeTable, [ServiceOrderIdColumn, TargetIdColumn]),
        ];
    }

    private static MovedTable Map(IModel model, string schema, string name, MovedRowScope scope, string[] requiredColumns)
    {
        var entityType = model.GetEntityTypes().SingleOrDefault(e =>
                string.Equals(e.GetTableName(), name, StringComparison.Ordinal)
                && string.Equals(e.GetSchema() ?? KernelSchema, schema, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The EF model no longer maps {schema}.{name}: this tool needs updating.");

        var table = StoreObjectIdentifier.Table(name, entityType.GetSchema());
        var columns = entityType.GetProperties()
            .Select(p => p.GetColumnName(table))
            .OfType<string>()
            .ToList();
        var key = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"{schema}.{name} has no primary key in the EF model.");
        var keyColumns = key.Properties.Select(p => p.GetColumnName(table) ?? p.Name).ToList();

        var missing = requiredColumns.Where(c => !columns.Contains(c, StringComparer.Ordinal)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"The EF model of {schema}.{name} no longer maps {string.Join(", ", missing)}: this tool needs updating.");

        var copied = columns;
        var compared = columns;
        if (scope == MovedRowScope.OrderSequence)
        {
            if (key.Properties.Count != 1 || key.Properties[0].ValueGenerated != ValueGenerated.OnAdd)
                throw new InvalidOperationException($"{schema}.{name} was expected to be keyed by a generated identity.");

            copied = [.. columns.Where(c => !keyColumns.Contains(c, StringComparer.Ordinal))];
            compared = copied;
        }
        else if (scope == MovedRowScope.PendingOutbox)
        {
            compared = [.. columns.Where(c => !OutboxDeliveryColumns.Contains(c, StringComparer.Ordinal))];
        }

        var unique = entityType.GetIndexes()
            .Where(i => i.IsUnique)
            .Select(i => new UniqueIndex([.. i.Properties.Select(p => p.GetColumnName(table) ?? p.Name)], i.GetFilter()))
            .Concat(entityType.GetKeys()
                .Where(k => !k.IsPrimaryKey())
                .Select(k => new UniqueIndex([.. k.Properties.Select(p => p.GetColumnName(table) ?? p.Name)], null)))
            .ToList();

        return new MovedTable
        {
            Schema = schema,
            Name = name,
            Scope = scope,
            Columns = columns,
            KeyColumns = keyColumns,
            ComparedColumns = compared,
            CopiedColumns = copied,
            UniqueIndexes = unique,
        };
    }
}
