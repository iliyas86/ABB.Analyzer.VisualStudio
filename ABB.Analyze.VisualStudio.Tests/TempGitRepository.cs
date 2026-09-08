using System;
using System.Diagnostics;
using System.IO;

namespace ABB.Analyze.VisualStudio.Tests;

/// <summary>
/// Creates and manages a throwaway git repository under %TEMP% for integration-style
/// tests against the extension's git-shelling services.
/// </summary>
internal sealed class TempGitRepository : IDisposable
{
    public string Path { get; }

    public TempGitRepository()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "abb-analyze-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);

        RunGit("init");
        RunGit("config user.email test@example.com");
        RunGit("config user.name Test");
        RunGit("config commit.gpgsign false");
    }

    public void WriteFile(string relativePath, string content)
    {
        string fullPath = System.IO.Path.Combine(Path, relativePath);
        string directory = System.IO.Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    public void Add(string relativePath) => RunGit($"add -- \"{relativePath}\"");

    public void AddAll() => RunGit("add -A");

    public void Commit(string message) => RunGit($"commit -m \"{message}\" --allow-empty");

    public string RunGit(string arguments)
    {
        var info = new ProcessStartInfo("git.exe", arguments)
        {
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = Process.Start(info);
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000);
        return output.Trim();
    }

    public void Dispose()
    {
        try
        {
            var directory = new DirectoryInfo(Path);

            foreach (FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; leftover temp folders don't fail the test run.
        }
    }
}
