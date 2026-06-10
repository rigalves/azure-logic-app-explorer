using wtrfll.AzureLogicAppExplorer.Azure;
using wtrfll.AzureLogicAppExplorer.Model;
using wtrfll.AzureLogicAppExplorer.Parsing;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace wtrfll.AzureLogicAppExplorer.Services;

public sealed class ScanService : IHostedService
{
    private readonly AzureLogicAppClient _client;
    private readonly ConnectionsParser _connectionsParser = new();
    private readonly ParametersParser _parametersParser = new();
    private readonly WorkflowParser _workflowParser = new();
    private readonly AppOptions _opts;
    private readonly ILogger<ScanService> _logger;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Inventory _current = Inventory.Empty;
    private bool _isScanning;
    private int _totalApps;
    private int _scannedApps;
    private string _currentAppName = "";

    public Inventory CurrentInventory => _current;
    public bool IsScanning => _isScanning;
    public int TotalApps => _totalApps;
    public int ScannedApps => _scannedApps;
    public string CurrentAppName => _currentAppName;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ScanService(
        AzureLogicAppClient client,
        IOptions<AppOptions> opts,
        ILogger<ScanService> logger)
    {
        _client = client;
        _opts = opts.Value;
        _logger = logger;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        await TryLoadSnapshotAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ── Public scan API ───────────────────────────────────────────────────────

    /// <summary>
    /// Scans the configured resource group, builds the full inventory, persists a snapshot.
    /// Concurrent calls are ignored (only one scan runs at a time).
    /// </summary>
    public async Task ScanAsync(CancellationToken ct = default)
    {
        if (!await _lock.WaitAsync(0, ct))
        {
            _logger.LogInformation("Scan already in progress — ignoring duplicate request.");
            return;
        }

        _isScanning = true;
        _totalApps = 0;
        _scannedApps = 0;
        _currentAppName = "";

        try
        {
            _logger.LogInformation("Scan started for resource group(s): {RGs}.", string.Join(", ", _opts.ResourceGroups));
            var globalErrors = new List<string>();

            var apps = new List<(string ResourceGroup, string Name, string Kind, string State)>();
            foreach (var rg in _opts.ResourceGroups)
            {
                var rgApps = await _client.ListStandardLogicAppsAsync(rg, ct);
                apps.AddRange(rgApps.Select(a => (ResourceGroup: rg, a.Name, a.Kind, a.State)));
            }
            _totalApps = apps.Count;
            _logger.LogInformation("Found {Count} Standard Logic App(s) across {RgCount} resource group(s).", apps.Count, _opts.ResourceGroups.Count);

            var logicApps = new List<LogicAppInfo>();

            await Parallel.ForEachAsync(apps,
                new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct },
                async (app, innerCt) =>
                {
                    _currentAppName = app.Name;
                    var isRunning = app.State.Equals("Running", StringComparison.OrdinalIgnoreCase);
                    var info = isRunning
                        ? await ScanAppAsync(app.ResourceGroup, app.Name, innerCt)
                        : new LogicAppInfo { Name = app.Name, Workflows = [], IsRunning = false };
                    if (!isRunning)
                        _logger.LogInformation("App '{App}' is stopped (state: {State}) — skipping workflow scan.", app.Name, app.State);
                    lock (logicApps) logicApps.Add(info);
                    Interlocked.Increment(ref _scannedApps);
                });

            logicApps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            _current = new Inventory
            {
                LogicApps = logicApps,
                ScannedAt = DateTimeOffset.UtcNow,
                ScanErrors = globalErrors,
            };

            await PersistSnapshotAsync(_current, ct);
            _logger.LogInformation("Scan complete. {Apps} apps, {Wfs} workflows.",
                logicApps.Count,
                logicApps.Sum(a => a.Workflows.Count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed with an unhandled exception.");
        }
        finally
        {
            _isScanning = false;
            _lock.Release();
        }
    }

    // ── Per-app scanning ──────────────────────────────────────────────────────

    private async Task<LogicAppInfo> ScanAppAsync(string resourceGroup, string appName, CancellationToken ct)
    {
        var errors = new List<string>();

        // Read connections.json (null = no managed connections, which is fine)
        ConnectionsLookup connections;
        try
        {
            var connDoc = await _client.GetConnectionsJsonAsync(resourceGroup, appName, ct);
            connections = _connectionsParser.Parse(connDoc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read connections.json for '{App}': {Err}", appName, ex.Message);
            connections = ConnectionsLookup.Empty;
        }

        // Read parameters.json — used to resolve @parameters('name') in HTTP URIs
        JsonDocument? appParamsDoc = null;
        try
        {
            appParamsDoc = await _client.GetParametersJsonAsync(resourceGroup, appName, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read parameters.json for '{App}': {Err}", appName, ex.Message);
        }

        // List workflows
        List<string> workflowNames;
        try
        {
            workflowNames = await _client.ListWorkflowsAsync(resourceGroup, appName, ct);
        }
        catch (Exception ex)
        {
            var msg = $"Could not list workflows: {ex.Message}";
            _logger.LogWarning("App '{App}': {Msg}", appName, msg);
            return new LogicAppInfo { Name = appName, Workflows = [], ScanErrors = [msg] };
        }

        // Read + parse each workflow
        var workflows = new List<WorkflowInfo>();
        foreach (var wfName in workflowNames)
        {
            var wf = await ScanWorkflowAsync(resourceGroup, appName, wfName, connections, appParamsDoc, errors, ct);
            if (wf is not null) workflows.Add(wf);
        }

        return new LogicAppInfo { Name = appName, Workflows = workflows, ScanErrors = errors };
    }

    private async Task<WorkflowInfo?> ScanWorkflowAsync(
        string resourceGroup, string appName, string wfName, ConnectionsLookup connections,
        JsonDocument? appParamsDoc, List<string> errors, CancellationToken ct)
    {
        try
        {
            var doc = await _client.GetWorkflowJsonAsync(resourceGroup, appName, wfName, ct);
            if (doc is null)
            {
                _logger.LogDebug("workflow.json for '{App}/{Wf}' returned 404 — skipping.", appName, wfName);
                return null;
            }

            var parameters = _parametersParser.Parse(appParamsDoc, doc);
            var isStateful = IsStateful(doc);
            var edges = _workflowParser.Parse(doc, connections, parameters);
            var trigger = _workflowParser.ParseTrigger(doc, connections, parameters);
            var domain = _workflowParser.ParseDomain(doc);

            return new WorkflowInfo
            {
                Name = wfName,
                LogicAppName = appName,
                IsStateful = isStateful,
                Edges = edges.ToList(),
                Trigger = trigger,
                Domain = domain,
                Classification = WorkflowParser.ClassifyWorkflow(wfName, trigger, appName),
            };
        }
        catch (Exception ex)
        {
            var msg = $"{wfName}: {ex.Message}";
            _logger.LogWarning("Failed to parse workflow '{App}/{Wf}': {Err}", appName, wfName, ex.Message);
            errors.Add(msg);
            return null;
        }
    }

    private static bool IsStateful(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.TryGetProperty("kind", out var kind))
            return kind.GetString()?.Equals("Stateful", StringComparison.OrdinalIgnoreCase) ?? true;
        return true; // default assumption
    }

    // ── Snapshot persistence ──────────────────────────────────────────────────

    private async Task PersistSnapshotAsync(Inventory inventory, CancellationToken ct)
    {
        try
        {
            var path = _opts.SnapshotPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(inventory, SerializerOptions);
            await File.WriteAllTextAsync(path, json, ct);
            _logger.LogInformation("Snapshot saved to '{Path}'.", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist snapshot.");
        }
    }

    private async Task TryLoadSnapshotAsync(CancellationToken ct)
    {
        var path = _opts.SnapshotPath;
        if (!File.Exists(path))
        {
            _logger.LogInformation("No snapshot found at '{Path}' — starting with empty inventory.", path);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var inventory = JsonSerializer.Deserialize<Inventory>(json, SerializerOptions);
            if (inventory is not null)
            {
                _current = inventory;
                _logger.LogInformation(
                    "Loaded snapshot from '{Path}' (scanned {At:u}, {Apps} apps).",
                    path, inventory.ScannedAt, inventory.LogicApps.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load snapshot from '{Path}' — starting fresh.", path);
        }
    }
}
