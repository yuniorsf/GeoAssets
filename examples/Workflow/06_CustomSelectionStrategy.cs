// ────────────────────────────────────────────────────────────────────────────
// 06 · Custom Feature Selection Strategy
//
// Muestra el ciclo completo para crear una nueva estrategia de selección de
// GeoFeature e integrarla con el sistema MEF de GeoAssets.Workflow.
//
// Pasos cubiertos:
//   1. Implementar IFeatureSelectionStrategy con [ExportFeatureSelectionStrategy]
//   2. Implementar BackgroundProcessSelectionStrategy (proceso automático)
//   3. Registrar ambas estrategias en FeatureSelectionRegistry
//   4. Ejecutar las estrategias y poblar ServiceOrders
//   5. Armar jerarquía padre → hijos con distinto origen de features
//   6. Inspeccionar FeatureSelectionSpec para auditoría y reproducibilidad
//
// Red de ejemplo (distribución eléctrica + hídrica en dos capas):
//
//   [Capa: electrica]
//   Subestación ──▶ Transformador ──▶ Medidor A
//                                 └──▶ Medidor B
//
//   [Capa: hidrica]
//   Planta Potabilizadora ──▶ Válvula Principal ──▶ Hidrante Norte
//                                               └──▶ Hidrante Sur
// ────────────────────────────────────────────────────────────────────────────
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Provider.InMemory;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;

namespace GeoAssets.Examples.Workflow;

