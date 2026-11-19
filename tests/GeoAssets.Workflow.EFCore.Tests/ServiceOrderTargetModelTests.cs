using FluentAssertions;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Configurations;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// The <c>ServiceOrderTargets</c> table as the EF model describes it (XD01-306). The PostgreSQL
/// model is built without a server: reading <see cref="DbContext.Model"/> never connects.
/// </summary>
public sealed class ServiceOrderTargetModelTests : IDisposable
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

    /// <summary>Collations are migration-time metadata, kept only in the design-time model.</summary>
    private IEntityType DesignTimeTarget(bool postgres) =>
        (postgres ? _postgres : _fixture.Context).GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ServiceOrderTargetRecord))!;

    [Theory]
    [MemberData(nameof(Providers))]
    public void Table_IsServiceOrderTargets_KeyedById(bool postgres)
    {
        var target = Target(postgres);

        target.GetTableName().Should().Be("ServiceOrderTargets");
        target.FindPrimaryKey()!.Properties.Select(p => p.Name).Should().Equal(nameof(ServiceOrderTargetRecord.Id));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Indexes_AreUniqueVerticalOrderFeature_PartialOpenFeature_OrderStatus_Organization_AndKernelOrder(bool postgres)
    {
        var indexes = Target(postgres).GetIndexes()
            .Select(index => (Columns: string.Join(',', index.Properties.Select(p => p.Name)), index.IsUnique, Filter: index.GetFilter()))
            .ToList();

        indexes.Should().BeEquivalentTo(
        [
            ("VerticalId,ServiceOrderId,FeatureId", true, (string?)null),
            ("FeatureId", false, ServiceOrderTargetRecordConfiguration.OpenTargetFilter),
            ("ServiceOrderId,Status", false, null),
            ("OrganizationId", false, null),
            ("KernelOrderId", false, null),
        ]);
        ServiceOrderTargetRecordConfiguration.OpenTargetFilter.Should().Be("\"ReleasedAt\" IS NULL");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void ForeignKey_PointsAtTheOrderThroughKernelOrderId_Optional_WithCascadeDelete(bool postgres)
    {
        var foreignKey = Target(postgres).GetForeignKeys().Should().ContainSingle().Subject;

        foreignKey.PrincipalEntityType.ClrType.Should().Be<ServiceOrderRecord>();
        foreignKey.Properties.Select(p => p.Name).Should().Equal(nameof(ServiceOrderTargetRecord.KernelOrderId));
        foreignKey.IsRequired.Should().BeFalse("a remote vertical's target has no order row here");
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Columns_HaveTheAgreedLengthsAndNullability(bool postgres)
    {
        var columns = Target(postgres).GetProperties()
            .ToDictionary(p => p.Name, p => (MaxLength: p.GetMaxLength(), p.IsNullable), StringComparer.Ordinal);

        columns.Should().BeEquivalentTo(new Dictionary<string, (int?, bool)>(StringComparer.Ordinal)
        {
            ["Id"] = (36, false),
            ["ServiceOrderId"] = (36, false),
            ["VerticalId"] = (64, false),
            ["KernelOrderId"] = (36, true),
            ["OrderTypeId"] = (64, true),
            ["Exclusivity"] = (null, true),
            ["FeatureId"] = (64, false),
            ["AssetTypeId"] = (64, false),
            ["Alias"] = (256, true),
            ["ActivityIdsJson"] = (null, false),
            ["Status"] = (64, false),
            ["ConflictLevel"] = (null, false),
            ["Note"] = (2048, true),
            ["Longitude"] = (null, true),
            ["Latitude"] = (null, true),
            ["AddedAt"] = (null, false),
            ["AddedBy"] = (256, false),
            ["OrganizationId"] = (null, false),
            ["Version"] = (null, false),
            ["ReleasedAt"] = (null, true),
        });
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Version_IsAPlainAppManagedInteger_NotADatabaseGeneratedOrEfCheckedToken(bool postgres)
    {
        var version = Target(postgres).FindProperty(nameof(ServiceOrderTargetRecord.Version))!;

        version.ClrType.Should().Be<int>();
        version.ValueGenerated.Should().Be(ValueGenerated.Never, "no trigger or database default maintains it");
        version.IsConcurrencyToken.Should().BeFalse(
            "the repository compares it in its own UPDATE; as an EF token it would also guard the release stamp an order's closing writes");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TenantQueryFilter_IsDeclared(bool postgres)
    {
        Target(postgres).GetDeclaredQueryFilters().Should().NotBeEmpty();
    }

    [Fact]
    public void SortableTextColumns_UseTheByteWiseCollationOnPostgres_AndTheProviderDefaultOnSqlite()
    {
        string?[] Collations(bool postgres) =>
            new[] { "Id", "FeatureId", "Alias", "Status", "ServiceOrderId", "AssetTypeId" }
                .Select(name => DesignTimeTarget(postgres).FindProperty(name)!.GetCollation())
                .ToArray();

        Collations(postgres: true).Should().Equal("C", "C", "C", "C", null, null);
        Collations(postgres: false).Should().OnlyContain(collation => collation == null, "\"C\" is not a SQLite collation; its default is already byte-wise");
    }

    [Fact]
    public void RepositoryInsertColumns_CoverEveryMappedProperty_SoANewColumnCannotBeSilentlyLeftOutOfInserts()
    {
        var mapped = Target(postgres: true).GetProperties().Select(p => p.Name);

        EFServiceOrderTargetRepository.InsertColumns.Select(column => column.Property).Should().BeEquivalentTo(mapped);
    }
}
