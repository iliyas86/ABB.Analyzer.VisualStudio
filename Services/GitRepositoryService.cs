using System;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace ABB.Analyze.VisualStudio.Services;

/// <summary>
/// Visual Studio-facing entry point for git operations. Repository discovery relies
/// on EnvDTE/ThreadHelper; the actual git command shelling is delegated to
/// <see cref="GitCommandService"/>, which has no VS SDK dependency and is directly
/// unit-testable.
/// </summary>
internal static class GitRepositoryService
{
    public static string GetCurrentRepository()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
        string file = dte?.Solution?.FullName ?? throw new InvalidOperationException("Open a Git-based solution first.");
        string directory = Path.GetDirectoryName(file) ?? throw new InvalidOperationException("Solution directory unavailable.");
        return GitCommandService.GetRepositoryRoot(directory) ?? throw new InvalidOperationException("Solution is not in a Git repository.");
    }

    public static string GetCurrentBranch(string repository) => GitCommandService.GetCurrentBranch(repository);

    public static string[] GetStagedFiles(string repository) => GitCommandService.GetStagedFiles(repository);

    public static string[] GetModifiedFiles(string repository) => GitCommandService.GetModifiedFiles(repository);

    public static string[] GetAllTrackedFiles(string repository) => GitCommandService.GetAllTrackedFiles(repository);

    public static string[] GetUntrackedFiles(string repository) => GitCommandService.GetUntrackedFiles(repository);

    public static bool StageFile(string repository, string relativePath) => GitCommandService.StageFile(repository, relativePath);
}
