using System.Net.Http;
using Bunit;
using FluentAssertions;
using GeoAssets.Core.Localization;
using GeoAssets.Shared.Components.Map;
using GeoAssets.Shared.Services.FeatureActions;
using GeoAssets.Workflow.Registry;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Map;

/// <summary>bUnit coverage of <see cref="MapContextMenu"/>'s kernel feature actions (XD01-370).</summary>
public sealed class MapContextMenuTests : BunitContext
{
    private static readonly FeatureActionDescriptor SharedUse =
        new("pole", "create", "featureActions.createSharedUseOrder", "shared-use-pole-work", "service-orders");

    private static readonly FeatureActionDescriptor Inspect =
        new("pole", "inspect", "featureActions.inspect", "inspection", "service-orders");

    private sealed class KeyLocalizer : IJsonStringLocalizer
    {
        public string this[string key] => key;
        public string GetString(string key, params object[] args) => key;
        public event EventHandler LocalizationChanged { add { } remove { } }
    }

    private sealed class StubClient(Func<string, CancellationToken, Task<IReadOnlyList<FeatureActionDescriptor>>> handler) : IFeatureActionsClient
    {
        public List<string> Requests { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<IReadOnlyList<FeatureActionDescriptor>> GetActionsAsync(string featureId, CancellationToken ct)
        {
            Requests.Add(featureId);
            Tokens.Add(ct);
            return handler(featureId, ct);
        }
    }

    private sealed class Callbacks
    {
        public List<string> Log { get; } = [];
        public List<FeatureActionDescriptor> Actions { get; } = [];
    }

    private StubClient UseClient(Func<string, CancellationToken, Task<IReadOnlyList<FeatureActionDescriptor>>> handler)
    {
        var client = new StubClient(handler);
        Services.AddSingleton<IFeatureActionsClient>(client);
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton(NullLogger<MapContextMenu>.Instance);
        return client;
    }

    private StubClient UseClient(params FeatureActionDescriptor[] actions) =>
        UseClient((_, _) => Task.FromResult<IReadOnlyList<FeatureActionDescriptor>>(actions));

