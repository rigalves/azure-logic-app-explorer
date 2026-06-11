using wtrfll.AzureLogicAppExplorer.Filtering;
using wtrfll.AzureLogicAppExplorer.Model;

namespace wtrfll.AzureLogicAppExplorer.Tests;

public class FilterTests
{
    private static Inventory BuildInventory() => new()
    {
        ScannedAt = DateTimeOffset.UtcNow,
        LogicApps =
        [
            new LogicAppInfo
            {
                Name = "lapp-orders",
                Workflows =
                [
                    new WorkflowInfo
                    {
                        Name = "wf-create-order",
                        LogicAppName = "lapp-orders",
                        IsStateful = true,
                        Edges =
                        [
                            new CallEdge("Sync_Salesforce", CallType.Salesforce,
                                new ExternalTarget(CallType.Salesforce, "Salesforce")),
                            new CallEdge("Call_API", CallType.Http,
                                new ExternalTarget(CallType.Http, "api.orders.com")),
                        ],
                    },
                    new WorkflowInfo
                    {
                        Name = "wf-cancel-order",
                        LogicAppName = "lapp-orders",
                        IsStateful = true,
                        Edges =
                        [
                            new CallEdge("Notify_Teams", CallType.ManagedConnector,
                                new ExternalTarget(CallType.ManagedConnector, "Microsoft Teams")),
                        ],
                    },
                ],
            },
            new LogicAppInfo
            {
                Name = "lapp-patients",
                Workflows =
                [
                    new WorkflowInfo
                    {
                        Name = "wf-sync-patient",
                        LogicAppName = "lapp-patients",
                        IsStateful = true,
                        Edges =
                        [
                            new CallEdge("Get_Patient", CallType.Http,
                                new ExternalTarget(CallType.Http, "api.patients.com")),
                            new CallEdge("Update_SF", CallType.Salesforce,
                                new ExternalTarget(CallType.Salesforce, "Salesforce")),
                        ],
                    },
                ],
            },
        ],
    };

    [Fact]
    public void NoSelection_ReturnsAll()
    {
        var result = InventoryFilter.Apply(BuildInventory(), InventorySelection.All);
        Assert.Equal(2, result.LogicApps.Count);
        Assert.Equal(2, result.LogicApps[0].Workflows.Count);
        Assert.Single(result.LogicApps[1].Workflows);
    }

    [Fact]
    public void AppNamesFilter_ScopesToSelectedApps()
    {
        var result = InventoryFilter.Apply(BuildInventory(),
            new InventorySelection(AppNames: ["lapp-orders"]));
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-orders", result.LogicApps[0].Name);
        Assert.Equal(2, result.LogicApps[0].Workflows.Count);
    }

    [Fact]
    public void AppNamesFilter_UnknownName_ReturnsEmpty()
    {
        var result = InventoryFilter.Apply(BuildInventory(),
            new InventorySelection(AppNames: ["does-not-exist"]));
        Assert.Empty(result.LogicApps);
    }

    [Fact]
    public void WorkflowKeysFilter_ScopesToSelectedWorkflow()
    {
        var result = InventoryFilter.Apply(BuildInventory(),
            new InventorySelection(WorkflowKeys: [InventorySelection.WorkflowKey("lapp-orders", "wf-create-order")]));
        Assert.Single(result.LogicApps);
        Assert.Single(result.LogicApps[0].Workflows);
        Assert.Equal("wf-create-order", result.LogicApps[0].Workflows[0].Name);
    }

    [Fact]
    public void WorkflowKeysFilter_DropsRunningAppsWithNoMatch()
    {
        // wf-cancel-order only exists in lapp-orders, so the (running) lapp-patients should be dropped.
        var result = InventoryFilter.Apply(BuildInventory(),
            new InventorySelection(WorkflowKeys: [InventorySelection.WorkflowKey("lapp-orders", "wf-cancel-order")]));
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-orders", result.LogicApps[0].Name);
    }

    [Fact]
    public void KeywordFilter_MatchesOnTargetName()
    {
        var result = InventoryFilter.Apply(BuildInventory(), new InventorySelection(Keyword: "salesforce"));
        // Both apps have Salesforce edges
        Assert.Equal(2, result.LogicApps.Count);
    }

