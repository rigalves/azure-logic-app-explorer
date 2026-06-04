using FlowAtlas.Filtering;
using FlowAtlas.Model;

namespace FlowAtlas.Tests;

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
    public void LogicAppFilter_ScopesToOneApp()
    {
        var result = InventoryFilter.Apply(BuildInventory(), logicAppName: "lapp-orders");
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-orders", result.LogicApps[0].Name);
        Assert.Equal(2, result.LogicApps[0].Workflows.Count);
    }

    [Fact]
    public void LogicAppFilter_IsCaseInsensitive()
    {
        var result = InventoryFilter.Apply(BuildInventory(), logicAppName: "LAPP-ORDERS");
        Assert.Single(result.LogicApps);
    }

    [Fact]
    public void LogicAppFilter_UnknownName_ReturnsEmpty()
    {
        var result = InventoryFilter.Apply(BuildInventory(), logicAppName: "does-not-exist");
        Assert.Empty(result.LogicApps);
    }

    [Fact]
    public void WorkflowFilter_ScopesToOneWorkflow()
    {
        var result = InventoryFilter.Apply(BuildInventory(), workflowName: "wf-create-order");
        Assert.Single(result.LogicApps);
        Assert.Single(result.LogicApps[0].Workflows);
        Assert.Equal("wf-create-order", result.LogicApps[0].Workflows[0].Name);
    }

    [Fact]
    public void WorkflowFilter_DropsAppsWithNoMatch()
    {
        // wf-cancel-order only exists in lapp-orders, so lapp-patients should be dropped
        var result = InventoryFilter.Apply(BuildInventory(), workflowName: "wf-cancel-order");
        Assert.Single(result.LogicApps);
        Assert.Equal("lapp-orders", result.LogicApps[0].Name);
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

    [Fact]
    public void CombinedFilter_AppAndKeyword()
    {
        var result = InventoryFilter.Apply(BuildInventory(),
            logicAppName: "lapp-orders", keyword: "salesforce");
        Assert.Single(result.LogicApps);
        Assert.Single(result.LogicApps[0].Workflows); // only wf-create-order has Salesforce
    }

    [Fact]
    public void EmptyInventory_ReturnsEmpty()
    {
        var result = InventoryFilter.Apply(Inventory.Empty, keyword: "anything");
        Assert.Empty(result.LogicApps);
    }
}
