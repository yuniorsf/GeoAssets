using Azure;
using Azure.Communication.Email;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests;

public class AcsEmailInvitationSenderTests
{
    /// <summary>
    /// EmailClient's methods are virtual specifically for mocking (Azure SDK convention) — no
    /// wrapper interface needed. EmailSendOperation has a protected parameterless constructor
    /// for the same reason.
    /// </summary>
    private sealed class CapturingEmailClient : EmailClient
    {
        public EmailMessage? CapturedMessage { get; private set; }
        public WaitUntil CapturedWaitUntil { get; private set; }

        public override Task<EmailSendOperation> SendAsync(
            WaitUntil wait, EmailMessage message, CancellationToken cancellationToken = default)
        {
            CapturedWaitUntil = wait;
            CapturedMessage   = message;
            return Task.FromResult<EmailSendOperation>(new FakeEmailSendOperation());
        }
    }

    private sealed class FakeEmailSendOperation : EmailSendOperation;

    private static (AcsEmailInvitationSender Sut, CapturingEmailClient Client) BuildSut(
        string publicWebAppUrl = "https://app.geoassets.example",
        string fromAddress     = "invitations@geoassets.example")
    {
        var client = new CapturingEmailClient();
        var sut = new AcsEmailInvitationSender(
            client,
            new AcsEmailOptions { FromAddress = fromAddress },
            new InvitationOptions { PublicWebAppUrl = publicWebAppUrl });
        return (sut, client);
    }

    [Fact]
    public async Task SendInvitationAsync_SendsFromTheConfiguredFromAddress()
    {
        var (sut, client) = BuildSut(fromAddress: "invitations@geoassets.example");

        await sut.SendInvitationAsync("invitee@example.com", "Invitee Name");

        client.CapturedMessage!.SenderAddress.Should().Be("invitations@geoassets.example");
    }

    [Fact]
    public async Task SendInvitationAsync_SendsToTheInviteeEmail()
    {
        var (sut, client) = BuildSut();

        await sut.SendInvitationAsync("invitee@example.com", "Invitee Name");

        client.CapturedMessage!.Recipients.To.Should().ContainSingle(a => a.Address == "invitee@example.com");
    }

    [Fact]
    public async Task SendInvitationAsync_BodyIncludesTheConfiguredSignInUrl()
    {
        // The ticket's own acceptance criterion: assert on the composed message content, not
        // just that a call was made — this would fail if PublicWebAppUrl were dropped or typo'd.
        var (sut, client) = BuildSut(publicWebAppUrl: "https://app.geoassets.example");

        await sut.SendInvitationAsync("invitee@example.com", "Invitee Name");

        client.CapturedMessage!.Content.PlainText.Should().Contain("https://app.geoassets.example");
        client.CapturedMessage!.Content.Html.Should().Contain("https://app.geoassets.example");
    }

    [Fact]
    public async Task SendInvitationAsync_BodyInstructsUsingForgotPassword()
    {
        var (sut, client) = BuildSut();

        await sut.SendInvitationAsync("invitee@example.com", "Invitee Name");

        client.CapturedMessage!.Content.PlainText.Should().ContainEquivalentOf("forgot password");
        client.CapturedMessage!.Content.Html.Should().ContainEquivalentOf("forgot password");
    }

    [Fact]
    public async Task SendInvitationAsync_IncludesTheDisplayNameInTheGreeting()
    {
        var (sut, client) = BuildSut();

        await sut.SendInvitationAsync("invitee@example.com", "Jane Doe");

        client.CapturedMessage!.Content.PlainText.Should().Contain("Jane Doe");
        client.CapturedMessage!.Content.Html.Should().Contain("Jane Doe");
    }

    [Fact]
    public async Task SendInvitationAsync_HtmlEncodesTheDisplayName()
    {
        // Invite-time display names are admin-supplied free text — the HTML body must not let
        // one flow through unescaped into the email markup.
        var (sut, client) = BuildSut();

        await sut.SendInvitationAsync("invitee@example.com", "<script>alert(1)</script>");

        client.CapturedMessage!.Content.Html.Should().NotContain("<script>");
        client.CapturedMessage!.Content.Html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public async Task SendInvitationAsync_WaitsUntilTheSendCompletes()
    {
        // Proves the caller learns about a failed send rather than fire-and-forgetting it.
        var (sut, client) = BuildSut();

        await sut.SendInvitationAsync("invitee@example.com", "Invitee Name");

        client.CapturedWaitUntil.Should().Be(WaitUntil.Completed);
    }
}
