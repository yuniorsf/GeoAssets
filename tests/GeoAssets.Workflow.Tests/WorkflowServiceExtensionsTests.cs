using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Tests;

public class WorkflowServiceExtensionsTests
{
    [Fact]
    public void AddWorkflowInMemory_ResolvesIServiceOrderRepositoryAsValidatingDecorator()
    {
        var services = new ServiceCollection();
        services.AddWorkflowInMemory();
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IServiceOrderRepository>()
            .Should().BeOfType<ValidatingServiceOrderRepository>();
    }

    [Fact]
    public void AddWorkflowInMemory_IServiceOrderReaderAndWriterResolveToSameUnderlyingRepository()
    {
        var services = new ServiceCollection();
        services.AddWorkflowInMemory();
        using var sp = services.BuildServiceProvider();

        var repository = sp.GetRequiredService<IServiceOrderRepository>();
        var reader     = sp.GetRequiredService<IServiceOrderReader>();
        var writer     = sp.GetRequiredService<IServiceOrderWriter>();

        reader.Should().BeSameAs(repository);
        writer.Should().BeSameAs(repository);
    }

    [Fact]
    public async Task AddWorkflowInMemory_IServiceOrderReader_SeesWritesMadeThroughIServiceOrderWriter()
    {
        var services = new ServiceCollection();
        services.AddWorkflowInMemory();
        using var sp = services.BuildServiceProvider();

        var writer = sp.GetRequiredService<IServiceOrderWriter>();
        var reader = sp.GetRequiredService<IServiceOrderReader>();

        await writer.AddAsync(new ServiceOrder { Id = "a", Title = "Via writer-only dependency" });

        (await reader.GetByIdAsync("a"))!.Title.Should().Be("Via writer-only dependency");
    }
}
