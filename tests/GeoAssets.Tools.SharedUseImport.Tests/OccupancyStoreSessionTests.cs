using FluentAssertions;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class OccupancyStoreSessionTests
{
    private sealed class Resource : IAsyncDisposable
    {
        public int Disposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task DisposeAsync_ReleasesTheResource()
    {
        var store = new InMemoryPoleOccupancyStore();
        var resource = new Resource();

        await new OccupancyStoreSession(store, store, resource).DisposeAsync();

        resource.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WithoutResource_DoesNothing()
    {
        var store = new InMemoryPoleOccupancyStore();
        var session = new OccupancyStoreSession(store, store);

        var act = async () => await session.DisposeAsync();

        await act.Should().NotThrowAsync();
        session.Reader.Should().BeSameAs(store);
        session.Writer.Should().BeSameAs(store);
    }

    [Fact]
    public void Constructor_NullPorts_Throw()
    {
        var store = new InMemoryPoleOccupancyStore();

        var nullReader = () => new OccupancyStoreSession(null!, store);
        var nullWriter = () => new OccupancyStoreSession(store, null!);

        nullReader.Should().Throw<ArgumentNullException>().WithParameterName("reader");
        nullWriter.Should().Throw<ArgumentNullException>().WithParameterName("writer");
    }
}
