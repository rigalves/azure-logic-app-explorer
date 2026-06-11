using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace wtrfll.AzureLogicAppExplorer.Azure;

/// <summary>
/// Reads Standard Logic App artifacts from Azure via ARM-token-authenticated calls.
/// No Kudu / SCM credentials needed — only RBAC Contributor+ on the resource group.
/// </summary>
public sealed class AzureLogicAppClient
{
    private readonly HttpClient _http;
    private readonly AppOptions _opts;
    private readonly TokenCredential _credential;
    private readonly ILogger<AzureLogicAppClient> _logger;

    private const string ArmScope = "https://management.azure.com/.default";
    private const string ArmBase = "https://management.azure.com";
    private const string ServiceBusApiVersion = "2021-11-01";

    public AzureLogicAppClient(
        HttpClient http,
        IOptions<AppOptions> opts,
        ILogger<AzureLogicAppClient> logger)
    {
        _http = http;
        _opts = opts.Value;
        _logger = logger;
        _credential = new DefaultAzureCredential();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<List<(string Name, string Kind, string State)>> ListStandardLogicAppsAsync(string resourceGroup, CancellationToken ct = default)
    {
        var url = SiteUrl(resourceGroup) + $"?api-version={_opts.HostRuntimeApiVersion}";
        var doc = await GetJsonAsync(url, "list sites", ct);

        var results = new List<(string, string, string)>();
        foreach (var site in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            var kind = site.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
            if (kind.Contains("workflowapp", StringComparison.OrdinalIgnoreCase))
            {
                var state = site.TryGetProperty("properties", out var props)
                            && props.TryGetProperty("state", out var s)
                            ? s.GetString() ?? "Unknown" : "Unknown";
                results.Add((site.GetProperty("name").GetString()!, kind, state));
            }
        }

        _logger.LogInformation("Found {Count} Standard Logic App(s) in '{RG}'.",
            results.Count, resourceGroup);
        return results;
    }

    public async Task<List<string>> ListWorkflowsAsync(string resourceGroup, string appName, CancellationToken ct = default)
    {
        // Uses the ARM Microsoft.Web/sites/workflows resource (same as the Portal Designer) —
        // a standard ARM read covered by Reader, unlike the hostruntime/admin/vfs Kudu proxy
        // which requires Contributor-level access.
        var url = WorkflowsArmUrl(resourceGroup, appName, null);
        var doc = await GetJsonAsync(url, $"{appName}: list workflows", ct);

        var names = doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(w => w.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
            .Select(n => n.Contains('/') ? n[(n.LastIndexOf('/') + 1)..] : n)
            .Where(n => n.Length > 0)
            .ToList();

        _logger.LogDebug("{App}: {Count} workflow(s).", appName, names.Count);
        return names;
    }

    public async Task<JsonDocument?> GetWorkflowJsonAsync(string resourceGroup, string appName, string workflowName, CancellationToken ct = default)
    {
        // Same ARM resource as above — returns properties.files["workflow.json"], whose
        // shape ({ "definition": ..., "kind": ... }) matches the workflow.json file directly.
        var url = WorkflowsArmUrl(resourceGroup, appName, workflowName);
        var doc = await GetJsonOrNullAsync(url, $"{appName}/{workflowName}: workflow.json", ct);
        if (doc is null) return null;

        if (doc.RootElement.TryGetProperty("properties", out var props)
            && props.TryGetProperty("files", out var files)
            && files.TryGetProperty("workflow.json", out var wfJson))
        {
            return JsonDocument.Parse(wfJson.GetRawText());
        }

        return null;
    }

    public async Task<JsonDocument?> GetConnectionsJsonAsync(string resourceGroup, string appName, CancellationToken ct = default)
    {
        var url = VfsFileUrl(resourceGroup, appName, "site/wwwroot/connections.json");
        return await GetJsonOrNullAsync(url, $"{appName}: connections.json", ct);
    }

    public async Task<JsonDocument?> GetParametersJsonAsync(string resourceGroup, string appName, CancellationToken ct = default)
    {
        var url = VfsFileUrl(resourceGroup, appName, "site/wwwroot/parameters.json");
        return await GetJsonOrNullAsync(url, $"{appName}: parameters.json", ct);
    }

    public async Task<List<string>> ListServiceBusNamespacesAsync(string resourceGroup, CancellationToken ct = default)
    {
        var url = $"{ArmBase}/subscriptions/{_opts.SubscriptionId}/resourceGroups/{resourceGroup}" +
                  $"/providers/Microsoft.ServiceBus/namespaces?api-version={ServiceBusApiVersion}";
        var doc = await GetJsonAsync(url, "list Service Bus namespaces", ct);

        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(ns => ns.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
            .Where(n => n.Length > 0)
            .ToList();
    }

    public async Task<List<string>> ListServiceBusTopicsAsync(string resourceGroup, string namespaceName, CancellationToken ct = default)
    {
        var url = $"{ArmBase}/subscriptions/{_opts.SubscriptionId}/resourceGroups/{resourceGroup}" +
                  $"/providers/Microsoft.ServiceBus/namespaces/{namespaceName}/topics?api-version={ServiceBusApiVersion}";
        var doc = await GetJsonAsync(url, $"{namespaceName}: list Service Bus topics", ct);

        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(t => t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
            .Where(n => n.Length > 0)
            .ToList();
    }

    public async Task<List<string>> ListServiceBusSubscriptionsAsync(string resourceGroup, string namespaceName, string topicName, CancellationToken ct = default)
    {
        var url = $"{ArmBase}/subscriptions/{_opts.SubscriptionId}/resourceGroups/{resourceGroup}" +
                  $"/providers/Microsoft.ServiceBus/namespaces/{namespaceName}/topics/{Uri.EscapeDataString(topicName)}/subscriptions?api-version={ServiceBusApiVersion}";
        var doc = await GetJsonAsync(url, $"{namespaceName}/{topicName}: list Service Bus subscriptions", ct);

        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(s => s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
            .Where(n => n.Length > 0)
            .ToList();
    }

    public async Task<List<(string Name, string ApiId)>> ListManagedConnectionsAsync(string resourceGroup, CancellationToken ct = default)
    {
        var url = $"{ArmBase}/subscriptions/{_opts.SubscriptionId}/resourceGroups/{resourceGroup}" +
                  $"/providers/Microsoft.Web/connections?api-version=2016-06-01";
        var doc = await GetJsonAsync(url, "list managed connections", ct);

        var results = new List<(string, string)>();
        foreach (var conn in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            var name = conn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var apiId = conn.TryGetProperty("properties", out var props)
                        && props.TryGetProperty("api", out var api)
                        && api.TryGetProperty("id", out var id)
                        ? id.GetString() ?? "" : "";
            if (name.Length > 0) results.Add((name, apiId));
        }
        return results;
    }

    // ── URL builders ──────────────────────────────────────────────────────────

    private string SiteUrl(string resourceGroup) =>
        $"{ArmBase}/subscriptions/{_opts.SubscriptionId}/resourceGroups/{resourceGroup}" +
        $"/providers/Microsoft.Web/sites";

    private string VfsFileUrl(string resourceGroup, string appName, string relativePath) =>
        $"{ArmBase}/subscriptions/{_opts.SubscriptionId}/resourceGroups/{resourceGroup}" +
        $"/providers/Microsoft.Web/sites/{appName}/hostruntime/admin/vfs/{relativePath}" +
        $"?api-version={_opts.HostRuntimeApiVersion}";

    private string WorkflowsArmUrl(string resourceGroup, string appName, string? workflowName)
    {
        var wf = workflowName is not null ? $"/{Uri.EscapeDataString(workflowName)}" : "";
        return $"{ArmBase}/subscriptions/{_opts.SubscriptionId}/resourceGroups/{resourceGroup}" +
               $"/providers/Microsoft.Web/sites/{appName}/workflows{wf}" +
               $"?api-version={_opts.HostRuntimeApiVersion}";
    }

    // ── HTTP helpers ──────────────────────────────────────────────────────────

    private async Task<JsonDocument> GetJsonAsync(string url, string label, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("GET {Label}", label);
        using var req = BuildGet(url, await GetTokenAsync(ct));
        var resp = await _http.SendAsync(req, ct);
        await ThrowIfNotSuccessAsync(resp, label, url, ct);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        _logger.LogDebug("GET {Label} → {Status} ({Ms}ms)", label, (int)resp.StatusCode, sw.ElapsedMilliseconds);
        return doc;
    }

    private async Task<JsonDocument?> GetJsonOrNullAsync(string url, string label, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogDebug("GET {Label}", label);
        using var req = BuildGet(url, await GetTokenAsync(ct));
        var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogDebug("GET {Label} → 404 ({Ms}ms)", label, sw.ElapsedMilliseconds);
            return null;
        }
        await ThrowIfNotSuccessAsync(resp, label, url, ct);
        var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        _logger.LogDebug("GET {Label} → {Status} ({Ms}ms)", label, (int)resp.StatusCode, sw.ElapsedMilliseconds);
        return doc;
    }

    private async Task ThrowIfNotSuccessAsync(
        HttpResponseMessage resp, string label, string url, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("GET {Label} → {Status}\nURL: {Url}\nBody: {Body}",
            label, (int)resp.StatusCode, url, body);
        throw new HttpRequestException(
            $"HTTP {(int)resp.StatusCode} {resp.StatusCode} — {label}\nBody: {body}");
    }

    private static HttpRequestMessage BuildGet(string url, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var result = await _credential.GetTokenAsync(new TokenRequestContext([ArmScope]), ct);
        return result.Token;
    }
}
