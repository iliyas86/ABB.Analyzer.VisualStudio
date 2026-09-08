using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ABB.Analyze.VisualStudio.Services;

/// <summary>
/// Detects existing test files (by naming convention) that correspond to staged
/// source file changes but are themselves not staged. QUACK's "check"/"watch" commands
/// only analyze staged changes, so an existing-but-unstaged test file can cause a false
/// "Coverage gap" result even though a test already exists on disk.
/// </summary>
internal static class TestFileStagingService
{
    private static readonly string[] TestSuffixes = { "Tests", "Test", "Specs", "Spec" };

    /// <summary>
    /// Finds test files that match a staged source file by naming convention
    /// (e.g. FilterHelper.cs -> FilterHelperTests.cs) but are not currently staged.
    /// Returned paths are relative to the repository root, as reported by git.
    /// </summary>
    public static IReadOnlyList<string> FindUnstagedTestFiles(string repository)
    {
        string[] staged = GitCommandService.GetStagedFiles(repository);

        var stagedSet =
            new HashSet<string>(staged, StringComparer.OrdinalIgnoreCase);

        string[] stagedSourceFiles = staged
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !IsTestFile(f))
            .ToArray();

        if (stagedSourceFiles.Length == 0)
        {
            return Array.Empty<string>();
        }

        string[] candidates = GitCommandService.GetAllTrackedFiles(repository)
            .Concat(GitCommandService.GetUntrackedFiles(repository))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<string>();

        foreach (string sourceFile in stagedSourceFiles)
        {
            string baseName = Path.GetFileNameWithoutExtension(sourceFile);

            foreach (string suffix in TestSuffixes)
            {
                string testFileName = baseName + suffix + ".cs";

                string? match = candidates.FirstOrDefault(c =>
                    string.Equals(Path.GetFileName(c), testFileName, StringComparison.OrdinalIgnoreCase));

                if (match != null && !stagedSet.Contains(match))
                {
                    result.Add(match);
                }
            }
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsTestFile(string file)
    {
        string baseName = Path.GetFileNameWithoutExtension(file);

        return TestSuffixes.Any(suffix =>
            baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
