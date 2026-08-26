using FluentAssertions;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

public class CurrentMapContextTests
{
    [Fact]
    public void MapDivId_IsTheSharedMapDivId() =>
        new CurrentMapContext().MapDivId.Should().Be("geoassets-map");
}
