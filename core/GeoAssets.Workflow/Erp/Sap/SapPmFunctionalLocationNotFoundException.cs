namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>Thrown by <see cref="SapPmOrderSpawnService.SpawnFromNotificationAsync"/> when a notification's <c>FunctionalLocation</c> doesn't resolve to any known <c>geo_entity</c>.</summary>
public sealed class SapPmFunctionalLocationNotFoundException : Exception
{
    public SapPmFunctionalLocationNotFoundException() { }

    public SapPmFunctionalLocationNotFoundException(string message) : base(message) { }

    public SapPmFunctionalLocationNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
