using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Server;
using GeoAssets.Workflow.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>
/// XD01-395: <see cref="MunicipioGrantSyncDispatcher"/> over a fake outbox store, a fake kernel client and a
/// <see cref="FakeTimeProvider"/> — backoff, give-up, failure text, call shapes, metrics, options and the polling loop.
/// </summary>
public sealed class MunicipioGrantSyncDispatcherTests
{
    private const string Ibge = "3550308";
    private const string OldIbge = "3304557";

    private static readonly Guid Distribuidora = new("d0d0d0d0-0000-4000-8000-000000000395");
    private static readonly Guid OldDistribuidora = new("d1d1d1d1-0000-4000-8000-000000000395");
    private static readonly Guid PrefeituraA = new("a0a0a0a0-0000-4000-8000-000000000395");
    private static readonly Guid PrefeituraB = new("b0b0b0b0-0000-4000-8000-000000000395");

    private sealed class FakeStore : IMunicipioGrantSyncStore
    {
        private int _claims;

        public Queue<IReadOnlyList<MunicipioGrantSyncWorkItem>> Batches { get; } = new();

        public Exception? ClaimFailure { get; set; }

        public bool LeaseHeld { get; set; } = true;

        public int Claims => Volatile.Read(ref _claims);

        public List<(string Worker, int BatchSize, TimeSpan Lease)> ClaimArguments { get; } = [];

        public List<long> Completed { get; } = [];

        public List<(long Id, string Failure, DateTimeOffset NextAttemptAt)> Retries { get; } = [];

        public List<(long Id, string Failure)> Failed { get; } = [];

        public Task<IReadOnlyList<MunicipioGrantSyncWorkItem>> ClaimDueAsync(int batchSize, TimeSpan leaseDuration, string workerId, CancellationToken ct = default)
        {
            ClaimArguments.Add((workerId, batchSize, leaseDuration));
            Interlocked.Increment(ref _claims);
            if (ClaimFailure is not null) return Task.FromException<IReadOnlyList<MunicipioGrantSyncWorkItem>>(ClaimFailure);
            return Task.FromResult(Batches.Count > 0 ? Batches.Dequeue() : []);
        }

        public Task<bool> MarkCompletedAsync(long id, string workerId, CancellationToken ct = default)
        {
            if (LeaseHeld) Completed.Add(id);
            return Task.FromResult(LeaseHeld);
        }

        public Task<bool> ScheduleRetryAsync(long id, string workerId, string failure, DateTimeOffset nextAttemptAt, CancellationToken ct = default)
        {
            if (LeaseHeld) Retries.Add((id, failure, nextAttemptAt));
            return Task.FromResult(LeaseHeld);
        }

        public Task<bool> MarkFailedAsync(long id, string workerId, string failure, CancellationToken ct = default)
        {
            if (LeaseHeld) Failed.Add((id, failure));
            return Task.FromResult(LeaseHeld);
        }

