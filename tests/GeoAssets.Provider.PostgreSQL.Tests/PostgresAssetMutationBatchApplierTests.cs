using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// <see cref="PostgresAssetMutationBatchApplier"/> (XD01-337) against the collection's migrated
/// PostGIS instance: a batch lands on the features of its own organization through the provider's
/// write path, is recorded, is applied once however often it is delivered, and leaves nothing
/// behind when any part of it fails. Each test uses organizations, features and batches of its
/// own, so nothing is truncated.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresAssetMutationBatchApplierTests(PostgresContainerFixture fixture)
{
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FirstDelivery = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private PostgresAssetMutationBatchApplier NewSut(TimeProvider? clock = null) =>
        new(fixture.CreateOptions(), NullLoggerFactory.Instance, clock ?? new SettableClock(FirstDelivery));

    /// <summary>A fresh feature id within <c>geo_entity</c>'s 36 characters.</summary>
    private static string NewId(string prefix) => $"{prefix[..1]}-{Guid.NewGuid():N}";

    private static GeoFeature Pole(string id, Guid organizationId, string assetTypeId = "type-pole") => new()
    {
        Id = id,
        Geometry = new GeoPoint(-38.5, -12.9),
        Properties = new GeoFeatureProperties
        {
            Name = id,
            AssetTypeId = assetTypeId,
            OrganizationId = organizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = "P-1" },
        },
    };

    private async Task SeedAsync(params GeoFeature[] features)
    {
        var options = fixture.CreateOptions();
        await using var provider = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        provider.AddRange(features);
    }

    private static AssetFeatureMutationItem SetAttributes(string featureId, params (string Key, string Value)[] attributes) => new(
        FeatureId: featureId,
        MutationKind: AssetMutationKind.UpdateAttributes,
        Name: string.Empty,
        AssetTypeId: "type-pole",
        Description: string.Empty,
        LayerId: string.Empty,
        Geometry: null,
        TopologyEdges: [],
        CustomAttributes: attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal));

    private static AssetMutationBatchRequestedEvent Batch(Guid organizationId, params AssetFeatureMutationItem[] mutations) =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: "default",
            organizationId: organizationId,
            requestedByService: "tests",
            performedByUserId: "approver-1",
            mutations: mutations,
            timeProvider: new SettableClock(FirstDelivery),
            batchId: Guid.NewGuid(),
            sourceOrderId: "order-1",
            correlationId: "order-1");

    /// <summary>The feature as its own organization reads it.</summary>
    private async Task<GeoFeature?> ReadAsync(string featureId, Guid organizationId)
    {
        var options = fixture.CreateOptions();
        await using var provider = new PostgresAssetProvider(
            new GeoAssetsDbContext(options, new FakeTenantContext(organizationId)), options,
            NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
        return provider.GetById(featureId);
    }

    private async Task<int> RecordsAsync(Guid batchId)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return await db.AppliedMutationBatches.CountAsync(row => row.BatchId == batchId);
    }

    private async Task<Dictionary<string, string>> AttributeRowsAsync(string featureId)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return await db.GeoEntityAttributes
            .Where(row => row.EntityId == featureId)
            .ToDictionaryAsync(row => row.Key, row => row.Value, StringComparer.Ordinal);
    }

    // ── Applying ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_WritesTheAttributesToTheOrganizationsFeature_KeepsTheOthers_AndRecordsTheBatch()
    {
        var organization = Guid.NewGuid();
        var pole = NewId("pole");
        await SeedAsync(Pole(pole, organization));
        var batch = Batch(organization, SetAttributes(pole, ("occupancyClass", "High"), ("occupantCount", "6")));

        var applied = await NewSut().ApplyAsync(batch, CancellationToken.None);

        applied.BatchId.Should().Be(batch.BatchId);
        applied.OrganizationId.Should().Be(organization);
        applied.AppliedItems.Should().ContainSingle().Which.FeatureId.Should().Be(pole);
        var feature = (await ReadAsync(pole, organization))!;
        feature.Properties.CustomAttributes.Should().Equal(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["plate"] = "P-1",
            ["occupancyClass"] = "High",
            ["occupantCount"] = "6",
        });
        feature.Properties.UpdatedAt.Should().Be(FirstDelivery.UtcDateTime);
        feature.Properties.OrganizationId.Should().Be(organization);
        (await AttributeRowsAsync(pole)).Should().Contain("occupancyClass", "High", "the indexed attribute copy is written too");
        (await RecordsAsync(batch.BatchId)).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_TheSameBatchDeliveredAgain_ChangesNothing_AndReturnsTheFirstResult()
    {
        var organization = Guid.NewGuid();
        var pole = NewId("pole");
        await SeedAsync(Pole(pole, organization));
        var batch = Batch(organization, SetAttributes(pole, ("occupancyClass", "Low")));
        var clock = new SettableClock(FirstDelivery);
        var sut = NewSut(clock);
        var first = await sut.ApplyAsync(batch, CancellationToken.None);
        var afterFirst = (await ReadAsync(pole, organization))!;

        clock.Now = FirstDelivery.AddHours(1);
        var second = await sut.ApplyAsync(batch, CancellationToken.None);

        second.AppliedAtUtc.Should().Be(first.AppliedAtUtc);
        second.AppliedItems.Should().BeEquivalentTo(first.AppliedItems);
        var afterSecond = (await ReadAsync(pole, organization))!;
        afterSecond.Properties.UpdatedAt.Should().Be(afterFirst.Properties.UpdatedAt, "the feature was not written again");
        afterSecond.Properties.CustomAttributes.Should().Equal(afterFirst.Properties.CustomAttributes);
        (await RecordsAsync(batch.BatchId)).Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_AFeatureOfAnotherOrganization_IsNotFound_AndNothingIsWritten()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var pole = NewId("pole");
        await SeedAsync(Pole(pole, owner));
        var batch = Batch(other, SetAttributes(pole, ("occupancyClass", "High")));

        var act = () => NewSut().ApplyAsync(batch, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await ReadAsync(pole, owner))!.Properties.CustomAttributes.Should().NotContainKey("occupancyClass");
        (await RecordsAsync(batch.BatchId)).Should().Be(0, "a batch that failed is not recorded, so its retry applies it");
    }

    [Fact]
    public async Task ApplyAsync_AMutationThatFails_RollsBackTheMutationsBeforeIt()
    {
        var organization = Guid.NewGuid();
        var pole = NewId("pole");
        await SeedAsync(Pole(pole, organization));
        var batch = Batch(
            organization,
            SetAttributes(pole, ("occupancyClass", "High")),
            SetAttributes(NewId("missing"), ("occupancyClass", "Low")));

        var act = () => NewSut().ApplyAsync(batch, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await ReadAsync(pole, organization))!.Properties.CustomAttributes.Should().NotContainKey("occupancyClass");
        (await AttributeRowsAsync(pole)).Should().NotContainKey("occupancyClass");
        (await RecordsAsync(batch.BatchId)).Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_AnAttributeTheAssetTypesSchemaRejects_GoesThroughTheValidatingProvider_AndNothingIsWritten()
    {
        var organization = Guid.NewGuid();
        var assetType = new AssetType
        {
            Id = Guid.NewGuid(),
            Name = "Validated pole",
            OrganizationId = organization,
            AttributesSchemaJson = """{"type":"object","properties":{"occupantCount":{"type":"integer"}}}""",
        };
        var options = fixture.CreateOptions();
        await using (var seed = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System))
            seed.AddAssetType(assetType);
        var pole = NewId("pole");
        await SeedAsync(Pole(pole, organization, assetType.Id.ToString()));
        var batch = Batch(organization, SetAttributes(pole, ("occupantCount", "many")));

        var act = () => NewSut().ApplyAsync(batch, CancellationToken.None);

        await act.Should().ThrowAsync<GeoFeatureAttributeValidationException>();
        (await ReadAsync(pole, organization))!.Properties.CustomAttributes.Should().NotContainKey("occupantCount");
        (await RecordsAsync(batch.BatchId)).Should().Be(0);
    }

    // ── Guards ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_ABatchWithoutAnOrganization_IsRefusedBeforeAnyRead()
    {
        var batch = Batch(Guid.Empty, SetAttributes(NewId("pole"), ("occupancyClass", "High")));

        var act = () => NewSut().ApplyAsync(batch, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).Which.ParamName.Should().Be("batch");
        (await RecordsAsync(batch.BatchId)).Should().Be(0);
    }

    [Fact]
    public Task ApplyAsync_NullBatch_Throws()
    {
        var act = () => NewSut().ApplyAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ApplyAsync_ACancelledToken_ThrowsBeforeTouchingTheDatabase()
    {
        var organization = Guid.NewGuid();
        var pole = NewId("pole");
        await SeedAsync(Pole(pole, organization));
        var batch = Batch(organization, SetAttributes(pole, ("occupancyClass", "High")));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => NewSut().ApplyAsync(batch, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await RecordsAsync(batch.BatchId)).Should().Be(0);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var options = fixture.CreateOptions();

        ((Action)(() => _ = new PostgresAssetMutationBatchApplier(null!, NullLoggerFactory.Instance, TimeProvider.System)))
            .Should().Throw<ArgumentNullException>().WithParameterName("dbOptions");
        ((Action)(() => _ = new PostgresAssetMutationBatchApplier(options, null!, TimeProvider.System)))
            .Should().Throw<ArgumentNullException>().WithParameterName("loggerFactory");
        ((Action)(() => _ = new PostgresAssetMutationBatchApplier(options, NullLoggerFactory.Instance, null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPostgresAssetMutationBatchApplier_RegistersOneSingletonAsItselfAndAsThePort()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddPostgresAssetMutationBatchApplier(fixture.ConnectionString);

        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var concrete = provider.GetRequiredService<PostgresAssetMutationBatchApplier>();
        provider.GetRequiredService<IAssetMutationBatchApplier>().Should().BeSameAs(concrete);
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddPostgresAssetMutationBatchApplier_InvalidArguments_Throw()
    {
        ((Action)(() => ((IServiceCollection)null!).AddPostgresAssetMutationBatchApplier("Host=x")))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddPostgresAssetMutationBatchApplier(" ")))
            .Should().Throw<ArgumentException>();
    }

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
