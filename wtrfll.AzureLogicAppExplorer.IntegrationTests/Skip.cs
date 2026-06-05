using Xunit.Sdk;

namespace wtrfll.AzureLogicAppExplorer.IntegrationTests;

/// <summary>Minimal xUnit skip helper (xUnit v2 doesn't have Skip.If built-in).</summary>
internal static class Skip
{
    public static void If(bool condition, string reason)
    {
        if (condition) throw new SkipException(reason);
    }
}

/// <summary>Causes xUnit to report the test as skipped rather than failed.</summary>
internal sealed class SkipException(string reason) : XunitException(reason) { }
