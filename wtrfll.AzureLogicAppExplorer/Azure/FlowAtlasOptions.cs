using System.ComponentModel.DataAnnotations;

namespace wtrfll.AzureLogicAppExplorer.Azure;

public sealed class AppOptions
{
    public const string Section = "AzureLogicAppExplorer";

    [Required(ErrorMessage = "AzureLogicAppExplorer:SubscriptionId is required. Set it in appsettings.json or the AzureLogicAppExplorer__SubscriptionId environment variable.")]
    public string SubscriptionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "AzureLogicAppExplorer:ResourceGroup is required. Set it in appsettings.json or the AzureLogicAppExplorer__ResourceGroup environment variable.")]
    public string ResourceGroup { get; set; } = string.Empty;

    public string HostRuntimeApiVersion { get; set; } = "2022-03-01";
    public string SnapshotPath { get; set; } = "data/snapshot.json";
}
