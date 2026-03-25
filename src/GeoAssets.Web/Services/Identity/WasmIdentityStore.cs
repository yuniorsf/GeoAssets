using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Web.Services.Identity;

/// <summary>
/// Singleton in-memory store for all identity entities in Blazor WASM.
///
/// Replaces EF Core (which cannot run in the browser).
/// In production, swap the repository implementations for HTTP clients
/// that call a secured backend API.
/// </summary>
public sealed class WasmIdentityStore
{
    public List<Organization>   Organizations   { get; } = [];
    public List<AppGroup>       Groups          { get; } = [];
    public List<UserGroup>      UserGroups      { get; } = [];
    public List<AppUser>        Users           { get; } = [];
    public List<AppRole>        Roles           { get; } = [];
    public List<AppPermission>  Permissions     { get; } = [];
    public List<UserClaim>      UserClaims      { get; } = [];
    public List<UserRole>       UserRoles       { get; } = [];
    public List<RolePermission> RolePermissions { get; } = [];
    public List<AppPolicy>      Policies        { get; } = [];
}
