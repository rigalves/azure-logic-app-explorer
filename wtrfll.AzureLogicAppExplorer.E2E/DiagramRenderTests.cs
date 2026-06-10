using System.Text.Json;
using Microsoft.Playwright;

namespace wtrfll.AzureLogicAppExplorer.E2E;

/// <summary>
/// Drives the running Explorer page in a real browser to verify diagram rendering
/// for a set of Logic Apps end-to-end.
///
/// Prerequisites (not run as part of `dotnet test` by default):
///   1. Start the app:            dotnet run --project wtrfll.AzureLogicAppExplorer
///   2. Install Chromium once:    pwsh wtrfll.AzureLogicAppExplorer.E2E/bin/Debug/net8.0/playwright.ps1 install chromium
///   3. Set env vars and run:
///        $env:E2E_APPS = "lapp-foo-dev-01,lapp-bar-dev-01"
///        dotnet test wtrfll.AzureLogicAppExplorer.E2E
///
/// E2E_APPS (required, comma-separated Logic App names) is intentionally read from
/// the environment rather than hardcoded, so real app names never land in source control.
/// E2E_BASE_URL (optional) defaults to http://localhost:5033.
/// </summary>
public class DiagramRenderTests : IAsyncLifetime
{
    private static readonly HashSet<string> KnownTypeClasses =
    [
        "logicapp", "workflow", "http", "funcapp", "salesforce",
        "managed", "serviceprovider", "servicebus", "childwf", "keyvault",
    ];

    private static readonly string[] PlaceholderStrings =
    [
        "unknown-function-app", "unknown-connection", "unknown-service-provider", "unknown-workflow",
    ];

    private readonly string _baseUrl;
    private readonly string[] _appNames;
    private readonly List<string> _consoleErrors = [];

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage _page = null!;

