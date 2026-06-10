# wtrfll.AzureLogicAppExplorer

A .NET 8 Blazor Server app that scans an Azure Resource Group for **Standard Logic Apps**, inventories every outbound call, and renders filterable **Mermaid.js relationship diagrams**.

Inventoried call types:
- **HTTP / API calls** — outbound `Http` and `HttpWebhook` actions; literal hosts are resolved, `@expression` URIs are shown as `⟨dynamic⟩`
- **Azure Function calls** — resolves the target Function App name
- **Salesforce** — flagged distinctly from other managed connectors (blue node, distinct style)
- **Managed connectors** — `ApiConnection` / `ApiConnectionWebhook` (Office 365, SharePoint, Teams, SAP, ServiceNow, Dynamics, …)
- **Built-in service providers** — Service Bus, Azure Blob, SQL Server, Event Hubs, Cosmos DB, …
- **Child workflow calls** — workflow-to-workflow invocations within a Logic App

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- One of:
  - **Azure CLI** (`az login`) — easiest for local dev
  - **Environment variables** for a Service Principal (see below)
  - **Managed Identity** — when running in Azure

---

## Required Azure RBAC

The identity running wtrfll.AzureLogicAppExplorer needs at least **Reader** on the target Resource Group — workflow listing and `workflow.json` are read via the ARM `Microsoft.Web/sites/workflows` resource (the same one the Portal Designer uses), which Reader covers.

**Contributor** (or higher) is recommended: `connections.json` and `parameters.json` are still read via the `hostruntime/admin/vfs` Kudu proxy, which requires Contributor-level access. Without it, those files are skipped (a warning is logged) and managed-connector targets / `@parameters(...)` resolution will be less complete for that app, but the scan still completes and the app's workflows still appear in the diagram.

---

## Configuration

Edit `wtrfll.AzureLogicAppExplorer/appsettings.json`:

```json
{
  "wtrfll.AzureLogicAppExplorer": {
    "SubscriptionId": "your-subscription-guid",
    "ResourceGroups": ["your-resource-group-name", "another-resource-group-name"],
    "HostRuntimeApiVersion": "2022-03-01",
    "SnapshotPath": "data/snapshot.json"
  }
}
```

A scan combines the Logic Apps found across all listed resource groups (within the same subscription) into a single inventory.

Or use environment variables (useful for CI / containerised deployments):

```
wtrfll.AzureLogicAppExplorer__SubscriptionId=<guid>
wtrfll.AzureLogicAppExplorer__ResourceGroups__0=<rg-name>
wtrfll.AzureLogicAppExplorer__ResourceGroups__1=<another-rg-name>
```

### Service Principal (alternative to az login)

```powershell
$env:AZURE_TENANT_ID     = "your-tenant-guid"
$env:AZURE_CLIENT_ID     = "your-app-registration-client-id"
$env:AZURE_CLIENT_SECRET = "your-client-secret"
```

---

## Running

```bash
az login
dotnet run --project wtrfll.AzureLogicAppExplorer
```

Open the URL printed in the console (e.g. `http://localhost:5148`), then click **⟳ Scan**.

### VS Code (F5)

`.vscode/launch.json` and `.vscode/tasks.json` are included. Press **F5** to build and launch with the debugger attached; the browser opens automatically.

---

## Usage

1. **Scan** — Pulls all Standard Logic Apps from the configured Resource Group. Runs up to 5 apps in parallel. A progress bar shows scan status. Results are cached to `data/snapshot.json` and reloaded on next startup.

2. **Filter** — Use the three-column filter bar to scope the diagram:
   - **Logic Apps** — searchable multi-select checkbox list
   - **Workflows** — searchable multi-select, grouped under parent Logic App; updates when app selection changes
   - **Keyword** — matches against workflow name, action name, target name, and call type

3. **Render Diagram** — Click **▶ Render Diagram** to generate the Mermaid diagram for the current filter selection.
   - **Summary** — Logic App → unique external target nodes (one edge per unique app→target pair)
   - **Detail** — Workflow → target nodes with action-name edge labels

4. **Export** — Copy or download the raw `.mmd` source for use in any Mermaid-compatible tool (GitHub, Notion, VS Code Mermaid extension, etc.)

5. **Azure Access Probe** — Navigate to `/probe` to run five diagnostic checks that verify your identity has all required Azure permissions before scanning.

---

## Architecture

```
wtrfll.AzureLogicAppExplorer/
├── Azure/
│   ├── AzureLogicAppClient.cs   # ARM REST client (no Kudu needed)
│   └── AppOptions.cs      # Config + startup validation
├── Parsing/
│   ├── WorkflowParser.cs        # Recursive action-tree walker
│   └── ConnectionsParser.cs     # connections.json → lookup
├── Services/
│   ├── ScanService.cs           # Orchestrates parallel scan + snapshot
│   └── MermaidBuilder.cs        # Inventory → Mermaid flowchart LR
├── Filtering/
│   └── InventoryFilter.cs       # App / workflow / keyword filter
├── Model/
│   └── Inventory.cs             # Domain types
└── Components/
    └── Pages/
        ├── Explorer.razor        # Main UI
        └── Probe.razor           # Azure access probe

wtrfll.AzureLogicAppExplorer.Tests/                 # Unit tests (parser + filter + Mermaid) — no Azure needed
wtrfll.AzureLogicAppExplorer.IntegrationTests/      # Azure access probes (requires az login + config)
```

---

## Testing

```bash
# Unit tests (no Azure required)
dotnet test wtrfll.AzureLogicAppExplorer.Tests

# Integration tests (requires az login + appsettings configured)
dotnet test wtrfll.AzureLogicAppExplorer.IntegrationTests --filter "Category=Integration" -v normal
```

---

## Known limitations

- `@parameters(...)` and `@appsettings(...)` URIs cannot be resolved at scan time — they appear as `⟨dynamic⟩` in diagrams and as the raw expression in the inventory table.
- The ARM `hostruntime/admin/vfs` endpoint is used to read `workflow.json` and `connections.json`. These calls only succeed while the app is running, so apps with `properties.state != "Running"` are skipped during scan and shown in the diagram as a greyed-out, dashed "⏸ Stopped" node with no workflows.
- Large diagrams (many workflows, many edges) can be slow to render in Mermaid. Use the filter bar to scope down before clicking Render.
