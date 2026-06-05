using System.Text.Json;

namespace wtrfll.AzureLogicAppExplorer.Parsing;

/// <summary>
/// Parses parameter values from two sources and merges them:
///   1. App-level parameters.json  (site/wwwroot/parameters.json)
///   2. Workflow-level "parameters" block at the root of workflow.json (not inside "definition")
/// Workflow-level values override app-level values for the same key.
/// </summary>
public sealed class ParametersParser
{
    public ParametersLookup Parse(JsonDocument? appParamsDoc, JsonDocument? workflowJson)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (appParamsDoc is not null)
            ExtractValues(appParamsDoc.RootElement, lookup);

        // Top-level "parameters" in workflow.json — distinct from definition.parameters
        if (workflowJson is not null &&
            workflowJson.RootElement.TryGetProperty("parameters", out var wfParams))
            ExtractValues(wfParams, lookup);

        return new ParametersLookup(lookup);
    }

    private static void ExtractValues(JsonElement element, Dictionary<string, string> lookup)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name.StartsWith('$')) continue; // skip $connections etc.
            if (prop.Value.TryGetProperty("value", out var val) &&
                val.ValueKind == JsonValueKind.String)
            {
                var str = val.GetString();
                if (str is not null) lookup[prop.Name] = str;
            }
        }
    }
}

public sealed class ParametersLookup(Dictionary<string, string> inner)
{
    public static ParametersLookup Empty => new(new());
    public bool TryGet(string name, out string value) => inner.TryGetValue(name, out value!);
    public int Count => inner.Count;
}
