using System.Text.Json;

namespace wtrfll.AzureLogicAppExplorer.Azure;

/// <summary>
/// The handful of reads <see cref="Services.ScanService"/> needs to build an Inventory: Standard
/// Logic Apps and their workflows, the connections/parameters files used to resolve call targets,
/// and Service Bus topics/subscriptions. <see cref="AzureLogicAppClient"/> is the live ARM adapter;
/// tests use a fixture-backed adapter so scan orchestration — parallelism, error aggregation,
/// stopped-app skipping — runs without an Azure login.
/// </summary>
public interface ILogicAppReads
{
    Task<List<(string Name, string Kind, string State)>> ListStandardLogicAppsAsync(string resourceGroup, CancellationToken ct = default);

    Task<List<string>> ListWorkflowsAsync(string resourceGroup, string appName, CancellationToken ct = default);

    Task<JsonDocument?> GetWorkflowJsonAsync(string resourceGroup, string appName, string workflowName, CancellationToken ct = default);

    Task<JsonDocument?> GetConnectionsJsonAsync(string resourceGroup, string appName, CancellationToken ct = default);

    Task<JsonDocument?> GetParametersJsonAsync(string resourceGroup, string appName, CancellationToken ct = default);

    Task<List<string>> ListServiceBusNamespacesAsync(string resourceGroup, CancellationToken ct = default);

    Task<List<string>> ListServiceBusTopicsAsync(string resourceGroup, string namespaceName, CancellationToken ct = default);

    Task<List<string>> ListServiceBusSubscriptionsAsync(string resourceGroup, string namespaceName, string topicName, CancellationToken ct = default);
}
