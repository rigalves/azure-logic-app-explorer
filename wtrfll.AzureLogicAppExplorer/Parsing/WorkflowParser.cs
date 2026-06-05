using wtrfll.AzureLogicAppExplorer.Model;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace wtrfll.AzureLogicAppExplorer.Parsing;

/// <summary>
/// Recursively walks a workflow.json definition and extracts all outbound call edges.
/// Handles nesting inside Scope, If/Else, Foreach, Until, and Switch containers.
/// Resolves @parameters('name') expressions using the supplied ParametersLookup.
/// </summary>
public sealed partial class WorkflowParser
{
    [GeneratedRegex(@"/sites/([^/]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex SiteSegmentRegex();

    // Matches @parameters('name') and @{parameters('name')} (with or without braces)
    [GeneratedRegex(@"parameters\('([^']+)'\)", RegexOptions.IgnoreCase)]
    private static partial Regex ParameterRefRegex();

    // Matches @appsettings('key') or @appsetting('key')
    [GeneratedRegex(@"@appsettings?\('([^']+)'\)", RegexOptions.IgnoreCase)]
    private static partial Regex AppSettingRefRegex();

    public IReadOnlyList<CallEdge> Parse(
        JsonDocument workflowJson,
        ConnectionsLookup connections,
        ParametersLookup? parameters = null)
    {
        parameters ??= ParametersLookup.Empty;
        var root = workflowJson.RootElement;
        var definition = root.TryGetProperty("definition", out var def) ? def : root;

        if (!definition.TryGetProperty("actions", out var actions))
            return [];

        var edges = new List<CallEdge>();
        WalkActions(actions, connections, parameters, edges);
        return edges;
    }

    private void WalkActions(
        JsonElement actions, ConnectionsLookup connections,
        ParametersLookup parameters, List<CallEdge> edges)
    {
        foreach (var action in actions.EnumerateObject())
        {
            var name = action.Name;
            var node = action.Value;

            if (!node.TryGetProperty("type", out var typeProp)) continue;
            var type = typeProp.GetString() ?? "";

            switch (type.ToLowerInvariant())
            {
                case "http":
                case "httpwebhook":
                    edges.Add(ParseHttp(name, node, parameters));
                    break;

                case "function":
                    edges.Add(ParseFunction(name, node, connections));
                    break;

                case "apiconnection":
                case "apiconnectionwebhook":
                case "apiconnectionnotification":
                    edges.Add(ParseApiConnection(name, node, connections));
                    break;

                case "serviceprovider":
                    edges.Add(ParseServiceProvider(name, node, connections, parameters));
                    break;

                case "workflow":
                    edges.Add(ParseChildWorkflow(name, node));
                    break;

                // ── containers: recurse ────────────────────────────────────
                case "scope":
                    RecurseInto(node, "actions", connections, parameters, edges);
                    break;

                case "if":
                    RecurseInto(node, "actions", connections, parameters, edges);
                    if (node.TryGetProperty("else", out var elseBranch))
                        RecurseInto(elseBranch, "actions", connections, parameters, edges);
                    break;

                case "foreach":
                case "until":
                    RecurseInto(node, "actions", connections, parameters, edges);
                    break;

                case "switch":
                    if (node.TryGetProperty("cases", out var cases))
                        foreach (var c in cases.EnumerateObject())
                            RecurseInto(c.Value, "actions", connections, parameters, edges);
                    RecurseInto(node, "default", connections, parameters, edges, childKey: "actions");
                    break;
            }
        }
    }

    private void RecurseInto(
        JsonElement parent, string key, ConnectionsLookup connections,
        ParametersLookup parameters, List<CallEdge> edges, string? childKey = null)
    {
        if (!parent.TryGetProperty(key, out var child)) return;
        var target = childKey is null ? child
            : child.TryGetProperty(childKey, out var inner) ? inner : default;
        if (target.ValueKind == JsonValueKind.Object)
            WalkActions(target, connections, parameters, edges);
    }

    // ── action parsers ────────────────────────────────────────────────────────

    private static CallEdge ParseHttp(string name, JsonElement node, ParametersLookup parameters)
    {
        string? rawUri = null;

        if (node.TryGetProperty("inputs", out var inputs))
        {
            if (inputs.TryGetProperty("uri", out var uri))
                rawUri = uri.GetString();
            else if (inputs.TryGetProperty("subscribe", out var sub) &&
                     sub.TryGetProperty("uri", out var subUri))
                rawUri = subUri.GetString();
        }

        var (targetName, isExpr) = ResolveUri(rawUri, parameters);
        return new CallEdge(name, CallType.Http,
            new ExternalTarget(CallType.Http, targetName, isExpr ? rawUri : null));
    }

    private static CallEdge ParseFunction(string name, JsonElement node, ConnectionsLookup connections)
    {
        if (node.TryGetProperty("inputs", out var inputs) &&
            inputs.TryGetProperty("function", out var func) &&
            func.TryGetProperty("id", out var id))
        {
            var match = SiteSegmentRegex().Match(id.GetString() ?? "");
            if (match.Success)
                return new CallEdge(name, CallType.Function,
                    new ExternalTarget(CallType.Function, match.Groups[1].Value));
        }

        if (node.TryGetProperty("inputs", out var inp2) &&
            inp2.TryGetProperty("host", out var host) &&
            host.TryGetProperty("connection", out var conn) &&
            conn.TryGetProperty("referenceName", out var refName) &&
            connections.TryGet(refName.GetString() ?? "", out var info))
            return new CallEdge(name, CallType.Function,
                new ExternalTarget(CallType.Function, info.DisplayName));

        return new CallEdge(name, CallType.Function,
            new ExternalTarget(CallType.Function, "unknown-function-app"));
    }

    private static CallEdge ParseApiConnection(string name, JsonElement node, ConnectionsLookup connections)
    {
        string? refName = null;

        if (node.TryGetProperty("inputs", out var inputs) &&
            inputs.TryGetProperty("host", out var host) &&
            host.TryGetProperty("connection", out var conn) &&
            conn.TryGetProperty("referenceName", out var refProp))
            refName = refProp.GetString();

        if (refName is not null && connections.TryGet(refName, out var info))
            return new CallEdge(name, info.CallType, new ExternalTarget(info.CallType, info.DisplayName));

        return new CallEdge(name, CallType.ManagedConnector,
            new ExternalTarget(CallType.ManagedConnector, refName ?? "unknown-connection"));
    }

    private static CallEdge ParseServiceProvider(
        string name, JsonElement node, ConnectionsLookup connections, ParametersLookup parameters)
    {
        if (node.TryGetProperty("inputs", out var inputs) &&
            inputs.TryGetProperty("serviceProviderConfiguration", out var spConfig) &&
            spConfig.TryGetProperty("serviceProviderId", out var spId))
        {
            var providerId = spId.GetString() ?? "";

            // Service Bus gets a dedicated CallType so it can link with SB-triggered workflows
            if (IsServiceBusProviderId(providerId))
            {
                var entityName = ExtractSbEntityName(inputs, parameters);
                if (entityName is not null)
                    return new CallEdge(name, CallType.ServiceBus,
                        new ExternalTarget(CallType.ServiceBus, entityName));
            }

            var displayName = ConnectionsParser.MapServiceProviderId(providerId);
            return new CallEdge(name, CallType.ServiceProvider,
                new ExternalTarget(CallType.ServiceProvider, displayName));
        }

        if (node.TryGetProperty("inputs", out var inp2) &&
            inp2.TryGetProperty("serviceProviderConfiguration", out var spCfg2) &&
            spCfg2.TryGetProperty("connectionName", out var connName))
        {
            var refName = connName.GetString() ?? "";
            if (connections.TryGet(refName, out var info))
                return new CallEdge(name, CallType.ServiceProvider,
                    new ExternalTarget(CallType.ServiceProvider, info.DisplayName));
        }

        return new CallEdge(name, CallType.ServiceProvider,
            new ExternalTarget(CallType.ServiceProvider, "unknown-service-provider"));
    }

    /// <summary>
    /// Parses the triggers block and returns a TriggerInfo for the first trigger found.
    /// For Service Bus triggers, extracts the queue or topic name so the diagram can
    /// draw a reverse edge: SB-node → this workflow.
    /// </summary>
    public TriggerInfo? ParseTrigger(
        JsonDocument workflowJson,
        ConnectionsLookup connections,
        ParametersLookup? parameters = null)
    {
        parameters ??= ParametersLookup.Empty;
        var root = workflowJson.RootElement;
        var definition = root.TryGetProperty("definition", out var def) ? def : root;

        if (!definition.TryGetProperty("triggers", out var triggers)) return null;

        foreach (var trigger in triggers.EnumerateObject())
        {
            var node = trigger.Value;
            if (!node.TryGetProperty("type", out var typeProp)) continue;

            return (typeProp.GetString() ?? "").ToLowerInvariant() switch
            {
                "serviceprovider" => ParseServiceProviderTrigger(node, parameters),
                "request"         => new TriggerInfo("Http", null),
                "http" or "httpwebhook" => new TriggerInfo("Http", null),
                "recurrence"      => new TriggerInfo("Recurrence", null),
                "apiconnection" or "apiconnectionwebhook" => new TriggerInfo("ApiConnection", null),
                var t             => new TriggerInfo(t, null),
            };
        }

        return null;
    }

    private static TriggerInfo ParseServiceProviderTrigger(JsonElement node, ParametersLookup parameters)
    {
        if (!node.TryGetProperty("inputs", out var inputs))
            return new TriggerInfo("ServiceProvider", null);

        if (inputs.TryGetProperty("serviceProviderConfiguration", out var spConfig) &&
            spConfig.TryGetProperty("serviceProviderId", out var spId) &&
            IsServiceBusProviderId(spId.GetString() ?? ""))
        {
            return new TriggerInfo("ServiceBus", ExtractSbEntityName(inputs, parameters));
        }

        return new TriggerInfo("ServiceProvider", null);
    }

    // ── Service Bus helpers ───────────────────────────────────────────────────

    private static bool IsServiceBusProviderId(string providerId) =>
        providerId.Contains("serviceBus", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the queue or topic name from an inputs element.
    /// Tries entityName (send actions) → queueName → topicName, then resolves @parameters() if needed.
    /// Returns null when the entity name cannot be determined (falls back to generic SB node).
    /// </summary>
    private static string? ExtractSbEntityName(JsonElement inputs, ParametersLookup parameters)
    {
        if (!inputs.TryGetProperty("parameters", out var inputParams)) return null;

        string? raw = null;
        foreach (var key in (ReadOnlySpan<string>)["entityName", "queueName", "topicName"])
        {
            if (inputParams.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                raw = v.GetString();
                break;
            }
        }

        if (raw is null) return null;

        if (raw.StartsWith('@'))
        {
            var m = ParameterRefRegex().Match(raw);
            return m.Success && parameters.TryGet(m.Groups[1].Value, out var resolved) ? resolved : null;
        }

        return raw;
    }

    private static CallEdge ParseChildWorkflow(string name, JsonElement node)
    {
        if (node.TryGetProperty("inputs", out var inputs) &&
            inputs.TryGetProperty("host", out var host) &&
            host.TryGetProperty("workflow", out var wf) &&
            wf.TryGetProperty("id", out var id))
        {
            var workflowName = id.GetString()?.Split('/').LastOrDefault() ?? "unknown";
            return new CallEdge(name, CallType.ChildWorkflow,
                new ExternalTarget(CallType.ChildWorkflow, workflowName));
        }

        return new CallEdge(name, CallType.ChildWorkflow,
            new ExternalTarget(CallType.ChildWorkflow, "unknown-workflow"));
    }

    // ── URI resolution + best-guess ──────────────────────────────────────────

    /// <summary>
    /// Extracts the most meaningful human-readable label from an unresolved expression.
    /// Priority: parameter name → appsetting key → first clean identifier.
    /// The full raw expression is preserved separately for table display.
    /// </summary>
    private static string BestGuessFromExpression(string expr)
    {
        // Prefer the parameter name: @parameters('name') or @{parameters('name')}
        var paramMatch = ParameterRefRegex().Match(expr);
        if (paramMatch.Success) return paramMatch.Groups[1].Value;

        // Prefer the appsetting key: @appsettings('KEY')
        var asMatch = AppSettingRefRegex().Match(expr);
        if (asMatch.Success) return asMatch.Groups[1].Value;

        // Generic cleanup — strip @{ } @ and common function names
        var cleaned = expr
            .Replace("@{", "").Replace("@", "").Replace("}", "")
            .Trim('/', ' ', ',');
        // Remove function call syntax: concat(, trim(, etc.
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[a-zA-Z]+\(", "")
            .Replace(")", "").Replace("'", "").Trim();

        // Take the first meaningful path segment
        var first = cleaned.Split('/', ' ', ',')[0].Trim();
        if (first.Length > 1) return first;

        // Absolute fallback: truncate
        const int max = 30;
        return expr.Length > max ? expr[..max] + "…" : expr;
    }

    // ── URI resolution ────────────────────────────────────────────────────────

    private static (string targetName, bool isExpression) ResolveUri(
        string? rawUri, ParametersLookup parameters)
    {
        if (rawUri is null) return ("unknown", false);

        // Literal URL — no resolution needed
        if (!rawUri.StartsWith('@'))
        {
            if (Uri.TryCreate(rawUri, UriKind.Absolute, out var parsed))
                return (parsed.Host, false);
            return (rawUri, false);
        }

        // Substitute all @parameters('name') / @{parameters('name')} references
        var working = ParameterRefRegex().Replace(rawUri, m =>
        {
            var paramName = m.Groups[1].Value;
            return parameters.TryGet(paramName, out var val) ? val : m.Value;
        });

        // Replace all @{value} interpolation wrappers with just the value
        // e.g. "@{https://api.com}/path" → "https://api.com/path"
        working = System.Text.RegularExpressions.Regex.Replace(working, @"@\{([^}]+)\}", "$1");

        // Check for @appsettings BEFORE stripping the leading @ so the regex still matches
        var asMatch = AppSettingRefRegex().Match(working);
        if (asMatch.Success)
            return (BestGuessFromExpression(rawUri!), true);

        // Strip a bare leading @ (e.g. "@https://..." after full single-value resolution)
        if (working.StartsWith('@'))
            working = working[1..];

        // Fully resolved to a URL?
        if (Uri.TryCreate(working, UriKind.Absolute, out var uri))
            return (uri.Host, false);

        // Still unresolved — derive a human-readable best-guess from the original expression
        return (BestGuessFromExpression(rawUri!), true);
    }
}