        public Task<int> PruneAsync(DateTimeOffset completedBefore, DateTimeOffset? failedBefore, int batchSize, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class FakeGrantClient : IOrganizationGrantClient
    {
        public List<string> Calls { get; } = [];

        public List<OrganizationGrantUpsertRequest> Upserts { get; } = [];

        public List<OrganizationGrantKey> Revokes { get; } = [];

        public Exception? Failure { get; set; }

        public Task<OrganizationGrantUpsertResult> UpsertAsync(OrganizationGrantUpsertRequest request, CancellationToken ct)
        {
            Calls.Add("upsert");
            Upserts.Add(request);
            if (Failure is not null) return Task.FromException<OrganizationGrantUpsertResult>(Failure);

            var grant = new OrganizationGrantDto(
                Guid.NewGuid(), request.Key.GranteeOrganizationId, request.Key.ResourceOrganizationId, request.Key.ResourceType, request.Key.ScopeId,
                request.AllowedActions, request.RequiredRole, request.ExpiresAt, request.GrantedBy, DateTime.UnixEpoch, true);
            return Task.FromResult(new OrganizationGrantUpsertResult(grant, OrganizationGrantUpsertOutcome.Created));
        }

        public Task<OrganizationGrantRevokeResult> RevokeAsync(OrganizationGrantKey key, CancellationToken ct)
        {
            Calls.Add("revoke");
            Revokes.Add(key);
            return Failure is not null
                ? Task.FromException<OrganizationGrantRevokeResult>(Failure)
                : Task.FromResult(new OrganizationGrantRevokeResult(1));
        }

        public Task<OrganizationGrantPage> ListActiveAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, int page, int pageSize, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;

        public Fixture(MunicipioGrantSyncOptions? options = null)
        {
            Options = options ?? new MunicipioGrantSyncOptions();
            var services = new ServiceCollection();
            services.AddMetrics();
            services.AddSingleton<IMunicipioGrantSyncStore>(Store);
            services.AddSingleton<IOrganizationGrantClient>(Client);
            services.AddScoped<MunicipioProvisioningService>();
            _services = services.BuildServiceProvider();
            Tracer = new GeoAssetsActivitySource("test");
            Metrics = new MunicipioGrantSyncMetrics(_services.GetRequiredService<IMeterFactory>());
            Dispatcher = new MunicipioGrantSyncDispatcher(
                _services.GetRequiredService<IServiceScopeFactory>(), Microsoft.Extensions.Options.Options.Create(Options), Time, Tracer, Metrics,
                NullLogger<MunicipioGrantSyncDispatcher>.Instance);
        }

        public MunicipioGrantSyncOptions Options { get; }

        public FakeStore Store { get; } = new();

        public FakeGrantClient Client { get; } = new();

        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

        public GeoAssetsActivitySource Tracer { get; }

        public MunicipioGrantSyncMetrics Metrics { get; }

        public MunicipioGrantSyncDispatcher Dispatcher { get; }

        public void Dispose()
        {
            Dispatcher.Dispose();
            Tracer.Dispose();
            _services.Dispose();
        }
    }

    private static MunicipioGrantSyncWorkItem Item(MunicipioGrantChange change, int attempts = 0, long id = 1) =>
        new(id, change, "requester-1", new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero), attempts);

    private static MunicipioGrantChange SyncChange(Guid? previousPrefeitura = null, string? previousIbge = null, Guid? previousDistribuidora = null) =>
        new(Guid.NewGuid(), MunicipioGrantSyncKind.Sync, Ibge, Distribuidora, PrefeituraA, previousPrefeitura, previousIbge, previousDistribuidora);

    private static OrganizationGrantKey Key(Guid prefeitura, Guid distribuidora, string ibge) => new(prefeitura, distribuidora, ResourceType: null, ScopeId: ibge);

