using System.Globalization;
using GeoAssets.Core.Rendering;
using GeoAssets.Shared.Services.ReferenceLayers;

namespace GeoAssets.Shared.Tests.Services.ReferenceLayers;

/// <summary>
/// The reference-features route: records every request and answers through <see cref="Respond"/> (by default
/// <see cref="Body"/> for the organization and box, so a test can tell which answer was drawn).
/// </summary>
internal sealed class FakeReferenceFeatureClient : IReferenceFeatureClient
{
    private readonly Lock _gate = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];

    public List<(Guid OrganizationId, BoundingBox Bbox, CancellationToken Token)> Requests { get; } = [];

    public Func<Guid, BoundingBox, CancellationToken, Task<ReferenceFeatureResponse>> Respond { get; set; } =
        (organizationId, bbox, _) => Task.FromResult(ReferenceFeatureResponse.Granted(Body(organizationId, bbox)));

    public static string Body(Guid organizationId, BoundingBox bbox) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""[{"id":"{{organizationId:N}}-{{bbox.MinLon}}","geometry":{"type":"Point","coordinates":[{{bbox.MinLon}},{{bbox.MinLat}}]},"label":"L"}]""");

    public Task<ReferenceFeatureResponse> GetAsync(Guid organizationId, BoundingBox bbox, CancellationToken ct)
    {
        lock (_gate)
        {
            Requests.Add((organizationId, bbox, ct));
            foreach (var waiter in _waiters.Where(w => Requests.Count >= w.Count).ToList())
            {
                _waiters.Remove(waiter);
                waiter.Signal.TrySetResult();
            }
        }

        return Respond(organizationId, bbox, ct);
    }

    /// <summary>
    /// Completes once <paramref name="count"/> requests have been made. A load that follows a debounce resumes wherever
    /// the timer's continuation runs, so a test waits for it rather than assuming it already happened.
    /// </summary>
    public Task WhenRequestedAsync(int count)
    {
        lock (_gate)
        {
            if (Requests.Count >= count) return Task.CompletedTask;
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((count, signal));
            return signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}
