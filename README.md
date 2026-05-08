# GeoAssets

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-512BD4.svg)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![MAUI](https://img.shields.io/badge/.NET-MAUI-512BD4.svg)](https://dotnet.microsoft.com/apps/maui)

> A modular .NET 9 platform for managing georeferenced assets across web, mobile, and desktop — built on NetTopologySuite, PostGIS, and a plugin-based architecture, and developed as a working lab for AI-augmented engineering practices.

---

## Overview

**GeoAssets** is a personal R&D project exploring how to design a modern, extensible geospatial platform in .NET. The codebase delivers the same domain model across multiple targets (Blazor WebAssembly, MAUI mobile and desktop) from a single shared core, integrates real spatial primitives via [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite), and exposes data through standard OGC protocols (WFS, WMS, Shapefile) as well as a PostgreSQL + PostGIS provider.

The project also serves as a deliberate exercise in **AI-augmented engineering**: it is developed with [Claude Code](https://www.anthropic.com/claude-code) as part of the development loop. The `CLAUDE.md` file at the repo root captures the project's conventions, architectural decisions, and key file paths so that LLM agents operate under the same engineering standards as a human contributor.

---

### Continuous AI-Augmented Review

Every pull request to this repository triggers an automated code review by [Claude Code](https://www.anthropic.com/claude-code), using Anthropic's official `claude-code-action`. Reviewers and contributors can also invoke Claude on-demand in any issue or PR comment by mentioning `@claude`. Workflow definitions live under `.github/workflows/`.

## Highlights

- **Multi-target delivery from a single core** — Blazor WebAssembly for the web, .NET MAUI for mobile and desktop, and a shared Razor Class Library (`GeoAssets.Shared`) for components, CSS, and JavaScript interop.
- **Real spatial library** — full integration with NetTopologySuite (geometry predicates, measurements, derived geometries, spatial queries) using SRID 4326 and RFC 7946 GeoJSON.
- **Topology as a first-class concept** — directed-graph model over features with classical graph algorithms (Dijkstra shortest path, BFS path finding, Kahn's topological sort, cycle detection, connected components).
- **Pluggable providers** — InMemory, PostgreSQL/PostGIS, REST, WFS, WMS, Shapefile, all behind a uniform repository contract.
- **Plugin architecture** — extensions live outside the core; Hydrology and GeoJSON import plugins are included as reference implementations.
- **Workflow pipeline** — orchestration layer with EF Core persistence and messaging integrations for Kafka and Azure Service Bus.
- **Observability layer** — dedicated infrastructure project (`GeoAssets.Infrastructure.Observability`) for logs, metrics, and tracing.
- **Identity & authentication** — MSAL integration for OIDC/OAuth flows, with EF Core-backed identity persistence.
- **AI-augmented development** — `CLAUDE.md` provides a stable contract for LLM coding agents working in the repo.

---

## Architecture

```
GeoAssets/
├── core/
│   ├── GeoAssets.Core/                    # Domain model, geometry, services, repositories
│   ├── GeoAssets.Commands/                # Command abstractions
│   ├── GeoAssets.Workflow/                # Workflow orchestration core
│   ├── GeoAssets.Identity/                # Identity domain
│   └── GeoAssets.Infrastructure.Observability/
│
├── apps/
│   ├── GeoAssets.Shared/                  # Razor Class Library — components, CSS, JS interop
│   ├── GeoAssets.Web/                     # Blazor WebAssembly host
│   ├── GeoAssets.Server/                  # Server-side host
│   ├── GeoAssets.MAUI/                    # Mobile + desktop app
│   ├── GeoAssets.Commands.Builtin/        # Built-in command implementations
│   └── GeoAssets.Identity.EFCore/         # EF Core identity persistence
│
├── providers/
│   ├── GeoAssets.Provider.InMemory/       # In-memory repository (default for Web/WASM)
│   ├── GeoAssets.Provider.PostgreSQL/     # EF Core + Npgsql + PostGIS (server-side only)
│   ├── GeoAssets.Provider.Rest/           # Generic REST adapter
│   ├── GeoAssets.Provider.WFS/            # OGC Web Feature Service client
│   ├── GeoAssets.Provider.WMS/            # OGC Web Map Service client
│   └── GeoAssets.Provider.Shapefile/      # Shapefile reader
│
├── plugins/
│   └── commands/
│       ├── GeoAssets.Plugin.Hydrology/    # Hydrology-specific commands
│       └── GeoAssets.Plugin.GeoJsonImport/
│
├── workflow/
│   ├── GeoAssets.Workflow.EFCore/         # Persistent workflow store
│   ├── GeoAssets.Workflow.Messaging.Kafka/
│   └── GeoAssets.Workflow.Messaging.ServiceBus/
│
├── examples/
│   └── GeoAssets.Examples/                # Spatial, Topology, Workflow, Print samples
│
├── tests/
│   └── GeoAssets.Core.Tests/              # Unit tests for the core domain
│
├── CLAUDE.md                              # AI-agent operating instructions
└── GeoAssets.sln                          # .NET solution file
```

### Design principles

- **Separation of concerns** — `core/` knows nothing about UI, infrastructure, or specific data sources.
- **Provider pattern** — all external systems live behind a `IExternalRepositoryFactory` contract, discovered at startup; the UI renders one entry per registered factory.
- **Plugin extensibility** — additional behavior is delivered as plugins, not core changes.
- **Workflow isolation** — multi-step processes are orchestrated in `workflow/` rather than scattered through services.
- **Multi-target by design** — the same domain runs in WebAssembly (no server), in a server-side host, and in MAUI, with feature flags rather than divergent codebases.

---

## Tech Stack

| Area | Technology |
|---|---|
| Runtime | .NET 9 |
| Web | Blazor WebAssembly · Razor Class Library · Blazored.LocalStorage |
| Mobile / Desktop | .NET MAUI |
| Spatial | NetTopologySuite 2.5 · NTS.IO.GeoJSON4STJ 4.0 |
| Map UI | Leaflet 1.9.4 · Leaflet-Geoman 2.18.3 |
| Persistence | PostgreSQL + PostGIS · Entity Framework Core · Npgsql |
| OGC providers | WFS · WMS · Shapefile |
| Auth | MSAL (OIDC / OAuth 2.0) |
| Messaging | Apache Kafka · Azure Service Bus |
| Observability | Logs / metrics / tracing infrastructure project |
| Conventions | RFC 7946 GeoJSON · SRID 4326 · `[lon, lat]` order |
| Dev workflow | Claude Code (AI-augmented engineering — see `CLAUDE.md`) |

---

## Getting Started

### Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Optional, for the MAUI app: the MAUI workload (`dotnet workload install maui`)
- Optional, for the PostgreSQL provider: a PostgreSQL instance with the PostGIS extension enabled

### Build

```bash
git clone https://github.com/yuniorsf/GeoAssets.git
cd GeoAssets
git checkout develop

dotnet restore GeoAssets.sln
dotnet build GeoAssets.sln
```

### Run the web app

```bash
cd apps/GeoAssets.Web
dotnet run
```

### Run the example projects

```bash
cd examples/GeoAssets.Examples
dotnet run
```

The examples cover spatial queries, topology graph algorithms, and workflow orchestration.

### Run the tests

```bash
dotnet test tests/GeoAssets.Core.Tests/
```

---

## Status

This project is under **active development** as a personal R&D vehicle. The public API is not yet stable and breaking changes are expected before a tagged release.

Issues, discussions, and contributions are welcome — feel free to open an issue if you want to talk about a specific design decision.

---

## Why This Project

Beyond the technical exploration, GeoAssets is a deliberate exercise in three areas that matter to me as a senior engineer:

1. **Sustainable architecture for evolving systems** — applying separation of concerns, provider patterns, plugin extensibility, and workflow isolation to a non-trivial, multi-target domain.
2. **Spatial computing done right** — using the same algorithms and standards (NTS, RFC 7946 GeoJSON, OGC services, PostGIS) that the GIS industry relies on, rather than ad-hoc reinventions.
3. **AI-augmented engineering as a daily practice** — using LLM agents not as a novelty but as a co-developer that operates under the same engineering standards as a human contributor (clean code, SOLID, tests, peer review).

These threads connect to my broader work as a senior software engineer focused on distributed systems, cloud-native platforms, and the integration of AI into production engineering practice.

---

## Roadmap

Short-term focus areas (subject to change as the design evolves):

- [ ] Stabilize the `IAssetRepository` contract and freeze the public surface exposed to providers.
- [ ] Expand test coverage beyond `GeoAssets.Core.Tests` to include providers and workflow.
- [ ] Add a CI pipeline (GitHub Actions) for build + test on every PR.
- [ ] Document the plugin contract and the `IExternalRepositoryFactory` discovery mechanism.
- [ ] Tag a `v0.1.0` once the above are in place.
- [ ] Add observability examples (OpenTelemetry exporter wiring) using the Observability project.

---

## License

Released under the [MIT License](LICENSE).

---

## Author

**Yunior Sánchez Fernández**
Senior Software Engineer · Cloud · Distributed Systems · AI-Augmented Development
[LinkedIn](https://linkedin.com/in/yuniorsf) · `yuniorsf@xdicor.com.br`
