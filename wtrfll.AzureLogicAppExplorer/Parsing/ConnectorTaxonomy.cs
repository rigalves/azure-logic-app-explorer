using wtrfll.AzureLogicAppExplorer.Model;

namespace wtrfll.AzureLogicAppExplorer.Parsing;

/// <summary>
/// Single source of truth for "what kind of connector is this?" — maps managed-API slugs
/// (from connections.json's managedApiConnections) and serviceProviderId values (from
/// serviceProviderConnections and inline ServiceProvider actions/triggers) to a CallType,
/// display name, and friendly operation labels. ConnectionsParser and WorkflowParser both
/// read from here instead of cross-calling each other.
/// </summary>
public static class ConnectorTaxonomy
{
    public static (CallType CallType, string DisplayName) ClassifyManagedApi(string slug) => slug.ToLowerInvariant() switch
    {
        "salesforce"     => (CallType.Salesforce, "Salesforce"),
        "office365"      => (CallType.ManagedConnector, "Office 365"),
        "office365users" => (CallType.ManagedConnector, "Office 365 Users"),
        "sharepointonline" or "sharepoint" => (CallType.ManagedConnector, "SharePoint"),
        "sql"            => (CallType.ManagedConnector, "SQL Server (managed)"),
        "servicebus"     => (CallType.ManagedConnector, "Service Bus (managed)"),
        "azureblob"      => (CallType.ManagedConnector, "Azure Blob (managed)"),
        "teams"          => (CallType.ManagedConnector, "Microsoft Teams"),
        "dynamicscrmonline" or "commondataservice" => (CallType.ManagedConnector, "Dynamics 365"),
        "sap"            => (CallType.ManagedConnector, "SAP"),
        "servicenow"     => (CallType.ManagedConnector, "ServiceNow"),
        var s            => (CallType.ManagedConnector, string.IsNullOrEmpty(s) ? "Unknown connector" : s),
    };

    public static string MapServiceProviderId(string? providerId) => providerId switch
    {
        "/serviceProviders/serviceBus"    => "Service Bus",
        "/serviceProviders/azureBlob"     => "Azure Blob",
        "/serviceProviders/sql"           => "SQL Server",
        "/serviceProviders/azureQueues"   => "Azure Queues",
        "/serviceProviders/azureTables"   => "Azure Tables",
        "/serviceProviders/eventHubs"     => "Event Hubs",
        "/serviceProviders/cosmosDb"      => "Cosmos DB",
        "/serviceProviders/ftp"           => "FTP",
        "/serviceProviders/sftp"          => "SFTP",
        "/serviceProviders/keyVault"      => "Key Vault",
        var s => s?.Split('/').LastOrDefault() ?? "ServiceProvider",
    };

    public static bool IsKeyVaultProviderId(string? providerId) =>
        providerId is not null && providerId.Contains("keyVault", StringComparison.OrdinalIgnoreCase);

    public static bool IsServiceBusProviderId(string? providerId) =>
        providerId is not null && providerId.Contains("serviceBus", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a Service Bus serviceProviderConfiguration.operationId to a friendly
    /// operation label, e.g. "sendMessage" → "Send", "peekLockTopicMessagesV2" → "Receive (Peek-Lock)".
    /// Returns the raw operationId when it doesn't match a known pattern, or null when absent.
    /// </summary>
    public static string? MapServiceBusOperation(string? operationId)
    {
        if (operationId is null) return null;
        var id = operationId.ToLowerInvariant();

        if (id.Contains("send")) return "Send";
        if (id.Contains("peeklock")) return "Receive (Peek-Lock)";
        if (id.Contains("receive")) return "Receive";
        if (id.Contains("complete")) return "Complete";
        if (id.Contains("abandon")) return "Abandon";
        if (id.Contains("deadletter")) return "Dead-letter";
        if (id.Contains("renewlock")) return "Renew Lock";

        return operationId;
    }

    /// <summary>
    /// Maps a Key Vault serviceProviderConfiguration.operationId to a friendly
    /// operation label, e.g. "getSecret" → "Get Secret". Returns the raw operationId
    /// when it doesn't match a known pattern, or null when absent.
    /// </summary>
    public static string? MapKeyVaultOperation(string? operationId)
    {
        if (operationId is null) return null;
        var id = operationId.ToLowerInvariant();

        if (id.Contains("getsecret")) return "Get Secret";
        if (id.Contains("setsecret")) return "Set Secret";

        return operationId;
    }
}
