using FluentAssertions;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

public class PendingDrawTypeStateTests
{
    [Fact]
    public void InitialState_NoPendingType()
    {
        var state = new PendingDrawTypeState();

        state.AssetTypeId.Should().BeNull();
    }

    [Fact]
    public void Set_StoresTheAssetTypeId()
    {
        var state = new PendingDrawTypeState();

        state.Set("type-1");

        state.AssetTypeId.Should().Be("type-1");
    }

    [Fact]
    public void Set_Twice_OverwritesThePreviousValue()
    {
        var state = new PendingDrawTypeState();
        state.Set("type-1");

        state.Set("type-2");

        state.AssetTypeId.Should().Be("type-2");
    }

    [Fact]
    public void Clear_ResetsToNull()
    {
        var state = new PendingDrawTypeState();
        state.Set("type-1");

        state.Clear();

        state.AssetTypeId.Should().BeNull();
    }
}
