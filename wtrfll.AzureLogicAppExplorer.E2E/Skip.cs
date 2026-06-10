using Xunit.Sdk;

namespace wtrfll.AzureLogicAppExplorer.E2E;

internal static class Skip
{
    public static void If(bool condition, string reason)
    {
        if (condition) throw new SkipException(reason);
    }
}

internal sealed class SkipException(string reason) : XunitException(reason);
