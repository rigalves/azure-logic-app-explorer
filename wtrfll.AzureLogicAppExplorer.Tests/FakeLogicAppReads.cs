using System.Text.Json;
using wtrfll.AzureLogicAppExplorer.Azure;

namespace wtrfll.AzureLogicAppExplorer.Tests;

/// <summary>
/// In-memory <see cref="ILogicAppReads"/> adapter for ScanService orchestration tests — the
/// second adapter at the read seam, alongside <see cref="AzureLogicAppClient"/>. Lets tests
/// exercise parallel scanning, error aggregation, and stopped-app skipping without an Azure login.
/// </summary>
internal sealed class FakeLogicAppReads : ILogicAppReads
{
    public Dictionary<string, List<(string Name, string Kind, string State)>> AppsByResourceGroup { get; } = new();
    public Dictionary<string, List<string>> WorkflowNamesByApp { get; } = new();
    public Dictionary<(string App, string Workflow), string> WorkflowJsonByKey { get; } = new();
    public HashSet<string> AppsWhereListWorkflowsThrows { get; } = [];
    public HashSet<(string App, string Workflow)> WorkflowsWhereGetJsonThrows { get; } = [];
    public Dictionary<string, List<string>> ServiceBusNamespacesByResourceGroup { get; } = new();
    public Dictionary<string, List<string>> ServiceBusTopicsByNamespace { get; } = new();
    public Dictionary<(string Namespace, string Topic), List<string>> ServiceBusSubscriptions { get; } = new();

    public List<string> ListWorkflowsCalledForApps { get; } = [];

    public Task<List<(string Name, string Kind, string State)>> ListStandardLogicAppsAsync(string resourceGroup, CancellationToken ct = default) =>
        Task.FromResult(AppsByResourceGroup.GetValueOrDefault(resourceGroup, []));

    public Task<List<string>> ListWorkflowsAsync(string resourceGroup, string appName, CancellationToken ct = default)
    {
        lock (ListWorkflowsCalledForApps) ListWorkflowsCalledForApps.Add(appName);
        if (AppsWhereListWorkflowsThrows.Contains(appName))
            throw new InvalidOperationException($"fake: cannot list workflows for '{appName}'");
        return Task.FromResult(WorkflowNamesByApp.GetValueOrDefault(appName, []));
    }

    public Task<JsonDocument?> GetWorkflowJsonAsync(string resourceGroup, string appName, string workflowName, CancellationToken ct = default)
    {
        if (WorkflowsWhereGetJsonThrows.Contains((appName, workflowName)))
            throw new InvalidOperationException($"fake: cannot read workflow.json for '{appName}/{workflowName}'");

        return Task.FromResult(WorkflowJsonByKey.TryGetValue((appName, workflowName), out var json)
            ? JsonDocument.Parse(json)
            : null);
    }

    public Task<JsonDocument?> GetConnectionsJsonAsync(string resourceGroup, string appName, CancellationToken ct = default) =>
        Task.FromResult<JsonDocument?>(null);

    public Task<JsonDocument?> GetParametersJsonAsync(string resourceGroup, string appName, CancellationToken ct = default) =>
        Task.FromResult<JsonDocument?>(null);

    public Task<List<string>> ListServiceBusNamespacesAsync(string resourceGroup, CancellationToken ct = default) =>
        Task.FromResult(ServiceBusNamespacesByResourceGroup.GetValueOrDefault(resourceGroup, []));

    public Task<List<string>> ListServiceBusTopicsAsync(string resourceGroup, string namespaceName, CancellationToken ct = default) =>
        Task.FromResult(ServiceBusTopicsByNamespace.GetValueOrDefault(namespaceName, []));

    public Task<List<string>> ListServiceBusSubscriptionsAsync(string resourceGroup, string namespaceName, string topicName, CancellationToken ct = default) =>
        Task.FromResult(ServiceBusSubscriptions.GetValueOrDefault((namespaceName, topicName), []));
}
