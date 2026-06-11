using wtrfll.AzureLogicAppExplorer.Model;
using wtrfll.AzureLogicAppExplorer.Parsing;
using System.Text.Json;

namespace wtrfll.AzureLogicAppExplorer.Tests;

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
    public void Parser_Function_UnresolvedConnection_FallsBackToActionName()
    {
        var (parser, connections) = Setup();
        var doc = JsonDocument.Parse("""
            {
              "definition": {
                "actions": {
                  "Call_Orphaned_Function": {
                    "type": "Function",
                    "inputs": {
                      "function": { "connectionName": "doesNotExist" },
                      "method": "POST"
                    }
                  }
                }
              }
            }
            """);

        var edges = parser.Parse(doc, connections);
        var edge = edges.Single(e => e.ActionName == "Call_Orphaned_Function");

        Assert.Equal(CallType.Function, edge.CallType);
        // No matching functionConnections entry — fall back to the action name
        // instead of a meaningless "unknown-function-app" placeholder.
        Assert.Equal("Call_Orphaned_Function", edge.Target.Name);
    }

    [Fact]
    public void Parser_Http_ExtractsMethod()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Call_External_API");

        Assert.Equal("POST", edge.Method);
    }

    [Fact]
    public void Parser_ApiConnection_ExtractsMethod_Uppercased()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Sync_to_Salesforce");

        // Fixture has lowercase "post" — should be normalized to uppercase
        Assert.Equal("POST", edge.Method);
    }

    [Fact]
    public void ParseTrigger_Http_WithMethod_SetsMethodAndDisplayType()
    {
        var (parser, connections) = Setup();
        var doc = JsonDocument.Parse("""
            {
              "definition": {
                "triggers": {
                  "When_HTTP_request_received": {
                    "type": "Request",
                    "kind": "Http",
                    "inputs": { "method": "POST" }
                  }
                },
                "actions": {}
              }
            }
            """);

        var trigger = parser.ParseTrigger(doc, connections);

        Assert.NotNull(trigger);
        Assert.Equal("POST", trigger.Method);
        Assert.Equal("API (POST)", trigger.DisplayType);
    }

    [Fact]
    public void Parser_ApiConnection_Salesforce_IsDistinct()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Sync_to_Salesforce");

        Assert.Equal(CallType.Salesforce, edge.CallType);
        // Salesforce's display name is generic, so the action name is used as the
        // node title to keep distinct Salesforce operations distinguishable.
        Assert.Equal("Sync_to_Salesforce", edge.Target.Name);
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
    public void Parser_ServiceProvider_ServiceBus_ExtractsEntityName()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Send_Service_Bus_Message");

        Assert.Equal(CallType.ServiceBus, edge.CallType);
        Assert.Equal("orders-queue", edge.Target.Name);
    }

    [Fact]
    public void Parser_ServiceProvider_ServiceBus_ExtractsSendOperation()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Send_Service_Bus_Message");

        Assert.Equal("Send", edge.Operation);
    }

    [Theory]
    [InlineData("peekLockTopicMessagesV2", "Receive (Peek-Lock)")]
    [InlineData("completeMessage", "Complete")]
    [InlineData("abandonMessage", "Abandon")]
    [InlineData("deadletterMessage", "Dead-letter")]
    [InlineData("renewLock", "Renew Lock")]
    public void Parser_ServiceProvider_ServiceBus_MapsOperationLabels(string operationId, string expectedLabel)
    {
        var (parser, connections) = Setup();
        var doc = JsonDocument.Parse($$"""
            {
              "definition": {
                "actions": {
                  "Sb_Action": {
                    "type": "ServiceProvider",
                    "inputs": {
                      "parameters": { "entityName": "orders-queue" },
                      "serviceProviderConfiguration": {
                        "connectionName": "serviceBusConn",
                        "operationId": "{{operationId}}",
                        "serviceProviderId": "/serviceProviders/serviceBus"
                      }
                    }
                  }
                }
              }
            }
            """);

        var edges = parser.Parse(doc, connections);
        var edge = edges.Single(e => e.ActionName == "Sb_Action");

        Assert.Equal(expectedLabel, edge.Operation);
    }

    [Fact]
    public void Parser_ServiceProvider_KeyVault_IsClassifiedAsKeyVault()
    {
        var (parser, connections) = Setup();
        var doc = JsonDocument.Parse("""
            {
              "definition": {
                "actions": {
                  "Get_Secret": {
                    "type": "ServiceProvider",
                    "inputs": {
                      "parameters": { "secretName": "db-password" },
                      "serviceProviderConfiguration": {
                        "connectionName": "keyVaultConn",
                        "operationId": "getSecret",
                        "serviceProviderId": "/serviceProviders/keyVault"
                      }
                    }
                  }
                }
              }
            }
            """);

        var edges = parser.Parse(doc, connections);
        var edge = edges.Single(e => e.ActionName == "Get_Secret");

        Assert.Equal(CallType.KeyVault, edge.CallType);
        Assert.Equal("Key Vault", edge.Target.Name);
        Assert.Equal("Get Secret", edge.Operation);
    }

    [Fact]
    public void Parser_Http_LiteralUri_ExtractsPath()
    {
        var edges = ParseAllTypes();
        var edge = edges.Single(e => e.ActionName == "Call_External_API");

        Assert.Equal("api.contoso.com", edge.Target.Name);
        Assert.Equal("/orders", edge.Target.Path);
    }

    [Fact]
    public void ParseTrigger_ServiceBus_ExtractsQueueName()
    {
        var (parser, connections) = Setup();
        var doc = LoadFixture("workflow-sb-triggered.json");
        var trigger = parser.ParseTrigger(doc, connections);

        Assert.NotNull(trigger);
        Assert.Equal("ServiceBus", trigger.Kind);
        Assert.Equal("orders-queue", trigger.EntityName);
    }

    [Fact]
    public void ParseTrigger_Http_ReturnsHttpKind()
    {
        var (parser, connections) = Setup();
        var doc = LoadFixture("workflow-all-types.json");
        var trigger = parser.ParseTrigger(doc, connections);

        Assert.NotNull(trigger);
        Assert.Equal("Http", trigger.Kind);
        Assert.Null(trigger.EntityName);
    }

    [Fact]
    public void ParseTrigger_NoTriggers_ReturnsNull()
    {
        var (parser, connections) = Setup();
        var doc = System.Text.Json.JsonDocument.Parse(@"{""definition"":{""actions"":{}}}");
        var trigger = parser.ParseTrigger(doc, connections);

        Assert.Null(trigger);
    }

    [Fact]
    public void ParseTrigger_ServiceBusTopic_SetsEntityKindAndDisplayType()
    {
        var (parser, connections) = Setup();
        var doc = LoadFixture("workflow-topic-with-domain.json");
        var trigger = parser.ParseTrigger(doc, connections);

        Assert.NotNull(trigger);
        Assert.Equal("ServiceBus", trigger.Kind);
        Assert.Equal("orders-created-topic", trigger.EntityName);
        Assert.Equal("Topic", trigger.EntityKind);
        Assert.Equal("Topic Event", trigger.DisplayType);
        Assert.Equal("orders-created-topic", trigger.Source);
    }

    [Fact]
    public void ParseTrigger_ServiceBusTopic_AppSettingTopicName_FallsBackToPlaceholder()
    {
        var (parser, connections) = Setup();
        var doc = LoadFixture("workflow-topic-appsetting.json");
        var trigger = parser.ParseTrigger(doc, connections);

        Assert.NotNull(trigger);
        Assert.Equal("ServiceBus", trigger.Kind);
        Assert.Equal("Topic", trigger.EntityKind);
        Assert.Equal("<appsetting:ServiceBusTopicName>", trigger.EntityName);
        Assert.Equal("<appsetting:ServiceBusTopicName>", trigger.Source);
        Assert.False(trigger.HasResolvedEntityName);
    }

    [Fact]
    public void Parser_ServiceBusAction_UnresolvedParameterTopicName_FallsBackToGenericServiceProvider()
    {
        // When an action's topicName is an @parameters() reference that can't be resolved,
        // it must NOT be classified as CallType.ServiceBus with a placeholder name — that
        // would merge it with unrelated SB nodes that share the same unresolved placeholder.
        var (parser, connections) = Setup();
        var doc = LoadFixture("workflow-sb-action-unresolved.json");

        var edges = parser.Parse(doc, connections);
        var edge = edges.Single(e => e.ActionName == "Send_To_Topic");

        Assert.Equal(CallType.ServiceProvider, edge.CallType);
        Assert.Equal("Service Bus", edge.Target.Name);
    }

    [Theory]
    [InlineData("Http", null, "API")]
    [InlineData("ApiConnection", null, "API Connector")]
    [InlineData("Recurrence", null, "Timer (Recurrence)")]
    [InlineData("ServiceBus", "Queue", "Queue Event")]
    public void TriggerInfo_DisplayType_MapsToFriendlyLabel(string kind, string? entityKind, string expected)
    {
        var trigger = new TriggerInfo(kind, null, entityKind);
        Assert.Equal(expected, trigger.DisplayType);
    }

    [Fact]
    public void TriggerInfo_Source_FallsBackToGenericDescriptionWhenNoEntityName()
    {
        Assert.Equal("External caller", new TriggerInfo("Http", null).Source);
        Assert.Equal("Schedule", new TriggerInfo("Recurrence", null).Source);
        Assert.Equal("Unknown", new TriggerInfo("ServiceProvider", null).Source);
    }

    // ── ParseDomain ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseDomain_MetadataPresent_ReturnsXEspDomain()
    {
        var (parser, _) = Setup();
        var doc = LoadFixture("workflow-topic-with-domain.json");

        Assert.Equal("orders", parser.ParseDomain(doc));
    }

    [Fact]
    public void ParseDomain_NoMetadata_ReturnsNull()
    {
        var (parser, _) = Setup();
        var doc = LoadFixture("workflow-all-types.json");

        Assert.Null(parser.ParseDomain(doc));
    }

    // ── ClassifyWorkflow ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("wf-orders-created-publisher", "Http", WorkflowClassification.Pub)]
    [InlineData("wf-orders-created-subscriber", "ServiceBus", WorkflowClassification.Sub)]
    [InlineData("wf-orders-create-session", "Http", WorkflowClassification.Facade)]
    [InlineData("wf-orders-internal-helper", "Recurrence", WorkflowClassification.Other)]
    public void ClassifyWorkflow_UsesNameSuffixThenTriggerKind(string name, string triggerKind, WorkflowClassification expected)
    {
        var trigger = new TriggerInfo(triggerKind, null);
        Assert.Equal(expected, WorkflowParser.ClassifyWorkflow(name, trigger));
    }

    [Fact]
    public void ClassifyWorkflow_NoTrigger_DefaultsToOther()
    {
        Assert.Equal(WorkflowClassification.Other, WorkflowParser.ClassifyWorkflow("wf-some-workflow", null));
    }

    [Theory]
    [InlineData("lapp-eus2-nsrv-diagnosis-entry-pub-prod-01", WorkflowClassification.Pub)]
    [InlineData("lapp-eus2-nsrv-diagnosis-entry-sub-prod-01", WorkflowClassification.Sub)]
    [InlineData("wf-esp-diagnosis-entry-msp-dx-info-publish", WorkflowClassification.Pub)]
    [InlineData("wf-esp-diagnosis-entry-msp-dx-info-subscribe", WorkflowClassification.Sub)]
    public void ClassifyWorkflow_PubSubSegmentInName_ClassifiesAccordingly(string name, WorkflowClassification expected)
    {
        // Recurrence trigger so the only signal is the "pub"/"sub" name segment
        var trigger = new TriggerInfo("Recurrence", null);
        Assert.Equal(expected, WorkflowParser.ClassifyWorkflow(name, trigger));
    }

    [Theory]
    [InlineData("lapp-esp-order-updates-pub-prod-01", WorkflowClassification.Pub)]
    [InlineData("lapp-esp-order-updates-sub-prod-01", WorkflowClassification.Sub)]
    public void ClassifyWorkflow_PubSubSegmentInLogicAppName_ClassifiesAccordingly(string logicAppName, WorkflowClassification expected)
    {
        // Workflow name itself has no pub/sub clue — only the parent Logic App name does
        var trigger = new TriggerInfo("ServiceBus", "sb-topic-esp-rpt-accession-msg", "Topic");
        Assert.Equal(expected, WorkflowParser.ClassifyWorkflow("wf-map-accession-to-order-canonical", trigger, logicAppName));
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
