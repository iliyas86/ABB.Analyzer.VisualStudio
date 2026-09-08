using System;
using System.IO;
using System.Linq;

namespace ABB.Analyze.VisualStudio.Services;

/// <summary>
/// Independently locates an existing test file for a given source file by naming
/// convention (e.g. FilterHelper.cs -&gt; FilterHelperTests.cs), regardless of whether
/// that test file happens to be staged or has a pending diff.
///
/// This exists because QUACK's "check"/"watch" commands only analyze staged *diffs*.
/// An existing, already-committed, unmodified test file has no staged diff, so
/// running "git add" on it (see <see cref="TestFileStagingService"/>) does not make
/// QUACK aware of it. The dashboard must independently verify test-file existence so
/// it does not report a false "Coverage gap" for source changes that already have a
/// corresponding (unchanged) test.
/// </summary>
internal static class ExistingTestFileLocator
{
    private static readonly string[] TestSuffixes = { "Tests", "Test", "Specs", "Spec" };

    /// <summary>
    /// Returns the repository-relative path of an existing test file matching the
    /// given source file by naming convention, or null if none is found.
    /// </summary>
    public static string FindExistingTestFile(string repository, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(sourceFile))
        {
            return null;
        }

        string baseName = Path.GetFileNameWithoutExtension(sourceFile);

        if (string.IsNullOrWhiteSpace(baseName) || IsTestFile(sourceFile))
        {
            return null;
        }

        string[] candidates;

        try
        {
            candidates = GitCommandService.GetAllTrackedFiles(repository)
                .Concat(GitCommandService.GetUntrackedFiles(repository))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return null;
        }

        foreach (string suffix in TestSuffixes)
        {
            string testFileName = baseName + suffix + ".cs";

            string match = candidates.FirstOrDefault(c =>
                string.Equals(Path.GetFileName(c), testFileName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool IsTestFile(string file)
    {
        string baseName = Path.GetFileNameWithoutExtension(file);

        return TestSuffixes.Any(suffix =>
            baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a runnable "dotnet test" command targeting the located test file's
    /// containing project, filtered to the test class by naming convention. Used to
    /// populate the Command column when QUACK itself provided no command because it
    /// never saw the (unstaged/unchanged) test file.
    /// </summary>
    public static string BuildTestCommand(string repository, string existingTestFileRelativePath)
    {
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(existingTestFileRelativePath))
        {
            return string.Empty;
        }

        string className = Path.GetFileNameWithoutExtension(existingTestFileRelativePath);

        string testFileFullPath = Path.Combine(repository, existingTestFileRelativePath);
        string projectFile = FindContainingProject(testFileFullPath);

        if (string.IsNullOrWhiteSpace(projectFile))
        {
            return string.Empty;
        }

        return $"dotnet test \"{projectFile}\" --filter \"FullyQualifiedName~{className}\"";
    }

    private static string FindContainingProject(string testFileFullPath)
    {
        try
        {
            string directory = Path.GetDirectoryName(testFileFullPath);

            while (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                string[] projectFiles = Directory.GetFiles(directory, "*.csproj");

                if (projectFiles.Length > 0)
                {
                    return projectFiles[0];
                }

                directory = Path.GetDirectoryName(directory);
            }
        }
        catch
        {
            // Fall through and report no project found.
        }

        return null;
    }
}
