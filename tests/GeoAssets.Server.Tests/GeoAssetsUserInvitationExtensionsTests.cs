using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

public class GeoAssetsUserInvitationExtensionsTests
{
    private sealed class RecordingUserInvitationProvider : IUserInvitationProvider
    {
        public Task<string> CreateInvitedAccountAsync(string email, string displayName, CancellationToken ct = default)
            => Task.FromResult("external-oid");
        public Task RevokeInvitedAccountAsync(string externalObjectId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration InvitationConfiguration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Invitation:Enabled"]   = enabled.ToString(),
                ["RoleSync:TenantId"]     = "11111111-2222-3333-4444-555555555555",
                ["RoleSync:ClientId"]      = "66666666-7777-8888-9999-aaaaaaaaaaaa",
                ["RoleSync:ClientSecret"]  = "not-a-real-secret",
            })
            .Build();

    private static IConfiguration BothFeaturesEnabledConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RoleSync:Enabled"]                       = "true",
                ["RoleSync:TenantId"]                       = "11111111-2222-3333-4444-555555555555",
                ["RoleSync:ClientId"]                        = "66666666-7777-8888-9999-aaaaaaaaaaaa",
                ["RoleSync:ClientSecret"]                    = "not-a-real-secret",
                ["RoleSync:TargetApplicationClientIds:0"]    = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
                ["Invitation:Enabled"]                       = "true",
            })
            .Build();

    [Fact]
    public void AddUserInvitationProvider_NoProviderSupplied_RegistersNullUserInvitationProvider()
    {
        var services = new ServiceCollection();

        services.AddUserInvitationProvider(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IUserInvitationProvider>()
            .Should().BeOfType<NullUserInvitationProvider>();
    }

    [Fact]
    public void AddUserInvitationProvider_InvitationExplicitlyDisabled_RegistersNullUserInvitationProvider()
    {
        var services = new ServiceCollection();

        services.AddUserInvitationProvider(InvitationConfiguration(enabled: false));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IUserInvitationProvider>()
            .Should().BeOfType<NullUserInvitationProvider>();
    }

    [Fact]
    public void AddUserInvitationProvider_InvitationEnabled_RegistersEntraGraphUserInvitationProvider()
    {
        var services = new ServiceCollection();

        services.AddUserInvitationProvider(InvitationConfiguration(enabled: true));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IUserInvitationProvider>()
            .Should().BeOfType<EntraGraphUserInvitationProvider>();
    }

    [Fact]
    public void AddUserInvitationProvider_CustomProviderSupplied_IsUsedInsteadOfNullDefault()
    {
        var services = new ServiceCollection();
        var customProvider = new RecordingUserInvitationProvider();

        services.AddUserInvitationProvider(EmptyConfiguration(), customProvider);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IUserInvitationProvider>().Should().BeSameAs(customProvider);
    }

    [Fact]
    public void AddUserInvitationProvider_CustomProviderSupplied_TakesPrecedenceEvenWhenInvitationEnabled()
    {
        var services = new ServiceCollection();
        var customProvider = new RecordingUserInvitationProvider();

        services.AddUserInvitationProvider(InvitationConfiguration(enabled: true), customProvider);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IUserInvitationProvider>().Should().BeSameAs(customProvider);
    }

    // ── Shared Graph credential reuse (XD01-67's GraphCredentialOptions generalization) ──

    [Fact]
    public void AddUserInvitationProvider_AfterAddRoleAssignmentProvider_ReusesTheSameGraphAccessTokenProviderInstance()
    {
        var configuration = BothFeaturesEnabledConfiguration();
        var services = new ServiceCollection();

        services.AddRoleAssignmentProvider(configuration);
        services.AddUserInvitationProvider(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IGraphAccessTokenProvider>().Should().ContainSingle(
            "both providers should share one MSAL confidential-client instance rather than each standing up its own");
    }

    [Fact]
    public void AddUserInvitationProvider_BeforeAddRoleAssignmentProvider_StillReusesTheSameInstance()
    {
        // Registration order between the two extension methods must not matter.
        var configuration = BothFeaturesEnabledConfiguration();
        var services = new ServiceCollection();

        services.AddUserInvitationProvider(configuration);
        services.AddRoleAssignmentProvider(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IGraphAccessTokenProvider>().Should().ContainSingle();
    }
}
