using System.ComponentModel.DataAnnotations;

namespace FlowAtlas.Azure;

public sealed class FlowAtlasOptions
{
    public const string Section = "FlowAtlas";

    [Required(ErrorMessage = "FlowAtlas:SubscriptionId is required. Set it in appsettings.json or the FlowAtlas__SubscriptionId environment variable.")]
    public string SubscriptionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "FlowAtlas:ResourceGroup is required. Set it in appsettings.json or the FlowAtlas__ResourceGroup environment variable.")]
    public string ResourceGroup { get; set; } = string.Empty;

    public string HostRuntimeApiVersion { get; set; } = "2022-03-01";
    public string SnapshotPath { get; set; } = "data/snapshot.json";
}
