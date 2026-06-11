using wtrfll.AzureLogicAppExplorer.Model;

namespace wtrfll.AzureLogicAppExplorer.Filtering;

public static class InventoryFilter
{
    /// <summary>
    /// Returns a new Inventory narrowed to the given <see cref="InventorySelection"/>: apps
    /// restricted to <see cref="InventorySelection.AppNames"/> (if any), workflows restricted
    /// to <see cref="InventorySelection.WorkflowKeys"/> and/or matching
    /// <see cref="InventorySelection.Keyword"/> (if either is set). <see cref="InventorySelection.All"/>
    /// returns the inventory unchanged.
    /// </summary>
    public static Inventory Apply(Inventory source, InventorySelection selection)
    {
        var apps = source.LogicApps.AsEnumerable();

        if (selection.AppNames is { Count: > 0 } appNames)
            apps = apps.Where(a => appNames.Contains(a.Name));

        var filtered = apps
            .Select(app => FilterApp(app, selection))
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        return new Inventory { LogicApps = filtered, ScannedAt = source.ScannedAt };
    }

    private static LogicAppInfo? FilterApp(LogicAppInfo app, InventorySelection selection)
    {
        var workflows = app.Workflows.AsEnumerable();

        if (selection.WorkflowKeys is { Count: > 0 } workflowKeys)
            workflows = workflows.Where(w => workflowKeys.Contains(InventorySelection.WorkflowKey(app.Name, w.Name)));

        if (!string.IsNullOrWhiteSpace(selection.Keyword))
            workflows = workflows.Where(w => WorkflowMatchesKeyword(w, selection.Keyword));

        var list = workflows.ToList();

        // If a workflow-key or keyword filter reduced workflows to zero, drop the whole app —
        // but never drop a stopped app, since it stays visible regardless of its workflows.
        var workflowFilterActive = selection.WorkflowKeys is { Count: > 0 } || !string.IsNullOrWhiteSpace(selection.Keyword);
        if (app.Workflows.Count > 0 && list.Count == 0 && workflowFilterActive && app.IsRunning)
            return null;

        return new LogicAppInfo { Name = app.Name, Workflows = list, IsRunning = app.IsRunning, ScanErrors = app.ScanErrors };
    }

    private static bool WorkflowMatchesKeyword(WorkflowInfo wf, string keyword) =>
        wf.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        (wf.Trigger?.EntityName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (wf.Domain?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
        wf.Classification.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        wf.Edges.Any(e => EdgeMatchesKeyword(e, keyword));

    private static bool EdgeMatchesKeyword(CallEdge edge, string keyword) =>
        edge.ActionName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        edge.Target.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        (edge.Target.RawExpression?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
        edge.CallType.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
