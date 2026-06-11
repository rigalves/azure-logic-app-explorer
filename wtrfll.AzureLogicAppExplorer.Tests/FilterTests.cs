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
    public void NoFilter_ReturnsAll()
    {
        var result = InventoryFilter.Apply(BuildInventory());
        Assert.Equal(2, result.LogicApps.Count);
        Assert.Equal(2, result.LogicApps[0].Workflows.Count);
        Assert.Single(result.LogicApps[1].Workflows);
    }

    [Fact]
    public void KeywordFilter_MatchesOnTargetName()
    {
        var result = InventoryFilter.Apply(BuildInventory(), keyword: "salesforce");
        // Both apps have Salesforce edges
        Assert.Equal(2, result.LogicApps.Count);
    }

    [Fact]
    public void KeywordFilter_MatchesOnActionName()
    {
        var result = InventoryFilter.Apply(BuildInventory(), keyword: "Notify_Teams");
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-orders", result.LogicApps[0].Name);
        Assert.Single(result.LogicApps[0].Workflows);
        Assert.Equal("wf-cancel-order", result.LogicApps[0].Workflows[0].Name);
    }

    [Fact]
    public void KeywordFilter_MatchesOnWorkflowName()
    {
        var result = InventoryFilter.Apply(BuildInventory(), keyword: "patient");
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-patients", result.LogicApps[0].Name);
    }

    [Fact]
    public void KeywordFilter_NoMatch_ReturnsEmpty()
    {
        var result = InventoryFilter.Apply(BuildInventory(), keyword: "xyzzy-does-not-exist");
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
        var result = InventoryFilter.Apply(BuildInventoryWithStoppedApp(), keyword: "salesforce");
        Assert.Contains(result.LogicApps, a => a.Name == "lapp-stopped");
        var stopped = result.LogicApps.Single(a => a.Name == "lapp-stopped");
        Assert.False(stopped.IsRunning);
        Assert.Empty(stopped.Workflows);
    }

    [Fact]
    public void KeywordFilter_NoMatch_KeepsStoppedAppOnly()
    {
        var result = InventoryFilter.Apply(BuildInventoryWithStoppedApp(), keyword: "xyzzy-does-not-exist");
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-stopped", result.LogicApps[0].Name);
    }

    [Fact]
    public void NoFilter_PreservesIsRunningFlag()
    {
        var result = InventoryFilter.Apply(BuildInventoryWithStoppedApp());
        var stopped = result.LogicApps.Single(a => a.Name == "lapp-stopped");
        var running = result.LogicApps.Single(a => a.Name == "lapp-orders");
        Assert.False(stopped.IsRunning);
        Assert.True(running.IsRunning);
    }

    [Fact]
    public void EmptyInventory_ReturnsEmpty()
    {
        var result = InventoryFilter.Apply(Inventory.Empty, keyword: "anything");
        Assert.Empty(result.LogicApps);
    }
}
