# Entra Invitation Flow — Azure/Graph/ACS Setup Tutorial

This is the runbook for **XD01-65** (Identity & Access Admin Phase 3, part of
epic **XD01-64**): provisioning the extra Microsoft Graph permissions and the
Azure Communication Services (ACS) Email resource that invite-only
registration needs. It blocks **XD01-67** (Graph provider needs the extra
credential permissions to verify against) and **XD01-68** (email sender needs
a real ACS resource to verify against). Like XD01-60, this is a one-time,
per-environment manual setup — no code dependency.

This extends the existing **GeoAssets Role Sync** app registration from
XD01-60 rather than creating a new one, and follows the same Azure CLI (`az`)
-first style as `RoleSyncAzureSetup.md`: `az login --tenant <id>` pins the
CIAM tenant explicitly, avoiding the trap where Graph Explorer silently
authenticates against your account's *home* tenant instead.

Run everything below from a terminal, not from GeoAssets.Server or any app
code.

## Prerequisites

- Azure CLI installed (`az --version`).
- Cloud Application Administrator, Application Administrator, or Global
  Administrator rights in the GeoAssets Entra External ID (CIAM) tenant.
- Tenant ID: `94bb6627-6a6f-4219-b6d2-ce9ca5e82215`.
- The GeoAssets Role Sync app registration already exists (XD01-60): Client ID
  `62a54588-a3d0-42e7-9279-28ab10627d55`.

## Step 1 — Sign in to the correct tenant

```bash
az login --tenant 94bb6627-6a6f-4219-b6d2-ce9ca5e82215
```

**"No subscriptions found for \<you\>@xdicor.com.br"** afterwards is expected
and harmless — see `RoleSyncAzureSetup.md` for why.

## Step 2 — Add `User.ReadWrite.All` to the Role Sync app (portal)

Not scriptable ahead of time — admin consent is exactly the human
authorization step Azure requires:

