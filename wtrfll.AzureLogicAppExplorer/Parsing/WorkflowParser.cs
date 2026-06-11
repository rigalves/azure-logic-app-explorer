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
        string? method = null;

        if (node.TryGetProperty("inputs", out var inputs))
        {
            if (inputs.TryGetProperty("uri", out var uri))
                rawUri = uri.GetString();
            else if (inputs.TryGetProperty("subscribe", out var sub) &&
                     sub.TryGetProperty("uri", out var subUri))
                rawUri = subUri.GetString();

            method = ExtractMethod(inputs);
        }

        var (targetName, path, isExpr) = ResolveUri(rawUri, parameters);
        return new CallEdge(name, CallType.Http,
            new ExternalTarget(CallType.Http, targetName, isExpr ? rawUri : null, isExpr ? null : path), method);
    }

    private static CallEdge ParseFunction(string name, JsonElement node, ConnectionsLookup connections)
    {
        if (node.TryGetProperty("inputs", out var inputs) &&
            inputs.TryGetProperty("function", out var func))
        {
            // Real-world Logic Apps Standard shape: inputs.function.connectionName
            // references connections.json's functionConnections, which holds the
            // actual function app + function name.
            if (func.TryGetProperty("connectionName", out var connName) &&
                connections.TryGet(connName.GetString() ?? "", out var connInfo))
                return new CallEdge(name, CallType.Function,
                    new ExternalTarget(CallType.Function, connInfo.DisplayName));

            if (func.TryGetProperty("id", out var id))
            {
                var match = SiteSegmentRegex().Match(id.GetString() ?? "");
                if (match.Success)
                    return new CallEdge(name, CallType.Function,
                        new ExternalTarget(CallType.Function, match.Groups[1].Value));
            }
        }

        if (node.TryGetProperty("inputs", out var inp2) &&
            inp2.TryGetProperty("host", out var host) &&
            host.TryGetProperty("connection", out var conn) &&
            conn.TryGetProperty("referenceName", out var refName) &&
            connections.TryGet(refName.GetString() ?? "", out var info))
            return new CallEdge(name, CallType.Function,
                new ExternalTarget(CallType.Function, info.DisplayName));

        // No connection info available — fall back to the action name rather
        // than a meaningless placeholder.
        return new CallEdge(name, CallType.Function, new ExternalTarget(CallType.Function, name));
    }

    private static CallEdge ParseApiConnection(string name, JsonElement node, ConnectionsLookup connections)
    {
        string? refName = null;
        string? method = null;

        if (node.TryGetProperty("inputs", out var inputs))
        {
            if (inputs.TryGetProperty("host", out var host) &&
                host.TryGetProperty("connection", out var conn) &&
                conn.TryGetProperty("referenceName", out var refProp))
                refName = refProp.GetString();

            method = ExtractMethod(inputs);
        }

        if (refName is not null && connections.TryGet(refName, out var info))
        {
            // Salesforce's display name is generic ("Salesforce") for every connection,
            // so use the action name to give each operation its own node.
            var targetName = info.CallType == CallType.Salesforce ? name : info.DisplayName;
            return new CallEdge(name, info.CallType, new ExternalTarget(info.CallType, targetName), method);
        }

        return new CallEdge(name, CallType.ManagedConnector,
            new ExternalTarget(CallType.ManagedConnector, refName ?? "unknown-connection"), method);
    }

    private static CallEdge ParseServiceProvider(
        string name, JsonElement node, ConnectionsLookup connections, ParametersLookup parameters)
    {
        if (node.TryGetProperty("inputs", out var inputs) &&
            inputs.TryGetProperty("serviceProviderConfiguration", out var spConfig) &&
            spConfig.TryGetProperty("serviceProviderId", out var spId))
        {
            var providerId = spId.GetString() ?? "";
            var operationId = spConfig.TryGetProperty("operationId", out var opIdProp) ? opIdProp.GetString() : null;

            // Service Bus gets a dedicated CallType so it can link with SB-triggered workflows
            if (ConnectorTaxonomy.IsServiceBusProviderId(providerId))
            {
                var entityName = ExtractSbEntityName(inputs, parameters);
                if (entityName is not null && !TriggerInfo.IsPlaceholder(entityName))
                    return new CallEdge(name, CallType.ServiceBus,
                        new ExternalTarget(CallType.ServiceBus, entityName), Operation: ConnectorTaxonomy.MapServiceBusOperation(operationId));
            }

            // Key Vault gets a dedicated CallType so it can be hidden independently in the legend
            if (ConnectorTaxonomy.IsKeyVaultProviderId(providerId))
                return new CallEdge(name, CallType.KeyVault,
                    new ExternalTarget(CallType.KeyVault, ConnectorTaxonomy.MapServiceProviderId(providerId)), Operation: ConnectorTaxonomy.MapKeyVaultOperation(operationId));

            var displayName = ConnectorTaxonomy.MapServiceProviderId(providerId);
            return new CallEdge(name, CallType.ServiceProvider,
                new ExternalTarget(CallType.ServiceProvider, displayName));
        }

        if (node.TryGetProperty("inputs", out var inp2) &&
            inp2.TryGetProperty("serviceProviderConfiguration", out var spCfg2) &&
            spCfg2.TryGetProperty("connectionName", out var connName))
        {
            var refName = connName.GetString() ?? "";
            if (connections.TryGet(refName, out var info))
                return new CallEdge(name, info.CallType,
                    new ExternalTarget(info.CallType, info.DisplayName));
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

            var inputs = node.TryGetProperty("inputs", out var triggerInputs) ? triggerInputs : default;
            var method = inputs.ValueKind == JsonValueKind.Object ? ExtractMethod(inputs) : null;

            return (typeProp.GetString() ?? "").ToLowerInvariant() switch
            {
                "serviceprovider" => ParseServiceProviderTrigger(node, parameters),
                "request"         => new TriggerInfo("Http", null, Method: method),
                "http" or "httpwebhook" => new TriggerInfo("Http", null, Method: method),
                "recurrence"      => new TriggerInfo("Recurrence", null),
                "apiconnection" or "apiconnectionwebhook" => new TriggerInfo("ApiConnection", null, Method: method),
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
            ConnectorTaxonomy.IsServiceBusProviderId(spId.GetString()))
        {
            var operationId = spConfig.TryGetProperty("operationId", out var opId) ? opId.GetString() ?? "" : "";
            var entityKind = operationId.Contains("Topic", StringComparison.OrdinalIgnoreCase) ? "Topic"
                : operationId.Contains("Queue", StringComparison.OrdinalIgnoreCase) ? "Queue"
                : null;
            return new TriggerInfo("ServiceBus", ExtractSbEntityName(inputs, parameters), entityKind);
        }

        return new TriggerInfo("ServiceProvider", null);
    }

    /// <summary>
    /// Extracts definition.metadata["x-esp-domain"], or null when the workflow has no
    /// metadata block or the key is absent/non-string.
    /// </summary>
    public string? ParseDomain(JsonDocument workflowJson)
    {
        var root = workflowJson.RootElement;
        var definition = root.TryGetProperty("definition", out var def) ? def : root;

        if (definition.TryGetProperty("metadata", out var metadata) &&
            metadata.TryGetProperty("x-esp-domain", out var domain) &&
            domain.ValueKind == JsonValueKind.String)
        {
            return domain.GetString();
        }

        return null;
    }

    // Name segments (split on '-'/'_') that indicate a Pub/Sub role.
    private static readonly string[] PubSegments = ["pub", "publisher", "publish"];
    private static readonly string[] SubSegments = ["sub", "subscriber", "subscribe"];

    /// <summary>
    /// Classifies a workflow's role using its name, its parent Logic App's name, and its
    /// trigger: "-publisher"/"-subscriber" name suffixes win; otherwise any "pub"/"sub"-like
    /// segment in the workflow name or the Logic App name (split on '-'/'_') indicates
    /// Pub/Sub; otherwise an HTTP/API-triggered workflow is treated as a Facade; everything
    /// else is "Other".
    /// </summary>
    public static WorkflowClassification ClassifyWorkflow(string workflowName, TriggerInfo? trigger, string? logicAppName = null)
    {
        if (workflowName.EndsWith("-publisher", StringComparison.OrdinalIgnoreCase))
            return WorkflowClassification.Pub;

        if (workflowName.EndsWith("-subscriber", StringComparison.OrdinalIgnoreCase))
            return WorkflowClassification.Sub;

        var segments = workflowName.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (logicAppName is not null)
            segments = [.. segments, .. logicAppName.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)];

        if (segments.Any(s => PubSegments.Contains(s, StringComparer.OrdinalIgnoreCase)))
            return WorkflowClassification.Pub;

        if (segments.Any(s => SubSegments.Contains(s, StringComparer.OrdinalIgnoreCase)))
            return WorkflowClassification.Sub;

        if (trigger?.Kind is "Http" or "ApiConnection")
            return WorkflowClassification.Facade;

        return WorkflowClassification.Other;
    }

    /// <summary>
    /// Extracts the HTTP method from an inputs element: a single "method" string
    /// (uppercased), or a "methods" array joined with "/". Returns null if absent.
    /// </summary>
    private static string? ExtractMethod(JsonElement inputs)
    {
        if (inputs.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String)
            return method.GetString()?.ToUpperInvariant();

        if (inputs.TryGetProperty("methods", out var methods) && methods.ValueKind == JsonValueKind.Array)
        {
            var values = methods.EnumerateArray()
                .Select(m => m.GetString()?.ToUpperInvariant())
                .Where(m => m is not null)
                .ToList();
            return values.Count > 0 ? string.Join("/", values) : null;
        }

        return null;
    }

    // ── Service Bus helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Extracts the queue or topic name from an inputs element.
    /// Tries entityName (send actions) → queueName → topicName, then resolves @parameters() if needed.
    /// When the value is an unresolved @appsettings()/@parameters() reference, falls back to a
    /// "&lt;appsetting:KEY&gt;" / "&lt;parameter:KEY&gt;" placeholder (see TriggerInfo.IsPlaceholder)
    /// so the diagram still shows something meaningful instead of "Unknown". Returns null only when
    /// no entity name field is present at all.
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
        if (!raw.StartsWith('@')) return raw;

        var paramMatch = ParameterRefRegex().Match(raw);
        if (paramMatch.Success)
            return parameters.TryGet(paramMatch.Groups[1].Value, out var resolved)
                ? resolved
                : $"<parameter:{paramMatch.Groups[1].Value}>";

        var appSettingMatch = AppSettingRefRegex().Match(raw);
        if (appSettingMatch.Success)
            return $"<appsetting:{appSettingMatch.Groups[1].Value}>";

        return null;
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

    private static (string targetName, string? path, bool isExpression) ResolveUri(
        string? rawUri, ParametersLookup parameters)
    {
        if (rawUri is null) return ("unknown", null, false);

        // Literal URL — no resolution needed
        if (!rawUri.StartsWith('@'))
        {
            if (Uri.TryCreate(rawUri, UriKind.Absolute, out var parsed))
                return (parsed.Host, NormalizePath(parsed.AbsolutePath), false);
            return (rawUri, null, false);
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
            return (BestGuessFromExpression(rawUri!), null, true);

        // Strip a bare leading @ (e.g. "@https://..." after full single-value resolution)
        if (working.StartsWith('@'))
            working = working[1..];

        // Fully resolved to a URL?
        if (Uri.TryCreate(working, UriKind.Absolute, out var uri))
            return (uri.Host, NormalizePath(uri.AbsolutePath), false);

        // Still unresolved — derive a human-readable best-guess from the original expression
        return (BestGuessFromExpression(rawUri!), null, true);
    }

    /// <summary>Returns the path, or null when it's just "/" (i.e. no meaningful path).</summary>
    private static string? NormalizePath(string path) =>
        string.IsNullOrEmpty(path) || path == "/" ? null : path;
}
