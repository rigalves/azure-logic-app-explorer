using wtrfll.AzureLogicAppExplorer.Model;
using wtrfll.AzureLogicAppExplorer.Presentation;

namespace wtrfll.AzureLogicAppExplorer.Tests;

public class WorkflowInteractionsTests
{
    private static WorkflowInfo BuildWorkflow(params CallEdge[] edges) => new()
    {
        Name = "wf-test",
        LogicAppName = "lapp-test",
        IsStateful = true,
        Edges = [.. edges],
    };

    [Fact]
    public void NoEdges_ReturnsEmpty()
    {
        var wf = BuildWorkflow();

        Assert.Empty(WorkflowInteractions.Build(wf));
    }

    [Fact]
    public void DistinctEdges_AreKeptInOrderWithCountOne()
    {
        var wf = BuildWorkflow(
            new CallEdge("Call_API", CallType.Http, new ExternalTarget(CallType.Http, "api.contoso.com"), Method: "GET"),
            new CallEdge("Sync_Salesforce", CallType.Salesforce, new ExternalTarget(CallType.Salesforce, "Sync_Salesforce")));

        var interactions = WorkflowInteractions.Build(wf);

        Assert.Equal(2, interactions.Count);
        Assert.Equal(CallType.Http, interactions[0].CallType);
        Assert.Equal("GET", interactions[0].Detail);
        Assert.Equal("api.contoso.com", interactions[0].Target);
        Assert.Equal(1, interactions[0].Count);
        Assert.Equal(CallType.Salesforce, interactions[1].CallType);
        Assert.Equal(1, interactions[1].Count);
    }

    [Fact]
    public void IdenticalEdges_AreCollapsedWithCount()
    {
        var edge = new CallEdge("Send_SB_Message", CallType.ServiceBus,
            new ExternalTarget(CallType.ServiceBus, "orders-queue"), Operation: "Send");
        var wf = BuildWorkflow(edge, edge, edge);

        var interactions = WorkflowInteractions.Build(wf);

        var interaction = Assert.Single(interactions);
        Assert.Equal(CallType.ServiceBus, interaction.CallType);
        Assert.Equal("Send", interaction.Detail);
        Assert.Equal("orders-queue", interaction.Target);
        Assert.Equal(3, interaction.Count);
    }

    [Fact]
    public void EdgesDifferingOnlyByActionName_AreStillCollapsed()
    {
        var wf = BuildWorkflow(
            new CallEdge("Action_1", CallType.Http, new ExternalTarget(CallType.Http, "api.contoso.com"), Method: "GET"),
            new CallEdge("Action_2", CallType.Http, new ExternalTarget(CallType.Http, "api.contoso.com"), Method: "GET"));

        var interaction = Assert.Single(WorkflowInteractions.Build(wf));
        Assert.Equal(2, interaction.Count);
    }

    [Fact]
    public void TargetWithPath_ConcatenatesNameAndPath()
    {
        var wf = BuildWorkflow(
            new CallEdge("Call_API", CallType.Http,
                new ExternalTarget(CallType.Http, "api.contoso.com", Path: "/orders")));

        var interaction = Assert.Single(WorkflowInteractions.Build(wf));
        Assert.Equal("api.contoso.com/orders", interaction.Target);
    }

    [Fact]
    public void RawExpressionTarget_IsPreservedSeparately()
    {
        var wf = BuildWorkflow(
            new CallEdge("Call_Dynamic", CallType.Http,
                new ExternalTarget(CallType.Http, "dynamic-host", RawExpression: "@variables('host')")));

        var interaction = Assert.Single(WorkflowInteractions.Build(wf));
        Assert.Equal("@variables('host')", interaction.RawExpression);
        Assert.Equal("dynamic-host", interaction.Target);
    }
}
