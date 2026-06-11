namespace wtrfll.AzureLogicAppExplorer.Filtering;

/// <summary>
/// What the user has chosen to view: a set of Logic App names, a set of workflow keys
/// (see <see cref="WorkflowKey"/>), and/or a free-text keyword. A null or empty set/keyword
/// means that dimension is unfiltered. This is the one shape <see cref="InventoryFilter.Apply"/>
/// accepts — the view builds it from its checkboxes and search box, and crosses the filter
/// seam with a single call.
/// </summary>
public sealed record InventorySelection(
    IReadOnlyCollection<string>? AppNames = null,
    IReadOnlyCollection<string>? WorkflowKeys = null,
    string? Keyword = null)
{
    /// <summary>The unfiltered selection — every app and workflow.</summary>
    public static readonly InventorySelection All = new();

    /// <summary>The composite key identifying a workflow within an app, e.g. "lapp-orders||wf-create-order".</summary>
    public static string WorkflowKey(string appName, string workflowName) => $"{appName}||{workflowName}";
}