    // ── backoff ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(7, 300)]
    public void Backoff_DoublesFromTheInitialBackoff_JitteredTo50To100Percent(int failedAttempts, double expectedSeconds)
    {
        var options = new MunicipioGrantSyncOptions();

        for (var i = 0; i < 300; i++)
        {
            var delay = MunicipioGrantSyncDispatcher.Backoff(options, failedAttempts);
            delay.TotalSeconds.Should().BeInRange(expectedSeconds * 0.5, expectedSeconds);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(20)]
    [InlineData(1000)]
    public void Backoff_NeverExceedsTheMaxBackoff_AndDoesNotOverflow(int failedAttempts)
    {
        var options = new MunicipioGrantSyncOptions();

        for (var i = 0; i < 300; i++)
        {
            var delay = MunicipioGrantSyncDispatcher.Backoff(options, failedAttempts);
            delay.Should().BeLessThanOrEqualTo(options.MaxBackoff).And.BeGreaterThanOrEqualTo(options.MaxBackoff / 2);
        }
    }

    [Fact]
    public void Backoff_ForZeroFailedAttempts_IsTheInitialBackoffRange()
    {
        var options = new MunicipioGrantSyncOptions();

        MunicipioGrantSyncDispatcher.Backoff(options, 0).TotalSeconds.Should().BeInRange(2.5, 5);
    }

    // ── what a cycle does with an outcome ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchOnce_ASentRequest_IsMarkedCompleted_AndCountedSynced()
    {
        using var fixture = new Fixture();
        fixture.Store.Batches.Enqueue([Item(SyncChange(), id: 7)]);

        var outcomes = new List<string>();
        using var listener = Listen(outcomes);
        var claimed = await fixture.Dispatcher.DispatchOnceAsync(CancellationToken.None);

        claimed.Should().Be(1);
        fixture.Store.Completed.Should().Equal(7);
        fixture.Store.Retries.Should().BeEmpty();
        outcomes.Should().Equal("synced");
        fixture.Store.ClaimArguments.Should().ContainSingle()
            .Which.Should().Match<(string Worker, int BatchSize, TimeSpan Lease)>(a => a.BatchSize == 20 && a.Lease == TimeSpan.FromMinutes(2) && a.Worker.Length > 0);
    }

    [Fact]
    public async Task DispatchOnce_AFailedAttempt_SchedulesTheRetryAfterTheBackoff_AndCountsRetry()
    {
        using var fixture = new Fixture();
        fixture.Client.Failure = new HttpRequestException("kernel unreachable");
        fixture.Store.Batches.Enqueue([Item(SyncChange(), attempts: 2, id: 9)]);

        var outcomes = new List<string>();
        using var listener = Listen(outcomes);
        await fixture.Dispatcher.DispatchOnceAsync(CancellationToken.None);

        var retry = fixture.Store.Retries.Should().ContainSingle().Subject;
        retry.Id.Should().Be(9);
        retry.Failure.Should().Be("HttpRequestException: kernel unreachable");
        (retry.NextAttemptAt - fixture.Time.GetUtcNow()).TotalSeconds.Should().BeInRange(10, 20, "the third failed attempt waits 5 s doubled twice, jittered");
        fixture.Store.Completed.Should().BeEmpty();
        fixture.Store.Failed.Should().BeEmpty();
        outcomes.Should().Equal("retry");
    }

    [Fact]
    public async Task DispatchOnce_TheAttemptReachingMaxAttempts_IsMarkedFailed_AndCountsFailed()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { MaxAttempts = 3 });
        fixture.Client.Failure = new InvalidOperationException("still down");
        fixture.Store.Batches.Enqueue([Item(SyncChange(), attempts: 2, id: 11)]);

        var outcomes = new List<string>();
        using var listener = Listen(outcomes);
        await fixture.Dispatcher.DispatchOnceAsync(CancellationToken.None);

        fixture.Store.Failed.Should().ContainSingle().Which.Should().Be((11L, "InvalidOperationException: still down"));
        fixture.Store.Retries.Should().BeEmpty();
        outcomes.Should().Equal("failed");
    }