    [Fact]
    public void KeywordFilter_MatchesOnActionName()
    {
        var result = InventoryFilter.Apply(BuildInventory(), new InventorySelection(Keyword: "Notify_Teams"));
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-orders", result.LogicApps[0].Name);
        Assert.Single(result.LogicApps[0].Workflows);
        Assert.Equal("wf-cancel-order", result.LogicApps[0].Workflows[0].Name);
    }

    [Fact]
    public void KeywordFilter_MatchesOnWorkflowName()
    {
        var result = InventoryFilter.Apply(BuildInventory(), new InventorySelection(Keyword: "patient"));
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-patients", result.LogicApps[0].Name);
    }

    [Fact]
    public void KeywordFilter_NoMatch_ReturnsEmpty()
    {
        var result = InventoryFilter.Apply(BuildInventory(), new InventorySelection(Keyword: "xyzzy-does-not-exist"));
        Assert.Empty(result.LogicApps);
    }

    [Fact]
    public void AppAndKeywordFilter_Combine()
    {
        var result = InventoryFilter.Apply(BuildInventory(),
            new InventorySelection(AppNames: ["lapp-orders"], Keyword: "salesforce"));
        Assert.Single(result.LogicApps);
        Assert.Single(result.LogicApps[0].Workflows); // only wf-create-order has Salesforce
    }

    [Fact]
    public void WorkflowKeysAndKeywordFilter_BothMustMatch()
    {
        // wf-create-order is selected, but only wf-cancel-order matches "Notify_Teams" — no overlap.
        var result = InventoryFilter.Apply(BuildInventory(),
            new InventorySelection(
                WorkflowKeys: [InventorySelection.WorkflowKey("lapp-orders", "wf-create-order")],
                Keyword: "Notify_Teams"));
        Assert.Empty(result.LogicApps);
    }

    private static Inventory BuildInventoryWithStoppedApp() => new()
    {
        ScannedAt = DateTimeOffset.UtcNow,
        LogicApps =
        [
            .. BuildInventory().LogicApps,
            new LogicAppInfo { Name = "lapp-stopped", Workflows = [], IsRunning = false },
        ],
    };

    [Fact]
    public void KeywordFilter_StoppedApp_StillIncluded()
    {
        var result = InventoryFilter.Apply(BuildInventoryWithStoppedApp(), new InventorySelection(Keyword: "salesforce"));
        Assert.Contains(result.LogicApps, a => a.Name == "lapp-stopped");
        var stopped = result.LogicApps.Single(a => a.Name == "lapp-stopped");
        Assert.False(stopped.IsRunning);
        Assert.Empty(stopped.Workflows);
    }

    [Fact]
    public void KeywordFilter_NoMatch_KeepsStoppedAppOnly()
    {
        var result = InventoryFilter.Apply(BuildInventoryWithStoppedApp(), new InventorySelection(Keyword: "xyzzy-does-not-exist"));
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-stopped", result.LogicApps[0].Name);
    }

    [Fact]
    public void WorkflowKeysFilter_StoppedApp_StillIncluded()
    {
        var result = InventoryFilter.Apply(BuildInventoryWithStoppedApp(),
            new InventorySelection(WorkflowKeys: [InventorySelection.WorkflowKey("lapp-orders", "wf-create-order")]));
        Assert.Contains(result.LogicApps, a => a.Name == "lapp-stopped");
    }

    [Fact]
    public void NoSelection_PreservesIsRunningFlag()
    {
        var result = InventoryFilter.Apply(BuildInventoryWithStoppedApp(), InventorySelection.All);
        var stopped = result.LogicApps.Single(a => a.Name == "lapp-stopped");
        var running = result.LogicApps.Single(a => a.Name == "lapp-orders");
        Assert.False(stopped.IsRunning);
        Assert.True(running.IsRunning);
    }

    [Fact]
    public void EmptyInventory_ReturnsEmpty()
    {
        var result = InventoryFilter.Apply(Inventory.Empty, new InventorySelection(Keyword: "anything"));
        Assert.Empty(result.LogicApps);
    }
}
