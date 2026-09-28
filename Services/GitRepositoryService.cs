using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using DiagnosticProcess = System.Diagnostics.Process;

namespace ABB.Analyze.VisualStudio.Services;

internal static class GitRepositoryService
{
    private static readonly string[] BaseBranchCandidates =
    {
        "origin/main", "main", "origin/master", "master", "origin/develop", "develop"
    };

    public static string GetCurrentRepository()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        string file = dte?.Solution?.FullName ?? throw new InvalidOperationException("Open a Git-based solution first.");
        string directory = Path.GetDirectoryName(file) ?? throw new InvalidOperationException("Solution directory unavailable.");
        return Git(directory, "rev-parse --show-toplevel") ?? throw new InvalidOperationException("Solution is not in a Git repository.");
    }
    public static string GetCurrentBranch(string repository) => Git(repository, "branch --show-current") ?? "detached";

    /// <summary>Returns repo-relative, mutation-worthy .cs files that are currently modified/staged/untracked or differ from the base branch.</summary>
    public static List<string> GetChangedSourceFiles(string repository)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddPath(string rawPath)
        {
            string cleaned = rawPath.Trim().Trim('"');
            int arrowIndex = cleaned.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrowIndex >= 0)
            {
                cleaned = cleaned.Substring(arrowIndex + 4).Trim().Trim('"');
            }
            if (cleaned.Length > 0)
            {
                files.Add(cleaned.Replace('\\', '/'));
            }
        }

        void AddNameOnlyLines(string? output)
        {
            if (string.IsNullOrWhiteSpace(output)) return;
            foreach (string line in output.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line)) AddPath(line);
            }
        }

        // 1. Working tree status (staged, unstaged, untracked, renamed).
        string? statusOutput = Git(repository, "status --porcelain -uall");
        if (!string.IsNullOrWhiteSpace(statusOutput))
        {
            foreach (string line in statusOutput.Split('\n'))
            {
                if (line.Length < 4) continue;
                AddPath(line.Substring(3));
            }
        }

        // 2. Modifications vs HEAD, staged changes, and new untracked files.
        AddNameOnlyLines(Git(repository, "diff --name-only HEAD"));
        AddNameOnlyLines(Git(repository, "diff --name-only --cached"));
        AddNameOnlyLines(Git(repository, "ls-files --others --exclude-standard"));

        // 3. Everything that differs from a detected base branch.
        string? baseCommit = FindBaseCommit(repository);
        if (!string.IsNullOrWhiteSpace(baseCommit))
        {
            string? branchDiff = Git(repository, $"diff --name-only {baseCommit}...HEAD");
            AddNameOnlyLines(!string.IsNullOrWhiteSpace(branchDiff)
                ? branchDiff
                : Git(repository, $"diff --name-only {baseCommit} HEAD"));
        }

        // 4. Fall back to the most recent commit if nothing else was detected.
        if (files.Count == 0)
        {
            string? headDiff = Git(repository, "diff --name-only HEAD~1 HEAD");
            AddNameOnlyLines(!string.IsNullOrWhiteSpace(headDiff)
                ? headDiff
                : Git(repository, "log -1 --name-only --format="));
        }

        return files
            .Where(f => IsMutationCandidate(f, repository))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? FindBaseCommit(string repository)
    {
        string? headRev = Git(repository, "rev-parse HEAD");
        if (string.IsNullOrWhiteSpace(headRev)) return null;

        string? upstream = Git(repository, "rev-parse --abbrev-ref @{upstream}");
        if (!string.IsNullOrWhiteSpace(upstream) && !upstream.Contains("@{upstream}"))
        {
            string? mergeBase = Git(repository, $"merge-base HEAD {upstream.Trim()}");
            if (!string.IsNullOrWhiteSpace(mergeBase) && mergeBase.Trim() != headRev.Trim())
            {
                return mergeBase.Trim();
            }
        }

        foreach (string branch in BaseBranchCandidates)
        {
            string? mergeBase = Git(repository, $"merge-base HEAD {branch}");
            if (!string.IsNullOrWhiteSpace(mergeBase) && mergeBase.Trim() != headRev.Trim())
            {
                return mergeBase.Trim();
            }
        }

        return null;
    }

    private static bool IsMutationCandidate(string relativePath, string repository)
    {
        string lower = relativePath.ToLowerInvariant();
        if (!lower.EndsWith(".cs", StringComparison.Ordinal)) return false;

        if (lower.EndsWith(".designer.cs") ||
            lower.EndsWith(".g.cs") ||
            lower.EndsWith(".generated.cs") ||
            lower.EndsWith(".assemblyinfo.cs") ||
            lower.EndsWith(".feature.cs"))
        {
            return false;
        }

        if (lower.Contains("/bin/") || lower.Contains("/obj/") ||
            lower.StartsWith("bin/") || lower.StartsWith("obj/"))
        {
            return false;
        }

        string baseName = Path.GetFileNameWithoutExtension(lower);
        if (baseName.EndsWith("tests") || baseName.EndsWith("test") ||
            baseName.EndsWith("spec") || baseName.EndsWith("specs") ||
            baseName.StartsWith("test"))
        {
            return false;
        }

        bool isTestDirectory = lower.Split('/').Any(segment =>
            segment is "tests" or "test" or "unittests" or "unit-tests" or
                "integrationtests" or "bdd" or "specflow" ||
            segment.EndsWith(".tests") || segment.EndsWith(".test"));
        if (isTestDirectory) return false;

        string fullPath = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(repository, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath);
    }

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
