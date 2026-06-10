using wtrfll.AzureLogicAppExplorer.Model;
using wtrfll.AzureLogicAppExplorer.Services;

namespace wtrfll.AzureLogicAppExplorer.Tests;

public class MermaidBuilderTests
{
    private readonly MermaidBuilder _builder = new();

    private static Inventory TwoAppInventory() => new()
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
                            new CallEdge("Call_Orders_API", CallType.Http,
                                new ExternalTarget(CallType.Http, "api.orders.com")),
                            new CallEdge("Call_Func", CallType.Function,
                                new ExternalTarget(CallType.Function, "func-app-orders")),
                        ],
                    },
                    new WorkflowInfo
                    {
                        Name = "wf-cancel-order",
                        LogicAppName = "lapp-orders",
                        IsStateful = true,
                        Edges =
                        [
                            // Same Salesforce target as wf-create-order → shared node
                            new CallEdge("Also_Sync_SF", CallType.Salesforce,
                                new ExternalTarget(CallType.Salesforce, "Salesforce")),
                            new CallEdge("Call_Child", CallType.ChildWorkflow,
                                new ExternalTarget(CallType.ChildWorkflow, "wf-validate")),
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
                            new CallEdge("Send_SB", CallType.ServiceBus,
                                new ExternalTarget(CallType.ServiceBus, "orders-queue")),
                        ],
                    },
                ],
            },
        ],
    };

    // ── Summary mode ──────────────────────────────────────────────────────────

    [Fact]
    public void Summary_StartsWithFlowchartLR()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Summary);
        Assert.Contains("flowchart LR", mmd);
    }

    [Fact]
    public void Summary_ContainsBothLogicAppNodes()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Summary);
        Assert.Contains("lapp-orders", mmd);
        Assert.Contains("lapp-patients", mmd);
    }

    [Fact]
    public void Summary_SalesforceNodeHasSalesforceClass()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Summary);
        Assert.Contains(":::salesforce", mmd);
    }

    [Fact]
    public void Summary_SalesforceNodeAppearsOnce_DespiteTwoWorkflowsUsingIt()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Summary);
        var salesforceNodeDeclarations = mmd
            .Split('\n')
            .Count(line => line.Contains(":::salesforce") && line.TrimStart().StartsWith("t_"));
        Assert.Equal(1, salesforceNodeDeclarations);
    }

    [Fact]
    public void Summary_ExpressionTargets_AreInDiagram_WithUnresolvedSubtitle()
    {
        var inv = new Inventory
        {
            ScannedAt = DateTimeOffset.UtcNow,
            LogicApps =
            [
                new LogicAppInfo
                {
                    Name = "lapp-test",
                    Workflows =
                    [
                        new WorkflowInfo
                        {
                            Name = "wf-test", LogicAppName = "lapp-test", IsStateful = true,
                            Edges =
                            [
                                new CallEdge("Expr_Call", CallType.Http,
                                    new ExternalTarget(CallType.Http, "⟨appsetting:API_URL⟩",
                                        RawExpression: "@{parameters('url')}/api")),
                                new CallEdge("Real_Call", CallType.Http,
                                    new ExternalTarget(CallType.Http, "api.real.com")),
                            ],
                        },
                    ],
                },
            ],
        };
        var mmd = _builder.Build(inv, DiagramMode.Summary);
        // Both targets appear in the diagram
        Assert.Contains("api.real.com", mmd);
        Assert.Contains("appsetting:API_URL", mmd);
        // Dynamic (unresolved) target gets the "dynamic" subtitle
        Assert.Contains("dynamic", mmd);
    }

    [Fact]
    public void Summary_ResolvedExpressionTarget_HasNoUnresolvedSubtitle()
    {
        var inv = new Inventory
        {
            ScannedAt = DateTimeOffset.UtcNow,
            LogicApps =
            [
                new LogicAppInfo
                {
                    Name = "lapp-test",
                    Workflows =
                    [
                        new WorkflowInfo
                        {
                            Name = "wf-test", LogicAppName = "lapp-test", IsStateful = true,
                            Edges =
                            [
                                // RawExpression is null — fully resolved by ParametersParser
                                new CallEdge("Resolved_Call", CallType.Http,
                                    new ExternalTarget(CallType.Http, "api.resolved.com")),
                            ],
                        },
                    ],
                },
            ],
        };
        var mmd = _builder.Build(inv, DiagramMode.Summary);
        Assert.Contains("api.resolved.com", mmd);
        Assert.DoesNotContain("unresolved", mmd);
    }

    [Fact]
    public void Summary_ContainsClassDefs_ForAllTypes()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Summary);
        Assert.Contains("classDef salesforce", mmd);
        Assert.Contains("classDef http", mmd);
        Assert.Contains("classDef funcapp", mmd);
        Assert.Contains("classDef serviceprovider", mmd);
        Assert.Contains("classDef childwf", mmd);
        Assert.Contains("classDef logicapp", mmd);
    }

    [Fact]
    public void Summary_LogicAppNodes_HaveLogicappClass()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Summary);
        Assert.Contains(":::logicapp", mmd);
    }

    [Fact]
    public void Summary_EmptyInventory_ReturnsPlaceholder()
    {
        var mmd = _builder.Build(Inventory.Empty, DiagramMode.Summary);
        Assert.StartsWith("flowchart LR", mmd);
        Assert.Contains("No logic apps found", mmd);
    }

    [Fact]
    public void Summary_DeduplicatesEdges_SameAppToSameTarget()
    {
        // lapp-orders has TWO workflows both hitting Salesforce
        // In summary mode there should be only ONE edge from app_lapp_orders to Salesforce
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Summary);
        var appNode = mmd.Split('\n')
            .First(l => l.Contains("app_lapp_orders["));
        var appId = appNode.Trim().Split('[')[0];

        var edgesToSalesforce = mmd
            .Split('\n')
            .Count(l => l.Contains($"{appId} -->") && l.Contains("Salesforce"));
        Assert.Equal(1, edgesToSalesforce);
    }

    // ── Detail mode ───────────────────────────────────────────────────────────

    [Fact]
    public void Detail_ContainsWorkflowNodes()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Detail);
        Assert.Contains("wf-create-order", mmd);
        Assert.Contains("wf-cancel-order", mmd);
        Assert.Contains("wf-sync-patient", mmd);
    }

    [Fact]
    public void Detail_WorkflowNodesHaveWorkflowClass()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Detail);
        Assert.Contains(":::workflow", mmd);
    }

    [Fact]
    public void Detail_EdgesDoNotIncludeActionNames()
    {
        // Action names are internal detail — diagrams show only the workflow→target relationship
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Detail);
        Assert.DoesNotContain("|\"Sync_Salesforce\"", mmd);
        Assert.DoesNotContain("|\"Call_Orders_API\"", mmd);
        Assert.DoesNotContain("|\"Call_Child\"", mmd);
    }

    [Fact]
    public void Detail_SalesforceNodeIsSharedAcrossWorkflows()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Detail);
        var salesforceNodeDeclarations = mmd
            .Split('\n')
            .Count(line => line.Contains(":::salesforce") && line.TrimStart().StartsWith("t_"));
        Assert.Equal(1, salesforceNodeDeclarations);
    }

    [Fact]
    public void Detail_ShowsAppNameInWorkflowLabel_WhenMultipleApps()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Detail);
        // Multi-app detail should show parent app name via <br/>
        Assert.Contains("<br/>", mmd);
    }

    [Fact]
    public void Detail_SingleApp_UsesGenericWorkflowSubtitle_NotAppName()
    {
        var single = new Inventory
        {
            ScannedAt = DateTimeOffset.UtcNow,
            LogicApps = [TwoAppInventory().LogicApps[0]],
        };
        var mmd = _builder.Build(single, DiagramMode.Detail);
        // Single-app: workflow nodes should NOT show the app name as subtitle
        // (they show "Workflow" instead)
        var workflowLines = mmd.Split('\n')
            .Where(l => l.TrimStart().StartsWith("wf_") && l.Contains(":::workflow"));
        foreach (var line in workflowLines)
            Assert.DoesNotContain("lapp-", line);
    }

    [Fact]
    public void Detail_EmptyAfterFilter_ReturnsPlaceholder()
    {
        var empty = new Inventory { ScannedAt = DateTimeOffset.UtcNow, LogicApps = [] };
        var mmd = _builder.Build(empty, DiagramMode.Detail);
        Assert.Contains("No logic apps found", mmd);
    }

    // ── Service Bus chain ─────────────────────────────────────────────────────

    private static Inventory SbChainInventory() => new()
    {
        ScannedAt = DateTimeOffset.UtcNow,
        LogicApps =
        [
            new LogicAppInfo
            {
                Name = "lapp-sender",
                Workflows =
                [
                    new WorkflowInfo
                    {
                        Name = "wf-publish",
                        LogicAppName = "lapp-sender",
                        IsStateful = true,
                        Edges =
                        [
                            new CallEdge("Send_Message", CallType.ServiceBus,
                                new ExternalTarget(CallType.ServiceBus, "orders-queue")),
                        ],
                    },
                ],
            },
            new LogicAppInfo
            {
                Name = "lapp-receiver",
                Workflows =
                [
                    new WorkflowInfo
                    {
                        Name = "wf-process",
                        LogicAppName = "lapp-receiver",
                        IsStateful = true,
                        Edges = [],
                        Trigger = new TriggerInfo("ServiceBus", "orders-queue"),
                    },
                ],
            },
        ],
    };

    [Fact]
    public void Detail_ServiceBusChain_SenderAndReceiverLinkedThroughQueue()
    {
        var mmd = _builder.Build(SbChainInventory(), DiagramMode.Detail);

        // Both workflow nodes must be present
        Assert.Contains("wf-publish", mmd);
        Assert.Contains("wf-process", mmd);

        // The shared SB queue node must be present
        Assert.Contains("orders-queue", mmd);
        Assert.Contains(":::servicebus", mmd);

        // Sender workflow → SB node
        var lines = mmd.Split('\n');
        Assert.True(lines.Any(l => l.Contains("wf_") && l.Contains("-->") && l.Contains("t_ServiceBus")),
            "Expected a sender → SB edge");

        // SB node → receiver workflow
        Assert.True(lines.Any(l => l.Contains("t_ServiceBus") && l.Contains("-->") && l.Contains("wf_")),
            "Expected a SB → receiver edge");
    }

    [Fact]
    public void Summary_ServiceBusChain_BothAppsLinkedThroughQueue()
    {
        var mmd = _builder.Build(SbChainInventory(), DiagramMode.Summary);

        Assert.Contains("lapp-sender", mmd);
        Assert.Contains("lapp-receiver", mmd);
        Assert.Contains("orders-queue", mmd);
        Assert.Contains(":::servicebus", mmd);

        var lines = mmd.Split('\n');
        // Sender app → SB node
        Assert.True(lines.Any(l => l.Contains("app_lapp_sender") && l.Contains("-->") && l.Contains("t_ServiceBus")),
            "Expected sender app → SB edge");
        // SB node → receiver app
        Assert.True(lines.Any(l => l.Contains("t_ServiceBus") && l.Contains("-->") && l.Contains("app_lapp_receiver")),
            "Expected SB → receiver app edge");
    }

    [Fact]
    public void Detail_SbTriggeredWorkflow_WithNoSenderInFilter_StillShowsSbNode()
    {
        // Only the receiver app is in the inventory (sender filtered out)
        var receiverOnly = new Inventory
        {
            ScannedAt = DateTimeOffset.UtcNow,
            LogicApps =
            [
                new LogicAppInfo
                {
                    Name = "lapp-receiver",
                    Workflows =
                    [
                        new WorkflowInfo
                        {
                            Name = "wf-process",
                            LogicAppName = "lapp-receiver",
                            IsStateful = true,
                            Edges = [],
                            Trigger = new TriggerInfo("ServiceBus", "orders-queue"),
                        },
                    ],
                },
            ],
        };
        var mmd = _builder.Build(receiverOnly, DiagramMode.Detail);

        // SB node declared even though no outbound edge points to it
        Assert.Contains("orders-queue", mmd);
        Assert.Contains(":::servicebus", mmd);
        // Reverse edge exists
        Assert.Contains("-->", mmd);
    }

    [Fact]
    public void Summary_ContainsClassDefs_IncludingServiceBus()
    {
        var mmd = _builder.Build(SbChainInventory(), DiagramMode.Summary);
        Assert.Contains("classDef servicebus", mmd);
    }

    // ── Node ID safety ────────────────────────────────────────────────────────

    [Fact]
    public void NodeIds_ContainNoSpacesOrDashes()
    {
        var mmd = _builder.Build(TwoAppInventory(), DiagramMode.Summary);
        var lines = mmd.Split('\n')
            .Where(l => (l.TrimStart().StartsWith("app_") || l.TrimStart().StartsWith("t_"))
                        && l.Contains('['))   // node declarations only, not edges
            .Select(l => l.Trim().Split('[')[0].Trim());

        foreach (var id in lines)
        {
            Assert.DoesNotContain(" ", id);
            Assert.DoesNotContain("-", id);
            Assert.DoesNotContain(".", id);
        }
    }
}
