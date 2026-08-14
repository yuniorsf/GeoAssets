# Configuration and the Options pattern

**Status: `current`**

**Source**: Microsoft Learn, ["Options pattern in
.NET"](https://learn.microsoft.com/en-us/dotnet/core/extensions/options) and
["Configuration in
ASP.NET Core"](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/).
Official documentation.

## A settings class, not scattered `IConfiguration["..."]` reads

Any component with more than one or two configurable values should bind
them into a dedicated `sealed class` with plain public properties defaulted
to sane values, rather than reading `IConfiguration["Section:Key"]` strings
at point of use. The class is: discoverable (one place lists every setting
a component has), type-safe (no repeated string-to-`int`/`bool` parsing at
each call site), and testable (construct it directly in a unit test instead
of standing up an `IConfiguration`).

## The `SectionName` constant convention

Give the options class a `public const string SectionName = "..."` and bind
with `services.Configure<TOptions>(config.GetSection(TOptions.SectionName))`
(or the `services.Configure<TOptions>(opts => { ... })` delegate form when
values come from environment/code rather than `appsettings.json`). Keeping
the section name as a constant on the options type itself — rather than a
magic string duplicated at every call site — means the binding code and the
type it binds into can't drift out of sync.

## Inject `IOptions<T>` unless the value can legitimately change at runtime

- **`IOptions<T>`** — resolved once, cached for the app's lifetime. Correct
  default for a Singleton-lifetime consumer, or any value that's genuinely
  fixed for the process (a Kafka bootstrap server list, a base URL).
- **`IOptionsSnapshot<T>`** — re-read per scope (per web request, typically).
  Only reach for this when the underlying config source can change without a
  restart (a reloadable `appsettings.json` in an environment that supports
  hot-reload) *and* the consumer is Scoped — injecting `IOptionsSnapshot<T>`
  into a Singleton is a captive-dependency-shaped bug, the same class of
  mistake `design-patterns-and-dotnet.md` already flags for DI lifetimes
  generally.
- **`IOptionsMonitor<T>`** — for a Singleton that does need to react to
  config changes at runtime (via `OnChange`), which is rarer and should be a
  deliberate choice, not a default.

## Validate at startup, not at first use

Where a setting has real invariants (a required connection string, a
positive timeout), use `.Configure<TOptions>(...).ValidateDataAnnotations()`
or `.ValidateOnStart()` so a misconfiguration fails the app at boot with a
clear error, rather than surfacing as a confusing `NullReferenceException`
or silent no-op deep in a request path the first time the bad value is
actually read.

## Per-host differences are a config-source difference, not a code-path difference

GeoAssets ships the same options types (e.g. `MapInteropOptions`) across
Blazor WASM (`GeoAssets.Web`, reading `wwwroot/appsettings.json`), a
server-side host (`GeoAssets.Server`, reading `appsettings.json` via the
.NET Generic Host's layered config providers), and MAUI (reading its own
bundled `appsettings.json`). The consuming component's code should stay
identical across hosts — only *how* configuration is loaded into
`IConfiguration` before binding differs per host type. Don't special-case a
component's logic per host to work around a config-loading difference;
fix the loading, not the consumer.

## Where this would apply in GeoAssets

- `KafkaPublisherOptions`/`ServiceBusPublisherOptions`
  (`workflow/GeoAssets.Workflow.Messaging.Kafka/`,
  `.../Messaging.ServiceBus/`) are the clearest existing examples of the
  `SectionName` convention (`SectionName = "WorkflowKafka"`, bound via
  `WorkflowKafkaServiceExtensions`) — new options types should follow this
  exact shape rather than inventing a different binding style.
- `MapInteropOptions` (`apps/GeoAssets.Shared/Services/`) is the concrete
  case of the "same options type across hosts" rule: it's consumed
  identically by `GeoAssets.Web/Program.cs` and `GeoAssets.MAUI/MauiProgram.cs`,
  each supplying its own `appsettings.json` (`apps/GeoAssets.Web/wwwroot/appsettings.json`
  vs. `apps/GeoAssets.MAUI/appsettings.json`) with host-appropriate values
  (e.g. MAUI's `"BatchSize": 5` tuned for on-device performance) while
  `MapInteropService` itself has no per-host branching.
- `ServiceOrderRulesOptions`, `AgentIdentityOptions`, `ObservabilityOptions`,
  and `LocalizationOptions` are further existing examples worth reading
  before adding a new options type, to keep the property-naming and
  default-value style consistent.
