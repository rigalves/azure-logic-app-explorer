using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using wtrfll.AzureLogicAppExplorer.Azure;
using wtrfll.AzureLogicAppExplorer.Model;
using wtrfll.AzureLogicAppExplorer.Services;

namespace wtrfll.AzureLogicAppExplorer.Tests;

/// <summary>
/// Exercises ScanService's orchestration — parallelism, sorting, error aggregation, and
/// stopped-app skipping — against the in-memory <see cref="FakeLogicAppReads"/> adapter.
/// No Azure login required.
/// </summary>
public class ScanServiceTests
{
    private const string Rg = "rg-test";

    private const string SimpleHttpWorkflowJson = """
        {"definition":{"actions":{"Call_API":{"type":"Http","inputs":{"uri":"https://api.example.com/orders","method":"GET"}}}}}
        """;

    private static ScanService BuildService(FakeLogicAppReads reads)
    {
        var options = Options.Create(new AppOptions
        {
            SubscriptionId = "sub-test",
            ResourceGroups = [Rg],
            SnapshotPath = Path.Combine(Path.GetTempPath(), $"flow-atlas-test-{Guid.NewGuid()}.json"),
        });
        return new ScanService(reads, options, NullLogger<ScanService>.Instance);
    }

    [Fact]
    public async Task Scan_SortsAppsByNameAndParsesWorkflows()
    {
        var reads = new FakeLogicAppReads();
        reads.AppsByResourceGroup[Rg] =
        [
            ("b-app", "workflowapp", "Running"),
            ("a-app", "workflowapp", "Running"),
        ];
        reads.WorkflowNamesByApp["a-app"] = ["wf-a"];
        reads.WorkflowNamesByApp["b-app"] = ["wf-b"];
        reads.WorkflowJsonByKey[("a-app", "wf-a")] = SimpleHttpWorkflowJson;
        reads.WorkflowJsonByKey[("b-app", "wf-b")] = SimpleHttpWorkflowJson;

        var scan = BuildService(reads);
        await scan.ScanAsync();

        Assert.Equal(["a-app", "b-app"], scan.CurrentInventory.LogicApps.Select(a => a.Name));
        var appA = scan.CurrentInventory.LogicApps.Single(a => a.Name == "a-app");
        Assert.Single(appA.Workflows);
        Assert.Equal("wf-a", appA.Workflows[0].Name);
        Assert.Single(appA.Workflows[0].Edges);
        Assert.Equal(CallType.Http, appA.Workflows[0].Edges[0].CallType);
    }

    [Fact]
    public async Task Scan_StoppedApp_SkipsWorkflowListing()
    {
        var reads = new FakeLogicAppReads();
        reads.AppsByResourceGroup[Rg] = [("stopped-app", "workflowapp", "Stopped")];

        var scan = BuildService(reads);
        await scan.ScanAsync();

        var app = scan.CurrentInventory.LogicApps.Single();
        Assert.Equal("stopped-app", app.Name);
        Assert.False(app.IsRunning);
        Assert.Empty(app.Workflows);
        Assert.DoesNotContain("stopped-app", reads.ListWorkflowsCalledForApps);
    }

    [Fact]
    public async Task Scan_ListWorkflowsThrows_RecordsAppError()
    {
        var reads = new FakeLogicAppReads();
        reads.AppsByResourceGroup[Rg] = [("broken-app", "workflowapp", "Running")];
        reads.AppsWhereListWorkflowsThrows.Add("broken-app");

        var scan = BuildService(reads);
        await scan.ScanAsync();

        var app = scan.CurrentInventory.LogicApps.Single();
        Assert.Empty(app.Workflows);
        Assert.Single(app.ScanErrors);
        Assert.Contains("broken-app", app.ScanErrors[0]);
    }

    [Fact]
    public async Task Scan_GetWorkflowJsonThrows_RecordsWorkflowErrorAndContinuesWithOthers()
    {
        var reads = new FakeLogicAppReads();
        reads.AppsByResourceGroup[Rg] = [("app", "workflowapp", "Running")];
        reads.WorkflowNamesByApp["app"] = ["wf-good", "wf-bad"];
        reads.WorkflowJsonByKey[("app", "wf-good")] = SimpleHttpWorkflowJson;
        reads.WorkflowsWhereGetJsonThrows.Add(("app", "wf-bad"));

        var scan = BuildService(reads);
        await scan.ScanAsync();

        var app = scan.CurrentInventory.LogicApps.Single();
        Assert.Single(app.Workflows);
        Assert.Equal("wf-good", app.Workflows[0].Name);
        Assert.Single(app.ScanErrors);
        Assert.Contains("wf-bad", app.ScanErrors[0]);
    }

    [Fact]
    public async Task Scan_GetWorkflowJsonReturnsNull_SkipsWorkflowSilently()
    {
        var reads = new FakeLogicAppReads();
        reads.AppsByResourceGroup[Rg] = [("app", "workflowapp", "Running")];
        reads.WorkflowNamesByApp["app"] = ["wf-missing"];
        // No entry in WorkflowJsonByKey → GetWorkflowJsonAsync returns null (404).

        var scan = BuildService(reads);
        await scan.ScanAsync();

        var app = scan.CurrentInventory.LogicApps.Single();
        Assert.Empty(app.Workflows);
        Assert.Empty(app.ScanErrors);
    }

    [Fact]
    public async Task Scan_EnumeratesServiceBusTopicsSortedByNamespaceThenTopic()
    {
        var reads = new FakeLogicAppReads();
        reads.AppsByResourceGroup[Rg] = [];
        reads.ServiceBusNamespacesByResourceGroup[Rg] = ["ns-b", "ns-a"];
        reads.ServiceBusTopicsByNamespace["ns-a"] = ["topic-2", "topic-1"];
        reads.ServiceBusTopicsByNamespace["ns-b"] = ["topic-x"];
        reads.ServiceBusSubscriptions[("ns-a", "topic-1")] = ["sub-1"];
        reads.ServiceBusSubscriptions[("ns-a", "topic-2")] = ["sub-2"];
        reads.ServiceBusSubscriptions[("ns-b", "topic-x")] = [];

        var scan = BuildService(reads);
        await scan.ScanAsync();

        var topics = scan.CurrentInventory.ServiceBusTopics;
        Assert.Equal(
            [("ns-a", "topic-1"), ("ns-a", "topic-2"), ("ns-b", "topic-x")],
            topics.Select(t => (t.Namespace, t.TopicName)));
        Assert.Equal(["sub-1"], topics.Single(t => t.TopicName == "topic-1").Subscriptions);
    }
}
