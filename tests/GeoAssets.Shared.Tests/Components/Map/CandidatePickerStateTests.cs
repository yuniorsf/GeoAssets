using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Components.Map;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Map;

/// <summary>
/// <see cref="CandidatePickerState"/> is the pure interaction-state machine behind the
/// multi-candidate "Connect to…" picker (XD01-152) — factored out of the Razor component so
/// it's directly unit-testable without a Blazor render tree (this repo has no bUnit; same
/// pattern as <c>AssetForm.FindAutoLinkCandidate</c>).
/// </summary>
public class CandidatePickerStateTests
{
    private static GeoFeature LineFeature(string id) => new()
    {
        Id = id,
        Geometry = new GeoLineString([(0, 0), (1, 1)]),
        Properties = new GeoFeatureProperties { Name = id }
    };

    private static IReadOnlyList<GeoFeature> ThreeCandidates() =>
        [LineFeature("wire-a"), LineFeature("wire-b"), LineFeature("wire-c")];

    [Fact]
    public void Constructor_EmptyCandidates_Throws()
    {
        var act = () => new CandidatePickerState([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_DefaultsToSingleModeAndFirstCandidate()
    {
        var state = new CandidatePickerState(ThreeCandidates());

        state.Mode.Should().Be(CandidatePickerMode.Single);
        state.CurrentIndex.Should().Be(0);
        state.Current.Id.Should().Be("wire-a");
    }

    [Fact]
    public void CycleNext_AdvancesToNextCandidate()
    {
        var state = new CandidatePickerState(ThreeCandidates());

        state.CycleNext();

        state.CurrentIndex.Should().Be(1);
        state.Current.Id.Should().Be("wire-b");
    }

    [Fact]
    public void CycleNext_WrapsCircularlyFromLastToFirst()
    {
        var state = new CandidatePickerState(ThreeCandidates());

        state.CycleNext(); // -> b
        state.CycleNext(); // -> c
        state.CycleNext(); // -> wraps to a

        state.CurrentIndex.Should().Be(0);
        state.Current.Id.Should().Be("wire-a");
    }

    [Fact]
    public void Confirm_SingleMode_ReturnsOnlyTheCurrentCandidate()
    {
        var state = new CandidatePickerState(ThreeCandidates());
        state.CycleNext(); // current = wire-b

        var result = state.Confirm();

        result.Should().ContainSingle().Which.Id.Should().Be("wire-b");
    }

    [Fact]
    public void SwitchToMultiple_CarriesOverCurrentCandidateAsSelected()
    {
        // XD01-151 design decision: switching Single -> Multiple mid-flow carries the
        // currently-cycled candidate over as already added, instead of starting empty.
        var state = new CandidatePickerState(ThreeCandidates());
        state.CycleNext(); // current = wire-b

        state.SwitchMode(CandidatePickerMode.Multiple);

        state.IsSelected(state.Current).Should().BeTrue();
        state.SelectedCount.Should().Be(1);
        state.Confirm().Should().ContainSingle().Which.Id.Should().Be("wire-b");
    }

    [Fact]
    public void ToggleCurrentSelected_TogglesMembership()
    {
        var state = new CandidatePickerState(ThreeCandidates());
        state.SwitchMode(CandidatePickerMode.Multiple); // carries over wire-a

        state.ToggleCurrentSelected(); // removes wire-a
        state.IsSelected(state.Current).Should().BeFalse();
        state.SelectedCount.Should().Be(0);

        state.ToggleCurrentSelected(); // re-adds wire-a
        state.IsSelected(state.Current).Should().BeTrue();
        state.SelectedCount.Should().Be(1);
    }

    [Fact]
    public void Confirm_MultipleMode_ReturnsSelectedCandidatesInCandidateOrder()
    {
        var state = new CandidatePickerState(ThreeCandidates());
        state.SwitchMode(CandidatePickerMode.Multiple);

        state.CycleNext(); // -> wire-b
        state.CycleNext(); // -> wire-c
        state.ToggleCurrentSelected(); // select wire-c (wire-a already selected from the switch)

        var result = state.Confirm();

        result.Select(c => c.Id).Should().Equal("wire-a", "wire-c");
    }

    [Fact]
    public void Confirm_MultipleMode_NoneSelected_ReturnsEmpty()
    {
        var state = new CandidatePickerState(ThreeCandidates());
        state.SwitchMode(CandidatePickerMode.Multiple);
        state.ToggleCurrentSelected(); // deselect the carried-over wire-a

        var result = state.Confirm();

        result.Should().BeEmpty();
    }

    [Fact]
    public void SwitchMode_BackToSingle_ConfirmIgnoresLeftoverMultiSelection()
    {
        var state = new CandidatePickerState(ThreeCandidates());
        state.SwitchMode(CandidatePickerMode.Multiple);
        state.CycleNext();
        state.ToggleCurrentSelected(); // wire-a and wire-b both selected

        state.SwitchMode(CandidatePickerMode.Single); // current is still wire-b

        state.Confirm().Should().ContainSingle().Which.Id.Should().Be("wire-b");
    }
}
