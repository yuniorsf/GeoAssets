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
/// The <c>field_pole_occupancies</c> and <c>field_pole_occupancy_entries</c> tables as the EF
/// model describes them (XD01-314). The PostgreSQL model is built without a server: reading
/// <see cref="DbContext.Model"/> never connects.
/// </summary>
public sealed class PoleOccupancyModelTests : IDisposable
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

    private IModel Model(bool postgres) => (postgres ? _postgres : _fixture.Context).GetService<IDesignTimeModel>().Model;

    private IEntityType Pole(bool postgres) => Model(postgres).FindEntityType(typeof(PoleOccupancyRecord))!;

    private IEntityType Entry(bool postgres) => Model(postgres).FindEntityType(typeof(PoleOccupancyEntryRecord))!;

    private static List<(string Columns, bool IsUnique, string? Filter)> Indexes(IEntityType entityType) =>
        entityType.GetIndexes()
            .Select(index => (string.Join(',', index.Properties.Select(p => p.Name)), index.IsUnique, index.GetFilter()))
            .ToList();

    [Theory]
    [MemberData(nameof(Providers))]
    public void Tables_AreTheTwoFieldPoleOccupancyTables_KeyedById(bool postgres)
    {
        Pole(postgres).GetTableName().Should().Be("field_pole_occupancies");
        Entry(postgres).GetTableName().Should().Be("field_pole_occupancy_entries");
        Pole(postgres).FindPrimaryKey()!.Properties.Select(p => p.Name).Should().Equal("Id");
        Entry(postgres).FindPrimaryKey()!.Properties.Select(p => p.Name).Should().Equal("Id");
        Pole(postgres).FindProperty("Id")!.ValueGenerated.Should().Be(ValueGenerated.Never);
        Entry(postgres).FindProperty("Id")!.ValueGenerated.Should().Be(ValueGenerated.Never);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void PoleIndexes_AreUniqueOwnerAndPole_AndPoleFeatureId(bool postgres)
    {
        Indexes(Pole(postgres)).Should().BeEquivalentTo(new (string, bool, string?)[]
        {
            ("OrganizationId,PoleFeatureId", true, null),
            ("PoleFeatureId", false, null),
        });
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EntryIndexes_AreUniquePerRegulatedSlotOnly_PlusPoleWithOrganization_Occupant_AndOrganization(bool postgres)
    {
        Indexes(Entry(postgres)).Should().BeEquivalentTo(new (string, bool, string?)[]
        {
            ("PoleOccupancyId,SlotIndex", true, PoleOccupancyEntryRecordConfiguration.RegulatedSlotFilter),
            ("PoleOccupancyId,OrganizationId", false, null),
            ("OccupantOrganizationId", false, null),
            ("OrganizationId", false, null),
        });
        PoleOccupancyEntryRecordConfiguration.RegulatedSlotFilter.Should().Be("\"SlotIndex\" IS NOT NULL");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EntryCheckConstraint_AllowsNoSlotOrOneToSix(bool postgres)
    {
        var check = Entry(postgres).GetCheckConstraints().Should().ContainSingle().Subject;

        check.Name.Should().Be("CK_field_pole_occupancy_entries_SlotIndex");
        check.Sql.Should().Be("\"SlotIndex\" IS NULL OR \"SlotIndex\" BETWEEN 1 AND 6");
        Core.Isp.NeutralOperator.PoleAttachmentSlotMatrix.MaxSlots.Should().Be(6, "the check constraint spells the limit out");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void EntryForeignKey_PointsAtThePoleAndItsOrganization_Required_WithCascadeDelete(bool postgres)
    {
        var foreignKey = Entry(postgres).GetForeignKeys().Should().ContainSingle().Subject;

        foreignKey.PrincipalEntityType.ClrType.Should().Be<PoleOccupancyRecord>();
        foreignKey.Properties.Select(p => p.Name).Should().Equal(
            nameof(PoleOccupancyEntryRecord.PoleOccupancyId), nameof(PoleOccupancyEntryRecord.OrganizationId));
        foreignKey.PrincipalKey.Properties.Select(p => p.Name).Should().Equal(
            nameof(PoleOccupancyRecord.Id), nameof(PoleOccupancyRecord.OrganizationId));
        foreignKey.GetConstraintName().Should().Be("FK_field_pole_occupancy_entries_field_pole_occupancies");
        foreignKey.IsRequired.Should().BeTrue();
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Columns_HaveTheAgreedLengthsPrecisionAndNullability(bool postgres)
    {
        static Dictionary<string, (int? MaxLength, int? Precision, int? Scale, bool IsNullable)> Describe(IEntityType entityType) =>
            entityType.GetProperties().ToDictionary(
                p => p.Name, p => (p.GetMaxLength(), p.GetPrecision(), p.GetScale(), p.IsNullable), StringComparer.Ordinal);

        Describe(Pole(postgres)).Should().BeEquivalentTo(new Dictionary<string, (int?, int?, int?, bool)>(StringComparer.Ordinal)
        {
            ["Id"] = (null, null, null, false),
            ["PoleFeatureId"] = (64, null, null, false),
            ["OrganizationId"] = (null, null, null, false),
            ["Version"] = (null, null, null, false),
            ["UpdatedAt"] = (null, null, null, false),
        });
        Describe(Entry(postgres)).Should().BeEquivalentTo(new Dictionary<string, (int?, int?, int?, bool)>(StringComparer.Ordinal)
        {
            ["Id"] = (null, null, null, false),
            ["PoleOccupancyId"] = (null, null, null, false),
            ["SlotIndex"] = (null, null, null, true),
            ["Status"] = (32, null, null, false),
            ["OccupantOrganizationId"] = (null, null, null, true),
            ["OperatorCnpj"] = (18, null, null, true),
            ["AnatelLicenseNumber"] = (64, null, null, true),
            ["CableDiameterMm"] = (null, 8, 3, false),
            ["CableLoadDaN"] = (null, 12, 3, false),
            ["AttachedCableFeatureId"] = (64, null, null, true),
            ["OrganizationId"] = (null, null, null, false),
        });
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Version_IsAPlainAppManagedInteger_NotADatabaseGeneratedOrEfCheckedToken(bool postgres)
    {
        var version = Pole(postgres).FindProperty(nameof(PoleOccupancyRecord.Version))!;

        version.ClrType.Should().Be<int>();
        version.ValueGenerated.Should().Be(ValueGenerated.Never, "no trigger or database default maintains it");
        version.IsConcurrencyToken.Should().BeFalse("the repository compares it in its own UPDATE");
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void TenantQueryFilter_IsDeclaredOnBothTables(bool postgres)
    {
        Pole(postgres).GetDeclaredQueryFilters().Should().NotBeEmpty();
        Entry(postgres).GetDeclaredQueryFilters().Should().NotBeEmpty();
    }

    [Fact]
    public void PoleFeatureId_UsesTheByteWiseCollationOnPostgres_AndTheProviderDefaultOnSqlite()
    {
        Pole(postgres: true).FindProperty("PoleFeatureId")!.GetCollation().Should().Be("C");
        Pole(postgres: false).FindProperty("PoleFeatureId")!.GetCollation().Should().BeNull("\"C\" is not a SQLite collation; its default is already byte-wise");
        Entry(postgres: true).GetProperties().Should().OnlyContain(p => p.GetCollation() == null);
    }

    [Fact]
    public void RepositoryInsertColumns_CoverEveryMappedProperty_SoANewColumnCannotBeSilentlyLeftOutOfInserts()
    {
        EFPoleOccupancyRepository.PoleColumns.Select(column => column.Property)
            .Should().BeEquivalentTo(Pole(postgres: true).GetProperties().Select(p => p.Name));
        EFPoleOccupancyRepository.EntryColumns.Select(column => column.Property)
            .Should().BeEquivalentTo(Entry(postgres: true).GetProperties().Select(p => p.Name));
    }
}
