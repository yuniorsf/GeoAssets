using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>A <see cref="VerticalRegistrationService"/> on a <see cref="FakeTimeProvider"/>, over a <see cref="FakeVerticalRegistrationClient"/>, started and stoppable.</summary>
internal sealed class RegistrationLoop : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly VerticalRegistrationService _service;

    public RegistrationLoop(VerticalRegistrationOptions? options = null, IEnumerable<IModulePermissionContributor>? extraContributors = null)
    {
        Client = new FakeVerticalRegistrationClient();
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        Options = options ?? new VerticalRegistrationOptions();
        State = new VerticalRegistrationState();
        OrderTypes = new OrderTypeRegistry();
        NeutralOperatorModule.RegisterOrderTypes(OrderTypes);

        _provider = new ServiceCollection().AddScoped<IVerticalRegistrationClient>(_ => Client).BuildServiceProvider();
        _service = new VerticalRegistrationService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            OrderTypes,
            [new NeutralOperatorPermissionContributor(), .. extraContributors ?? []],
            State,
            Microsoft.Extensions.Options.Options.Create(Options),
            Time,
            NullLogger<VerticalRegistrationService>.Instance);
    }

    public FakeVerticalRegistrationClient Client { get; }

    public FakeTimeProvider Time { get; }

    public VerticalRegistrationOptions Options { get; }

    public VerticalRegistrationState State { get; }

    public OrderTypeRegistry OrderTypes { get; }

    public Task StartAsync() => _service.StartAsync(CancellationToken.None);

    public Task StopAsync() => _service.StopAsync(CancellationToken.None);

    /// <summary>Waits (in real time, up to ten seconds) until <paramref name="condition"/> holds; the loop itself runs on the fake clock.</summary>
    public static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = Task.Delay(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (deadline.IsCompleted) throw new TimeoutException("The registration loop did not reach the expected state in time.");
            await Task.Delay(5);
        }
    }

    /// <summary>Lets the loop run what is runnable at the current fake instant, then reports how many kernel calls it made.</summary>
    public async Task<int> SettledCallsAsync()
    {
        var before = -1;
        var after = Client.Calls.Count;
        while (before != after)
        {
            before = after;
            await Task.Delay(40);
            after = Client.Calls.Count;
        }

        return after;
    }

    public async ValueTask DisposeAsync()
    {
        await _service.StopAsync(CancellationToken.None);
        _service.Dispose();
        await _provider.DisposeAsync();
    }
}