    [Fact]
    public async Task DispatchOnce_TheAttemptBeforeMaxAttempts_IsStillRetried()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { MaxAttempts = 3 });
        fixture.Client.Failure = new InvalidOperationException("down");
        fixture.Store.Batches.Enqueue([Item(SyncChange(), attempts: 1)]);

        await fixture.Dispatcher.DispatchOnceAsync(CancellationToken.None);

        fixture.Store.Retries.Should().ContainSingle();
        fixture.Store.Failed.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchOnce_WithMaxAttemptsOne_FailsTheFirstFailureAtOnce()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { MaxAttempts = 1 });
        fixture.Client.Failure = new InvalidOperationException("down");
        fixture.Store.Batches.Enqueue([Item(SyncChange(), attempts: 0)]);

        await fixture.Dispatcher.DispatchOnceAsync(CancellationToken.None);

        fixture.Store.Failed.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchOnce_WhenTheLeaseWasLost_RecordsNoOutcomeAndCountsNothing()
    {
        using var fixture = new Fixture();
        fixture.Store.LeaseHeld = false;
        fixture.Store.Batches.Enqueue([Item(SyncChange()), Item(SyncChange(), id: 2)]);

        var outcomes = new List<string>();
        using var listener = Listen(outcomes);
        await fixture.Dispatcher.DispatchOnceAsync(CancellationToken.None);

        outcomes.Should().BeEmpty();
        fixture.Store.Completed.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchOnce_WhenTheLeaseWasLostOnAFailure_CountsNothing()
    {
        using var fixture = new Fixture();
        fixture.Store.LeaseHeld = false;
        fixture.Client.Failure = new InvalidOperationException("down");
        fixture.Store.Batches.Enqueue([Item(SyncChange())]);

        var outcomes = new List<string>();
        using var listener = Listen(outcomes);
        await fixture.Dispatcher.DispatchOnceAsync(CancellationToken.None);

        outcomes.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchOnce_ABatch_FailureOfOneRequestDoesNotStopTheOthers()
    {
        using var fixture = new Fixture();
        fixture.Client.Failure = new HttpRequestException("down");
        fixture.Store.Batches.Enqueue([Item(SyncChange(), id: 1), Item(SyncChange(), id: 2)]);

        var claimed = await fixture.Dispatcher.DispatchOnceAsync(CancellationToken.None);

        claimed.Should().Be(2);
        fixture.Store.Retries.Select(r => r.Id).Should().Equal(1, 2);
    }

    [Fact]
    public async Task DispatchOnce_ACancelledCall_Propagates_AndRecordsNoFailure()
    {
        using var fixture = new Fixture();
        using var cts = new CancellationTokenSource();
        fixture.Store.Batches.Enqueue([Item(SyncChange())]);
        await cts.CancelAsync();

        var act = () => fixture.Dispatcher.DispatchOnceAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Store.Retries.Should().BeEmpty();
        fixture.Store.Failed.Should().BeEmpty();
    }

    private static MeterListener Listen(List<string> outcomes)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Name, MunicipioGrantSyncMetrics.CounterName, StringComparison.Ordinal)) l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                if (string.Equals(tag.Key, "outcome", StringComparison.Ordinal)) outcomes.Add((string)tag.Value!);
        });
        listener.Start();
        return listener;
    }

    // ── failure text ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DescribeFailure_IsTheTypeAndMessage()
    {
        MunicipioGrantSyncDispatcher.DescribeFailure(new HttpRequestException("boom"))
            .Should().Be("HttpRequestException: boom");
    }

    [Fact]
    public void DescribeFailure_WithoutAMessage_IsTheTypeName()
    {
        MunicipioGrantSyncDispatcher.DescribeFailure(new HttpRequestException(" ")).Should().Be(nameof(HttpRequestException));
    }

    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOi.payload-part_1.sig+/= rejected")]
    [InlineData("rejected bearer   abc123")]
    public void DescribeFailure_RedactsBearerTokens(string message)
    {
        var text = MunicipioGrantSyncDispatcher.DescribeFailure(new InvalidOperationException(message));

        text.Should().Contain("Bearer [redacted]");
        text.Should().NotContain("eyJhbGciOi").And.NotContain("abc123").And.NotContain("payload-part_1");
    }

    [Fact]
    public void DescribeFailure_TruncatesTo1024Characters()
    {
        var text = MunicipioGrantSyncDispatcher.DescribeFailure(new InvalidOperationException(new string('x', 5000)));

        text.Should().HaveLength(1024).And.StartWith("InvalidOperationException: x");
    }

    // ── call shapes ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_Revoke_RevokesItsKeyOnly()
    {
        var client = new FakeGrantClient();
        var change = new MunicipioGrantChange(Guid.NewGuid(), MunicipioGrantSyncKind.Revoke, Ibge, Distribuidora, PrefeituraA);

        await MunicipioGrantSyncDispatcher.ApplyAsync(new MunicipioProvisioningService(client), change, "u", CancellationToken.None);

        client.Calls.Should().Equal("revoke");
        client.Revokes.Should().Equal(Key(PrefeituraA, Distribuidora, Ibge));
    }

    [Fact]
    public async Task Apply_Sync_UpsertsTheKey_GrantedByTheRequester()
    {
        var client = new FakeGrantClient();

        await MunicipioGrantSyncDispatcher.ApplyAsync(new MunicipioProvisioningService(client), SyncChange(), "requester-1", CancellationToken.None);

        client.Calls.Should().Equal("upsert");
        var request = client.Upserts.Should().ContainSingle().Subject;
        request.Key.Should().Be(Key(PrefeituraA, Distribuidora, Ibge));
        request.GrantedBy.Should().Be("requester-1");
    }

    [Fact]
    public async Task Apply_Sync_WithAPreviousPrefeitura_RevokesTheOldOrganizationsGrantThenUpserts()
    {
        var client = new FakeGrantClient();

        await MunicipioGrantSyncDispatcher.ApplyAsync(new MunicipioProvisioningService(client), SyncChange(previousPrefeitura: PrefeituraB), "u", CancellationToken.None);

        client.Calls.Should().Equal("revoke", "upsert");
        client.Revokes.Should().Equal(Key(PrefeituraB, Distribuidora, Ibge));
    }

    [Fact]
    public async Task Apply_Sync_WithAPreviousIbgeCode_RevokesTheOldKeyThenUpsertsTheNew_WithoutRevokingTwice()
    {
        var client = new FakeGrantClient();
        var change = SyncChange(previousIbge: OldIbge, previousDistribuidora: OldDistribuidora);

        await MunicipioGrantSyncDispatcher.ApplyAsync(new MunicipioProvisioningService(client), change, "u", CancellationToken.None);

        client.Calls.Should().Equal("revoke", "upsert");
        client.Revokes.Should().Equal(Key(PrefeituraA, OldDistribuidora, OldIbge));
        client.Upserts.Single().Key.Should().Be(Key(PrefeituraA, Distribuidora, Ibge));
    }

    [Fact]
    public async Task Apply_Sync_WithAPreviousIbgeCodeAndPrefeitura_RevokesTheOldKeyOfTheOldPrefeitura_ThenUpserts()
    {
        var client = new FakeGrantClient();
        var change = SyncChange(previousPrefeitura: PrefeituraB, previousIbge: OldIbge);

        await MunicipioGrantSyncDispatcher.ApplyAsync(new MunicipioProvisioningService(client), change, "u", CancellationToken.None);

        client.Calls.Should().Equal("revoke", "upsert");
        client.Revokes.Should().Equal(Key(PrefeituraB, Distribuidora, OldIbge));
    }

    [Fact]
    public async Task Apply_Sync_WithAPreviousIbgeCodeButNoPreviousDistribuidora_UsesTheCurrentDistribuidoraForTheOldKey()
    {
        var client = new FakeGrantClient();

        await MunicipioGrantSyncDispatcher.ApplyAsync(new MunicipioProvisioningService(client), SyncChange(previousIbge: OldIbge), "u", CancellationToken.None);

        client.Revokes.Should().Equal(Key(PrefeituraA, Distribuidora, OldIbge));
    }

    // ── options ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Options_Defaults_AreTheDocumentedOnes_AndValid()
    {
        var options = new MunicipioGrantSyncOptions();

        options.Enabled.Should().BeTrue();
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(5));
        options.BatchSize.Should().Be(20);
        options.LeaseDuration.Should().Be(TimeSpan.FromMinutes(2));
        options.InitialBackoff.Should().Be(TimeSpan.FromSeconds(5));
        options.MaxBackoff.Should().Be(TimeSpan.FromMinutes(5));
        options.MaxAttempts.Should().Be(20);
        MunicipioGrantSyncOptions.IsValid(options).Should().BeTrue();
    }

    private static MunicipioGrantSyncOptions InvalidOptionsFor(string reason) => reason switch
    {
        "zero PollInterval" => new MunicipioGrantSyncOptions { PollInterval = TimeSpan.Zero },
        "negative PollInterval" => new MunicipioGrantSyncOptions { PollInterval = TimeSpan.FromSeconds(-1) },
        "zero BatchSize" => new MunicipioGrantSyncOptions { BatchSize = 0 },
        "zero LeaseDuration" => new MunicipioGrantSyncOptions { LeaseDuration = TimeSpan.Zero },
        "zero InitialBackoff" => new MunicipioGrantSyncOptions { InitialBackoff = TimeSpan.Zero },
        "MaxBackoff below InitialBackoff" => new MunicipioGrantSyncOptions { InitialBackoff = TimeSpan.FromSeconds(10), MaxBackoff = TimeSpan.FromSeconds(9) },
        "zero MaxAttempts" => new MunicipioGrantSyncOptions { MaxAttempts = 0 },
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    [Theory]
    [InlineData("zero PollInterval")]
    [InlineData("negative PollInterval")]
    [InlineData("zero BatchSize")]
    [InlineData("zero LeaseDuration")]
    [InlineData("zero InitialBackoff")]
    [InlineData("MaxBackoff below InitialBackoff")]
    [InlineData("zero MaxAttempts")]
    public void Options_Invalid_AreRejected(string reason)
    {
        MunicipioGrantSyncOptions.IsValid(InvalidOptionsFor(reason)).Should().BeFalse(reason);
    }

    [Fact]
    public void Options_MaxBackoffEqualToInitialBackoff_IsValid()
    {
        MunicipioGrantSyncOptions.IsValid(new MunicipioGrantSyncOptions { InitialBackoff = TimeSpan.FromSeconds(3), MaxBackoff = TimeSpan.FromSeconds(3) }).Should().BeTrue();
    }

    [Fact]
    public void Options_Null_IsRejected()
    {
        MunicipioGrantSyncOptions.IsValid(null!).Should().BeFalse();
    }

    [Fact]
    public void AddMunicipioGrantSync_BindsTheGrantSyncSection_AndRegistersTheDispatcher()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GrantSync:PollInterval"] = "00:00:01",
            ["GrantSync:BatchSize"] = "7",
            ["GrantSync:MaxAttempts"] = "3",
        }).Build();
        var services = new ServiceCollection();

        services.AddMunicipioGrantSync(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<MunicipioGrantSyncOptions>>().Value;
        options.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
        options.BatchSize.Should().Be(7);
        options.MaxAttempts.Should().Be(3);
        options.Enabled.Should().BeTrue();
        services.Should().Contain(d => d.ImplementationType == typeof(MunicipioGrantSyncDispatcher));
    }

    [Fact]
    public void AddMunicipioGrantSync_InvalidSettings_FailTheOptionsValidation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["GrantSync:BatchSize"] = "0" }).Build();
        var services = new ServiceCollection();
        services.AddMunicipioGrantSync(configuration);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<MunicipioGrantSyncOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*GrantSync*");
    }

    // ── the polling loop ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_WhenDisabled_NeverClaimsAnything()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { Enabled = false });

        await fixture.Dispatcher.StartAsync(CancellationToken.None);
        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(50);
        await fixture.Dispatcher.StopAsync(CancellationToken.None);

        fixture.Store.Claims.Should().Be(0);
    }

    [Fact]
    public async Task Run_ClaimsAtStart_ThenOnceEveryPollInterval()
    {
        using var fixture = new Fixture();

        await fixture.Dispatcher.StartAsync(CancellationToken.None);
        await UntilAsync(() => fixture.Store.Claims >= 1, fixture);
        var afterStart = fixture.Store.Claims;
        await UntilAsync(() => fixture.Store.Claims > afterStart, fixture, fixture.Options.PollInterval);
        await fixture.Dispatcher.StopAsync(CancellationToken.None);

        afterStart.Should().Be(1);
        fixture.Store.Claims.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Run_AFullBatch_IsFollowedByAnotherClaimWithoutWaitingForThePollInterval()
    {
        using var fixture = new Fixture(new MunicipioGrantSyncOptions { BatchSize = 2 });
        fixture.Store.Batches.Enqueue([Item(SyncChange(), id: 1), Item(SyncChange(), id: 2)]);

        await fixture.Dispatcher.StartAsync(CancellationToken.None);
        await UntilAsync(() => fixture.Store.Claims >= 2, fixture);
        await fixture.Dispatcher.StopAsync(CancellationToken.None);

        fixture.Store.Completed.Should().Equal(1, 2);
        fixture.Time.GetUtcNow().Should().Be(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero), "no time had to pass");
    }

    [Fact]
    public async Task Run_WhenTheStoreFails_KeepsRunning_AndClaimsAgainAfterABackoff()
    {
        using var fixture = new Fixture();
        fixture.Store.ClaimFailure = new InvalidOperationException("database down");

        await fixture.Dispatcher.StartAsync(CancellationToken.None);
        await UntilAsync(() => fixture.Store.Claims >= 1, fixture);
        fixture.Store.ClaimFailure = null;
        var afterFailure = fixture.Store.Claims;
        await UntilAsync(() => fixture.Store.Claims > afterFailure, fixture, fixture.Options.MaxBackoff);
        await fixture.Dispatcher.StopAsync(CancellationToken.None);

        fixture.Store.Claims.Should().BeGreaterThan(afterFailure);
    }

    /// <summary>Waits (bounded) for <paramref name="condition"/>, advancing the fake clock by <paramref name="step"/> between looks.</summary>
    private static async Task UntilAsync(Func<bool> condition, Fixture fixture, TimeSpan? step = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("The dispatcher did not reach the expected state in time.");
            if (step is { } advance) fixture.Time.Advance(advance);
            await Task.Delay(5);
        }
    }
}
