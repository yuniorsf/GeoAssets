using System.Text.Json.Nodes;
using FluentAssertions;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class ProjectSchemaMigratorTests
{
    [Fact]
    public void Migrate_SameVersion_IsNoOp()
    {
        var payload = JsonNode.Parse("""{"name":"unchanged"}""")!;

        var result = ProjectSchemaMigrator.Migrate(payload, 1, 1);

        result.Should().BeSameAs(payload);
    }

    [Fact]
    public void Migrate_SameArbitraryVersion_IsNoOp()
    {
        var payload = JsonNode.Parse("""{"name":"unchanged"}""")!;

        var result = ProjectSchemaMigrator.Migrate(payload, 7, 7);

        result.Should().BeSameAs(payload);
    }

    [Fact]
    public void Migrate_NoRegisteredStepForRequestedUpgrade_Throws()
    {
        var payload = JsonNode.Parse("""{"name":"unchanged"}""")!;

        var act = () => ProjectSchemaMigrator.Migrate(payload, 1, 2);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*version 1*");
    }
}
