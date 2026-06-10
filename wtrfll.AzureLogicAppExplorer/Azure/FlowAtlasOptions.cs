using System.ComponentModel.DataAnnotations;

namespace wtrfll.AzureLogicAppExplorer.Azure;

public sealed class AppOptions
{
    public const string Section = "AzureLogicAppExplorer";

    [Required(ErrorMessage = "AzureLogicAppExplorer:SubscriptionId is required. Set it in appsettings.json or the AzureLogicAppExplorer__SubscriptionId environment variable.")]
    public string SubscriptionId { get; set; } = string.Empty;

    [Required(ErrorMessage = "AzureLogicAppExplorer:ResourceGroups is required. Set it in appsettings.json or the AzureLogicAppExplorer__ResourceGroups__0, __1, ... environment variables.")]
    [MinLength(1, ErrorMessage = "AzureLogicAppExplorer:ResourceGroups must contain at least one resource group name.")]
    public List<string> ResourceGroups { get; set; } = [];

    public string HostRuntimeApiVersion { get; set; } = "2022-03-01";
    public string SnapshotPath { get; set; } = "data/snapshot.json";
}
