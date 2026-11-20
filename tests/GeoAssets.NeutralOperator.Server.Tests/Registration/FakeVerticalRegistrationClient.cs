using System.Collections.Concurrent;
using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Rest;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>The kernel's registration routes as the registration loop calls them: records each call, and fails the ones it is told to.</summary>
internal sealed class FakeVerticalRegistrationClient : IVerticalRegistrationClient
{
    public const string OrderTypes = "order-types";
    public const string Permissions = "permissions";

    private readonly ConcurrentQueue<(string Kind, string VerticalId, object Payload)> _calls = new();

    /// <summary>Decides, per call kind (<see cref="OrderTypes"/> or <see cref="Permissions"/>), whether it fails.</summary>
    public Func<string, bool> Fails { get; set; } = _ => false;

    public IReadOnlyList<(string Kind, string VerticalId, object Payload)> Calls => [.. _calls];

    public IReadOnlyList<string> Kinds => [.. _calls.Select(c => c.Kind)];

    public Task PublishOrderTypesAsync(string verticalId, IReadOnlyList<OrderTypeDescriptor> descriptors, CancellationToken ct) =>
        Record(OrderTypes, verticalId, descriptors, ct);

    public Task PublishPermissionsAsync(string verticalId, ModulePermissionSet permissions, CancellationToken ct) =>
        Record(Permissions, verticalId, permissions, ct);

    private Task Record(string kind, string verticalId, object payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _calls.Enqueue((kind, verticalId, payload));
        return Fails(kind)
            ? Task.FromException(new HttpRequestException($"The kernel refused {kind}.", null, System.Net.HttpStatusCode.ServiceUnavailable))
            : Task.CompletedTask;
    }
}
