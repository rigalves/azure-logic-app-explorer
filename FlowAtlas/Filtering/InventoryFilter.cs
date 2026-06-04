using FlowAtlas.Model;

namespace FlowAtlas.Filtering;

public static class InventoryFilter
{
    /// <summary>
    /// Returns a new Inventory containing only the apps/workflows/edges that match all supplied filters.
    /// Null/empty filter values are treated as "no filter" for that dimension.
    /// </summary>
    public static Inventory Apply(
        Inventory source,
        string? logicAppName = null,
        string? workflowName = null,
        string? keyword = null)
    {
        var apps = source.LogicApps.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(logicAppName))
            apps = apps.Where(a => a.Name.Equals(logicAppName, StringComparison.OrdinalIgnoreCase));

        var filtered = apps
            .Select(app => FilterApp(app, workflowName, keyword))
            .Where(a => a is not null)
            .Select(a => a!)
            .ToList();

        return new Inventory { LogicApps = filtered, ScannedAt = source.ScannedAt };
    }

    private static LogicAppInfo? FilterApp(LogicAppInfo app, string? workflowName, string? keyword)
    {
        var workflows = app.Workflows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(workflowName))
            workflows = workflows.Where(w => w.Name.Equals(workflowName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(keyword))
            workflows = workflows.Where(w => WorkflowMatchesKeyword(w, keyword));

        var list = workflows.ToList();

        // If a filter reduced workflows to zero, drop the whole app
        if ((workflowName is not null || keyword is not null) && list.Count == 0)
            return null;

        return new LogicAppInfo { Name = app.Name, Workflows = list };
    }

    private static bool WorkflowMatchesKeyword(WorkflowInfo wf, string keyword) =>
        wf.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        wf.Edges.Any(e => EdgeMatchesKeyword(e, keyword));

    private static bool EdgeMatchesKeyword(CallEdge edge, string keyword) =>
        edge.ActionName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        edge.Target.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        (edge.Target.RawExpression?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
        edge.CallType.ToString().Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