    private IRenderedComponent<MapContextMenu> Render(Callbacks? callbacks = null, bool visible = true, string? featureId = "pole-1", bool showSave = false)
    {
        callbacks ??= new Callbacks();
        return Render<MapContextMenu>(p => p
            .Add(c => c.Visible, visible)
            .Add(c => c.FeatureId, featureId)
            .Add(c => c.ShowSave, showSave)
            .Add(c => c.OnEdit, EventCallback.Factory.Create(this, () => callbacks.Log.Add("edit")))
            .Add(c => c.OnSave, EventCallback.Factory.Create(this, () => callbacks.Log.Add("save")))
            .Add(c => c.OnDelete, EventCallback.Factory.Create(this, () => callbacks.Log.Add("delete")))
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => callbacks.Log.Add("close")))
            .Add(c => c.OnFeatureAction, EventCallback.Factory.Create<FeatureActionDescriptor>(this, a =>
            {
                callbacks.Actions.Add(a);
                callbacks.Log.Add("action");
            })));
    }

    [Fact]
    public void ContextMenu_RendersActionsFromTheClient()
    {
        UseClient(SharedUse, Inspect);

        var cut = Render();

        cut.WaitForAssertion(() => cut.FindAll("button.ctx-feature-action").Should().HaveCount(2));
        cut.FindAll("button.ctx-feature-action").Select(b => b.TextContent.Trim())
            .Should().Equal(
                new[] { SharedUse.LabelKey, Inspect.LabelKey }.Select(k => k).ToArray(),
                (actual, key) => actual.EndsWith(key, StringComparison.Ordinal));
        cut.FindAll(".ctx-status").Should().BeEmpty();
    }

    [Fact]
    public void ContextMenu_ClientFailure_LeavesBaseItemsIntact()
    {
        UseClient((_, _) => throw new HttpRequestException("boom"));
        var callbacks = new Callbacks();

        var cut = Render(callbacks);

        cut.WaitForAssertion(() => cut.Find(".ctx-status").TextContent.Should().Contain("featureActions.unavailable"));
        cut.FindAll("button.ctx-feature-action").Should().BeEmpty();
        cut.Find("button.ctx-item:not(.ctx-danger)").TextContent.Should().Contain("assets.properties");
        cut.Find("button.ctx-danger").TextContent.Should().Contain("assets.delete");

        cut.Find("button.ctx-item:not(.ctx-danger)").Click();
        callbacks.Log.Should().Equal("edit", "close");

        callbacks.Log.Clear();
        cut.Find("button.ctx-danger").Click();
        callbacks.Log.Should().Equal("delete", "close");
    }

    [Fact]
    public void ContextMenu_AsyncFailureFromTheTask_ShowsUnavailable()
    {
        UseClient((_, _) => Task.FromException<IReadOnlyList<FeatureActionDescriptor>>(new HttpRequestException("boom")));

        var cut = Render();

        cut.WaitForAssertion(() => cut.Find(".ctx-status").TextContent.Should().Contain("featureActions.unavailable"));
    }

    [Fact]
    public void ContextMenu_WhileLoading_ShowsTheLoadingStatus_ThenTheActions()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<FeatureActionDescriptor>>();
        UseClient((_, _) => pending.Task);

        var cut = Render();

        cut.Find(".ctx-status").TextContent.Should().Contain("featureActions.loading");
        cut.FindAll(".ctx-separator").Should().HaveCount(2);
        cut.FindAll("button.ctx-feature-action").Should().BeEmpty();
        cut.Find("button.ctx-danger").Should().NotBeNull();

        pending.SetResult([SharedUse]);

        cut.WaitForAssertion(() => cut.FindAll("button.ctx-feature-action").Should().ContainSingle());
        cut.FindAll(".ctx-status").Should().BeEmpty();
    }

    [Fact]
    public void ContextMenu_NoActions_ShowsNoFeatureActionsAndNoExtraSeparator()
    {
        var client = UseClient();

        var cut = Render();

        cut.WaitForAssertion(() => client.Requests.Should().ContainSingle());
        cut.FindAll("button.ctx-feature-action").Should().BeEmpty();
        cut.FindAll(".ctx-status").Should().BeEmpty();
        cut.FindAll(".ctx-separator").Should().ContainSingle();
    }

    [Fact]
    public void ContextMenu_Hidden_LoadsNothing()
    {
        var client = UseClient(SharedUse);

        var cut = Render(visible: false);

        client.Requests.Should().BeEmpty();
        cut.Markup.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ContextMenu_NoFeature_LoadsNothing(string? featureId)
    {
        var client = UseClient(SharedUse);

        var cut = Render(featureId: featureId);

        client.Requests.Should().BeEmpty();
        cut.FindAll("button.ctx-feature-action").Should().BeEmpty();
        cut.FindAll(".ctx-status").Should().BeEmpty();
        cut.Find("button.ctx-danger").Should().NotBeNull();
    }

    [Fact]
    public void ContextMenu_SameFeatureAgainWhileVisible_RequestsOnce()
    {
        var client = UseClient(SharedUse);
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll("button.ctx-feature-action").Should().ContainSingle());

        cut.Render(p => p.Add(c => c.X, 10).Add(c => c.Y, 20));
        cut.Render(p => p.Add(c => c.ShowSave, true));

        client.Requests.Should().Equal("pole-1");
    }

    [Fact]
    public void ContextMenu_ClosedThenReopened_Reloads()
    {
        var client = UseClient(SharedUse);
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll("button.ctx-feature-action").Should().ContainSingle());

        cut.Render(p => p.Add(c => c.Visible, false));
        cut.FindAll("button.ctx-feature-action").Should().BeEmpty();
        cut.Render(p => p.Add(c => c.Visible, true));

        cut.WaitForAssertion(() => client.Requests.Should().Equal("pole-1", "pole-1"));
        cut.WaitForAssertion(() => cut.FindAll("button.ctx-feature-action").Should().ContainSingle());
    }

    [Fact]
    public void ContextMenu_AnotherFeature_Reloads()
    {
        var client = UseClient(SharedUse);
        var cut = Render();
        cut.WaitForAssertion(() => cut.FindAll("button.ctx-feature-action").Should().ContainSingle());

        cut.Render(p => p.Add(c => c.FeatureId, "pole-2"));

        cut.WaitForAssertion(() => client.Requests.Should().Equal("pole-1", "pole-2"));
    }

    [Fact]
    public void ContextMenu_ClickingAnAction_RaisesOnFeatureActionThenOnClose()
    {
        UseClient(SharedUse, Inspect);
        var callbacks = new Callbacks();
        var cut = Render(callbacks);
        cut.WaitForAssertion(() => cut.FindAll("button.ctx-feature-action").Should().HaveCount(2));

        cut.FindAll("button.ctx-feature-action")[1].Click();

        callbacks.Actions.Should().Equal(Inspect);
        callbacks.Log.Should().Equal("action", "close");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContextMenu_ShowSave_TogglesTheSaveItem(bool showSave)
    {
        UseClient();
        var callbacks = new Callbacks();

        var cut = Render(callbacks, showSave: showSave);

        cut.FindAll("button.ctx-item").Any(b => b.TextContent.Contains("assets.save", StringComparison.Ordinal))
            .Should().Be(showSave);
        if (showSave)
        {
            cut.FindAll("button.ctx-item").Single(b => b.TextContent.Contains("assets.save", StringComparison.Ordinal)).Click();
            callbacks.Log.Should().Equal("save", "close");
        }
    }

    [Fact]
    public void ContextMenu_ClosingWhileLoading_CancelsThePendingRequest()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<FeatureActionDescriptor>>();
        var client = UseClient((_, _) => pending.Task);
        var cut = Render();
        client.Tokens.Should().ContainSingle().Which.IsCancellationRequested.Should().BeFalse();

        cut.Render(p => p.Add(c => c.Visible, false));

        client.Tokens[0].IsCancellationRequested.Should().BeTrue();
        cut.FindAll(".ctx-status").Should().BeEmpty();
    }

    [Fact]
    public async Task ContextMenu_DisposedWhileLoading_CancelsThePendingRequest()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<FeatureActionDescriptor>>();
        var client = UseClient((_, _) => pending.Task);
        var cut = Render();

        await DisposeComponentsAsync();

        client.Tokens.Should().ContainSingle().Which.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void ContextMenu_LoadCancelledByTheMenu_IsNotReportedAsUnavailable()
    {
        var client = UseClient(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        });
        var cut = Render();

        cut.Render(p => p.Add(c => c.Visible, false));

        client.Tokens[0].IsCancellationRequested.Should().BeTrue();
        cut.FindAll(".ctx-status").Should().BeEmpty();
    }
}
