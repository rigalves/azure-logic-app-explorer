using wtrfll.AzureLogicAppExplorer.Model;
using System.Text;

namespace wtrfll.AzureLogicAppExplorer.Services;

/// <summary>
/// The kind of node a diagram can draw. Eight kinds mirror the <see cref="CallType"/> values that
/// produce an outbound target node; three are structural roles that are not call types.
/// </summary>
public enum NodeKind
{
    // ── Target kinds — one per CallType that produces an outbound target node ──
    Http,
    Function,
    Salesforce,
    ManagedConnector,
    ServiceProvider,
    ServiceBus,
    ChildWorkflow,
    KeyVault,

    // ── Structural kinds — diagram roles that are not CallTypes ──
    LogicApp,
    Workflow,
    TriggerSource,
}

/// <summary>
/// The visual identity of one <see cref="NodeKind"/>.
/// </summary>
/// <param name="MermaidClass">Mermaid classDef name (e.g. "salesforce"). Stable string: node
/// ":::class" references and the legend's client-side show/hide filter both key on it.</param>
/// <param name="Fill">Node fill colour — the single colour source shared by the generated classDef
/// and the legend swatch, so the two can never drift.</param>
/// <param name="TextColor">Mermaid classDef text colour.</param>
/// <param name="Stroke">Mermaid classDef stroke colour.</param>
/// <param name="StrokeWidth">classDef stroke width in px, or null for the Mermaid default.</param>
/// <param name="LegendLabel">Label shown in the legend row (e.g. "HTTP / API").</param>
/// <param name="NodeSubtitle">Subtitle under a target node in the diagram (e.g. "HTTP"); null for
/// structural kinds, which build their own labels.</param>
/// <param name="BadgeClass">Bootstrap badge class for the Raw Inventory table; null for structural
/// kinds, which never appear as a table badge.</param>
public sealed record NodeStyle(
    NodeKind Kind,
    string MermaidClass,
    string Fill,
    string TextColor,
    string Stroke,
    int? StrokeWidth,
    string LegendLabel,
    string? NodeSubtitle,
    string? BadgeClass);

/// <summary>
/// The single source of truth for how every diagram node kind looks. The <see cref="MermaidBuilder"/>
/// reads node classes / subtitles and the generated <see cref="ClassDefs"/> from here; the Explorer
/// legend and inventory table read the same rows. A node kind's colour is therefore defined exactly
/// once, killing the hand-synced "legend hex must match classDef fill" drift.
/// </summary>
public static class DiagramPalette
{
    // Ordered for legend display.
    private static readonly NodeStyle[] Styles =
    [
        //         Kind                       class              fill       text    stroke     w     legend label          node subtitle         badge class
        new(NodeKind.LogicApp,        "logicapp",        "#0d6efd", "#fff", "#0a58ca", 2,    "Logic App",          null,                 null),
        new(NodeKind.Workflow,        "workflow",        "#0dcaf0", "#000", "#0aa2c0", 1,    "Workflow",           null,                 null),
        new(NodeKind.Http,            "http",            "#6c757d", "#fff", "#495057", null, "HTTP / API",         "HTTP",               "bg-secondary"),
        new(NodeKind.Function,        "funcapp",         "#198754", "#fff", "#146c43", null, "Azure Function",     "Azure Function",     "bg-success"),
        new(NodeKind.Salesforce,      "salesforce",      "#00A1E0", "#fff", "#0074a2", 2,    "Salesforce",         "Salesforce",         "bg-info text-dark"),
        new(NodeKind.ManagedConnector,"managed",         "#6f42c1", "#fff", "#5a2d91", null, "Managed Connector",  "Managed Connector",  "bg-primary"),
        new(NodeKind.ServiceProvider, "serviceprovider", "#fd7e14", "#000", "#d45e00", null, "Built-in Connector", "Built-in Connector", "bg-warning text-dark"),
        new(NodeKind.ServiceBus,      "servicebus",      "#f0ad4e", "#000", "#ec971f", 2,    "Service Bus",        "Service Bus",        "bg-dark"),
        new(NodeKind.ChildWorkflow,   "childwf",         "#20c997", "#000", "#17a589", null, "Child Workflow",     "Child Workflow",     "bg-success"),
        new(NodeKind.KeyVault,        "keyvault",        "#dc3545", "#fff", "#a02530", null, "Key Vault",          "Key Vault",          "bg-danger"),
        new(NodeKind.TriggerSource,   "trigger",         "#6610f2", "#fff", "#4d0bb8", null, "Trigger Source",     null,                 null),
    ];

    private static readonly Dictionary<NodeKind, NodeStyle> ByKind = Styles.ToDictionary(s => s.Kind);

    /// <summary>All node styles, in legend display order.</summary>
    public static IReadOnlyList<NodeStyle> All => Styles;

    /// <summary>The visual style for a node kind.</summary>
    public static NodeStyle For(NodeKind kind) => ByKind[kind];

    /// <summary>The visual style for a call type's node kind.</summary>
    public static NodeStyle For(CallType type) => ByKind[KindOf(type)];

    /// <summary>
    /// Maps a <see cref="CallType"/> to its diagram <see cref="NodeKind"/>. <see cref="CallType.Unknown"/>
    /// folds to <see cref="NodeKind.Http"/> — its long-standing visual fallback.
    /// </summary>
    public static NodeKind KindOf(CallType type) => type switch
    {
        CallType.Http             => NodeKind.Http,
        CallType.Function         => NodeKind.Function,
        CallType.Salesforce       => NodeKind.Salesforce,
        CallType.ManagedConnector => NodeKind.ManagedConnector,
        CallType.ServiceProvider  => NodeKind.ServiceProvider,
        CallType.ServiceBus       => NodeKind.ServiceBus,
        CallType.ChildWorkflow    => NodeKind.ChildWorkflow,
        CallType.KeyVault         => NodeKind.KeyVault,
        _                         => NodeKind.Http, // Unknown and any future value
    };

    /// <summary>
    /// Generates the Mermaid classDef block from <see cref="All"/>. Each line's colours are the same
    /// fields the legend swatches read, so the diagram and the legend cannot drift out of sync.
    /// </summary>
    public static string ClassDefs()
    {
        var sb = new StringBuilder("\n");
        foreach (var s in Styles)
        {
            sb.Append("    classDef ").Append(s.MermaidClass)
              .Append(" fill:").Append(s.Fill)
              .Append(",color:").Append(s.TextColor)
              .Append(",stroke:").Append(s.Stroke);
            if (s.StrokeWidth is int w)
                sb.Append(",stroke-width:").Append(w).Append("px");
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