- [entra.microsoft.com](https://entra.microsoft.com) → **App registrations**
  → **GeoAssets Role Sync (Graph automation)** → **API permissions** →
  **+ Add a permission** → **Microsoft Graph** → **Application permissions**
  → `User.ReadWrite.All`.
- **Grant admin consent for [tenant]** — confirm the new row shows a green
  checkmark under Status.

## Step 3 — Add the `authenticationEventsFlows` write permission (portal)

The exact scope was confirmed live against Microsoft Learn's beta API
reference for this resource, not guessed — beta-endpoint permission naming is
less stable than GA Graph areas, so re-verify against these pages if this
runbook is ever revisited far in the future:

- [Create authenticationEventsFlow](https://learn.microsoft.com/en-us/graph/api/identitycontainer-post-authenticationeventsflows?view=graph-rest-beta)
- [Update authenticationEventsFlow](https://learn.microsoft.com/en-us/graph/api/authenticationeventsflow-update?view=graph-rest-beta)

Both list a single least-privileged scope for both create and update, for
both delegated and application permission types: **`EventListener.ReadWrite.All`**.
(This is *not* `Policy.ReadWrite.AuthenticationFlows` — that permission
belongs to the unrelated v1.0 `authenticationFlowsPolicy` resource.)

- Same app, same **API permissions** blade → **+ Add a permission** →
  **Microsoft Graph** → **Application permissions** →
  `EventListener.ReadWrite.All`.
- **Grant admin consent for [tenant]** — confirm the green checkmark.

## Step 4 — Disable self-service sign-up

The user flow's `id` isn't visible in the admin center UI — retrieve it via
Graph first, then patch it. Both calls use the same authenticated `az`
session from step 1:

```bash
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/identity/authenticationEventsFlows" \
  -o json
```

`-o table` with an `{id:id, displayName:displayName}` projection can silently
drop the `id` column if it comes back null for every row — use `-o json` and
read the raw output instead. `externalUsersSelfServiceSignUpEventsFlow` is the
flow's **type** (`@odata.type`), not its display name, so don't expect to find
that literal string in `displayName`. Confirm which listed flow is the real
one by checking `@odata.type` (should be
`#microsoft.graph.externalUsersSelfServiceSignUpEventsFlow`) and
`conditions.applications.includeApplications` (should reference the GeoAssets
Web/Server app IDs from the reference table below) — a tenant can have
leftover sample/quickstart flows alongside the real one, and PATCHing the
wrong flow is easy to do and hard to notice.

Note its `id`, then PATCH it. This API is **beta-only** — there's no `v1.0`
equivalent for the write — and it's a polymorphic type, so both the flow
object and the nested `onInteractiveAuthFlowStart` object require an explicit
`@odata.type`; omitting either returns `400 Bad Request` ("The request body is
null or in bad format"):

```bash
FLOW_ID="<id from above>"

az rest --method PATCH \
  --url "https://graph.microsoft.com/beta/identity/authenticationEventsFlows/$FLOW_ID" \
  --body '{
    "@odata.type": "#microsoft.graph.externalUsersSelfServiceSignUpEventsFlow",
    "onInteractiveAuthFlowStart": {
      "@odata.type": "#microsoft.graph.onInteractiveAuthFlowStartExternalUsersSelfServiceSignUp",
      "isSignUpAllowed": false
    }
  }'
```

Returns no output on success (HTTP 204).

## Step 5 — Confirm SSPR stays enabled

SSPR ("Forgot password?") is an independent tenant setting from the sign-up
flow touched in step 4 — don't disable it by accident. No CLI action needed
here, just a manual check in step 8's acceptance pass: attempt "Forgot
password?" for an existing test account and confirm it still works.

## Step 6 — Provision an Azure Communication Services Email resource (portal)

ACS is a billable Azure *resource*, scoped to an Azure **subscription** — not
to the Entra External ID (CIAM) tenant used in steps 1–5. The CIAM tenant has
no subscription attached (same reason step 1's `az login --tenant ...` prints
"No subscriptions found" — expected, not an error), so this step needs a
separate login into whichever tenant/directory actually holds xdicor's Azure
subscription:

```bash
az login
```

(no `--tenant` pin — sign in with the account that has access to the org's
regular, billable subscription.) Then:

```bash
az account list -o table
az account set --subscription "<subscription name or id>"
az account show -o table
```

Confirm `IsDefault` shows the right subscription before creating anything in
the portal below.

Domain verification is also not scriptable ahead of time — it requires
interactive steps (DNS records or accepting Azure's managed domain):

- [portal.azure.com](https://portal.azure.com) → **Create a resource** →
  **Communication Services** → create the ACS resource (under the subscription
  selected above).
- Within the resource, add an **Email Communication Service**.
- Verify a sender domain — either your own (add the TXT/DKIM/SPF DNS records
  Azure provides) or Azure's free managed domain, sufficient for initial
  testing.
- Note the resource's **connection string** (or access key, whichever the ACS
  SDK needs) and the verified **`FromAddress`** (e.g.
  `DoNotReply@<your-managed-domain>.azurecomm.net`).

## Step 7 — Store secrets

Never in a tracked `appsettings.json` (see the project's public-repo CIAM
secrets convention). From `apps/GeoAssets.Server`:

```bash
dotnet user-secrets set "AcsEmail:ConnectionString" "<connection string from step 6>"
dotnet user-secrets set "AcsEmail:FromAddress"       "<verified FromAddress from step 6>"
```

No new Graph secret is needed — `RoleSync:TenantId`/`ClientId`/`ClientSecret`
from XD01-60 are reused as-is for the extra permissions granted in steps 2–3.
In deployed environments, use whatever secret store this project's existing
`AzureAdCiam` production secrets already live in.

## Step 8 — Sanity check both credentials

**Graph permissions** — create a throwaway test user, confirm it works, then
delete it:

```bash
az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/users" \
  --body '{
    "accountEnabled": true,
    "displayName": "XD01-65 Sanity Check",
    "mailNickname": "xd0165sanitycheck",
    "userPrincipalName": "xd0165sanitycheck@<your-tenant-domain>",
    "passwordProfile": {
      "forceChangePasswordNextSignIn": true,
      "password": "<a throwaway strong password>"
    }
  }' \
  --query id -o tsv
```

```bash
TEST_USER_ID="<id from above>"

az rest --method DELETE \
  --url "https://graph.microsoft.com/v1.0/users/$TEST_USER_ID"
```

Success on both calls confirms `User.ReadWrite.All` is active. (There's no
equivalent one-line sanity call for `EventListener.ReadWrite.All` beyond step
4 already having succeeded — that PATCH *is* the sanity check for it.)

**ACS Email** — use the Email Communication Service's built-in **Try Email**
/ test-send feature in the Azure portal (or a quick SDK/`curl` call using the
connection string from step 6) to send one test message from the verified
`FromAddress` and confirm delivery.

Also re-confirm step 5: attempt the public sign-up flow (should no longer be
reachable/offered) and "Forgot password?" for an existing test account
(should still work).

## Reference

| App | Client (Application) ID |
|---|---|
| GeoAssets Role Sync (Graph automation) | `62a54588-a3d0-42e7-9279-28ab10627d55` |

Tenant ID: `94bb6627-6a6f-4219-b6d2-ce9ca5e82215`.

Neither the ACS connection string/access key nor the `User.ReadWrite.All`/
`EventListener.ReadWrite.All`-scoped client secret (same secret as
`RoleSync:ClientSecret` from XD01-60) is recorded here or anywhere in the
repo — they live only in `dotnet user-secrets` (or the deployed secret
store).

## What's not automated here

Steps 2, 3, and 6 (permission grants + admin consent, ACS resource creation +
domain verification) still require the Entra/Azure portal — each is an
elevated, human-authorized action. Everything else (tenant sign-in, flow
lookup/patch, secret storage, sanity checks) is scriptable via `az` and shown
above.

## Next

With this done, XD01-65 is complete. XD01-67 (Graph provider) and XD01-68
(email sender) depend on these credentials existing and will consume
`AcsEmail:ConnectionString`/`FromAddress` from configuration, plus the
extended Role Sync app permissions, once implemented.
