using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DiagnosticProcess = System.Diagnostics.Process;

namespace ABB.Analyze.VisualStudio.Services;

/// <summary>
/// Shells out to git.exe to inspect and mutate repository state. Deliberately has no
/// dependency on Visual Studio SDK types (EnvDTE, ThreadHelper, etc.) so it can be
/// exercised directly from plain unit tests against real temp git repositories.
/// </summary>
internal static class GitCommandService
{
    public static string GetCurrentBranch(string repository) =>
        Git(repository, "branch --show-current") ?? "detached";

    public static string[] GetStagedFiles(string repository) =>
        SplitLines(Git(repository, "diff --cached --name-only --diff-filter=ACM"));

    public static string[] GetModifiedFiles(string repository)
    {
        string[] lines = SplitLines(Git(repository, "status --porcelain"));

        return lines
            .Select(line => line.Length > 3 ? line.Substring(3).Trim() : string.Empty)
            .Select(path =>
            {
                int arrowIndex = path.IndexOf("->", StringComparison.Ordinal);
                return arrowIndex >= 0 ? path.Substring(arrowIndex + 2).Trim() : path;
            })
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string[] GetAllTrackedFiles(string repository) =>
        SplitLines(Git(repository, "ls-files"));

    public static string[] GetUntrackedFiles(string repository) =>
        SplitLines(Git(repository, "ls-files --others --exclude-standard"));

    public static bool StageFile(string repository, string relativePath)
    {
        try
        {
            var info = new ProcessStartInfo("git.exe", $"add -- \"{relativePath}\"") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using DiagnosticProcess process = DiagnosticProcess.Start(info);
            if (process == null) return false;
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    public static string GetRepositoryRoot(string directory) =>
        Git(directory, "rev-parse --show-toplevel");

    private static string[] SplitLines(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return Array.Empty<string>();
        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Git(string directory, string arguments)
    {
        try
        {
            var info = new ProcessStartInfo("git.exe", arguments) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using DiagnosticProcess process = DiagnosticProcess.Start(info);
            if (process == null) return null;
            // Trim only trailing whitespace/newlines. A leading Trim() would strip the
            // leading space that "git status --porcelain" uses for unstaged-only
            // changes (e.g. " M Bar.cs"), corrupting the first line's file path.
            string output = process.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
            return process.WaitForExit(5000) && process.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }
}
