using wtrfll.AzureLogicAppExplorer.Model;
using wtrfll.AzureLogicAppExplorer.Services;

namespace wtrfll.AzureLogicAppExplorer.Tests;

public class DiagramPaletteTests
{
    public static IEnumerable<object[]> AllNodeKinds =>
        Enum.GetValues<NodeKind>().Select(k => new object[] { k });

    public static IEnumerable<object[]> AllCallTypes =>
        Enum.GetValues<CallType>().Select(t => new object[] { t });

    // ── Completeness ────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllNodeKinds))]
    public void EveryNodeKind_HasAStyle(NodeKind kind)
    {
        var style = DiagramPalette.For(kind);
        Assert.Equal(kind, style.Kind);
        Assert.False(string.IsNullOrWhiteSpace(style.MermaidClass));
        Assert.False(string.IsNullOrWhiteSpace(style.Fill));
        Assert.False(string.IsNullOrWhiteSpace(style.LegendLabel));
    }

    [Theory]
    [MemberData(nameof(AllCallTypes))]
    public void EveryCallType_ResolvesToATargetStyle(CallType type)
    {
        var style = DiagramPalette.For(type);
        // Target kinds always carry a node subtitle and a table badge class.
        Assert.False(string.IsNullOrWhiteSpace(style.NodeSubtitle));
        Assert.False(string.IsNullOrWhiteSpace(style.BadgeClass));
    }

    [Fact]
    public void All_ContainsEveryNodeKind_Once()
    {
        var kinds = DiagramPalette.All.Select(s => s.Kind).ToList();
        Assert.Equal(Enum.GetValues<NodeKind>().Length, kinds.Count);
        Assert.Equal(kinds.Distinct().Count(), kinds.Count);
    }

    [Fact]
    public void MermaidClassNames_AreUnique()
    {
        var classes = DiagramPalette.All.Select(s => s.MermaidClass).ToList();
        Assert.Equal(classes.Distinct(StringComparer.Ordinal).Count(), classes.Count);
    }

    // ── The invariant that used to be hand-maintained ───────────────────────────

    [Fact]
    public void LegendSwatchColour_And_GeneratedClassDefFill_CannotDrift()
    {
        // The legend swatch renders style.Fill; the Mermaid classDef must use the same value.
        // Because both read the one Fill field, this can never silently diverge again.
        var classDefs = DiagramPalette.ClassDefs();
        foreach (var s in DiagramPalette.All)
            Assert.Contains($"classDef {s.MermaidClass} fill:{s.Fill}", classDefs);
    }

    [Fact]
    public void ClassDefs_DeclareEveryNodeKind()
    {
        var classDefs = DiagramPalette.ClassDefs();
        foreach (var s in DiagramPalette.All)
            Assert.Contains($"classDef {s.MermaidClass} ", classDefs);
    }

    // ── Behaviour preserved from the old dictionaries ────────────────────────────

    [Fact]
    public void Unknown_FoldsToHttpNodeKind()
    {
        Assert.Equal(NodeKind.Http, DiagramPalette.KindOf(CallType.Unknown));
    }

    [Fact]
    public void Http_LegendLabel_And_NodeSubtitle_DifferAsBefore()
    {
        var http = DiagramPalette.For(NodeKind.Http);
        Assert.Equal("HTTP / API", http.LegendLabel);  // legend row text
        Assert.Equal("HTTP", http.NodeSubtitle);        // diagram node subtitle
    }

    [Fact]
    public void StructuralKinds_HaveNoSubtitleOrBadge()
    {
        foreach (var kind in new[] { NodeKind.LogicApp, NodeKind.Workflow, NodeKind.TriggerSource })
        {
            var style = DiagramPalette.For(kind);
            Assert.Null(style.NodeSubtitle);
            Assert.Null(style.BadgeClass);
        }
    }
}
