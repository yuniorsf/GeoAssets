using FluentAssertions;
using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Models;
using Xunit;

namespace GeoAssets.IpBilling.Tests.GrantSync;

public sealed class MunicipioGrantSyncPlannerTests
{
    private const string Ibge = "3550308";
    private const string OtherIbge = "3304557";

    private static readonly Guid MunicipioId = new("11111111-0000-4000-8000-000000000395");
    private static readonly Guid Distribuidora = new("d0d0d0d0-0000-4000-8000-000000000395");
    private static readonly Guid OtherDistribuidora = new("d1d1d1d1-0000-4000-8000-000000000395");
    private static readonly Guid PrefeituraA = new("a0a0a0a0-0000-4000-8000-000000000395");
    private static readonly Guid PrefeituraB = new("b0b0b0b0-0000-4000-8000-000000000395");

    private static MunicipioGrantLink Link(Guid? prefeitura, string ibge = Ibge, Guid? distribuidora = null) =>
        new(MunicipioId, ibge, distribuidora ?? Distribuidora, prefeitura);

    // ── created ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_CreatedWithAPrefeitura_IsASyncOfItsKey()
    {
        var change = MunicipioGrantSyncPlanner.Plan(before: null, Link(PrefeituraA));

        change.Should().Be(new MunicipioGrantChange(MunicipioId, MunicipioGrantSyncKind.Sync, Ibge, Distribuidora, PrefeituraA));
    }

    [Fact]
    public void Plan_CreatedWithoutAPrefeitura_IsNothing()
    {
        MunicipioGrantSyncPlanner.Plan(before: null, Link(null)).Should().BeNull();
    }

    [Fact]
    public void Plan_NeitherBeforeNorAfter_IsNothing()
    {
        MunicipioGrantSyncPlanner.Plan(before: null, after: null).Should().BeNull();
    }

    // ── deleted ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_DeletedWithAPrefeitura_IsARevokeOfItsKey()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA), after: null);

