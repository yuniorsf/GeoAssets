namespace GeoAssets.Workflow.Rules;

internal sealed class MemberTokenFixture
{
    public static int ContractorCount => 0;

    public static Guid Apply(Guid manholeId) => manholeId;
}
