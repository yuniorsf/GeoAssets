using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The persistence the kernel's asset-mutation consumer needs — the idempotent inbox, the outbox and the unit of work that
/// holds them together (XD01-383) — as in-memory test doubles, for tests of the host's wiring and routing that have no
/// database. Whatever the registrations before it made for those three is replaced.
/// </summary>
internal sealed class KernelApplyDoubles
{
    private KernelApplyDoubles()
    {
    }

    public RecordingIdempotentInbox Inbox { get; } = new();

    public RecordingOutboxWriter Outbox { get; } = new();

    public InMemoryWorkflowUnitOfWork UnitOfWork { get; } = new();

    public static KernelApplyDoubles Replace(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var doubles = new KernelApplyDoubles();
        services.RemoveAll<IIdempotentInbox>();
        services.RemoveAll<IOutboxWriter>();
        services.RemoveAll<IWorkflowUnitOfWork>();
        services.AddSingleton<IIdempotentInbox>(doubles.Inbox);
        services.AddSingleton<IOutboxWriter>(doubles.Outbox);
        services.AddSingleton<IWorkflowUnitOfWork>(doubles.UnitOfWork);
        return doubles;
    }
}
