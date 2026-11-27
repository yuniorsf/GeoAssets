using FluentAssertions;
using GeoAssets.MAUI.Services;
using Xunit;

namespace GeoAssets.MAUI.Tests;

/// <summary>
/// XD01-438 — what <c>MauiNativeFilePicker</c> asks each platform's picker for: every platform it runs on has a filter
/// (Mac Catalyst included — the system picker looks its own platform up, and had none), Apple by Uniform Type
/// Identifier, Android by MIME type, Windows and AppKit macOS by extension.
/// </summary>
public sealed class NativeFilePickerFileTypesTests
{
    [Fact]
    public void EveryPlatform_HasAFilter()
    {
        var filters = NativeFilePickerFileTypes.For([".pmtiles"]);

        filters.Keys.Should().BeEquivalentTo(Enum.GetValues<NativeFilePickerPlatform>());
    }

    [Fact]
    public void MacCatalyst_FiltersByUniformTypeIdentifier_LikeIOS()
    {
        var filters = NativeFilePickerFileTypes.For([".zip"]);

        filters[NativeFilePickerPlatform.MacCatalyst].Should().Equal("com.pkware.zip-archive", NativeFilePickerFileTypes.AnyAppleType);
        filters[NativeFilePickerPlatform.MacCatalyst].Should().Equal(filters[NativeFilePickerPlatform.IOS]);
    }

    [Fact]
    public void APMTilesPack_IsAnyDataOnApple_AndItsMimeTypesOnAndroid()
    {
        var filters = NativeFilePickerFileTypes.For([".pmtiles"]);

        filters[NativeFilePickerPlatform.IOS].Should().Equal(NativeFilePickerFileTypes.AnyAppleType);
        filters[NativeFilePickerPlatform.MacCatalyst].Should().Equal(NativeFilePickerFileTypes.AnyAppleType);
        filters[NativeFilePickerPlatform.Android].Should().Equal("application/vnd.pmtiles", "application/octet-stream");
        filters[NativeFilePickerPlatform.WinUI].Should().Equal(".pmtiles");
        filters[NativeFilePickerPlatform.MacOS].Should().Equal("pmtiles");
    }

    [Fact]
    public void AnExtensionWithNoKnownType_OffersAnyFileOnAndroid()
    {
        var filters = NativeFilePickerFileTypes.For([".zip", ".fgb"]);

        filters[NativeFilePickerPlatform.Android].Should().Equal("application/zip", "application/x-zip-compressed", NativeFilePickerFileTypes.AnyAndroidType);
    }

    [Fact]
    public void KnownExtensions_OnAndroid_OfferOnlyTheirTypes_MatchedIgnoringCase()
    {
        var filters = NativeFilePickerFileTypes.For([".ZIP", ".PMTiles"]);

        filters[NativeFilePickerPlatform.Android].Should().Equal(
            "application/zip", "application/x-zip-compressed", "application/vnd.pmtiles", "application/octet-stream");
    }

    [Fact]
    public void ARepeatedType_IsOfferedOnce()
    {
        var filters = NativeFilePickerFileTypes.For([".fgb", ".parquet"]);

        filters[NativeFilePickerPlatform.Android].Should().Equal(NativeFilePickerFileTypes.AnyAndroidType);
        filters[NativeFilePickerPlatform.IOS].Should().Equal(NativeFilePickerFileTypes.AnyAppleType);
    }
}
