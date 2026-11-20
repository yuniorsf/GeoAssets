using GeoAssets.Workflow.Hosting;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>Whether this host's outbox relay publishes to a broker (<c>Workflow:Outbox:Transport</c>, XD01-357).</summary>
/// <param name="HasBroker">True when a broker is configured; false for <c>None</c> (the default).</param>
internal sealed record NeutralOperatorMessaging(bool HasBroker)
{
    public static NeutralOperatorMessaging From(IConfiguration configuration) =>
        new(WorkflowMessagingHostExtensions.HasBroker(configuration));
}
