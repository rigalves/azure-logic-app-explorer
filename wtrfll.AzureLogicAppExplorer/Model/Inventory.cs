namespace wtrfll.AzureLogicAppExplorer.Model;

public enum CallType
{
    Http,
    Function,
    Salesforce,
    ManagedConnector,
    ServiceProvider,
    ServiceBus,        // Service Bus queue/topic — has entity name, can also be a trigger source
    ChildWorkflow,
    Unknown,
}

/// <summary>
/// Describes how a workflow is triggered. Populated for every workflow; null only when
/// the trigger block is missing or unparseable.
/// </summary>
/// <param name="Kind">Trigger kind string: "ServiceBus", "Http", "Recurrence", "ApiConnection", etc.</param>
/// <param name="EntityName">Queue or topic name for ServiceBus triggers; null for other kinds.</param>
public sealed record TriggerInfo(string Kind, string? EntityName);

/// <summary>A resolved or best-effort external call target.</summary>
public sealed record ExternalTarget(
    CallType CallType,
    string Name,                  // host / function app / connector name / workflow name
    string? RawExpression = null  // set when Name is derived from a @expression
);

/// <summary>One outbound call edge extracted from an action in a workflow.</summary>
public sealed record CallEdge(
    string ActionName,
    CallType CallType,
    ExternalTarget Target
);

public sealed class WorkflowInfo
{
    public required string Name { get; init; }
    public required string LogicAppName { get; init; }
    public required bool IsStateful { get; init; }
    public required List<CallEdge> Edges { get; init; }
    public TriggerInfo? Trigger { get; init; }
}

public sealed class LogicAppInfo
{
    public required string Name { get; init; }
    public required List<WorkflowInfo> Workflows { get; init; }
    public List<string> ScanErrors { get; init; } = [];
}

public sealed class Inventory
{
    public required List<LogicAppInfo> LogicApps { get; init; }
    public required DateTimeOffset ScannedAt { get; init; }
    public List<string> ScanErrors { get; init; } = [];

    public static Inventory Empty =>
        new() { LogicApps = [], ScannedAt = DateTimeOffset.UtcNow };
}
