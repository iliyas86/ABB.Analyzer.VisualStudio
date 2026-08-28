using System.IO;
using System.Reflection;

namespace ABB.Analyze.VisualStudio.Services;

internal static class QuackLocator
{
    public static string GetExecutable()
    {
        string extensionFolder =
            Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location)!;

        return Path.Combine(
            extensionFolder,
            "Quack",
            "quack.exe");
    }
}