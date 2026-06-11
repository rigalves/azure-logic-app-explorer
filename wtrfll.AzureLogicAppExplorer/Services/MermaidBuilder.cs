using wtrfll.AzureLogicAppExplorer.Model;
using System.Text;
using System.Text.RegularExpressions;

namespace wtrfll.AzureLogicAppExplorer.Services;

public enum DiagramMode { Summary, Detail }

/// <summary>
/// Converts a (possibly filtered) Inventory into a Mermaid flowchart LR diagram.
///
/// Summary: Logic App (+ workflow count) → unique external target nodes.
/// Detail:  Workflow (+ parent app) → external target nodes. Edges deduplicated per (wf, target).
///
/// Every node has a title (primary identifier) and subtitle (type label).
/// Expression-based HTTP targets that were fully resolved appear as plain host nodes.
/// Targets that could only be partially resolved (e.g. ⟨appsetting:Key⟩) appear with
/// an "unresolved" subtitle so their dynamic nature is visible at a glance.
/// </summary>
public sealed partial class MermaidBuilder
{
    [GeneratedRegex(@"[^a-zA-Z0-9]")]
    private static partial Regex NonAlphanumeric();

    private const string LinearCurveDirective = "%%{init: {'flowchart': {'curve': 'stepAfter'}}}%%\n";

    private static readonly Dictionary<CallType, string> TypeClass = new()
    {
        [CallType.Http]             = "http",
        [CallType.Function]         = "funcapp",
        [CallType.Salesforce]       = "salesforce",
        [CallType.ManagedConnector] = "managed",
        [CallType.ServiceProvider]  = "serviceprovider",
        [CallType.ServiceBus]       = "servicebus",
        [CallType.ChildWorkflow]    = "childwf",
        [CallType.KeyVault]         = "keyvault",
        [CallType.Unknown]          = "http",
    };

    private static readonly Dictionary<CallType, string> TypeSubtitle = new()
    {
        [CallType.Http]             = "HTTP",
        [CallType.Function]         = "Azure Function",
        [CallType.Salesforce]       = "Salesforce",
        [CallType.ManagedConnector] = "Managed Connector",
        [CallType.ServiceProvider]  = "Built-in Connector",
        [CallType.ServiceBus]       = "Service Bus",
        [CallType.ChildWorkflow]    = "Child Workflow",
        [CallType.KeyVault]         = "Key Vault",
        [CallType.Unknown]          = "HTTP",
    };

    private const string ClassDefs = """

        classDef logicapp      fill:#0d6efd,color:#fff,stroke:#0a58ca,stroke-width:2px
        classDef workflow      fill:#0dcaf0,color:#000,stroke:#0aa2c0,stroke-width:1px
        classDef salesforce    fill:#00A1E0,color:#fff,stroke:#0074a2,stroke-width:2px
        classDef http          fill:#6c757d,color:#fff,stroke:#495057
        classDef funcapp       fill:#198754,color:#fff,stroke:#146c43
        classDef managed       fill:#6f42c1,color:#fff,stroke:#5a2d91
        classDef serviceprovider fill:#fd7e14,color:#000,stroke:#d45e00
        classDef servicebus    fill:#f0ad4e,color:#000,stroke:#ec971f,stroke-width:2px
        classDef childwf       fill:#20c997,color:#000,stroke:#17a589
        classDef keyvault      fill:#dc3545,color:#fff,stroke:#a02530
        classDef trigger       fill:#6610f2,color:#fff,stroke:#4d0bb8
    """;

    public string Build(Inventory inventory, DiagramMode mode) =>
        mode == DiagramMode.Summary ? BuildSummary(inventory) : BuildDetail(inventory);

    // ── Summary mode ──────────────────────────────────────────────────────────

