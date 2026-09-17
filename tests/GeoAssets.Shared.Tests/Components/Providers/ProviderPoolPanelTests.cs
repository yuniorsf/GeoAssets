using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Components.Providers;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Providers;

/// <summary>
/// <see cref="ProviderPoolPanel.ShouldHintAsForkOnWrite"/> is the pure decision behind the "add
/// provider" button's capability-aware gating (XD01-148) — factored out as a static method so
/// it's directly unit-testable without a Blazor render tree, matching ProjectPanel.Categorize's
/// pattern (this repo has no bUnit yet).
/// </summary>
public class ProviderPoolPanelTests
{
    [Fact]
    public void GeneralProjectOpen_PermissionDenied_ShouldHint()
    {
        // Literal acceptance criterion: a user without projects:manage-providers sees the "add
        // provider" control visibly hinted on the open General Project.
        ProviderPoolPanel.ShouldHintAsForkOnWrite(ProjectKind.General, canManage: false).Should().BeTrue();
    }

    [Fact]
    public void GeneralProjectOpen_PermissionGranted_DoesNotHint()
    {
        ProviderPoolPanel.ShouldHintAsForkOnWrite(ProjectKind.General, canManage: true).Should().BeFalse();
    }

    [Fact]
    public void UserForkOpen_PermissionDenied_DoesNotHint()
    {
        // A user always has full rights on their own fork — gating only applies to the open
        // General Project, never to a User Project the caller already owns.
        ProviderPoolPanel.ShouldHintAsForkOnWrite(ProjectKind.User, canManage: false).Should().BeFalse();
    }

    [Fact]
    public void NoProjectOpen_DoesNotHint()
    {
        ProviderPoolPanel.ShouldHintAsForkOnWrite(openProjectKind: null, canManage: false).Should().BeFalse();
    }
}