    public DiagramRenderTests()
    {
        _baseUrl = Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5033";
        var apps = Environment.GetEnvironmentVariable("E2E_APPS");
        _appNames = string.IsNullOrWhiteSpace(apps)
            ? []
            : apps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task InitializeAsync()
    {
        // Skip launching a browser entirely when the test has nothing to do —
        // the Fact below raises the actual SkipException once xUnit reports the result.
        if (_appNames.Length == 0) return;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
        _page = await _browser.NewPageAsync(new() { ViewportSize = new() { Width = 1600, Height = 1200 } });
        _page.Console += (_, msg) => { if (msg.Type == "error") _consoleErrors.Add(msg.Text); };
        _page.PageError += (_, error) => _consoleErrors.Add(error);
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task SelectedApps_RenderDiagram_ProducesValidNodes()
    {
        Skip.If(_appNames.Length == 0,
            "Set E2E_APPS (comma-separated Logic App names) and run the app at E2E_BASE_URL " +
            "(default http://localhost:5033) to run this test. See class doc comment for details.");

        await _page.GotoAsync($"{_baseUrl}/explorer", new() { WaitUntil = WaitUntilState.NetworkIdle });

        foreach (var app in _appNames)
        {
            var checkbox = _page.Locator($"#app_{app}");
            await Assertions.Expect(checkbox).ToBeVisibleAsync(new() { Timeout = 30000 });
            await checkbox.CheckAsync();
        }

        // Workflows panel should now show only the selected apps' headers.
        var headers = await _page.Locator("div.text-muted.small.fw-semibold.mt-1.px-1").AllInnerTextsAsync();
        Assert.Equal(_appNames.OrderBy(a => a, StringComparer.Ordinal),
            headers.Distinct().OrderBy(h => h, StringComparer.Ordinal));

        // Use the Detail view so the diagram includes per-action nodes/edges, not just
        // the per-app summary, since that's what most regressions actually affect.
        // The radio is a visually-hidden Bootstrap btn-check, so click its label.
        await _page.Locator("label[for=modeDetail]").ClickAsync();
        await Assertions.Expect(_page.Locator("#modeDetail")).ToBeCheckedAsync();

        await _page.Locator("button:has-text(\"Render Diagram\")").ClickAsync();
        await _page.Locator("#mermaid-diagram svg").WaitForAsync(new() { Timeout = 30000 });
        await _page.WaitForTimeoutAsync(500);

        var nodes = await ExtractNodes();
        Assert.NotEmpty(nodes);

        foreach (var node in nodes)
        {
            var classes = node.Classes;
            var typeClass = classes.FirstOrDefault(KnownTypeClasses.Contains);
            Assert.True(typeClass is not null,
                $"Node '{node.Id}' has no recognized type class: [{string.Join(",", classes)}]");

            foreach (var placeholder in PlaceholderStrings)
                Assert.DoesNotContain(placeholder, node.Text);
        }

        var outDir = Path.Combine(AppContext.BaseDirectory, "e2e-output");
        Directory.CreateDirectory(outDir);
        await _page.Locator("#mermaid-diagram").ScreenshotAsync(
            new() { Path = Path.Combine(outDir, "diagram.png") });

        // Key Vault is hidden by default — its edges must be hidden too, otherwise
        // the diagram shows dangling arrows pointing at nothing.
        await AssertNoDanglingEdges();

        await VerifyLegendToggle("keyvault", nodes, defaultChecked: false);
        await VerifyLegendToggle("http", nodes, defaultChecked: true);

        Assert.Empty(_consoleErrors);
    }

    private async Task VerifyLegendToggle(string className, IReadOnlyList<NodeInfo> nodes, bool defaultChecked)
    {
        if (!nodes.Any(n => n.Classes.Contains(className)))
            return;

        var legendCheckbox = _page.Locator($"#legend_{className}");
        var nodeLocator = _page.Locator($"#mermaid-diagram svg .node.{className}").First;

        if (defaultChecked)
            await Assertions.Expect(legendCheckbox).ToBeCheckedAsync();
        else
            await Assertions.Expect(legendCheckbox).Not.ToBeCheckedAsync();

        // Toggle off
        if (defaultChecked)
            await legendCheckbox.UncheckAsync();
        else
            await legendCheckbox.CheckAsync();
        await AssertNodeDisplay(nodeLocator, hidden: defaultChecked);
        await AssertNoDanglingEdges();

        // Toggle back to original state
        if (defaultChecked)
            await legendCheckbox.CheckAsync();
        else
            await legendCheckbox.UncheckAsync();
        await AssertNodeDisplay(nodeLocator, hidden: !defaultChecked);
        await AssertNoDanglingEdges();
    }

    // A "dangling edge" is a visible edge path/label connected to a node that's
    // currently hidden via the legend filter — i.e. an arrow pointing at nothing.
    private async Task AssertNoDanglingEdges()
    {
        var dangling = await _page.EvaluateAsync<JsonElement>("""
            () => {
                const svg = document.querySelector('#mermaid-diagram svg');
                const hiddenIds = [];
                svg.querySelectorAll('.node').forEach(n => {
                    if (getComputedStyle(n).display === 'none') {
                        const m = n.id.match(/flowchart-(.+)-\d+$/);
                        if (m) hiddenIds.push(m[1]);
                    }
                });

                const result = [];
                svg.querySelectorAll('.edgePaths > path, .edgeLabels > .edgeLabel, .edge').forEach(e => {
                    if (getComputedStyle(e).display === 'none') return;
                    if (hiddenIds.some(id => (e.id || '').includes(id)))
                        result.push(e.id);
                });
                return result;
            }
            """);

        var danglingIds = dangling.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.True(danglingIds.Count == 0,
            $"Found {danglingIds.Count} edge(s) connected to hidden nodes that are still visible: " +
            string.Join(", ", danglingIds));
    }

    // Playwright's ToBeHidden/ToBeVisible visibility heuristics don't reliably
    // detect display:none on SVG <g> elements in headless Chromium, so check
    // the computed style directly with manual polling instead.
    private async Task AssertNodeDisplay(ILocator nodeLocator, bool hidden)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string display;
        do
        {
            display = await nodeLocator.EvaluateAsync<string>("n => getComputedStyle(n).display");
            if (hidden == (display == "none")) return;
            await _page.WaitForTimeoutAsync(100);
        } while (DateTime.UtcNow < deadline);

        Assert.Fail($"Expected node display to be {(hidden ? "'none'" : "not 'none'")}, but was '{display}'");
    }

    private async Task<IReadOnlyList<NodeInfo>> ExtractNodes()
    {
        var json = await _page.EvaluateAsync<JsonElement>("""
            () => {
                const svg = document.querySelector('#mermaid-diagram svg');
                const result = [];
                svg.querySelectorAll('.node').forEach(n => {
                    const text = (n.textContent || '').trim().replace(/\s+/g, ' ');
                    result.push({ id: n.id, classes: [...n.classList], text });
                });
                return result;
            }
            """);

        return [.. json.EnumerateArray().Select(e => new NodeInfo(
            e.GetProperty("id").GetString() ?? "",
            [.. e.GetProperty("classes").EnumerateArray().Select(c => c.GetString() ?? "")],
            e.GetProperty("text").GetString() ?? ""))];
    }

    private sealed record NodeInfo(string Id, IReadOnlyList<string> Classes, string Text);
}
