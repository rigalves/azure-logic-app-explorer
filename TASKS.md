# FlowAtlas — Implementation Tasks

## Phase 0: Azure Access Probe ← START HERE
Goal: verify the running identity can read every artifact we need **before** building the full app.
All checks must pass before moving to Phase 1.

- [x] 0.1 Scaffold minimal ASP.NET Core 8 Blazor Server project (`FlowAtlas`)
- [x] 0.2 Add NuGet packages: `Azure.Identity`, `Microsoft.Extensions.Azure`
- [x] 0.3 Wire `appsettings.json` with `SubscriptionId`, `ResourceGroup`, `HostRuntimeApiVersion`
- [x] 0.4 **Probe A — List Standard logic apps** (`Microsoft.Web/sites?kind=workflowapp`) ✅
- [x] 0.5 **Probe B — List workflows** via `hostruntime/runtime/webhooks/workflow/api/management/workflows` — returns bare array, not `{"value":[]}` ✅
- [x] 0.6 **Probe C — Read `workflow.json`** via VFS at `site/wwwroot/{wf}/workflow.json` (full path required) ✅
- [x] 0.7 **Probe D — Managed connections** via `Microsoft.Web/connections` ARM API ✅
- [x] 0.7b **Probe E — Read `connections.json`** via VFS at `site/wwwroot/connections.json` ✅
- [x] 0.8 `api-version=2022-03-01` confirmed working for all endpoints; locked in config

---

## Phase 1: Core Data Model & Parsing
- [x] 1.1 Define model types in `Model/Inventory.cs` (`Inventory`, `LogicAppInfo`, `WorkflowInfo`, `CallEdge`, `CallType`, `ExternalTarget`)
- [x] 1.2 `Parsing/ConnectionsParser.cs` — parses `connections.json` into lookup: ref name → `ConnectionInfo`
- [x] 1.3 `Parsing/WorkflowParser.cs` — recursively walks all action types including `Scope`/`If`/`Foreach`/`Until`/`Switch`
- [x] 1.4 Fixtures: `workflow-all-types.json` + `connections.json` covering all edge types
- [x] 1.5 20/20 parser unit tests passing (78ms, no Azure needed)

---

## Phase 2: Scan Service & Snapshot
- [x] 2.1 `Azure/AzureLogicAppClient.cs` — complete (list apps, list workflows, read workflow.json + connections.json, list managed connections)
- [x] 2.2 `Services/ScanService.cs` — IHostedService; ScanAsync orchestrates full RG scan, per-app error tracking, snapshot persistence
- [x] 2.3 Snapshot loaded from disk on startup; persisted after every scan
- [x] 2.4 `Filtering/InventoryFilter.cs` — filters by logic app, workflow, keyword (action name, target name, workflow name, CallType string)
- [x] 2.5 12 filter unit tests + 20 parser tests = 32 total, all passing

---

## Phase 3: Mermaid Builder
- [x] 3.1 `Services/MermaidBuilder.cs` — Summary + Detail modes; target nodes deduplicated across diagram; 8 CSS classes (Salesforce distinct blue); node IDs sanitized; multi-app detail shows parent app subtitle
- [x] 3.2 16 Mermaid builder tests (structural assertions, not string snapshots) — 48 total unit tests passing

---

## Phase 4: Blazor UI
- [x] 4.1 `wwwroot/js/mermaid-interop.js` — init + render + copyToClipboard + downloadFile; Mermaid 11 from CDN
- [x] 4.2 `Components/Pages/Explorer.razor` — Scan button + spinner + last-scan metadata; Logic App dropdown; dependent Workflow dropdown; keyword filter; Summary/Detail toggle; Mermaid pane via JS interop; re-renders only when diagram changes
- [x] 4.3 Copy .mmd + Download .mmd buttons with timestamped filename
- [x] 4.4 Collapsible raw inventory table (Logic App / Workflow / Action / Type badge / Target + raw expression)
- [x] 4.5 Scan error alert showing per-app and per-workflow failures

---

## Phase 5: Polish & Hardening
- [x] 5.1 Expression URIs rendered as `⟨@expr…⟩` in diagrams (truncated at 30 chars); table shows `⟨dynamic⟩` badge + full expression with word-break; `title` attribute for hover
- [x] 5.2 `[Required]` on SubscriptionId + ResourceGroup; `ValidateDataAnnotations().ValidateOnStart()` — app refuses to start with a clear message if config is missing
- [x] 5.3 `ILogger<AzureLogicAppClient>` added; every ARM request logged at Debug (label + ms); errors logged at Warning with URL + body
- [x] 5.4 README.md — prerequisites, RBAC, config, az login + SP auth, usage, architecture diagram, testing, known limitations
