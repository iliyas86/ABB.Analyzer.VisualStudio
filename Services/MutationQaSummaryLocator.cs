using System.IO;
using System.Reflection;

namespace ABB.Analyze.VisualStudio.Services;

/// <summary>
/// Locates the bundled MutationQaSummaryTool.dll, a separate net8.0 console
/// helper that uses GitHub.Copilot.SDK (which requires .NET 8+ and therefore
/// cannot be referenced directly from this .NET Framework 4.7.2 extension).
/// </summary>
internal static class MutationQaSummaryLocator
{
    public static string GetExecutable()
    {
        string extensionFolder =
            Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!;

        return Path.Combine(
            extensionFolder,
            "MutationQaSummaryTool",
            "MutationQaSummaryTool.exe");
    }
}
