# Entra Role Sync — Azure CLI Setup Tutorial

This is the runbook for **XD01-60** (child of epic **XD01-59**, Identity & Access
Admin Phase 2): provisioning the Microsoft Graph automation credential that
`EntraGraphRoleAssignmentProvider` (XD01-62) will use to create/assign Entra App
Roles on GeoAssets' behalf. It's a one-time, per-environment manual setup — Azure
requires a human with tenant privilege to authorize a new credential's elevated
access at least once; no Graph call from GeoAssets itself can grant itself these
permissions.

This tutorial documents the same 8 steps as the XD01-60 ticket, but using the
Azure CLI (`az`) wherever the Graph REST API can do the job, instead of the Entra
portal or Graph Explorer — useful because Graph Explorer authenticates against
whatever tenant is your account's *home* directory by default, which silently
breaks calls meant for a separate Entra External ID (CIAM) tenant like GeoAssets'.
`az login --tenant <id>` pins the tenant explicitly and avoids that trap entirely.

Run everything below from a terminal, not from GeoAssets.Server or any app code.

## Prerequisites

- Azure CLI installed (`az --version`).
- Cloud Application Administrator, Application Administrator, or Global
  Administrator rights in the GeoAssets Entra External ID (CIAM) tenant.
- Tenant ID: `94bb6627-6a6f-4219-b6d2-ce9ca5e82215` (the same tenant
  `GeoAssets.Web`/`GeoAssets.Server` already authenticate against).

## Step 1 — Sign in to the correct tenant

```bash
az login --tenant 94bb6627-6a6f-4219-b6d2-ce9ca5e82215
```

A browser window opens for the login. **"No subscriptions found for
\<you\>@xdicor.com.br"** afterwards is expected and harmless — this CIAM tenant is
an identity directory, not a resource/subscription tenant, and Graph calls don't
need a subscription. The CLI session is still correctly scoped to the tenant.

## Step 2 — Register the new application (portal)

Not scriptable without already having the elevated permissions this credential
is meant to bootstrap, so this one step stays in the portal:

