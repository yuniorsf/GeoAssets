using GeoAssets.Core.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Workflow.Messaging.Protobuf;

/// <summary>
/// Dependency injection registration helpers for Protocol Buffers integration messaging serialization.
/// </summary>
public static class WorkflowProtobufServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ProtobufIntegrationMessageSerializer"/> as the singleton
    /// <see cref="IIntegrationMessageSerializer"/> in the service collection.
    /// </summary>
    public static IServiceCollection AddWorkflowProtobufMessaging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ProtobufIntegrationMessageSerializer>();
        services.TryAddSingleton<IIntegrationMessageSerializer>(
            static sp => sp.GetRequiredService<ProtobufIntegrationMessageSerializer>());

        return services;
    }
}

