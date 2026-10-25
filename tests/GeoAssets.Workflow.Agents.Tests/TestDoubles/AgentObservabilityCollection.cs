using System.Diagnostics;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.TestDoubles;

/// <summary>
/// Groups every test class that asserts on captured <see cref="Activity"/> instances into one
/// serialized xunit collection. <see cref="ActivitySource"/> is process-global — with the default
/// per-class parallelization, two test classes' executors racing concurrently would both fire
/// activities into whichever <see cref="ActivityCapture"/> listeners happen to be registered at
/// that moment, corrupting each other's capture lists (and each other's counts).
/// </summary>
[CollectionDefinition("AgentObservability", DisableParallelization = true)]
public class AgentObservabilityCollection;
