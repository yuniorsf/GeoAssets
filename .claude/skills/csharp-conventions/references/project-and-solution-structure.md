# Project/solution structure and namespace conventions

**Status: `current`**

**Source**: Microsoft Learn, [".NET project SDKs and multi-targeting"](https://learn.microsoft.com/en-us/dotnet/core/project-sdk/overview)
and ["Naming
guidelines"](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/naming-guidelines)
(namespace section). Official documentation. This file formalizes rules that
already exist implicitly in the repo's folder layout and `ProjectReference`
graph — it doesn't introduce anything new, it names what's already being
followed so new projects/folders stay consistent with it. Complements
`entity-framework-core-and-onion-architecture.md`, which covers the Onion
architecture's *conceptual* layering; this file covers the physical
project/folder mechanics that implement it.

## Top-level folders are dependency-direction lanes, not just categories

`core/`, `providers/`, `apps/`, `plugins/`, `workflow/`, `examples/` aren't
an arbitrary grouping — the folder a project lives in constrains what it's
allowed to depend on:

- **`core/`** — innermost layer. `GeoAssets.Core` has **zero**
  `ProjectReference`s (verified: its `.csproj` has no `<ProjectReference>`
  entries at all) — nothing in `core/` may depend on a provider,
  app host, or plugin. This is the Onion architecture's center: models and
  interfaces only.
- **`providers/`** — implementations of `core/`'s interfaces
  (`IAssetRepository`, `IExternalRepositoryFactory`, etc.) for a specific
  backend (Postgres, REST, InMemory, Shapefile, WFS/WMS). May depend on
  `core/`, never on `apps/` or another sibling provider.
- **`workflow/`** — same shape as `providers/`, scoped to the Workflow
  module's own persistence/messaging backends (EFCore, Kafka, ServiceBus,
  Rest) plus `GeoAssets.Workflow.Agents`. May depend on `core/`.
- **`apps/`** — outermost layer, the only folder allowed to reference
  multiple providers/workflow backends at once and wire them together via
  DI (`GeoAssets.Server`'s `.csproj` references the Postgres provider, Core,
  Workflow, and Workflow.EFCore all at once — exactly because composition
  root wiring is `apps/`'s job, not any inner layer's).
- **`plugins/`** — optional, independently loadable extensions
  (`Plugin.Hydrology`, `Plugin.GeoJsonImport`); depends inward on `core/`
  like a provider does, but is never a hard dependency of anything else in
  the graph — the whole point of a plugin is that the app still builds and
  runs without it.

New projects should be placed by asking "what does this depend on, and what
should be allowed to depend on it" first, folder-by-convention second — if a
new project's answer doesn't fit one of the existing lanes cleanly, that's
worth surfacing rather than force-fitting it into the nearest folder.

## Namespace mirrors physical path, one project = one root namespace

Every project's namespaces start with `GeoAssets.<ProjectSuffix>` and then
mirror the folder structure underneath (`GeoAssets.Provider.PostgreSQL.Data`,
`.Entities`, `.Repositories`, `.Migrations` for the four subfolders of that
one project). Don't nest an unrelated project's namespace inside another's,
and don't let a namespace imply a dependency that isn't in the
`ProjectReference` graph — the namespace should always be discoverable from
the file's path alone, and the path should always be inferable from the
project's place in the dependency-direction lanes above.

## One `.csproj` per deployable/independently-versionable unit

Each provider, each workflow backend, each app host is its own project even
when small (`GeoAssets.Workflow.Rest` is a thin client wrapping `HttpClient`
calls) — this is what makes the dependency-direction rule enforceable by the
compiler (a project literally cannot reference what's not in its
`<ProjectReference>` list) rather than just a convention someone has to
remember. Don't fold a new backend into an existing project's namespace as
a "just this once, it's small" shortcut — the moment it's implementing an
interface for a *different* backend, it gets its own project.

## Where this would apply in GeoAssets

- `GeoAssets.Server`'s `.csproj`
  (`apps/GeoAssets.Server/GeoAssets.Server.csproj`) referencing
  `GeoAssets.Provider.PostgreSQL`, `GeoAssets.Core`, `GeoAssets.Workflow`, and
  `GeoAssets.Workflow.EFCore` simultaneously is the correct shape for an
  `apps/` composition root — any future app host doing similar multi-backend
  wiring should follow the same reference pattern rather than trying to
  route through an intermediate "everything" project.
- `GeoAssets.Workflow.Rest` (`workflow/GeoAssets.Workflow.Rest/`) is the
  newest example of the one-project-per-backend rule from XD01-8: it sits
  alongside `GeoAssets.Workflow.EFCore`/`.Messaging.Kafka`/`.Messaging.ServiceBus`
  as an independent implementation of the same Workflow persistence
  interfaces, not folded into an existing project.
- Any future provider (a new spatial backend, a new workflow persistence
  backend) should land in `providers/` or `workflow/` respectively with its
  own project referencing only `core/GeoAssets.Core` (or the relevant
  `core/GeoAssets.Workflow`), matching `GeoAssets.Provider.PostgreSQL`'s
  single-reference shape.
