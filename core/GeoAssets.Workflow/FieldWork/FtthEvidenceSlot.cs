namespace GeoAssets.Workflow.FieldWork;

/// <summary>
/// The four mandatory georeferenced photo slots XD01-278's AC names for an FTTH drop install:
/// CTO connection, drop anchor/tag, PTO+ONT, and customer sign-on-glass.
/// </summary>
public enum FtthEvidenceSlot
{
    CtoConnection,
    DropAnchorTag,
    PtoAndOnt,
    CustomerSignOnGlass,
}
