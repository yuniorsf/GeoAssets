namespace GeoAssets.Ingestion.Model;

/// <summary>
/// Role of a reference from an element to another, and how it becomes a <c>TopoEdge</c>
/// (owner = the element declaring the reference unless stated otherwise):
/// <list type="table">
///   <item><term><see cref="Start"/> / <see cref="End"/></term><description><c>connected-to</c>, metadata <c>terminal</c> = 1 / 2 and <c>node</c>.</description></item>
///   <item><term><see cref="Location"/></term><description><c>mounted-on</c>.</description></item>
///   <item><term><see cref="Connection"/></term><description><c>connected-to</c>, metadata <c>node</c>.</description></item>
///   <item><term><see cref="FedBy"/></term><description>reversed: the <b>target</b> owns <c>feeds</c> → this element.</description></item>
///   <item><term><see cref="MemberOf"/></term><description><c>member-of</c>, metadata <c>group</c> = the reference qualifier.</description></item>
/// </list>
/// </summary>
public enum ReferenceRole
{
    Start,
    End,
    Location,
    Connection,
    FedBy,
    MemberOf,
}
