using FluentAssertions;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// Cross-tenant data-leakage suite (XD01-204): runs raw ADO.NET SQL — never EF Core, so these
/// prove the Row-Level Security policies themselves, not any application-layer query filter —
/// across multiple fake tenants against every table this suite knows to be org-scoped, as a
/// non-owner Postgres role (RLS doesn't restrict a table's owner without FORCE ROW LEVEL
/// SECURITY, deliberately not set on any of these tables yet).
///
/// <see cref="SecurityTestContainerFixture"/> applies the real production EF Core migrations
/// (<c>Database.MigrateAsync()</c>), not a hand-copied schema+policy, so if any RLS migration
/// were ever dropped or misconfigured, these tests fail — that's the whole point of this suite
/// existing as its own project, decoupled from any single table's own feature tests.
/// </summary>
public sealed class CrossTenantRowLevelSecurityTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    // ── geo_entity ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GeoEntity_SelectAcrossTenants_OnlyReturnsOwnTenantsRows()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE geo_entity CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
                VALUES ('geo-a', 'Asset A', 'type-a', @orgA), ('geo-b', 'Asset B', 'type-a', @orgB);
                """;
            insert.Parameters.Add(new NpgsqlParameter("orgA", TenantA));
            insert.Parameters.Add(new NpgsqlParameter("orgB", TenantB));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT \"Id\" FROM geo_entity ORDER BY \"Id\"";
        var ids = new List<string>();
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                ids.Add(reader.GetString(0));

        ids.Should().Equal("geo-a");
    }

    [Fact]
    public async Task GeoEntity_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE geo_entity CASCADE";
            await truncate.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
                VALUES ('geo-c', 'Asset C', 'type-a', @orgId);
                """;
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantB));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501", because: "WITH CHECK must reject a row claiming a different tenant than the session's own, even via raw SQL");
    }

    // ── asset_type (XD01-216/XD01-288) ───────────────────────────────────────

    [Fact]
    public async Task AssetType_SelectAcrossTenants_OnlyReturnsOwnTenantsRowsAndGlobalBuiltIns()
    {
        var globalId = Guid.NewGuid();
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var allIds = new[] { globalId, tenantAId, tenantBId };

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO asset_type ("Id", "Name", "IsBuiltIn", "IsProtected", "OrganizationId")
                VALUES
                    (@globalId, 'Global Built-In', true, true, '00000000-0000-0000-0000-000000000000'),
                    (@tenantAId, 'Tenant A Custom', false, false, @orgA),
                    (@tenantBId, 'Tenant B Custom', false, false, @orgB);
                """;
            insert.Parameters.Add(new NpgsqlParameter("globalId", globalId));
            insert.Parameters.Add(new NpgsqlParameter("tenantAId", tenantAId));
            insert.Parameters.Add(new NpgsqlParameter("tenantBId", tenantBId));
            insert.Parameters.Add(new NpgsqlParameter("orgA", TenantA));
            insert.Parameters.Add(new NpgsqlParameter("orgB", TenantB));
            await insert.ExecuteNonQueryAsync();
        }

        try
        {
            await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
            await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

            await using var select = connection.CreateCommand();
            select.CommandText = "SELECT \"Id\" FROM asset_type WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\"";
            select.Parameters.Add(new NpgsqlParameter("ids", allIds));
            var ids = new List<Guid>();
            await using (var reader = await select.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    ids.Add(reader.GetGuid(0));

            ids.Should().BeEquivalentTo([globalId, tenantAId],
                "tenant A must see its own custom asset type plus the global built-in, but never tenant B's");
        }
        finally
        {
            await using var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync();
            await using var cleanup = owner.CreateCommand();
            cleanup.CommandText = "DELETE FROM asset_type WHERE \"Id\" = ANY(@ids)";
            cleanup.Parameters.Add(new NpgsqlParameter("ids", allIds));
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task AssetType_InsertClaimingGlobalOwnership_ViolatesRowSecurityPolicy()
    {
        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO asset_type ("Id", "Name", "IsBuiltIn", "IsProtected", "OrganizationId")
                VALUES (@id, 'Rogue Global Type', false, false, '00000000-0000-0000-0000-000000000000');
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501",
                because: "a tenant must never mint a new asset type visible to every other tenant by claiming OrganizationId = Guid.Empty");
    }

    [Fact]
    public async Task AssetType_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy()
    {
        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO asset_type ("Id", "Name", "IsBuiltIn", "IsProtected", "OrganizationId")
                VALUES (@id, 'Impersonated Type', false, false, @orgId);
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantB));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501");
    }

    // ── ServiceOrders ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ServiceOrders_SelectAcrossTenants_OnlyReturnsOwnTenantsRows()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE \"ServiceOrders\" CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO "ServiceOrders"
                    ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
                VALUES
                    ('order-a', 'Order A', '', 'type-a', 'Draft', 0, 'tester', @orgA, now(), '{}', '[]'),
                    ('order-b', 'Order B', '', 'type-a', 'Draft', 0, 'tester', @orgB, now(), '{}', '[]');
                """;
            insert.Parameters.Add(new NpgsqlParameter("orgA", TenantA));
            insert.Parameters.Add(new NpgsqlParameter("orgB", TenantB));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantB);

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT \"Id\" FROM \"ServiceOrders\" ORDER BY \"Id\"";
        var ids = new List<string>();
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                ids.Add(reader.GetString(0));

        ids.Should().Equal("order-b");
    }

    [Fact]
    public async Task ServiceOrders_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE \"ServiceOrders\" CASCADE";
            await truncate.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO "ServiceOrders"
                    ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
                VALUES
                    ('order-c', 'Order C', '', 'type-a', 'Draft', 0, 'tester', @orgId, now(), '{}', '[]');
                """;
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantB));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501");
    }

    // ── ContractorUsLedgerEntries ─────────────────────────────────────────────

    [Fact]
    public async Task ContractorUsLedgerEntries_SelectAcrossTenants_OnlyReturnsOwnTenantsRows()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE \"ContractorUsLedgerEntries\" CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO "ContractorUsLedgerEntries"
                    ("Id", "OrganizationId", "ServiceOrderId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt")
                VALUES
                    (@idA, @orgA, NULL, 'US-A', 'Entry A', 1, 100, '[]', now()),
                    (@idB, @orgB, NULL, 'US-B', 'Entry B', 1, 100, '[]', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("idA", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgA", TenantA));
            insert.Parameters.Add(new NpgsqlParameter("idB", idB));
            insert.Parameters.Add(new NpgsqlParameter("orgB", TenantB));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT \"Id\" FROM \"ContractorUsLedgerEntries\" ORDER BY \"Id\"";
        var ids = new List<Guid>();
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                ids.Add(reader.GetGuid(0));

        ids.Should().Equal(idA);
    }

    [Fact]
    public async Task ContractorUsLedgerEntries_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE \"ContractorUsLedgerEntries\" CASCADE";
            await truncate.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO "ContractorUsLedgerEntries"
                    ("Id", "OrganizationId", "ServiceOrderId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt")
                VALUES
                    (@id, @orgId, NULL, 'US-C', 'Entry C', 1, 100, '[]', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantB));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501");
    }

    [Fact]
    public async Task ContractorUsLedgerEntries_DeleteAcrossTenants_AffectsNoRows()
    {
        var idA = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE \"ContractorUsLedgerEntries\" CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO "ContractorUsLedgerEntries"
                    ("Id", "OrganizationId", "ServiceOrderId", "UsCode", "Description", "Quantity", "UnitValue", "PhotoProofJson", "CreatedAt")
                VALUES (@id, @orgId, NULL, 'US-A', 'Entry A', 1, 100, '[]', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantA));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantB);

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM \"ContractorUsLedgerEntries\" WHERE \"Id\" = @id";
        delete.Parameters.Add(new NpgsqlParameter("id", idA));
        var affected = await delete.ExecuteNonQueryAsync();

        affected.Should().Be(0, "a different tenant's raw-SQL DELETE must silently affect zero rows, never another tenant's row");
    }

    // ── field_manhole_audits (XD01-265) ───────────────────────────────────────

    [Fact]
    public async Task ManholeAudits_SelectAcrossTenants_OnlyReturnsOwnTenantsRows()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE field_manhole_audits CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO field_manhole_audits
                    ("Id", "ConduitFeatureId", "OrganizationId", "BoreOccupancyPercentage", "SubDuctsJson", "SpliceEnclosureCondition", "Nr33ChecklistAttributesJson", "AuditedBy", "AuditedAtUtc")
                VALUES
                    (@idA, 'conduit-a', @orgA, 50, '[]', 'Good', '{}', 'tech-a', now()),
                    (@idB, 'conduit-b', @orgB, 50, '[]', 'Good', '{}', 'tech-b', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("idA", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgA", TenantA));
            insert.Parameters.Add(new NpgsqlParameter("idB", idB));
            insert.Parameters.Add(new NpgsqlParameter("orgB", TenantB));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT \"Id\" FROM field_manhole_audits ORDER BY \"Id\"";
        var ids = new List<Guid>();
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                ids.Add(reader.GetGuid(0));

        ids.Should().Equal(idA);
    }

    [Fact]
    public async Task ManholeAudits_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE field_manhole_audits CASCADE";
            await truncate.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO field_manhole_audits
                    ("Id", "ConduitFeatureId", "OrganizationId", "BoreOccupancyPercentage", "SubDuctsJson", "SpliceEnclosureCondition", "Nr33ChecklistAttributesJson", "AuditedBy", "AuditedAtUtc")
                VALUES (@id, 'conduit-c', @orgId, 50, '[]', 'Good', '{}', 'tech-c', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantB));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501");
    }

    [Fact]
    public async Task ManholeAudits_DeleteAcrossTenants_AffectsNoRows()
    {
        var idA = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE field_manhole_audits CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO field_manhole_audits
                    ("Id", "ConduitFeatureId", "OrganizationId", "BoreOccupancyPercentage", "SubDuctsJson", "SpliceEnclosureCondition", "Nr33ChecklistAttributesJson", "AuditedBy", "AuditedAtUtc")
                VALUES (@id, 'conduit-a', @orgId, 50, '[]', 'Good', '{}', 'tech-a', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantA));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantB);

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM field_manhole_audits WHERE \"Id\" = @id";
        delete.Parameters.Add(new NpgsqlParameter("id", idA));
        var affected = await delete.ExecuteNonQueryAsync();

        affected.Should().Be(0, "a different tenant's raw-SQL DELETE must silently affect zero rows, never another tenant's row");
    }

    // ── applied_mutation_batches (XD01-296) ───────────────────────────────────

    [Fact]
    public async Task AppliedMutationBatches_SelectAcrossTenants_OnlyReturnsOwnTenantsRows()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE applied_mutation_batches CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO applied_mutation_batches
                    ("BatchId", "CatalogInstanceId", "OrganizationId", "RequestedByService", "AppliedByUserId", "AppliedItemsJson", "AppliedAtUtc")
                VALUES
                    (@idA, 'catalog-1', @orgA, 'svc-a', 'user-a', '[]', now()),
                    (@idB, 'catalog-1', @orgB, 'svc-b', 'user-b', '[]', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("idA", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgA", TenantA));
            insert.Parameters.Add(new NpgsqlParameter("idB", idB));
            insert.Parameters.Add(new NpgsqlParameter("orgB", TenantB));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT \"BatchId\" FROM applied_mutation_batches ORDER BY \"BatchId\"";
        var ids = new List<Guid>();
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                ids.Add(reader.GetGuid(0));

        ids.Should().Equal(idA);
    }

    [Fact]
    public async Task AppliedMutationBatches_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE applied_mutation_batches CASCADE";
            await truncate.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO applied_mutation_batches
                    ("BatchId", "CatalogInstanceId", "OrganizationId", "RequestedByService", "AppliedByUserId", "AppliedItemsJson", "AppliedAtUtc")
                VALUES (@id, 'catalog-1', @orgId, 'svc-c', 'user-c', '[]', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantB));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501");
    }

    [Fact]
    public async Task AppliedMutationBatches_DeleteAcrossTenants_AffectsNoRows()
    {
        var idA = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE applied_mutation_batches CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO applied_mutation_batches
                    ("BatchId", "CatalogInstanceId", "OrganizationId", "RequestedByService", "AppliedByUserId", "AppliedItemsJson", "AppliedAtUtc")
                VALUES (@id, 'catalog-1', @orgId, 'svc-a', 'user-a', '[]', now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantA));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantB);

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM applied_mutation_batches WHERE \"BatchId\" = @id";
        delete.Parameters.Add(new NpgsqlParameter("id", idA));
        var affected = await delete.ExecuteNonQueryAsync();

        affected.Should().Be(0, "a different tenant's raw-SQL DELETE must silently affect zero rows, never another tenant's row");
    }

    // ── field_icerp_paving_measurements (XD01-297) ────────────────────────────

    /// <summary>
    /// Unlike every other table in this suite, <c>field_icerp_paving_measurements</c> has real FK
    /// constraints to <c>"ServiceOrders"</c> (parent + child), so each test seeds the referenced
    /// orders first via <c>ON CONFLICT ... DO NOTHING</c> (idempotent, and deliberately not a
    /// <c>"ServiceOrders"</c> truncate — that would cascade-wipe every other test's fixture data
    /// sharing this table).
    /// </summary>
    private static async Task SeedServiceOrderAsync(NpgsqlConnection owner, string id, Guid organizationId)
    {
        await using var insert = owner.CreateCommand();
        insert.CommandText = """
            INSERT INTO "ServiceOrders"
                ("Id", "Title", "Description", "OrderTypeId", "Status", "Priority", "CreatedBy", "OrganizationId", "CreatedAt", "AttributesJson", "FeatureIdsJson")
            VALUES (@id, @id, '', 'type-a', 'Draft', 0, 'tester', @orgId, now(), '{}', '[]')
            ON CONFLICT ("Id") DO NOTHING;
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task PavingMeasurements_SelectAcrossTenants_OnlyReturnsOwnTenantsRows()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE field_icerp_paving_measurements CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await SeedServiceOrderAsync(owner, "paving-parent-a", TenantA);
            await SeedServiceOrderAsync(owner, "paving-child-a", TenantA);
            await SeedServiceOrderAsync(owner, "paving-parent-b", TenantB);
            await SeedServiceOrderAsync(owner, "paving-child-b", TenantB);

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO field_icerp_paving_measurements
                    ("Id", "ParentServiceOrderId", "ChildServiceOrderId", "OrganizationId", "LengthMeters", "WidthMeters", "DepthMeters", "RecordedAreaSquareMeters", "ActualAreaSquareMeters", "VolumeCubicMeters", "RecordedAtUtc")
                VALUES
                    (@idA, 'paving-parent-a', 'paving-child-a', @orgA, 2, 1, 0.5, 2, 2, 1, now()),
                    (@idB, 'paving-parent-b', 'paving-child-b', @orgB, 2, 1, 0.5, 2, 2, 1, now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("idA", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgA", TenantA));
            insert.Parameters.Add(new NpgsqlParameter("idB", idB));
            insert.Parameters.Add(new NpgsqlParameter("orgB", TenantB));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT \"Id\" FROM field_icerp_paving_measurements ORDER BY \"Id\"";
        var ids = new List<Guid>();
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                ids.Add(reader.GetGuid(0));

        ids.Should().Equal(idA);
    }

    [Fact]
    public async Task PavingMeasurements_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE field_icerp_paving_measurements CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await SeedServiceOrderAsync(owner, "paving-parent-c", TenantA);
            await SeedServiceOrderAsync(owner, "paving-child-c", TenantA);
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO field_icerp_paving_measurements
                    ("Id", "ParentServiceOrderId", "ChildServiceOrderId", "OrganizationId", "LengthMeters", "WidthMeters", "DepthMeters", "RecordedAreaSquareMeters", "ActualAreaSquareMeters", "VolumeCubicMeters", "RecordedAtUtc")
                VALUES (@id, 'paving-parent-c', 'paving-child-c', @orgId, 2, 1, 0.5, 2, 2, 1, now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantB));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501");
    }

    [Fact]
    public async Task PavingMeasurements_DeleteAcrossTenants_AffectsNoRows()
    {
        var idA = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE field_icerp_paving_measurements CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await SeedServiceOrderAsync(owner, "paving-parent-a", TenantA);
            await SeedServiceOrderAsync(owner, "paving-child-a", TenantA);

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO field_icerp_paving_measurements
                    ("Id", "ParentServiceOrderId", "ChildServiceOrderId", "OrganizationId", "LengthMeters", "WidthMeters", "DepthMeters", "RecordedAreaSquareMeters", "ActualAreaSquareMeters", "VolumeCubicMeters", "RecordedAtUtc")
                VALUES (@id, 'paving-parent-a', 'paving-child-a', @orgId, 2, 1, 0.5, 2, 2, 1, now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantA));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantB);

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM field_icerp_paving_measurements WHERE \"Id\" = @id";
        delete.Parameters.Add(new NpgsqlParameter("id", idA));
        var affected = await delete.ExecuteNonQueryAsync();

        affected.Should().Be(0, "a different tenant's raw-SQL DELETE must silently affect zero rows, never another tenant's row");
    }

    // ── UsCatalogItems (XD01-298) ──────────────────────────────────────────────

    [Fact]
    public async Task UsCatalogItems_SelectAcrossTenants_OnlyReturnsOwnTenantsRows()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE \"UsCatalogItems\" CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO "UsCatalogItems"
                    ("Id", "OrganizationId", "ItemCode", "Description", "Unit", "BaseUsFactor", "RegionalMultiplier", "MonetaryConversionRate", "EffectiveFrom")
                VALUES
                    (@idA, @orgA, 'US-EL-014', 'Poste concreto', 'un', 1.0, 1.0, 10.5, now()),
                    (@idB, @orgB, 'US-EL-014', 'Poste concreto', 'un', 1.0, 1.0, 12.0, now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("idA", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgA", TenantA));
            insert.Parameters.Add(new NpgsqlParameter("idB", idB));
            insert.Parameters.Add(new NpgsqlParameter("orgB", TenantB));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT \"Id\" FROM \"UsCatalogItems\" ORDER BY \"Id\"";
        var ids = new List<Guid>();
        await using (var reader = await select.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                ids.Add(reader.GetGuid(0));

        ids.Should().Equal(idA);
    }

    [Fact]
    public async Task UsCatalogItems_InsertUnderAnotherTenant_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE \"UsCatalogItems\" CASCADE";
            await truncate.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantA);

        var act = async () =>
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO "UsCatalogItems"
                    ("Id", "OrganizationId", "ItemCode", "Description", "Unit", "BaseUsFactor", "RegionalMultiplier", "MonetaryConversionRate", "EffectiveFrom")
                VALUES (@id, @orgId, 'US-EL-014', 'Poste concreto', 'un', 1.0, 1.0, 10.5, now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantB));
            await insert.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501");
    }

    [Fact]
    public async Task UsCatalogItems_DeleteAcrossTenants_AffectsNoRows()
    {
        var idA = Guid.NewGuid();

        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await using var truncate = owner.CreateCommand();
            truncate.CommandText = "TRUNCATE TABLE \"UsCatalogItems\" CASCADE";
            await truncate.ExecuteNonQueryAsync();

            await using var insert = owner.CreateCommand();
            insert.CommandText = """
                INSERT INTO "UsCatalogItems"
                    ("Id", "OrganizationId", "ItemCode", "Description", "Unit", "BaseUsFactor", "RegionalMultiplier", "MonetaryConversionRate", "EffectiveFrom")
                VALUES (@id, @orgId, 'US-EL-014', 'Poste concreto', 'un', 1.0, 1.0, 10.5, now());
                """;
            insert.Parameters.Add(new NpgsqlParameter("id", idA));
            insert.Parameters.Add(new NpgsqlParameter("orgId", TenantA));
            await insert.ExecuteNonQueryAsync();
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TenantB);

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM \"UsCatalogItems\" WHERE \"Id\" = @id";
        delete.Parameters.Add(new NpgsqlParameter("id", idA));
        var affected = await delete.ExecuteNonQueryAsync();

        affected.Should().Be(0, "a different tenant's raw-SQL DELETE must silently affect zero rows, never another tenant's row");
    }

    // ── ServiceOrderTargets (XD01-306) ────────────────────────────────────────
    //
    // A target follows its parent order: the policy's USING clause is "own organization, or the
    // parent order is visible", where "visible" is whatever "ServiceOrders"' own policy grants
    // the querying role. These facts use their own three organizations and their own order ids,
    // and never truncate "ServiceOrders", so they neither disturb nor depend on the facts above.

    private static readonly Guid TargetOwner = Guid.Parse("c1c1c1c1-0000-0000-0000-000000000001");
    private static readonly Guid TargetGrantee = Guid.Parse("c2c2c2c2-0000-0000-0000-000000000002");
    private static readonly Guid TargetStranger = Guid.Parse("c3c3c3c3-0000-0000-0000-000000000003");

    private const string InsertTargetSql = """
        INSERT INTO "ServiceOrderTargets"
            ("Id", "ServiceOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
        VALUES (@id, @orderId, @featureId, 'pole', '[]', 'Pending', 0, now(), 'tester', @orgId, 0)
        """;

    [Fact]
    public async Task ServiceOrderTargets_SelectAcrossTenants_OnlyReturnsTargetsOfOrdersTheTenantCanSee()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await ResetTargetFixtureAsync(owner);
            await SeedServiceOrderAsync(owner, "tgt-order-owner", TargetOwner);
            await SeedServiceOrderAsync(owner, "tgt-order-stranger", TargetStranger);
            await SeedTargetAsync(owner, "tgt-owner-1", "tgt-order-owner", TargetOwner);
            await SeedTargetAsync(owner, "tgt-owner-2", "tgt-order-owner", TargetOwner, featureId: "feat-2");
            await SeedTargetAsync(owner, "tgt-stranger-1", "tgt-order-stranger", TargetStranger);
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();

        // If the policy's sub-select on "ServiceOrders" were NOT itself filtered by that table's
        // policy for this non-owner role, every parent order would "exist" and each tenant would
        // read every target here.
        (await TargetIdsVisibleToAsync(connection, TargetOwner)).Should().Equal("tgt-owner-1", "tgt-owner-2");
        (await TargetIdsVisibleToAsync(connection, TargetStranger)).Should().Equal("tgt-stranger-1");
        (await TargetIdsVisibleToAsync(connection, TargetGrantee)).Should().BeEmpty("it owns no order and holds no grant");
    }

    [Fact]
    public async Task ServiceOrderTargets_InsertUnderAnOrderTheTenantCannotSee_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await ResetTargetFixtureAsync(owner);
            await SeedServiceOrderAsync(owner, "tgt-order-owner", TargetOwner);
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TargetStranger);

        // Claiming the order's organization, and claiming its own: neither gets a row in.
        var asTheOwner = async () => await InsertTargetAsync(connection, "tgt-forged-1", "tgt-order-owner", TargetOwner);
        var asItself = async () => await InsertTargetAsync(connection, "tgt-forged-2", "tgt-order-owner", TargetStranger);

        await asTheOwner.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        await asItself.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
    }

    [Fact]
    public async Task ServiceOrderTargets_InsertCarryingAnotherOrganizationThanItsOrder_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await ResetTargetFixtureAsync(owner);
            await SeedServiceOrderAsync(owner, "tgt-order-owner", TargetOwner);
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TargetOwner);

        var mislabelled = async () => await InsertTargetAsync(connection, "tgt-mislabelled", "tgt-order-owner", TargetStranger);
        var correct = async () => await InsertTargetAsync(connection, "tgt-correct", "tgt-order-owner", TargetOwner);

        await mislabelled.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
        await correct.Should().NotThrowAsync("the owner adding a target to its own order is the normal case");
    }

    [Fact]
    public async Task ServiceOrderTargets_UpdateAndDeleteAcrossTenants_AffectNoRows()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await ResetTargetFixtureAsync(owner);
            await SeedServiceOrderAsync(owner, "tgt-order-owner", TargetOwner);
            await SeedTargetAsync(owner, "tgt-owner-1", "tgt-order-owner", TargetOwner);
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TargetStranger);

        var updated = await ReleaseTargetsOfOrderAsync(connection, "tgt-order-owner");
        var deleted = await DeleteTargetAsync(connection, "tgt-owner-1");

        updated.Should().Be(0, "another tenant's raw-SQL UPDATE must silently affect zero rows");
        deleted.Should().Be(0, "another tenant's raw-SQL DELETE must silently affect zero rows");
        (await TargetIdsVisibleToAsync(connection, TargetOwner)).Should().Equal("tgt-owner-1");
    }

    [Fact]
    public async Task ServiceOrderTargets_UpdateMovingATargetToAnotherOrganization_ViolatesRowSecurityPolicy()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await ResetTargetFixtureAsync(owner);
            await SeedServiceOrderAsync(owner, "tgt-order-owner", TargetOwner);
            await SeedTargetAsync(owner, "tgt-owner-1", "tgt-order-owner", TargetOwner);
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TargetOwner);

        var act = async () =>
        {
            await using var update = connection.CreateCommand();
            update.CommandText = """UPDATE "ServiceOrderTargets" SET "OrganizationId" = @orgId WHERE "Id" = 'tgt-owner-1'""";
            update.Parameters.Add(new NpgsqlParameter("orgId", TargetStranger));
            await update.ExecuteNonQueryAsync();
        };

        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");
    }

    /// <summary>
    /// The grant-based half, proving the visibility is inherited from the parent order rather
    /// than re-implemented: each case changes only the <c>"OrganizationGrants"</c> row (and the
    /// order's scope), and the targets appear exactly when <c>"ServiceOrders"</c>' own policy
    /// would show the order.
    /// </summary>
    [Theory]
    [InlineData(true, false, null, null, true)]
    [InlineData(false, false, null, null, false)]
    [InlineData(true, true, null, null, false)]
    [InlineData(true, false, "AssetLayer", null, false)]
    [InlineData(true, false, "ServiceOrder", null, true)]
    [InlineData(true, false, null, "district-7", true)]
    [InlineData(true, false, null, "district-9", false)]
    public async Task ServiceOrderTargets_Grantee_SeesTheTargetsOfAnOrderExactlyWhenItsGrantShowsItThatOrder(
        bool grantIsActive, bool grantIsExpired, string? grantResourceType, string? grantScopeId, bool expectVisible)
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await ResetTargetFixtureAsync(owner);
            // The granted order lives in scope "district-7"; the stranger's order is never granted.
            await SeedServiceOrderAsync(owner, "tgt-order-owner", TargetOwner);
            await SetOrderScopeAsync(owner, "tgt-order-owner", "district-7");
            await SeedServiceOrderAsync(owner, "tgt-order-stranger", TargetStranger);
            await SeedTargetAsync(owner, "tgt-owner-1", "tgt-order-owner", TargetOwner);
            await SeedTargetAsync(owner, "tgt-stranger-1", "tgt-order-stranger", TargetStranger);
            await SeedGrantAsync(owner, TargetGrantee, TargetOwner, grantIsActive, grantIsExpired, grantResourceType, grantScopeId);
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();

        var visible = await TargetIdsVisibleToAsync(connection, TargetGrantee);

        if (expectVisible)
            visible.Should().Equal(["tgt-owner-1"], "the grant shows the grantee that order, so it shows its targets — and only that order's");
        else
            visible.Should().BeEmpty("without a grant that shows it the order, the grantee has no way to its targets");
    }

    [Fact]
    public async Task ServiceOrderTargets_Grantee_CanAddEditAndReleaseTargetsOfTheGrantedOrder_ButOnlyUnderTheOrdersOrganization()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await ResetTargetFixtureAsync(owner);
            await SeedServiceOrderAsync(owner, "tgt-order-owner", TargetOwner);
            await SeedTargetAsync(owner, "tgt-owner-1", "tgt-order-owner", TargetOwner);
            await SeedGrantAsync(owner, TargetGrantee, TargetOwner, isActive: true, isExpired: false, resourceType: null, scopeId: null);
        }

        await using var connection = await SecurityTestContainerFixture.OpenScopedRoleConnectionAsync();
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, TargetGrantee);

        var underItsOwnOrganization = async () => await InsertTargetAsync(connection, "tgt-grantee-own", "tgt-order-owner", TargetGrantee, featureId: "feat-2");
        await underItsOwnOrganization.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "42501");

        await InsertTargetAsync(connection, "tgt-grantee-added", "tgt-order-owner", TargetOwner, featureId: "feat-3");
        var released = await ReleaseTargetsOfOrderAsync(connection, "tgt-order-owner");
        var deleted = await DeleteTargetAsync(connection, "tgt-grantee-added");

        released.Should().Be(2, "whoever may close the granted order must be able to release every one of its targets");
        deleted.Should().Be(1);
    }

    [Fact]
    public async Task ServiceOrderTargets_SessionThatNeverSetAnOrganization_SeesNoRows()
    {
        await using (var owner = await SecurityTestContainerFixture.OpenOwnerConnectionAsync())
        {
            await ResetTargetFixtureAsync(owner);
            await SeedServiceOrderAsync(owner, "tgt-order-owner", TargetOwner);
            await SeedTargetAsync(owner, "tgt-owner-1", "tgt-order-owner", TargetOwner);
        }

        // Unpooled, so this is a physical session that has never had app.current_organization_id
        // set (a pooled one may carry the empty-string remnant of an earlier, reset setting).
        var unpooled = new NpgsqlConnectionStringBuilder(await SecurityTestContainerFixture.EnsureStartedAsync())
        {
            Username = SecurityTestContainerFixture.TenantScopedRole,
            Password = SecurityTestContainerFixture.TenantScopedRolePassword,
            Pooling = false,
        };
        await using var connection = new NpgsqlConnection(unpooled.ConnectionString);
        await connection.OpenAsync();

        (await TargetIdsAsync(connection)).Should().BeEmpty("with no organization the policy matches nothing — it fails closed");
    }

    /// <summary>Removes what the ServiceOrderTargets facts create — their own orders (targets cascade) and grants — and nothing else.</summary>
    private static async Task ResetTargetFixtureAsync(NpgsqlConnection owner)
    {
        await using var reset = owner.CreateCommand();
        reset.CommandText = """
            DELETE FROM "ServiceOrders" WHERE "Id" IN ('tgt-order-owner', 'tgt-order-stranger');
            DELETE FROM "OrganizationGrants" WHERE "GranteeOrganizationId" = @grantee;
            """;
        reset.Parameters.Add(new NpgsqlParameter("grantee", TargetGrantee));
        await reset.ExecuteNonQueryAsync();
    }

    private static Task SeedTargetAsync(NpgsqlConnection owner, string id, string orderId, Guid organizationId, string featureId = "feat-1") =>
        InsertTargetAsync(owner, id, orderId, organizationId, featureId);

    private static async Task InsertTargetAsync(NpgsqlConnection connection, string id, string orderId, Guid organizationId, string featureId = "feat-1")
    {
        await using var insert = connection.CreateCommand();
        insert.CommandText = InsertTargetSql;
        insert.Parameters.Add(new NpgsqlParameter("id", id));
        insert.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        insert.Parameters.Add(new NpgsqlParameter("featureId", featureId));
        insert.Parameters.Add(new NpgsqlParameter("orgId", organizationId));
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task SeedGrantAsync(
        NpgsqlConnection owner, Guid grantee, Guid resourceOrganization, bool isActive, bool isExpired, string? resourceType, string? scopeId)
    {
        await using var insert = owner.CreateCommand();
        insert.CommandText = """
            INSERT INTO "OrganizationGrants"
                ("Id", "GranteeOrganizationId", "ResourceOrganizationId", "ResourceType", "ScopeId", "AllowedActions", "GrantedBy", "GrantedAt", "IsActive", "ExpiresAt")
            VALUES (@id, @grantee, @resource, @resourceType, @scopeId, '', 'tester', now(), @isActive, @expiresAt)
            """;
        insert.Parameters.Add(new NpgsqlParameter("id", Guid.NewGuid()));
        insert.Parameters.Add(new NpgsqlParameter("grantee", grantee));
        insert.Parameters.Add(new NpgsqlParameter("resource", resourceOrganization));
        insert.Parameters.Add(new NpgsqlParameter("resourceType", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)resourceType ?? DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("scopeId", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)scopeId ?? DBNull.Value });
        insert.Parameters.Add(new NpgsqlParameter("isActive", isActive));
        insert.Parameters.Add(new NpgsqlParameter("expiresAt", NpgsqlTypes.NpgsqlDbType.TimestampTz)
        {
            Value = isExpired ? new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DBNull.Value,
        });
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> TargetIdsVisibleToAsync(NpgsqlConnection connection, Guid organizationId)
    {
        await SecurityTestContainerFixture.SetSessionOrganizationAsync(connection, organizationId);
        return await TargetIdsAsync(connection);
    }

    private static async Task<List<string>> TargetIdsAsync(NpgsqlConnection connection)
    {
        await using var select = connection.CreateCommand();
        select.CommandText = """SELECT "Id" FROM "ServiceOrderTargets" WHERE "Id" LIKE 'tgt-%' ORDER BY "Id" """;
        var ids = new List<string>();
        await using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetString(0));
        return ids;
    }

    /// <summary>What closing an order does to its targets, as raw SQL; returns the rows the role was allowed to change.</summary>
    private static async Task<int> ReleaseTargetsOfOrderAsync(NpgsqlConnection connection, string orderId)
    {
        await using var update = connection.CreateCommand();
        update.CommandText = """UPDATE "ServiceOrderTargets" SET "ReleasedAt" = now() WHERE "ServiceOrderId" = @orderId""";
        update.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        return await update.ExecuteNonQueryAsync();
    }

    private static async Task<int> DeleteTargetAsync(NpgsqlConnection connection, string id)
    {
        await using var delete = connection.CreateCommand();
        delete.CommandText = """DELETE FROM "ServiceOrderTargets" WHERE "Id" = @id""";
        delete.Parameters.Add(new NpgsqlParameter("id", id));
        return await delete.ExecuteNonQueryAsync();
    }

    /// <summary>Puts an order in a scope, the way <c>IScopeableResource.ScopeId</c> is persisted for a service order.</summary>
    private static async Task SetOrderScopeAsync(NpgsqlConnection owner, string orderId, string scopeId)
    {
        await using var update = owner.CreateCommand();
        update.CommandText = """UPDATE "ServiceOrders" SET "AttributesJson" = json_build_object('scopeId', @scopeId::text)::text WHERE "Id" = @orderId""";
        update.Parameters.Add(new NpgsqlParameter("scopeId", scopeId));
        update.Parameters.Add(new NpgsqlParameter("orderId", orderId));
        await update.ExecuteNonQueryAsync();
    }

    // ── Generic discovery: every org-scoped table must be RLS-protected ──────

    /// <summary>
    /// Scans every entity type in <see cref="GeoAssets.Provider.PostgreSQL.Data.GeoAssetsDbContext"/>'s
    /// and <see cref="GeoAssets.Workflow.Persistence.ServiceOrderDbContext"/>'s own EF models (not
    /// GeoIdentityDbContext's, even though this fixture migrates it too, purely as an incidental FK
    /// dependency of ServiceOrders' grant-aware RLS policy — GeoIdentityDbContext's own tables like
    /// <c>Groups</c> are a different subsystem, out of this ticket's stated scope) for any property
    /// named <c>OrganizationId</c> — this codebase's one consistent convention for an org-owned
    /// entity (<c>IOrgOwnedResource</c>) — and asserts its mapped table has Row Level Security
    /// enabled with at least one policy. This is XD01-204's AC: "fails loudly ... if a table is
    /// added without RLS", without needing to update this test every time a new org-scoped entity
    /// is added to either of those two DbContexts.
    ///
    /// Reads <c>pg_class</c>/<c>pg_policies</c> with no WHERE clause built from a variable (just
    /// two flat, fully literal queries), then matches rows against the EF-derived table-name set in
    /// memory — deliberately avoiding ever interpolating a table name into SQL text.
    ///
    /// <c>asset_type</c> was previously excluded here (XD01-288: it had an OrganizationId column
    /// but no RLS policy at all). Fixed by AddAssetTypeRowLevelSecurity (XD01-216's cross-tenant
    /// audit) — see <see cref="AssetType_SelectAcrossTenants_OnlyReturnsOwnTenantsRowsAndGlobalBuiltIns"/>
    /// below for its dedicated coverage, including the global-built-in-row nuance that made this
    /// table's policy not just a copy of geo_entity's.
    /// </summary>
    [Fact]
    public async Task EveryOrganizationScopedTable_HasRowLevelSecurityEnabledWithAtLeastOnePolicy()
    {
        var knownGapsTrackedSeparately = new HashSet<string>(StringComparer.Ordinal);
        var connectionString = await SecurityTestContainerFixture.EnsureStartedAsync();

        var orgScopedTables = new HashSet<string>(StringComparer.Ordinal);
        await using (var geoAssets = new GeoAssetsDbContext(SecurityTestContainerFixture.BuildGeoAssetsOptions(connectionString)))
            CollectOrganizationScopedTableNames(geoAssets.Model, orgScopedTables);
        await using (var serviceOrders = new ServiceOrderDbContext(SecurityTestContainerFixture.BuildServiceOrderOptions(connectionString)))
            CollectOrganizationScopedTableNames(serviceOrders.Model, orgScopedTables);

        orgScopedTables.Should().NotBeEmpty("this suite's own known org-scoped entities (geo_entity, ServiceOrders, ContractorUsLedgerEntries) must be discovered");

        await using var connection = await SecurityTestContainerFixture.OpenOwnerConnectionAsync();

        var rlsEnabledByTable = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using (var rlsQuery = connection.CreateCommand())
        {
            rlsQuery.CommandText = "SELECT relname, relrowsecurity FROM pg_class WHERE relkind = 'r' AND relnamespace = 'public'::regnamespace";
            await using var reader = await rlsQuery.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rlsEnabledByTable[reader.GetString(0)] = reader.GetBoolean(1);
        }

        var policyCountByTable = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var policyQuery = connection.CreateCommand())
        {
            policyQuery.CommandText = "SELECT tablename, COUNT(*) FROM pg_policies WHERE schemaname = 'public' GROUP BY tablename";
            await using var reader = await policyQuery.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                policyCountByTable[reader.GetString(0)] = reader.GetInt64(1);
        }

        foreach (var table in orgScopedTables)
        {
            if (knownGapsTrackedSeparately.Contains(table)) continue;

            rlsEnabledByTable.GetValueOrDefault(table).Should().BeTrue(
                $"table '{table}' has an OrganizationId column and must have Row Level Security enabled");
            policyCountByTable.GetValueOrDefault(table).Should().BeGreaterThan(0,
                $"table '{table}' has RLS enabled but no policy would still deny every row by default, which likely means the policy was dropped or never created");
        }
    }

    private static void CollectOrganizationScopedTableNames(IModel model, HashSet<string> tableNames)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.FindProperty("OrganizationId") is null) continue;
            var tableName = entityType.GetTableName();
            if (tableName is not null) tableNames.Add(tableName);
        }
    }
}