    private static string BuildSummary(Inventory inventory)
    {
        if (inventory.LogicApps.Count == 0)
            return "flowchart LR\n    empty[\"No logic apps found — run a Scan first\"]";

        var sb = new StringBuilder(LinearCurveDirective + "flowchart LR\n");
        var registry = new TargetRegistry();

        // Declare Logic App nodes + register outbound targets
        foreach (var app in inventory.LogicApps)
        {
            var appId = SafeId("app", app.Name);
            var wfCount = app.Workflows.Count;
            var subtitle = $"{wfCount} workflow{(wfCount == 1 ? "" : "s")}";
            var displayName = app.IsRunning ? app.Name : $"{app.Name} (stopped)";
            sb.AppendLine($"    {appId}[\"{Esc(displayName)}<br/><small>{subtitle}</small>\"]:::logicapp");

            foreach (var wf in app.Workflows)
                foreach (var edge in wf.Edges)
                    registry.Register(edge.Target, edge.ActionName);
        }

        // Declare target nodes
        sb.AppendLine();
        foreach (var node in registry.AllNodes())
            sb.AppendLine($"    {node.Id}[\"{NodeLabel(node)}\"]:::{TypeClass[node.CallType]}");

        // Outbound edges — deduplicated per (app, target, label)
        sb.AppendLine();
        foreach (var app in inventory.LogicApps)
        {
            var appId = SafeId("app", app.Name);
            var seen = new HashSet<(string, string)>();
            foreach (var wf in app.Workflows)
                foreach (var edge in wf.Edges)
                {
                    var targetId = registry.TryGetId(edge.Target);
                    var label = EdgeLabel(edge);
                    if (targetId is not null && seen.Add((targetId, label)))
                        sb.AppendLine(EdgeLine(appId, targetId, label));
                }
        }

        // Trigger-source nodes — every triggered workflow gets its own dedicated node
        // (e.g. "External caller", "orders-queue / Queue Event"), never shared between
        // workflows, with a "Triggers" edge to its parent Logic App.
        foreach (var app in inventory.LogicApps)
        {
            var appId = SafeId("app", app.Name);
            foreach (var wf in app.Workflows)
                if (wf.Trigger is { } trig)
                {
                    var trigId = SafeId("trig", $"{app.Name}_{wf.Name}");
                    sb.AppendLine($"    {trigId}[\"{Esc(trig.Source)}<br/><small>{Esc(trig.DisplayType)}</small>\"]:::trigger");
                    sb.AppendLine(EdgeLine(trigId, appId, "Triggers"));
                }
        }

        sb.Append(ClassDefs);
        return sb.ToString();
    }

    // ── Detail mode ───────────────────────────────────────────────────────────

