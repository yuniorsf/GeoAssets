using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

public class GeoAssetsRoleAssignmentExtensionsTests
{
    private sealed class RecordingRoleAssignmentProvider : IRoleAssignmentProvider
    {
        public Task RegisterRoleAsync(AppRole role, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnregisterRoleAsync(Guid roleId, CancellationToken ct = default) => Task.CompletedTask;
        public Task AssignRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeRoleAsync(string externalUserObjectId, string roleName, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetAssignedRoleNamesAsync(string externalUserObjectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration RoleSyncConfiguration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RoleSync:Enabled"]                       = enabled.ToString(),
                ["RoleSync:TenantId"]                       = "11111111-2222-3333-4444-555555555555",
                ["RoleSync:ClientId"]                        = "66666666-7777-8888-9999-aaaaaaaaaaaa",
                ["RoleSync:ClientSecret"]                    = "not-a-real-secret",
                ["RoleSync:TargetApplicationClientIds:0"]    = "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
            })
            .Build();

    [Fact]
    public void AddRoleAssignmentProvider_NoProviderSupplied_RegistersNullRoleAssignmentProvider()
    {
        var services = new ServiceCollection();

        services.AddRoleAssignmentProvider(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRoleAssignmentProvider>()
            .Should().BeOfType<NullRoleAssignmentProvider>();
    }

    [Fact]
    public void AddRoleAssignmentProvider_RoleSyncExplicitlyDisabled_RegistersNullRoleAssignmentProvider()
    {
        var services = new ServiceCollection();

        services.AddRoleAssignmentProvider(RoleSyncConfiguration(enabled: false));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRoleAssignmentProvider>()
            .Should().BeOfType<NullRoleAssignmentProvider>();
    }

    [Fact]
    public void AddRoleAssignmentProvider_RoleSyncEnabled_RegistersEntraGraphRoleAssignmentProvider()
    {
        var services = new ServiceCollection();

        services.AddRoleAssignmentProvider(RoleSyncConfiguration(enabled: true));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRoleAssignmentProvider>()
            .Should().BeOfType<EntraGraphRoleAssignmentProvider>();
    }

    [Fact]
    public void AddRoleAssignmentProvider_CustomProviderSupplied_IsUsedInsteadOfNullDefault()
    {
        // Proves the seam is real: a caller-supplied IRoleAssignmentProvider actually gets
        // registered, rather than AddRoleAssignmentProvider always wiring the no-op default
        // regardless of what's passed in.
        var services = new ServiceCollection();
        var customProvider = new RecordingRoleAssignmentProvider();

        services.AddRoleAssignmentProvider(EmptyConfiguration(), customProvider);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRoleAssignmentProvider>().Should().BeSameAs(customProvider);
    }

    [Fact]
    public void AddRoleAssignmentProvider_CustomProviderSupplied_TakesPrecedenceEvenWhenRoleSyncEnabled()
    {
        var services = new ServiceCollection();
        var customProvider = new RecordingRoleAssignmentProvider();

        services.AddRoleAssignmentProvider(RoleSyncConfiguration(enabled: true), customProvider);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRoleAssignmentProvider>().Should().BeSameAs(customProvider);
    }
}
