using FluentAssertions;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests;

public class GeoAssetsInvitationEmailExtensionsTests
{
    private sealed class RecordingInvitationEmailSender : IInvitationEmailSender
    {
        public Task SendInvitationAsync(string toEmail, string displayName, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private static IConfiguration InvitationConfiguration(bool enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Invitation:Enabled"]         = enabled.ToString(),
                ["Invitation:PublicWebAppUrl"] = "https://app.geoassets.example",
                ["AcsEmail:Endpoint"]           = "https://acs-resource.communication.azure.com",
                ["AcsEmail:AccessKey"]          = "not-a-real-key",
                ["AcsEmail:FromAddress"]        = "invitations@geoassets.example",
            })
            .Build();

    [Fact]
    public void AddInvitationEmailSender_NoSenderSupplied_RegistersNullInvitationEmailSender()
    {
        var services = new ServiceCollection();

        services.AddInvitationEmailSender(EmptyConfiguration());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IInvitationEmailSender>()
            .Should().BeOfType<NullInvitationEmailSender>();
    }

    [Fact]
    public void AddInvitationEmailSender_InvitationExplicitlyDisabled_RegistersNullInvitationEmailSender()
    {
        var services = new ServiceCollection();

        services.AddInvitationEmailSender(InvitationConfiguration(enabled: false));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IInvitationEmailSender>()
            .Should().BeOfType<NullInvitationEmailSender>();
    }

    [Fact]
    public void AddInvitationEmailSender_InvitationEnabled_RegistersAcsEmailInvitationSender()
    {
        var services = new ServiceCollection();

        services.AddInvitationEmailSender(InvitationConfiguration(enabled: true));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IInvitationEmailSender>()
            .Should().BeOfType<AcsEmailInvitationSender>();
    }

    [Fact]
    public void AddInvitationEmailSender_CustomSenderSupplied_IsUsedInsteadOfNullDefault()
    {
        var services = new ServiceCollection();
        var customSender = new RecordingInvitationEmailSender();

        services.AddInvitationEmailSender(EmptyConfiguration(), customSender);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IInvitationEmailSender>().Should().BeSameAs(customSender);
    }

    [Fact]
    public void AddInvitationEmailSender_CustomSenderSupplied_TakesPrecedenceEvenWhenInvitationEnabled()
    {
        var services = new ServiceCollection();
        var customSender = new RecordingInvitationEmailSender();

        services.AddInvitationEmailSender(InvitationConfiguration(enabled: true), customSender);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IInvitationEmailSender>().Should().BeSameAs(customSender);
    }
}
