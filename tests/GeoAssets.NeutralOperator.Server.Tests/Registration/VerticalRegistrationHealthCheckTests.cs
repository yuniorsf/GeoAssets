using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Registration;

/// <summary>The <c>vertical-registration</c> health check (XD01-382): Degraded until both registrations succeeded, Healthy after.</summary>
public sealed class VerticalRegistrationHealthCheckTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static Task<HealthCheckResult> CheckAsync(VerticalRegistrationState state) =>
        new VerticalRegistrationHealthCheck(state).CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Pending_IsDegraded_AndSaysItIsPending()
    {
        var result = await CheckAsync(new VerticalRegistrationState());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("pending");
        result.Data["status"].Should().Be("Pending");
        result.Data["consecutiveFailures"].Should().Be(0);
    }

    [Fact]
    public async Task Failing_IsDegraded_NotUnhealthy_AndSaysWhy()
    {
        var state = new VerticalRegistrationState();
        state.Failed("503 from the kernel");
        state.Failed("503 from the kernel");

        var result = await CheckAsync(state);

        result.Status.Should().Be(HealthStatus.Degraded, "a kernel outage must never restart this host");
        result.Description.Should().Contain("503 from the kernel");
        result.Data["status"].Should().Be("Failing");
        result.Data["consecutiveFailures"].Should().Be(2);
    }

    [Fact]
    public async Task Registered_IsHealthy_WithWhenItRegistered()
    {
        var state = new VerticalRegistrationState();
        state.Failed("not yet");
        state.Registered(At);

        var result = await CheckAsync(state);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["status"].Should().Be("Registered");
        result.Data["consecutiveFailures"].Should().Be(0);
        result.Data["registeredAt"].Should().Be("2026-11-20T12:00:00.0000000+00:00");
    }

    [Fact]
    public void TheCheck_IsNamedAsTheOperationalContractSays_AndTaggedForReadiness()
    {
        VerticalRegistrationHealthCheck.Name.Should().Be("vertical-registration");
        VerticalRegistrationHealthCheck.ReadinessTag.Should().Be("ready");
    }
}
