using FlowAtlas.Model;
using System.Text.Json;

namespace FlowAtlas.Parsing;

/// <summary>
/// Parses connections.json and builds a lookup from connection reference name
/// → resolved CallType + display name, used by WorkflowParser to classify ApiConnection actions.
/// </summary>
public sealed class ConnectionsParser
{
    public ConnectionsLookup Parse(JsonDocument? connectionsJson)
    {
        var lookup = new Dictionary<string, ConnectionInfo>(StringComparer.OrdinalIgnoreCase);

        if (connectionsJson is null)
            return new ConnectionsLookup(lookup);

        var root = connectionsJson.RootElement;

        // ── managedApiConnections ──────────────────────────────────────────────
        // { "refName": { "api": { "id": ".../managedApis/salesforce" }, "connection": {...} } }
        if (root.TryGetProperty("managedApiConnections", out var managed))
        {
            foreach (var entry in managed.EnumerateObject())
            {
                var refName = entry.Name;
                var apiId = entry.Value.TryGetProperty("api", out var api) &&
                            api.TryGetProperty("id", out var id)
                    ? id.GetString() ?? ""
                    : "";

                var connectorSlug = apiId.Split('/').LastOrDefault() ?? "";
                var (callType, displayName) = ClassifyManagedApi(connectorSlug);
                lookup[refName] = new ConnectionInfo(refName, callType, displayName);
            }
        }

        // ── serviceProviderConnections ────────────────────────────────────────
        // { "refName": { "serviceProvider": { "id": "/serviceProviders/serviceBus" } } }
        if (root.TryGetProperty("serviceProviderConnections", out var svcProviders))
        {
            foreach (var entry in svcProviders.EnumerateObject())
            {
                var refName = entry.Name;
                var providerId = entry.Value.TryGetProperty("serviceProvider", out var sp) &&
                                 sp.TryGetProperty("id", out var id)
                    ? id.GetString() ?? ""
                    : "";

                var displayName = MapServiceProviderId(providerId);
                lookup[refName] = new ConnectionInfo(refName, CallType.ServiceProvider, displayName);
            }
        }

        // ── functionConnections ───────────────────────────────────────────────
        // { "refName": { "function": { "id": ".../sites/{funcApp}/functions/{name}" } } }
        if (root.TryGetProperty("functionConnections", out var funcs))
        {
            foreach (var entry in funcs.EnumerateObject())
            {
                var refName = entry.Name;
                var funcId = entry.Value.TryGetProperty("function", out var func) &&
                             func.TryGetProperty("id", out var id)
                    ? id.GetString() ?? ""
                    : "";

                var funcAppName = ExtractSegmentAfter(funcId, "sites") ?? refName;
                lookup[refName] = new ConnectionInfo(refName, CallType.Function, funcAppName);
            }
        }

        return new ConnectionsLookup(lookup);
    }

    private static (CallType, string) ClassifyManagedApi(string slug) => slug.ToLowerInvariant() switch
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
        var s => s?.Split('/').LastOrDefault() ?? "ServiceProvider",
    };

    private static string? ExtractSegmentAfter(string resourceId, string segment)
    {
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals(segment, StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        return null;
    }
}

public sealed record ConnectionInfo(string RefName, CallType CallType, string DisplayName);

public sealed class ConnectionsLookup(Dictionary<string, ConnectionInfo> inner)
{
    public static ConnectionsLookup Empty => new(new());

    public bool TryGet(string refName, out ConnectionInfo info)
        => inner.TryGetValue(refName, out info!);
}
