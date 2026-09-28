using ABB.Analyze.VisualStudio.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ABB.Analyze.VisualStudio.Services;

internal static class MutationService
{
    private const int TimeoutMinutes = 30;

    public static async Task<MutationSummary> RunAsync(
        string repository,
        CancellationToken cancellationToken,
        bool changedFilesOnly = false)
    {
        string projectDirectory = FindProjectoirectory(repository);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            // --since:HEAD lets Stryker use its own git diff to scope mutation testing to modified code.
            Arguments = changedFilesOnly ? "stryker --reporter json --since:HEAD" : "stryker --reporter json",
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Stryker.NET could not be started. Install it with: " +
                "dotnet tool install -g dotnet-stryker");
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task exitTask = WaitForExitAsync(process);
        Task completedTask = await Task.WhenAny(
            exitTask,
            Task.Delay(TimeSpan.FromMinutes(TimeoutMinutes), cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();

        if (completedTask != exitTask)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Mutation testing exceeded {TimeoutMinutes} minutes.");
        }

        await exitTask;
        string output = await outputTask;
        string error = await errorTask;

        string reportPath = FindLatestReport(repository)
            ?? throw new FileNotFoundException(
                "Stryker completed but no mutation-report.json was found." +
                Environment.NewLine + output + Environment.NewLine + error);

        MutationSummary summary = ParseReport(reportPath);

        if (process.ExitCode != 0 && summary.Total == 0)
        {
            throw new InvalidOperationException(
                "Stryker.NET failed." + Environment.NewLine + error);
        }

        return summary;
    }

    private static string FindProjectoirectory(
    string repositoryRoot)
    {
        string[] projects =
            Directory.GetFiles(
                repositoryRoot,
                "*.csproj",
                SearchOption.AllDirectories);

        if (projects.Length == 0)
        {
            throw new FileNotFoundException(
                "No .csproj files found.");
        }

        string preferred =
            projects.FirstOrDefault(
                p =>
                    p.Contains(".Tests.") ||
                    p.Contains("Test"));

        string selected =
            preferred ??
            projects.First();

        return Path.GetDirectoryName(selected)!;
    }

    public static MutationSummary LoadLatest(string repository)
    {
        string reportPath = FindLatestReport(repository)
            ?? throw new FileNotFoundException(
                "No Stryker JSON report was found. Run mutation testing first.");

        return ParseReport(reportPath);
    }

    private static string? FindLatestReport(string repository)
    {
        IEnumerable<string> files = Directory.EnumerateFiles(
            repository,
            "*.json",
            SearchOption.AllDirectories)
            .Where(path =>
                path.IndexOf("StrykerOutput", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (Path.GetFileName(path).IndexOf("mutation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 Path.GetFileName(path).IndexOf("report", StringComparison.OrdinalIgnoreCase) >= 0));

        return files
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static MutationSummary ParseReport(string reportPath)
    {
        JObject root = JObject.Parse(File.ReadAllText(reportPath));
        var summary = new MutationSummary { ReportPath = reportPath };
        JObject? files = root["files"] as JObject;

        if (files == null)
        {
            return summary;
        }

        foreach (JProperty fileProperty in files.Properties())
        {
            JArray? mutants = fileProperty.Value["mutants"] as JArray;
            if (mutants == null) continue;

            foreach (JToken mutant in mutants)
            {
                string status = mutant["status"]?.Value<string>() ?? string.Empty;

                switch (status.ToLowerInvariant())
                {
                    case "killed": summary.Killed++; break;
                    case "survived":
                        summary.Survived++;
                        summary.Survivors.Add(CreateSurvivor(fileProperty.Name, mutant));
                        break;
                    case "timeout": summary.TimedOut++; break;
                    case "nocoverage":
                    case "no_coverage": summary.NoCoverage++; break;
                    case "ignored": summary.Ignored++; break;
                    case "compileerror":
                    case "compile_error": summary.CompileErrors++; break;
                }
            }
        }

        summary.Total =
            summary.Killed +
            summary.Survived +
            summary.TimedOut +
            summary.NoCoverage;

        summary.Score = summary.Total == 0
            ? 0
            : 100.0 * (summary.Killed + summary.TimedOut) / summary.Total;

        return summary;
    }

    private static SurvivingMutant CreateSurvivor(
        string file,
        JToken mutant)
    {
        int line =
            mutant["location"]?["start"]?["line"]?.Value<int>() ?? 0;

        return new SurvivingMutant
        {
            File = file,
            Line = line,
            Mutator = mutant["mutatorName"]?.Value<string>() ?? string.Empty,
            Description = mutant["description"]?.Value<string>() ?? string.Empty
        };
    }

    private static Task WaitForExitAsync(Process process)
    {
        var source = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        process.Exited += (_, _) => source.TrySetResult(null);
        if (process.HasExited) source.TrySetResult(null);
        return source.Task;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
        }
        catch
        {
            // Preserve timeout/cancellation result.
        }
    }
}
