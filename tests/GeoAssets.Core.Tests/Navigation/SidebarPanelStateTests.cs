using FluentAssertions;
using GeoAssets.Core.Navigation;
using Xunit;

namespace GeoAssets.Core.Tests.Navigation;

public class SidebarPanelStateTests
{
    [Fact]
    public void Open_SetsOpenPanelIdAndFiresChanged()
    {
        var sut = new SidebarPanelState();
        var fired = false;
        sut.Changed += (_, _) => fired = true;

        sut.Open("projects");

        sut.OpenPanelId.Should().Be("projects");
        fired.Should().BeTrue();
    }

    [Fact]
    public void Open_SameIdAlreadyOpen_DoesNotFireChanged()
    {
        var sut = new SidebarPanelState();
        sut.Open("projects");
        var fired = false;
        sut.Changed += (_, _) => fired = true;

        sut.Open("projects");

        fired.Should().BeFalse();
    }

    [Fact]
    public void Toggle_ClosedPanel_OpensIt()
    {
        var sut = new SidebarPanelState();

        sut.Toggle("assets");

        sut.OpenPanelId.Should().Be("assets");
    }

    [Fact]
    public void Toggle_AlreadyOpenPanel_ClosesIt()
    {
        var sut = new SidebarPanelState();
        sut.Toggle("assets");

        sut.Toggle("assets");

        sut.OpenPanelId.Should().BeNull();
    }

    [Fact]
    public void Toggle_DifferentPanelThanOpen_SwitchesToIt()
    {
        var sut = new SidebarPanelState();
        sut.Toggle("assets");

        sut.Toggle("projects");

        sut.OpenPanelId.Should().Be("projects");
    }
}