public static class CustomSelectionStrategy
{
    public static async Task RunAsync()
    {
        // ── 1. Preparar el repositorio con dos capas de activos ──────────────

        var repo = new InMemoryAssetProvider();

        // Capa eléctrica
        var subestacion   = Node("Subestación",          -66.90, 10.50, "electrica");
        var transformador = Node("Transformador",        -66.88, 10.50, "electrica");
        var medidorA      = Node("Medidor A",            -66.86, 10.52, "electrica");
        var medidorB      = Node("Medidor B",            -66.86, 10.48, "electrica");

        Connect(subestacion,   transformador, "electric-flow", 0.10);
        Connect(transformador, medidorA,      "electric-flow", 0.05);
        Connect(transformador, medidorB,      "electric-flow", 0.05);

        // Capa hídrica
        var planta         = Node("Planta Potabilizadora", -66.92, 10.50, "hidrica");
        var valvula        = Node("Válvula Principal",     -66.90, 10.50, "hidrica");
        var hidranteNorte  = Node("Hidrante Norte",        -66.88, 10.53, "hidrica");
        var hidranteSur    = Node("Hidrante Sur",          -66.88, 10.47, "hidrica");

        Connect(planta,  valvula,       "water-flow", 0.8);
        Connect(valvula, hidranteNorte, "water-flow", 0.4);
        Connect(valvula, hidranteSur,   "water-flow", 0.4);

        foreach (var f in new[] { subestacion, transformador, medidorA, medidorB,
                                   planta, valvula, hidranteNorte, hidranteSur })
            repo.Add(f);

        // ── 2. Crear el FeatureSelectionRegistry con estrategias built-in ────
        //      más las dos estrategias personalizadas definidas en este assembly.
        //
        //  ┌─────────────────────────────────────────────────────────────────┐
        //  │  CLAVE: pasar typeof(CustomSelectionStrategy).Assembly hace que │
        //  │  MEF descubra automáticamente las clases decoradas con           │
        //  │  [ExportFeatureSelectionStrategy] en este mismo proyecto.        │
        //  └─────────────────────────────────────────────────────────────────┘

        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        using var registry = new FeatureSelectionRegistry(
            pluginsDirectory: pluginsDir,
            builtInAssemblies:
            [
                typeof(BoundingBoxSelectionStrategy).Assembly, // GeoAssets.Workflow (built-in)
                typeof(CustomSelectionStrategy).Assembly        // este assembly → descubre las clases de abajo
            ]);

        var orderRepo = new InMemoryServiceOrderRepository();

        // ── 3. Listar todas las estrategias disponibles ──────────────────────

        Print.Section("Estrategias de selección disponibles");
        foreach (var meta in registry.GetAvailableStrategies())
            Console.WriteLine($"      [{meta.Category,-12}]  {meta.StrategyId,-28}  {meta.Description}");

        // ── 4. Orden raíz — bounding-box (estrategia built-in) ───────────────
        //      Cubre toda la red para obtener todos los activos del área.

        Print.Section("Orden Raíz — bounding-box (estrategia built-in)");

        var (allFeatures, rootSpec) = await registry.SelectAsync(
            "bounding-box",
            new FeatureSelectionContext
            {
                Repository      = repo,
                OrderRepository = orderRepo,
                Parameters      = new Dictionary<string, object>
                {
                    ["minLon"] = -66.95, ["minLat"] = 10.44,
                    ["maxLon"] = -66.84, ["maxLat"] = 10.56
                }
            },
            note: "Selección inicial completa del área de operación");

        var rootOrder = new ServiceOrder
        {
            Title       = "Inspección General de Red",
            CreatedBy   = "supervisor@empresa.com",
            Priority    = ServiceOrderPriority.Normal,
        }.WithFeatures(allFeatures, rootSpec);

        orderRepo.Add(rootOrder);
        PrintOrder(rootOrder);

        // ── 5. Orden hija A — LayerFilterStrategy (estrategia personalizada) ─
        //      Selecciona solo activos de la capa "electrica".

        Print.Section("Orden Hija A — layer-filter (estrategia personalizada)");

        var (electricFeatures, layerSpec) = await registry.SelectAsync(
            "layer-filter",
            new FeatureSelectionContext
            {
                Repository      = repo,
                OrderRepository = orderRepo,
                TargetOrder     = rootOrder,
                Parameters      = new Dictionary<string, object> { ["layerId"] = "electrica" }
            },
            note: "Activos eléctricos derivados de la orden raíz");

        var childOrderA = new ServiceOrder
        {
            Title         = "Revisión Red Eléctrica",
            CreatedBy     = "tecnico.electrico@empresa.com",
            Priority      = ServiceOrderPriority.High,
            ParentOrderId = rootOrder.Id,
        }.WithFeatures(electricFeatures, layerSpec);

        rootOrder.ChildOrderIds.Add(childOrderA.Id);
        orderRepo.Add(childOrderA);
        PrintOrder(childOrderA);

        // ── 6. Orden hija B — NetworkImpactStrategy (proceso de background) ──
        //      Calcula automáticamente los activos afectados por una falla
        //      en el Transformador (upstream + downstream).

        Print.Section("Orden Hija B — network-impact (proceso de background)");

        // Suscribirse al progreso del proceso background
        NetworkImpactStrategy.ProgressReported += (_, p) =>
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"      [{p.PercentComplete,3}%]  {p.Message}  (features encontradas: {p.FeaturesFoundSoFar})");
            Console.ResetColor();
        };

        var (impactFeatures, impactSpec) = await registry.SelectAsync(
            "network-impact",
            new FeatureSelectionContext
            {
                Repository      = repo,
                OrderRepository = orderRepo,
                TargetOrder     = rootOrder,
                Parameters      = new Dictionary<string, object>
                {
                    ["featureId"] = transformador.Id,
                    ["direction"] = TraversalDirection.Both,
                }
            },
            note: "Análisis de impacto por falla en Transformador");

        var childOrderB = new ServiceOrder
        {
            Title         = "Atención Falla — Transformador",
            CreatedBy     = "sistema.automatico",
            Priority      = ServiceOrderPriority.Critical,
            ParentOrderId = rootOrder.Id,
        }.WithFeatures(impactFeatures, impactSpec);

        rootOrder.ChildOrderIds.Add(childOrderB.Id);
        orderRepo.Add(childOrderB);
        PrintOrder(childOrderB);

        // ── 7. Inspeccionar jerarquía y FeatureSelectionSpec ─────────────────

        Print.Section("Jerarquía de órdenes");
        PrintHierarchy(rootOrder, orderRepo, indent: 0);

        Print.Section("Auditoría — FeatureSelectionSpec por orden");
        foreach (var order in new[] { rootOrder, childOrderA, childOrderB })
        {
            var spec = order.SelectionSpec!;
            Console.WriteLine($"      [{order.Title}]");
            Console.WriteLine($"        strategyId : {spec.StrategyId}");
            Console.WriteLine($"        executedAt : {spec.ExecutedAt:u}");
            Console.WriteLine($"        note       : {spec.Note}");
            Console.WriteLine($"        parameters : {string.Join(", ", spec.Parameters.Select(kv => $"{kv.Key}={kv.Value}"))}");
            Console.WriteLine();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeoFeature Node(string name, double lon, double lat, string layer) =>
        new()
        {
            Properties = new()
            {
                Name        = name,
                AssetTypeId = AssetType.Point.Id.ToString(),
                LayerId     = layer,
            },
            Geometry = new GeoPoint { Coordinates = [lon, lat] }
        };

    private static void Connect(GeoFeature from, GeoFeature to, string kind, double weight) =>
        from.Topology.Add(new TopoEdge { TargetId = to.Id, Kind = kind, Weight = weight });

    private static void PrintOrder(ServiceOrder order)
    {
        Console.WriteLine($"      Orden  : {order.Title}");
        Console.WriteLine($"      Status : {order.Status}    Priority: {order.Priority}");
        Console.WriteLine($"      Spec   : strategy={order.SelectionSpec?.StrategyId}");
        Print.List("Features", order.Features);
    }

    private static void PrintHierarchy(IServiceOrder order, IServiceOrderRepository repo, int indent)
    {
        var prefix = new string(' ', indent * 4 + 6);
        var icon   = indent == 0 ? "◆" : "└─";
        Console.WriteLine($"{prefix}{icon} [{order.Status}] {order.Title}  ({order.Features.Count} features)");
        foreach (var childId in order.ChildOrderIds)
        {
            var child = repo.GetById(childId);
            if (child is not null) PrintHierarchy(child, repo, indent + 1);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  ESTRATEGIA PERSONALIZADA 1 — LayerFilterStrategy
//
//  Selecciona features por LayerId.
//  Caso de uso: dividir una orden general en sub-órdenes por tipo de red.
//
//  Para crear una nueva estrategia:
//    1. Implementar IFeatureSelectionStrategy
//    2. Decorar con [ExportFeatureSelectionStrategy("id", ...)]
//    3. Pasar typeof(esta clase).Assembly al FeatureSelectionRegistry
// ═══════════════════════════════════════════════════════════════════════════

[ExportFeatureSelectionStrategy("layer-filter",
    Category    = "Filter",
    DisplayName = "Layer Filter",
    Description = "Selects all features belonging to a specific map layer.")]
file sealed class LayerFilterStrategy : IFeatureSelectionStrategy
{
    public string StrategyId  => "layer-filter";
    public string DisplayName => "Layer Filter";
    public string Description => "Selects all features belonging to a specific map layer.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        var layerId = (string)context.Parameters["layerId"];

        var result = context.Repository
            .GetAll()
            .Where(f => f.Properties.LayerId == layerId)
            .ToList();

        return Task.FromResult<IReadOnlyList<GeoFeature>>(result);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  ESTRATEGIA PERSONALIZADA 2 — NetworkImpactStrategy
//
//  Proceso de background que simula un análisis de impacto de falla:
//    - Fase 1: detecta nodos aguas arriba (fuentes afectadas)
//    - Fase 2: detecta nodos aguas abajo (consumidores afectados)
//    - Fase 3: agrega el nodo semilla
//
//  Subclase de BackgroundProcessSelectionStrategy:
//    - Sobreescribir RunAsync()
//    - Reportar progreso con IProgress<BackgroundSelectionProgress>
//    - Respetar CancellationToken en operaciones largas
// ═══════════════════════════════════════════════════════════════════════════

[ExportFeatureSelectionStrategy("network-impact",
    Category    = "Topology",
    DisplayName = "Network Impact Analysis",
    Description = "Automatically finds all assets affected by a node failure (upstream + downstream).")]
file sealed class NetworkImpactStrategy : BackgroundProcessSelectionStrategy
{
    public override string StrategyId  => "network-impact";
    public override string DisplayName => "Network Impact Analysis";
    public override string Description => "Automatically finds all assets affected by a node failure (upstream + downstream).";

    // Expuesto estáticamente para que el ejemplo pueda suscribirse al evento OnProgress
    public static event EventHandler<BackgroundSelectionProgress>? ProgressReported;

    public NetworkImpactStrategy()
    {
        OnProgress += (s, p) => ProgressReported?.Invoke(s, p);
    }

    protected override async Task<IReadOnlyList<GeoFeature>> RunAsync(
        IFeatureSelectionContext context,
        IProgress<BackgroundSelectionProgress> progress,
        CancellationToken ct)
    {
        var featureId = (string)context.Parameters["featureId"];
        var direction = context.Parameters.TryGetValue("direction", out var d)
                          ? (TraversalDirection)d
                          : TraversalDirection.Both;

        var result = new List<GeoFeature>();

        // Fase 1 — upstream
        if (direction is TraversalDirection.Upstream or TraversalDirection.Both)
        {
            progress.Report(new(10, "Analizando nodos upstream (fuentes)…"));
            await Task.Delay(40, ct); // simula I/O o cómputo real

            var ancestors = context.Repository.GetAncestors(featureId);
            result.AddRange(ancestors);
            progress.Report(new(40, "Upstream completado.", result.Count));
        }

        ct.ThrowIfCancellationRequested();

        // Fase 2 — downstream
        if (direction is TraversalDirection.Downstream or TraversalDirection.Both)
        {
            progress.Report(new(50, "Analizando nodos downstream (consumidores)…", result.Count));
            await Task.Delay(40, ct);

            var descendants = context.Repository.GetDescendants(featureId);
            foreach (var f in descendants)
                if (result.All(x => x.Id != f.Id))
                    result.Add(f);

            progress.Report(new(80, "Downstream completado.", result.Count));
        }

        // Fase 3 — nodo semilla
        var seed = context.Repository.GetById(featureId);
        if (seed is not null && result.All(x => x.Id != featureId))
            result.Insert(0, seed);

        progress.Report(new(100, "Análisis de impacto completado.", result.Count));
        return result;
    }
}
