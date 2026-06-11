# Flow Atlas — Domain Context

Domain vocabulary for Flow Atlas (the Azure Logic App &amp; Service Bus Explorer). Use these terms
exactly in code, tests, and architecture discussion.

## Inventory domain

- **Logic App** — a Standard Logic App (`Microsoft.Web/sites`, `kind=workflowapp`). Has a name, a
  running/stopped state, and a set of Workflows. Modelled by `LogicAppInfo`.
- **Workflow** — one workflow inside a Logic App. Has a Trigger, a Classification, an optional
  Domain tag, and a list of outbound Call Edges. Modelled by `WorkflowInfo`.
- **Call Edge** — one outbound call extracted from an action in a workflow (`CallEdge`): the action
  name, its **Call Type**, and its **External Target**.
- **Call Type** — what kind of thing a call reaches (`CallType`): Http, Function, Salesforce,
  ManagedConnector, ServiceProvider, ServiceBus, ChildWorkflow, KeyVault, Unknown.
- **External Target** — the resolved (or best-effort) destination of a Call Edge (`ExternalTarget`):
  host / function app / connector / workflow name, plus an optional path and raw expression.
- **Trigger** — how a workflow is started (`TriggerInfo`): kind, optional Service Bus entity
  name/kind, optional HTTP method.
- **Service Bus Topic** — an Azure Service Bus topic and its child subscriptions
  (`ServiceBusTopicInfo`).
- **Inventory** — the full scan result: all Logic Apps, their Workflows and Call Edges, the Service
  Bus Topics, scan errors, and a timestamp. Persisted as a **Snapshot**.
- **Inventory Selection** — what the user has chosen to view (`InventorySelection`): a set of
  Logic App names, a set of workflow keys (`appName||wfName`, see `WorkflowKey`), and/or a
  free-text keyword. `InventorySelection.All` is unfiltered. `InventoryFilter.Apply` is the one
  seam that turns a Selection into a narrowed Inventory — the Explorer view, the table, the
  diagram, and the tests all cross it the same way.

## Diagram presentation

- **Node Kind** — the kind of node a diagram can draw (`NodeKind`). Eight kinds mirror the Call
  Types that produce an outbound target node; three are structural roles that are **not** Call Types:
  **Logic App**, **Workflow**, and **Trigger Source**. `CallType.Unknown` folds to the Http node kind.
- **Node Style** — the visual identity of one Node Kind (`NodeStyle`): Mermaid class, fill colour,
  text/stroke colours, legend label, node subtitle, and Bootstrap badge class.
- **Diagram Palette** — the single source of truth mapping each Node Kind to its Node Style
  (`DiagramPalette`). The Mermaid builder, the legend, and the inventory table all read from it, so a
  node kind's colour is defined exactly once — the legend swatch and the generated Mermaid `classDef`
  fill are the same field and cannot drift apart.
