using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.MAUI.Services.Projects;
using Xunit;

namespace GeoAssets.MAUI.Tests;

/// <summary>XD01-476 — the app's Project session and client: no Project is ever open or listed, and nothing writes one.</summary>
public sealed class NoProjectSessionServiceTests
{
    private readonly NoProjectSessionService _session = new();
    private readonly NoProjectClient _client = new();

    [Fact]
    public void TheSession_HasNoProjectOpen_AndNothingUnsaved()
    {
        _session.Current.Should().BeNull();
        _session.RawCurrent.Should().BeNull();
        _session.IsDirty.Should().BeFalse();
        _session.LastReconnectFailures.Should().BeEmpty();
    }

    [Fact]
    public async Task Closing_IsAllowedWithoutAPrompt_AndCapabilitiesAreNotRestricted()
    {
        _session.CloseRequested = () => throw new InvalidOperationException("no prompt expected");

        (await _session.RequestCloseAsync()).Should().BeTrue();
        await _session.CloseAsync();
        (await _session.CanAsync("projects:manage-providers")).Should().BeTrue();
    }

    [Fact]
    public Task Opening_IsRefused_AsUnavailableInTheApp()
    {
        var open = () => _session.OpenAsync(Guid.NewGuid());

        return open.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Saving_OrDiscarding_IsRefused_AsNoProjectIsOpen()
    {
        await _session.Invoking(s => s.SaveAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("No Project is open.");
        await _session.Invoking(s => s.SaveAsAsync("copy", "")).Should().ThrowAsync<InvalidOperationException>();
        await _session.Invoking(s => s.DiscardChangesAsync()).Should().ThrowAsync<InvalidOperationException>();
        _session.Invoking(s => s.SetViewState(new ProjectViewState())).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task TheClient_ListsAndFindsNoProject()
    {
        (await _client.GetByOrganizationAsync(Guid.NewGuid())).Should().BeEmpty();
        (await _client.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task TheClient_RefusesEveryWrite()
    {
        var id = Guid.NewGuid();

        await _client.Invoking(c => c.CreateAsync(new Project())).Should().ThrowAsync<NotSupportedException>();
        await _client.Invoking(c => c.UpdateProvidersAsync(id, [])).Should().ThrowAsync<NotSupportedException>();
        await _client.Invoking(c => c.UpdateAssetTypeScopeAsync(id, null)).Should().ThrowAsync<NotSupportedException>();
        await _client.Invoking(c => c.UpdateLayerScopeAsync(id, null)).Should().ThrowAsync<NotSupportedException>();
        await _client.Invoking(c => c.UpdateViewStateAsync(id, null)).Should().ThrowAsync<NotSupportedException>();
    }
}
