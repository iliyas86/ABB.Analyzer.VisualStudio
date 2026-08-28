using System;
using System.Diagnostics;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using DiagnosticProcess = System.Diagnostics.Process;

namespace ABB.Analyze.VisualStudio.Services;

internal static class GitRepositoryService
{
    public static string GetCurrentRepository()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        string file = dte?.Solution?.FullName ?? throw new InvalidOperationException("Open a Git-based solution first.");
        string directory = Path.GetDirectoryName(file) ?? throw new InvalidOperationException("Solution directory unavailable.");
        return Git(directory, "rev-parse --show-toplevel") ?? throw new InvalidOperationException("Solution is not in a Git repository.");
    }
    public static string GetCurrentBranch(string repository) => Git(repository, "branch --show-current") ?? "detached";
    private static string? Git(string directory, string arguments)
    {
        try
        {
            var info = new ProcessStartInfo("git.exe", arguments) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using DiagnosticProcess? process = DiagnosticProcess.Start(info);
            if (process == null) return null;
            string output = process.StandardOutput.ReadToEnd().Trim();
            return process.WaitForExit(5000) && process.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }
}