        change.Should().Be(new MunicipioGrantChange(MunicipioId, MunicipioGrantSyncKind.Revoke, Ibge, Distribuidora, PrefeituraA));
    }

    [Fact]
    public void Plan_DeletedWithoutAPrefeitura_IsNothing()
    {
        MunicipioGrantSyncPlanner.Plan(Link(null), after: null).Should().BeNull();
    }

    // ── updated ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_UpdatedWithNothingGrantRelevantChanged_IsNothing()
    {
        MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA), Link(PrefeituraA)).Should().BeNull();
    }

    [Fact]
    public void Plan_UpdatedWithoutAPrefeituraEitherSide_IsNothing()
    {
        MunicipioGrantSyncPlanner.Plan(Link(null), Link(null)).Should().BeNull();
    }

    [Fact]
    public void Plan_UpdatedWithoutAPrefeituraEitherSide_ButAnotherIbgeCode_IsNothing()
    {
        MunicipioGrantSyncPlanner.Plan(Link(null), Link(null, OtherIbge)).Should().BeNull();
    }

    [Fact]
    public void Plan_UpdatedWithoutAPrefeituraEitherSide_ButAnotherDistribuidora_IsNothing()
    {
        MunicipioGrantSyncPlanner.Plan(Link(null), Link(null, distribuidora: OtherDistribuidora)).Should().BeNull();
    }

    [Fact]
    public void Plan_PrefeituraSetOnAnExistingMunicipio_IsASyncWithoutAPrevious()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(null), Link(PrefeituraA));

        change.Should().Be(new MunicipioGrantChange(MunicipioId, MunicipioGrantSyncKind.Sync, Ibge, Distribuidora, PrefeituraA));
    }

    [Fact]
    public void Plan_PrefeituraSetWhileTheKeyMoved_IsASyncOfTheNewKey_WithNoStaleKeyToRevoke()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(null), Link(PrefeituraA, OtherIbge, OtherDistribuidora));

        change.Should().Be(new MunicipioGrantChange(MunicipioId, MunicipioGrantSyncKind.Sync, OtherIbge, OtherDistribuidora, PrefeituraA));
    }

    [Fact]
    public void Plan_PrefeituraCleared_IsARevokeOfTheOldKey()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA), Link(null));

        change.Should().Be(new MunicipioGrantChange(MunicipioId, MunicipioGrantSyncKind.Revoke, Ibge, Distribuidora, PrefeituraA));
    }

    [Fact]
    public void Plan_PrefeituraClearedWhileTheKeyMoved_RevokesTheOldKey_NotTheNewOne()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA), Link(null, OtherIbge, OtherDistribuidora));

        change.Should().Be(new MunicipioGrantChange(MunicipioId, MunicipioGrantSyncKind.Revoke, Ibge, Distribuidora, PrefeituraA));
    }

    [Fact]
    public void Plan_PrefeituraChanged_IsASyncOfTheNewOne_NamingTheOldOne()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA), Link(PrefeituraB));

        change.Should().Be(new MunicipioGrantChange(
            MunicipioId, MunicipioGrantSyncKind.Sync, Ibge, Distribuidora, PrefeituraB, PreviousPrefeituraOrganizationId: PrefeituraA));
    }

    [Fact]
    public void Plan_IbgeCodeChangedUnderTheSamePrefeitura_IsASyncNamingTheOldKey()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA), Link(PrefeituraA, OtherIbge));

        change.Should().Be(new MunicipioGrantChange(
            MunicipioId, MunicipioGrantSyncKind.Sync, OtherIbge, Distribuidora, PrefeituraA,
            PreviousPrefeituraOrganizationId: null, PreviousIbgeCode: Ibge, PreviousDistribuidoraOrganizationId: Distribuidora));
    }

    [Fact]
    public void Plan_DistribuidoraChangedUnderTheSamePrefeitura_IsASyncNamingTheOldKey()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA), Link(PrefeituraA, distribuidora: OtherDistribuidora));

        change.Should().Be(new MunicipioGrantChange(
            MunicipioId, MunicipioGrantSyncKind.Sync, Ibge, OtherDistribuidora, PrefeituraA,
            PreviousPrefeituraOrganizationId: null, PreviousIbgeCode: Ibge, PreviousDistribuidoraOrganizationId: Distribuidora));
    }

    [Fact]
    public void Plan_PrefeituraAndKeyBothChanged_NamesTheOldPrefeituraAndTheOldKey()
    {
        var change = MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA), Link(PrefeituraB, OtherIbge, OtherDistribuidora));

        change.Should().Be(new MunicipioGrantChange(
            MunicipioId, MunicipioGrantSyncKind.Sync, OtherIbge, OtherDistribuidora, PrefeituraB,
            PreviousPrefeituraOrganizationId: PrefeituraA, PreviousIbgeCode: Ibge, PreviousDistribuidoraOrganizationId: Distribuidora));
    }

    [Fact]
    public void Plan_IbgeCodeComparison_IsOrdinal()
    {
        MunicipioGrantSyncPlanner.Plan(Link(PrefeituraA, "ABC"), Link(PrefeituraA, "abc"))
            .Should().NotBeNull("the grant scope is the exact string");
    }

    // ── the link ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Link_Of_TakesTheGrantRelevantFields()
    {
        var municipio = new Municipio
        {
            Id = MunicipioId,
            IbgeCode = Ibge,
            Name = "São Paulo",
            DistribuidoraOrganizationId = Distribuidora,
            PrefeituraOrganizationId = PrefeituraA,
        };

        MunicipioGrantLink.Of(municipio).Should().Be(new MunicipioGrantLink(MunicipioId, Ibge, Distribuidora, PrefeituraA));
    }

    [Fact]
    public void Link_Of_Null_Throws()
    {
        var act = () => MunicipioGrantLink.Of(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Municipio_ByDefault_ReportsNoGrantSync()
    {
        var municipio = new Municipio { IbgeCode = Ibge, Name = "São Paulo", DistribuidoraOrganizationId = Distribuidora };

        municipio.GrantSyncState.Should().Be(MunicipioGrantSyncState.None);
        municipio.GrantSyncError.Should().BeNull();
        municipio.GrantSyncedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(MunicipioGrantSyncState.None, "\"none\"")]
    [InlineData(MunicipioGrantSyncState.Pending, "\"pending\"")]
    [InlineData(MunicipioGrantSyncState.Synced, "\"synced\"")]
    [InlineData(MunicipioGrantSyncState.Failed, "\"failed\"")]
    public void State_SerializesLowercase_AndRoundTrips(MunicipioGrantSyncState state, string json)
    {
        System.Text.Json.JsonSerializer.Serialize(state).Should().Be(json);
        System.Text.Json.JsonSerializer.Deserialize<MunicipioGrantSyncState>(json).Should().Be(state);
    }
}
