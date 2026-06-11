using wtrfll.AzureLogicAppExplorer.Model;

namespace wtrfll.AzureLogicAppExplorer.Presentation;

/// <summary>One deduplicated interaction shown as a chip in the Raw Inventory table and CSV export.</summary>
public sealed record InteractionView(CallType CallType, string? Detail, string Target, string? RawExpression, int Count);

/// <summary>
/// Projects a workflow's outbound Call Edges into the deduplicated, ordered list of
/// interactions shared by the Raw Inventory table and the CSV export.
/// </summary>
public static class WorkflowInteractions
{
    /// <summary>
    /// Builds the deduplicated, ordered list of interactions for a workflow's edges.
    /// "Detail" is the HTTP method (Http/ApiConnection) or friendly operation
    /// (ServiceBus/KeyVault). Identical interactions (same type/detail/target) are
    /// collapsed into one entry with a Count, in first-seen order.
    /// </summary>
    public static List<InteractionView> Build(WorkflowInfo wf) =>
        wf.Edges
            .Select(e => new InteractionView(
                e.CallType,
                e.Method ?? e.Operation,
                e.Target.Name + (e.Target.Path ?? ""),
                e.Target.RawExpression,
                1))
            .GroupBy(i => (i.CallType, i.Detail, i.Target, i.RawExpression))
            .Select(g => g.First() with { Count = g.Count() })
            .ToList();
}
