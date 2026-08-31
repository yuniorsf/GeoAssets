using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

public class FeatureSelectionStateTests
{
    [Fact]
    public void InitialState_NothingSelected()
    {
        var state = new FeatureSelectionState();

        state.Selected.Should().BeNull();
        state.IsNew.Should().BeFalse();
    }

    [Fact]
    public void Select_SetsSelectedAndIsNew()
    {
        var state = new FeatureSelectionState();
        var feature = new GeoFeature();

        state.Select(feature, isNew: true);

        state.Selected.Should().BeSameAs(feature);
        state.IsNew.Should().BeTrue();
    }

    [Fact]
    public void Select_DefaultIsNew_IsFalse()
    {
        var state = new FeatureSelectionState();

        state.Select(new GeoFeature());

        state.IsNew.Should().BeFalse();
    }

    [Fact]
    public void Select_RaisesChanged()
    {
        var state = new FeatureSelectionState();
        var raised = 0;
        state.Changed += () => raised++;

        state.Select(new GeoFeature());

        raised.Should().Be(1);
    }

    [Fact]
    public void Clear_ResetsSelectedAndIsNew()
    {
        var state = new FeatureSelectionState();
        state.Select(new GeoFeature(), isNew: true);

        state.Clear();

        state.Selected.Should().BeNull();
        state.IsNew.Should().BeFalse();
    }

    [Fact]
    public void Clear_RaisesChanged()
    {
        var state = new FeatureSelectionState();
        state.Select(new GeoFeature());
        var raised = 0;
        state.Changed += () => raised++;

        state.Clear();

        raised.Should().Be(1);
    }
}
