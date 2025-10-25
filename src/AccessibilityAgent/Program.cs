using AccessibilityAgent.Cli;

namespace AccessibilityAgent;

internal static class Program
{
    public static Task<int> Main(string[] args) => CommandDispatcher.RunAsync(args);
}
