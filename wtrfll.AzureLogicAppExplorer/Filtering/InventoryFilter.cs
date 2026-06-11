using wtrfll.AzureLogicAppExplorer.Model;

namespace wtrfll.AzureLogicAppExplorer.Filtering;

public static class InventoryFilter
{
    /// <summary>
    /// Returns a new Inventory containing only the workflows whose name, trigger, domain,
    /// classification, or call edges match the keyword. A null/empty keyword returns the
    /// inventory unchanged.
    /// </summary>
    public static Inventory Apply(Inventory source, string? keyword = null)
    {
        var filtered = source.LogicApps
            .Select(app => FilterApp(app, keyword))
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        return new Inventory { LogicApps = filtered, ScannedAt = source.ScannedAt };
    }

    private static LogicAppInfo? FilterApp(LogicAppInfo app, string? keyword)
    {
        var workflows = app.Workflows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
            workflows = workflows.Where(w => WorkflowMatchesKeyword(w, keyword));

        var list = workflows.ToList();

        // If the keyword reduced workflows to zero, drop the whole app — but never drop a
        // stopped app, since it had no workflows to begin with and must stay visible.
        if (app.Workflows.Count > 0 && list.Count == 0 && keyword is not null)
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