- [entra.microsoft.com](https://entra.microsoft.com) → **App registrations** →
  **+ New registration**.
- Name: `GeoAssets Role Sync (Graph automation)`.
- Supported account types: **Accounts in this organizational directory only
  (single tenant)**.
- Redirect URI: leave blank (client-credentials flow — no user ever signs into
  this app).
- **Register**.

## Step 3 — Create a client secret (portal)

- App → **Certificates & secrets** → **Client secrets** → **+ New client
  secret**.
- Description: e.g. `role-sync-local-dev`.
- Expiry: shortest option your rotation policy allows (24 months max).
- **Copy the Value column immediately** — Azure only shows it once. If you miss
  it, delete the secret and create a new one.

## Step 4 — Grant API permissions + admin consent (portal)

Also not scriptable ahead of time — admin consent is exactly the human
authorization step Azure requires before any automation can use these
permissions.

- App → **API permissions** → **+ Add a permission** → **Microsoft Graph** →
  **Application permissions**: `Application.ReadWrite.OwnedBy`,
  `AppRoleAssignment.ReadWrite.All`, `Application.Read.All`.
- **Grant admin consent for [tenant]** — confirm every row shows a green
  checkmark under Status before continuing.

## Step 5 — Resolve object IDs via the CLI

Once step 1's `az login` has landed you in the right tenant, `az ad app show`/
`az ad sp show` resolve every ID you need — no portal hunting, no Graph Explorer:

```bash
WEB_APP_OBJ_ID=$(az ad app show --id 917e27b0-188b-490f-b182-99ff1e64d1c5 --query id -o tsv)
SERVER_APP_OBJ_ID=$(az ad app show --id 3f8c9e87-59c7-4b69-bdb4-8ac1e463ed16 --query id -o tsv)
NEW_SP_OBJ_ID=$(az ad sp show --id 62a54588-a3d0-42e7-9279-28ab10627d55 --query id -o tsv)

echo "Web app object id:    $WEB_APP_OBJ_ID"
echo "Server app object id: $SERVER_APP_OBJ_ID"
echo "New SP object id:     $NEW_SP_OBJ_ID"
```

`--id` takes each app's **Application (client) ID** — `az` resolves the
directory **Object ID** from it. Confirm all three `echo` lines print a real
GUID before moving on; an empty value means that lookup used the wrong Client
ID or ran against the wrong tenant.

## Step 6 — Add the new service principal as Owner of both app registrations

This is the step that makes the scoped-down `Application.ReadWrite.OwnedBy`
(rather than tenant-wide `Application.ReadWrite.All`) sufficient for role
registration later. Done via `az rest`, which calls the same Graph endpoint
Graph Explorer would, but under the tenant pinned in step 1:

```bash
az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/applications/$WEB_APP_OBJ_ID/owners/\$ref" \
  --body "{\"@odata.id\": \"https://graph.microsoft.com/v1.0/directoryObjects/$NEW_SP_OBJ_ID\"}"

az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/applications/$SERVER_APP_OBJ_ID/owners/\$ref" \
  --body "{\"@odata.id\": \"https://graph.microsoft.com/v1.0/directoryObjects/$NEW_SP_OBJ_ID\"}"
```

Both calls return no output on success (HTTP 204). Verify:

```bash
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/applications/$WEB_APP_OBJ_ID/owners?\$select=displayName" \
  --query "value[].displayName" -o tsv

az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/applications/$SERVER_APP_OBJ_ID/owners?\$select=displayName" \
  --query "value[].displayName" -o tsv
```

Both should list `GeoAssets Role Sync (Graph automation)`.

## Step 7 — Store the secret

Never in a tracked `appsettings.json` (see the project's public-repo CIAM
secrets convention). From `apps/GeoAssets.Server`:

```bash
dotnet user-secrets set "RoleSync:TenantId"     "94bb6627-6a6f-4219-b6d2-ce9ca5e82215"
dotnet user-secrets set "RoleSync:ClientId"     "62a54588-a3d0-42e7-9279-28ab10627d55"
dotnet user-secrets set "RoleSync:ClientSecret" "<the secret Value captured in step 3>"
```

In deployed environments, use whatever secret store this project's existing
`AzureAdCiam` production secrets already live in — config key names above are
placeholders finalized by XD01-62's implementation.

## Step 8 — Sanity check

Confirm the Web app's object ID is correct and reachable via Graph, using the
same authenticated `az` session (no separate Graph Explorer sign-in needed):

```bash
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/applications/$WEB_APP_OBJ_ID?\$select=appRoles"
```

Should return the Web app's current `appRoles` array (empty array is fine —
nothing has been registered yet; XD01-62 is what starts writing to it).

## Reference — IDs captured this session

| App | Client (Application) ID | Object ID |
|---|---|---|
| GeoAssets Web | `917e27b0-188b-490f-b182-99ff1e64d1c5` | `38c6b539-f893-4751-ac73-66506b4f359b` |
| GeoAssets Server API | `3f8c9e87-59c7-4b69-bdb4-8ac1e463ed16` | `0662024e-dee9-48d9-96dd-1cd982bba16e` |
| GeoAssets Role Sync (Graph automation) | `62a54588-a3d0-42e7-9279-28ab10627d55` | SP Object ID `e9d40520-1ae6-4ea9-860d-99683b989fa5` |

Tenant ID: `94bb6627-6a6f-4219-b6d2-ce9ca5e82215`.

The client secret value is intentionally not recorded here or anywhere in the
repo — it lives only in `dotnet user-secrets` (or the deployed secret store).

## What's not automated here

Steps 2–4 (app registration, client secret creation, API permission grant +
admin consent) still require the Entra portal — each is exactly the kind of
elevated, human-authorized action Azure's security model requires at least once
per new credential, and none of it can be delegated to a credential that doesn't
exist yet. Everything after that (ID lookups, ownership assignment, sanity
check) is scriptable and shown above.

## Next

With this done, XD01-60 is complete. XD01-62 (`EntraGraphRoleAssignmentProvider`)
depends on this credential existing and consumes `RoleSync:TenantId`/`ClientId`/
`ClientSecret` from configuration to acquire its own app-only Graph token via
client-credentials flow.
