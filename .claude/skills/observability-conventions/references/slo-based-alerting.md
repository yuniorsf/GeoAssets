# Symptom-based alerting, not cause-based (SRE philosophy)

**Status: `current`** — general SRE/observability engineering practice, not
gated by a language or runtime version.

**Source**: engineering directives provided by the user (observability
specialist), 2026-08-06. Distilled and paraphrased, not a reproduction.

## Resource-threshold alerts are low-fidelity

Alerting when CPU crosses 85% is a reactive, low-fidelity practice that
generates alert fatigue: high CPU doesn't necessarily mean any user is
experiencing degraded service, and low CPU doesn't guarantee they aren't.
The resource metric is a *possible cause*, not the thing anyone actually
cares about.

## Alert on Service Level Objectives instead

Base alerts strictly on the four golden signals as they impact the
customer-facing experience directly:

- **Latency** — how long requests take
- **Traffic** — demand on the system
- **Errors** — rate of failed requests
- **Saturation** — how "full" the service is relative to capacity

These are symptoms the user actually feels, unlike an internal resource
gauge.

## Error budgets and burn rate

Structure alerts around the *rate of consumption* of the error budget
(Error Budget Burn Rate), not a raw error count or percentage. Fire an
alert only when the current failure velocity would exhaust the entire
month's error-budget margin within the next few hours — this distinguishes
a real, urgent incident from routine background noise that's still within
the acceptable SLO envelope.

## Where this would apply in GeoAssets

No SLO/alerting configuration exists yet in the repo — `ObservabilityOptions`
(`core/GeoAssets.Infrastructure.Observability/ObservabilityOptions.cs`)
covers service identity, the Azure Monitor connection string, sampling
ratio, and per-instrumentation toggles, but has no SLO or alert-threshold
section. `AddAspNetCoreInstrumentation()` and `AddHttpClientInstrumentation()`
in `ObservabilityServiceExtensions.cs:96-110` already emit the request-rate,
latency, and error-status metrics an SLO/burn-rate alert would be built on
— the emission side of the four golden signals is in place; only the
alerting-rule layer (typically configured in Azure Monitor/Grafana, outside
this codebase) remains undefined.