    private static string BuildDetail(Inventory inventory)
    {
        if (inventory.LogicApps.Count == 0)
            return "flowchart LR\n    empty[\"No logic apps found — run a Scan first\"]";

        var allWorkflows = inventory.LogicApps.SelectMany(a => a.Workflows).ToList();
        if (allWorkflows.Count == 0)
            return "flowchart LR\n    empty[\"No workflows match the current filter\"]";

        var sb = new StringBuilder(LinearCurveDirective + "flowchart LR\n");
        var registry = new TargetRegistry(groupByPath: true);
        var multiApp = inventory.LogicApps.Count > 1;

        // Declare workflow nodes + register outbound targets
        foreach (var app in inventory.LogicApps)
        {
            if (app.Workflows.Count > 0)
                sb.AppendLine($"    %% {app.Name}");

            foreach (var wf in app.Workflows)
            {
                var wfId = SafeId("wf", $"{app.Name}_{wf.Name}");
                var subtitle = multiApp ? Esc(app.Name) : "Workflow";
                sb.AppendLine($"    {wfId}[\"{Esc(wf.Name)}<br/><small>{subtitle}</small>\"]:::workflow");

                foreach (var edge in wf.Edges)
                    registry.Register(edge.Target, edge.ActionName);
            }
        }

        // Declare target nodes
        sb.AppendLine();
        foreach (var node in registry.AllNodes())
            sb.AppendLine($"    {node.Id}[\"{NodeLabel(node)}\"]:::{TypeClass[node.CallType]}");

        // Outbound edges — deduplicated per (workflow, target, label)
        sb.AppendLine();
        foreach (var app in inventory.LogicApps)
        {
            foreach (var wf in app.Workflows)
            {
                var wfId = SafeId("wf", $"{app.Name}_{wf.Name}");
                var seen = new HashSet<(string, string)>();
                foreach (var edge in wf.Edges)
                {
                    var targetId = registry.TryGetId(edge.Target);
                    var label = EdgeLabel(edge);
                    if (targetId is not null && seen.Add((targetId, label)))
                        sb.AppendLine(EdgeLine(wfId, targetId, label));
                }
            }
        }

        // Trigger-source nodes — every triggered workflow gets its own dedicated node
        // (e.g. "External caller", "orders-queue / Queue Event"), never shared between
        // workflows, with a "Triggers" edge to the workflow.
        foreach (var app in inventory.LogicApps)
            foreach (var wf in app.Workflows)
                if (wf.Trigger is { } trig)
                {
                    var wfId = SafeId("wf", $"{app.Name}_{wf.Name}");
                    var trigId = SafeId("trig", $"{app.Name}_{wf.Name}");
                    sb.AppendLine($"    {trigId}[\"{Esc(trig.Source)}<br/><small>{Esc(trig.DisplayType)}</small>\"]:::trigger");
                    sb.AppendLine(EdgeLine(trigId, wfId, "Triggers"));
                }

        sb.Append(ClassDefs);
        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string SafeId(string prefix, string name)
    {
        var safe = NonAlphanumeric().Replace(name, "_").TrimStart('_');
        if (safe.Length == 0) safe = "node";
        return $"{prefix}_{safe}";
    }

    private static string Esc(string s) =>
        s.Replace("\"", "'").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Returns the label to show on an outbound edge: its operation (e.g. "Send",
    /// "Get Secret"), falling back to its HTTP method, falling back to "Call" — so every
    /// edge always has a label.</summary>
    private static string EdgeLabel(CallEdge edge) =>
        edge.Operation ?? edge.Method ?? "Call";

    /// <summary>Renders a flowchart edge, with an optional operation label (e.g. "Send", "Get Secret").</summary>
    private static string EdgeLine(string fromId, string toId, string? operation) =>
        operation is null
            ? $"    {fromId} --> {toId}"
            : $"    {fromId} -- {Esc(operation)} --> {toId}";

    private const int MaxActionNamesShown = 3;

    private static string NodeLabel(TargetNode node)
    {
        var subtitle = node.IsUnresolved
            ? $"{TypeSubtitle[node.CallType]} · dynamic"
            : TypeSubtitle[node.CallType];
        var title = node.Path is null ? Esc(node.Label) : $"{Esc(node.Label)}{Esc(node.Path)}";
        var label = $"{title}<br/><small>{subtitle}</small>";

        var showActions = node.CallType is CallType.Http or CallType.Unknown;
        if (showActions && node.ActionNames.Count > 0)
        {
            var names = node.ActionNames.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            var shown = string.Join(", ", names.Take(MaxActionNamesShown).Select(Esc));
            if (names.Count > MaxActionNamesShown)
                shown += $", +{names.Count - MaxActionNamesShown} more";
            label += $"<br/><small><em>{shown}</em></small>";
        }

        return label;
    }

    // ── Target node registry ──────────────────────────────────────────────────

    private sealed record TargetNode(string Id, string Label, string? Path, CallType CallType, bool IsUnresolved)
    {
        public HashSet<string> ActionNames { get; } = new();
    }

    /// <summary>
    /// Deduplicates external targets into diagram nodes. When <paramref name="groupByPath"/>
    /// is true (Detail mode), HTTP targets are split into separate nodes per (host, path) —
    /// e.g. distinct REST endpoints on the same host become distinct nodes — instead of
    /// collapsing every action against a host into one node. Summary mode keeps the
    /// coarser host-only grouping.
    /// </summary>
    private sealed class TargetRegistry(bool groupByPath = false)
    {
        private readonly Dictionary<(CallType, string, string?), TargetNode> _map = new();

        private (CallType, string, string?) KeyFor(ExternalTarget target) =>
            (target.CallType, target.Name, groupByPath ? target.Path : null);

        public void Register(ExternalTarget target, string? actionName = null)
        {
            var key = KeyFor(target);
            if (!_map.TryGetValue(key, out var node))
            {
                var nodeId = SafeId("t", $"{target.CallType}_{target.Name}{(key.Item3 is not null ? "_" + key.Item3 : "")}");
                node = new TargetNode(nodeId, target.Name, key.Item3, target.CallType,
                    IsUnresolved: target.RawExpression is not null);
                _map[key] = node;
            }
            if (actionName is not null)
                node.ActionNames.Add(actionName);
        }

        public string? TryGetId(ExternalTarget target) =>
            _map.TryGetValue(KeyFor(target), out var node) ? node.Id : null;

        public IEnumerable<TargetNode> AllNodes() => _map.Values;
    }
}
