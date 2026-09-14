using System.Security.Claims;
using GeoAssets.Core.Models;

namespace GeoAssets.Server;

/// <summary>
/// Application-level authorization for <see cref="Project"/> mutations (XD01-141) — beyond
/// the plain permission + org-boundary check <see cref="OrgResourceAuthorizationHandler"/>
/// already does for any <c>IOrgOwnedResource</c>, a <c>Kind == General</c> Project mutation
/// also needs copy-on-write fork-on-edit: a caller who can see the Project
/// (<c>projects:read</c>) but lacks the specific <c>projects:manage-*</c> permission is
/// transparently redirected onto their own personal fork instead of being denied.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Resolves which <see cref="Project"/> a mutation under <paramref name="permissionCode"/>
    /// should actually apply to:
    /// <list type="bullet">
    ///   <item><paramref name="project"/> itself — the caller owns it (a <see cref="ProjectKind.User"/>
    ///     fork they created), or holds <paramref name="permissionCode"/> directly on it.</item>
    ///   <item>the caller's existing-or-newly-created personal fork — only when
    ///     <paramref name="permissionCode"/> is one of the four <c>projects:manage-*</c> codes,
    ///     <paramref name="project"/> is a <see cref="ProjectKind.General"/> Project, and the
    ///     caller at least holds <c>projects:read</c> on it.</item>
    ///   <item><c>null</c> — a hard 403 for the endpoint to return (the caller lacks even
    ///     <c>projects:read</c> on <paramref name="project"/>, or the check failed on a code
    ///     that isn't eligible for copy-on-write, e.g. <c>projects:rename</c>/<c>projects:delete</c>).</item>
    /// </list>
    /// </summary>
    Task<Project?> ResolveMutationTargetAsync(
        ClaimsPrincipal caller, Project project, string permissionCode, CancellationToken ct = default);
}
