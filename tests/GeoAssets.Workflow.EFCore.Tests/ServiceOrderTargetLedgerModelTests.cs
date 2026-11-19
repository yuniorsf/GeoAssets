using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Configurations;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>The vertical-keyed shape of <c>ServiceOrderTargets</c> (XD01-379), as the EF model describes it.</summary>
public sealed class ServiceOrderTargetLedgerModelTests : IDisposable
{
    private readonly SqliteFixture _fixture = new();
    private readonly ServiceOrderDbContext _postgres = new(
        new DbContextOptionsBuilder<ServiceOrderDbContext>().UseNpgsql("Host=never-connected").Options);

    public void Dispose()
    {
        _postgres.Dispose();
        _fixture.Dispose();
    }

    public static TheoryData<bool> Providers => new() { { true }, { false } };

    private IEntityType Target(bool postgres) =>
        (postgres ? _postgres : _fixture.Context).Model.FindEntityType(typeof(ServiceOrderTargetRecord))!;

    [Theory]
    [MemberData(nameof(Providers))]
    public void VerticalId_IsRequired_64Long_AndDefaultsToKernel(bool postgres)
    {
        var vertical = Target(postgres).FindProperty(nameof(ServiceOrderTargetRecord.VerticalId))!;

        vertical.IsNullable.Should().BeFalse();
        vertical.GetMaxLength().Should().Be(64);
        vertical.GetDefaultValue().Should().Be("kernel");
        new ServiceOrderTargetRecord().VerticalId.Should().Be("kernel");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void UniqueIndex_IsVerticalOrderFeature_SoTwoVerticalsMayShareAnOrderId(bool postgres)
    {
        var unique = Target(postgres).GetIndexes().Single(i => i.IsUnique);

        unique.Properties.Select(p => p.Name).Should().Equal("VerticalId", "ServiceOrderId", "FeatureId");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void CheckConstraint_KeepsKernelOrderIdInStepWithTheVertical(bool postgres)
    {
        var design = (postgres ? _postgres : _fixture.Context).GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ServiceOrderTargetRecord))!;
        var check = design.GetCheckConstraints().Should().ContainSingle().Subject;

        check.Name.Should().Be(ServiceOrderTargetRecordConfiguration.KernelOrderCheckName).And.Be("CK_ServiceOrderTargets_KernelOrderId");
        check.Sql.Should().Contain("'kernel'").And.Contain("\"KernelOrderId\" IS NULL").And.Contain("\"KernelOrderId\" = \"ServiceOrderId\"");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void LedgerOnlyColumns_AreNullable_AndAbsentFromTheDomainTarget(bool postgres)
    {
        var entity = Target(postgres);

        entity.FindProperty(nameof(ServiceOrderTargetRecord.OrderTypeId))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(ServiceOrderTargetRecord.Exclusivity))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(ServiceOrderTargetRecord.KernelOrderId))!.IsNullable.Should().BeTrue();
        typeof(GeoAssets.Workflow.Targets.ServiceOrderTarget).GetProperty("OrderTypeId").Should().BeNull();
    }

    [Fact]
    public void Collations_OfTheNewTextColumns_AreTheProviderDefault()
    {
        var model = _postgres.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ServiceOrderTargetRecord))!;

        model.FindProperty(nameof(ServiceOrderTargetRecord.VerticalId))!.GetCollation().Should().BeNull();
    }
}
