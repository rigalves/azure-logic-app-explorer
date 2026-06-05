using wtrfll.AzureLogicAppExplorer.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit.Abstractions;

namespace wtrfll.AzureLogicAppExplorer.IntegrationTests;

/// <summary>
/// Integration tests that verify the running identity has all required Azure access.
/// Run these first before building the full app.
///
/// Requirements: Azure CLI logged in (az login) or DefaultAzureCredential configured.
/// Config: appsettings.json in this project with SubscriptionId + ResourceGroup set.
///
/// Run:  dotnet test --filter Category=Integration -v normal
/// </summary>
public class AzureAccessTests
{
    private readonly AzureLogicAppClient _client;
    private readonly AppOptions _opts;
    private readonly ITestOutputHelper _out;

    public AzureAccessTests(ITestOutputHelper output)
    {
        _out = output;

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        _opts = config.GetSection(AppOptions.Section).Get<AppOptions>()
                ?? throw new InvalidOperationException("wtrfll.AzureLogicAppExplorer config section missing.");

        SkipIfNotConfigured();

        var http = new HttpClient();
        var options = Options.Create(_opts);
        _client = new AzureLogicAppClient(http, options);
    }

    /// <summary>
    /// Probe A: ARM can list all Standard Logic Apps (kind=workflowapp) in the resource group.
    /// Failure here = wrong SubscriptionId/ResourceGroup, or identity lacks Reader on the RG.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProbeA_CanListStandardLogicApps()
    {
        var apps = await _client.ListStandardLogicAppsAsync();

        _out.WriteLine($"Found {apps.Count} Standard Logic App(s):");
        foreach (var (name, kind) in apps)
            _out.WriteLine($"  {name}  (kind: {kind})");

        Assert.True(apps.Count > 0,
            $"No Standard Logic Apps found in resource group '{_opts.ResourceGroup}'. " +
            "Check the resource group name and that Standard Logic Apps exist there.");
    }

    /// <summary>
    /// Probe B: ARM can list workflows inside the first Standard Logic App.
    /// Failure here = wrong api-version, app is stopped, or identity lacks access to hostruntime.
    /// The full ARM error body is included in the failure message.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProbeB_CanListWorkflows()
    {
        var apps = await _client.ListStandardLogicAppsAsync();
        Skip.If(apps.Count == 0, "No Standard Logic Apps found — Probe A must pass first.");

        var (appName, _) = apps.First();
        _out.WriteLine($"Testing app: {appName}");

        var workflows = await _client.ListWorkflowsAsync(appName);

        _out.WriteLine($"Found {workflows.Count} workflow(s):");
        foreach (var wf in workflows)
            _out.WriteLine($"  {wf}");

        Assert.True(workflows.Count > 0,
            $"No workflows found in '{appName}'. The app may be empty or stopped.");
    }

    /// <summary>
    /// Probe C: ARM can read a workflow.json definition with actions.
    /// This is the core data we need to build the inventory.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProbeC_CanReadWorkflowDefinition()
    {
        var apps = await _client.ListStandardLogicAppsAsync();
        Skip.If(apps.Count == 0, "No Standard Logic Apps found — Probe A must pass first.");

        string? foundApp = null;
        string? foundWorkflow = null;
        JsonDocument? definition = null;

        // Try each app until we find one with a workflow+definition
        foreach (var (appName, _) in apps)
        {
            List<string>? workflows;
            try { workflows = await _client.ListWorkflowsAsync(appName); }
            catch (Exception ex) { _out.WriteLine($"  {appName}: list workflows failed — {ex.Message[..Math.Min(120, ex.Message.Length)]}"); continue; }

            if (workflows.Count == 0) { _out.WriteLine($"  {appName}: no workflows"); continue; }

            _out.WriteLine($"  {appName}: {workflows.Count} workflow(s), reading first…");
            foreach (var wf in workflows)
            {
                try { definition = await _client.GetWorkflowJsonAsync(appName, wf); }
                catch (Exception ex) { _out.WriteLine($"    {wf}: failed — {ex.Message[..Math.Min(120, ex.Message.Length)]}"); continue; }

                if (definition is not null) { foundApp = appName; foundWorkflow = wf; break; }
                _out.WriteLine($"    {wf}: returned null (404)");
            }
            if (definition is not null) break;
        }

        Assert.NotNull(definition);
        _out.WriteLine($"App: {foundApp} / Workflow: {foundWorkflow}");

        var raw = definition.RootElement.GetRawText();
        _out.WriteLine($"Response length: {raw.Length} chars");
        _out.WriteLine($"Preview: {raw[..Math.Min(500, raw.Length)]}");

        // Definition must have a properties or definition element
        var root = definition.RootElement;
        var hasProperties = root.TryGetProperty("properties", out _);
        var hasDefinition = root.TryGetProperty("definition", out _);
        Assert.True(hasProperties || hasDefinition,
            "Response JSON has neither 'properties' nor 'definition' key — unexpected format.");
    }

    /// <summary>
    /// Probe D: ARM can list managed API connections in the resource group.
    /// This replaces the VFS connections.json read (VFS admin requires special Kudu auth).
    /// Having zero connections is acceptable — some environments use only built-in connectors.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProbeD_CanListManagedConnections()
    {
        var connections = await _client.ListManagedConnectionsAsync();

        _out.WriteLine($"Found {connections.Count} managed API connection(s) in resource group:");
        foreach (var (name, apiId) in connections)
            _out.WriteLine($"  {name}  →  {apiId}");

        if (connections.Count == 0)
            _out.WriteLine("(No managed connections found — resource group may use only built-in connectors. This is acceptable.)");

        // The call itself succeeding is the probe — no connections is fine
        Assert.True(true);
    }

    /// <summary>
    /// Probe E: ARM VFS proxy can read connections.json (full site/wwwroot path).
    /// 404 is fine — app may have no managed connections.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProbeE_CanReadConnectionsJson()
    {
        var apps = await _client.ListStandardLogicAppsAsync();
        Skip.If(apps.Count == 0, "No Standard Logic Apps found — Probe A must pass first.");

        var (appName, _) = apps.First();
        _out.WriteLine($"Testing app: {appName}");

        var doc = await _client.GetConnectionsJsonAsync(appName);

        if (doc is null)
        {
            _out.WriteLine("connections.json → 404 (no managed connections). Acceptable.");
            return;
        }

        var raw = doc.RootElement.GetRawText();
        _out.WriteLine($"connections.json ({raw.Length} chars): {raw[..Math.Min(500, raw.Length)]}");
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SkipIfNotConfigured()
    {
        if (string.IsNullOrWhiteSpace(_opts.SubscriptionId) || string.IsNullOrWhiteSpace(_opts.ResourceGroup))
            throw new SkipException(
                "wtrfll.AzureLogicAppExplorer:SubscriptionId and wtrfll.AzureLogicAppExplorer:ResourceGroup must be set in " +
                "wtrfll.AzureLogicAppExplorer.IntegrationTests/appsettings.json to run integration tests.");
    }

}
