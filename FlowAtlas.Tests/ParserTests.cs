using FlowAtlas.Model;
using FlowAtlas.Parsing;
using System.Text.Json;

namespace FlowAtlas.Tests;

public class ParserTests
{
    private static JsonDocument LoadFixture(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine("Fixtures", name)));

    private static (WorkflowParser parser, ConnectionsLookup connections) Setup()
    {
        var connectionsDoc = LoadFixture("connections.json");
        var connections = new ConnectionsParser().Parse(connectionsDoc);
        return (new WorkflowParser(), connections);
    }

    private static IReadOnlyList<CallEdge> ParseAllTypes()
    {
        var (parser, connections) = Setup();
        return parser.Parse(LoadFixture("workflow-all-types.json"), connections);
    }

    // ── ConnectionsParser ─────────────────────────────────────────────────────

    [Fact]
    public void Connections_Salesforce_IsClassifiedAsSalesforce()
    {
        var (_, connections) = Setup();
        Assert.True(connections.TryGet("salesforce", out var info));
        Assert.Equal(CallType.Salesforce, info.CallType);
        Assert.Equal("Salesforce", info.DisplayName);
    }

    [Fact]
    public void Connections_Office365_IsClassifiedAsManagedConnector()
    {
        var (_, connections) = Setup();
        Assert.True(connections.TryGet("office365", out var info));
        Assert.Equal(CallType.ManagedConnector, info.CallType);
        Assert.Equal("Office 365", info.DisplayName);
    }

    [Fact]
    public void Connections_ServiceProvider_IsClassifiedCorrectly()
    {
        var (_, connections) = Setup();
        Assert.True(connections.TryGet("serviceBusConn", out var info));
        Assert.Equal(CallType.ServiceProvider, info.CallType);
        Assert.Equal("Service Bus", info.DisplayName);
    }

    [Fact]
    public void Connections_FunctionConnection_IsClassifiedAsFunction()
    {
        var (_, connections) = Setup();
        Assert.True(connections.TryGet("funcConn", out var info));
        Assert.Equal(CallType.Function, info.CallType);
        Assert.Equal("func-app-orders", info.DisplayName);
    }

    [Fact]
    public void Connections_NullDocument_ReturnsEmptyLookup()
    {
        var lookup = new ConnectionsParser().Parse(null);
        Assert.False(lookup.TryGet("anything", out _));
    }

    // ── WorkflowParser — top-level actions ────────────────────────────────────

    [Fact]
    public void Parser_Http_LiteralUri_ResolvesHost()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Call_External_API");

        Assert.Equal(CallType.Http, edge.CallType);
        Assert.Equal("api.contoso.com", edge.Target.Name);
        Assert.Null(edge.Target.RawExpression);
    }

    [Fact]
    public void Parser_Http_ExpressionUri_PreservesExpression()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Call_API_via_Expression");

        Assert.Equal(CallType.Http, edge.CallType);
        Assert.NotNull(edge.Target.RawExpression);
        Assert.StartsWith("@", edge.Target.RawExpression);
    }

    [Fact]
    public void Parser_Function_ExtractsFunctionAppName()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Call_Azure_Function");

        Assert.Equal(CallType.Function, edge.CallType);
        Assert.Equal("func-app-orders", edge.Target.Name);
    }

    [Fact]
    public void Parser_ApiConnection_Salesforce_IsDistinct()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Sync_to_Salesforce");

        Assert.Equal(CallType.Salesforce, edge.CallType);
        Assert.Equal("Salesforce", edge.Target.Name);
    }

    [Fact]
    public void Parser_ApiConnection_Office365_IsManagedConnector()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Send_Email");

        Assert.Equal(CallType.ManagedConnector, edge.CallType);
        Assert.Equal("Office 365", edge.Target.Name);
    }

    [Fact]
    public void Parser_ServiceProvider_ServiceBus_IsClassified()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Send_Service_Bus_Message");

        Assert.Equal(CallType.ServiceProvider, edge.CallType);
        Assert.Equal("Service Bus", edge.Target.Name);
    }

    [Fact]
    public void Parser_ChildWorkflow_ExtractsWorkflowName()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Call_Child_Workflow");

        Assert.Equal(CallType.ChildWorkflow, edge.CallType);
        Assert.Equal("wf-validate-order", edge.Target.Name);
    }

    // ── WorkflowParser — nested containers ───────────────────────────────────

    [Fact]
    public void Parser_Scope_RecursesIntoChildren()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Nested_HTTP_in_Scope");

        Assert.Equal(CallType.Http, edge.CallType);
        Assert.Equal("nested.api.example.com", edge.Target.Name);
    }

    [Fact]
    public void Parser_If_RecursesTrueBranch()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "HTTP_in_True_Branch");

        Assert.Equal(CallType.Http, edge.CallType);
        Assert.Equal("active.api.example.com", edge.Target.Name);
    }

    [Fact]
    public void Parser_If_RecursesElseBranch()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "HTTP_in_Else_Branch");

        Assert.Equal(CallType.Http, edge.CallType);
        Assert.Equal("inactive.api.example.com", edge.Target.Name);
    }

    [Fact]
    public void Parser_Foreach_RecursesIntoLoop()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "HTTP_in_Loop");

        Assert.Equal(CallType.Http, edge.CallType);
        Assert.Equal("items.api.example.com", edge.Target.Name);
    }

    [Fact]
    public void Parser_Switch_RecursesCases()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "HTTP_in_Switch_Case");

        Assert.Equal(CallType.Http, edge.CallType);
        Assert.Equal("orders.api.example.com", edge.Target.Name);
    }

    [Fact]
    public void Parser_Switch_RecursesDefault()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "HTTP_in_Switch_Default");

        Assert.Equal(CallType.Http, edge.CallType);
        Assert.Equal("default.api.example.com", edge.Target.Name);
    }

    // ── totals sanity check ───────────────────────────────────────────────────

    [Fact]
    public void Parser_ExtractsAllEdgesFromFixture()
    {
        // 7 top-level + 1 scope + 2 if/else + 1 foreach + 2 switch = 13 total
        var edges = ParseAllTypes();
        Assert.Equal(13, edges.Count);
    }

    [Fact]
    public void Parser_EmptyWorkflow_ReturnsNoEdges()
    {
        var (parser, connections) = Setup();
        var emptyDoc = JsonDocument.Parse("""{"definition":{"actions":{}}}""");
        var edges = parser.Parse(emptyDoc, connections);
        Assert.Empty(edges);
    }

    // ── ParametersParser ──────────────────────────────────────────────────────

    [Fact]
    public void Parameters_ParsesAppLevelParameters()
    {
        var paramsDoc = LoadFixture("parameters.json");
        var lookup = new ParametersParser().Parse(paramsDoc, null);
        Assert.True(lookup.TryGet("baseUrl", out var val));
        Assert.Equal("https://api.external.com", val);
    }

    [Fact]
    public void Parameters_ParsesWorkflowLevelParameters()
    {
        var wfDoc = JsonDocument.Parse("""
            {
              "definition": { "actions": {} },
              "parameters": {
                "serviceUrl": { "value": "https://wf.service.com" }
              },
              "kind": "Stateful"
            }
            """);
        var lookup = new ParametersParser().Parse(null, wfDoc);
        Assert.True(lookup.TryGet("serviceUrl", out var val));
        Assert.Equal("https://wf.service.com", val);
    }

    [Fact]
    public void Parameters_WorkflowOverridesAppLevel()
    {
        var appParams = JsonDocument.Parse("""{ "baseUrl": { "value": "https://app.level.com" } }""");
        var wfDoc = JsonDocument.Parse("""
            {
              "definition": { "actions": {} },
              "parameters": { "baseUrl": { "value": "https://wf.level.com" } },
              "kind": "Stateful"
            }
            """);
        var lookup = new ParametersParser().Parse(appParams, wfDoc);
        Assert.True(lookup.TryGet("baseUrl", out var val));
        Assert.Equal("https://wf.level.com", val);
    }

    [Fact]
    public void Parameters_SkipsDollarPrefixedKeys()
    {
        var doc = JsonDocument.Parse("""{ "$connections": { "value": {} } }""");
        var lookup = new ParametersParser().Parse(doc, null);
        Assert.Equal(0, lookup.Count);
    }

    // ── Expression resolution ─────────────────────────────────────────────────

    [Fact]
    public void Parser_Http_ExpressionUri_ResolvesWhenParameterKnown()
    {
        var paramsDoc = LoadFixture("parameters.json");
        var parameters = new ParametersParser().Parse(paramsDoc, null);
        var (parser, connections) = Setup();
        var edges = parser.Parse(LoadFixture("workflow-all-types.json"), connections, parameters);

        var edge = edges.Single(e => e.ActionName == "Call_API_via_Expression");
        // baseUrl = "https://api.external.com", so URI resolves to that host
        Assert.Equal(CallType.Http, edge.CallType);
        Assert.Equal("api.external.com", edge.Target.Name);
        Assert.Null(edge.Target.RawExpression); // fully resolved — no longer an expression
    }

    [Fact]
    public void Parser_Http_ExpressionUri_RemainsExpressionWithoutParameters()
    {
        var (parser, connections) = Setup();
        // No parameters passed — expression stays unresolved
        var edges = parser.Parse(LoadFixture("workflow-all-types.json"), connections);
        var edge = edges.Single(e => e.ActionName == "Call_API_via_Expression");

        Assert.NotNull(edge.Target.RawExpression);
        Assert.StartsWith("@", edge.Target.RawExpression);
    }

    [Fact]
    public void Parser_Http_AppsettingRef_ShowsKeyName()
    {
        // A parameter whose value is itself an @appsettings('KEY') reference
        var paramsDoc = LoadFixture("parameters.json"); // has "appsettingRef" → "@appsettings('SOME_API_URL')"
        var parameters = new ParametersParser().Parse(paramsDoc, null);
        var (parser, _) = Setup();

        var wfDoc = JsonDocument.Parse("""
            {
              "definition": {
                "actions": {
                  "Call_Appsetting_URL": {
                    "type": "Http",
                    "inputs": { "uri": "@{parameters('appsettingRef')}/data", "method": "GET" }
                  }
                }
              },
              "kind": "Stateful"
            }
            """);

        var edges = parser.Parse(wfDoc, ConnectionsLookup.Empty, parameters);
        var edge = edges.Single();
        // BestGuessFromExpression prefers the parameter name (most direct identifier in the raw URI)
        Assert.Equal("appsettingRef", edge.Target.Name);
        Assert.NotNull(edge.Target.RawExpression); // raw expression preserved for table display
    }
}
