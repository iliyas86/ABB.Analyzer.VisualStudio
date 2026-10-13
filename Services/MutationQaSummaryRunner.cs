using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ABB.Analyze.VisualStudio.Models;
using Newtonsoft.Json;

namespace ABB.Analyze.VisualStudio.Services;

/// <summary>
/// Invokes the bundled MutationQaSummaryTool (a separate net8.0 console helper
/// using GitHub.Copilot.SDK) out-of-process, passing the surviving mutants as a
/// temporary JSON file and returning the Copilot-generated QA summary text.
/// </summary>
internal static class MutationQaSummaryRunner
{
    public static async Task<string> SummarizeAsync(
        MutationSummary summary,
        string repository,
        string scope,
        CancellationToken cancellationToken)
    {
        if (summary == null || summary.Survivors.Count == 0)
        {
            throw new InvalidOperationException(
                "No surviving mutants are available to summarize.");
        }

        string executable = MutationQaSummaryLocator.GetExecutable();

        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                $"Bundled MutationQaSummaryTool executable was not found: {executable}");
        }

        string tempFile = Path.Combine(
            Path.GetTempPath(),
            $"mutation-qa-{Guid.NewGuid():N}.json");

        try
        {
            string payload = BuildPayload(summary, repository, scope);
            File.WriteAllText(tempFile, payload, Encoding.UTF8);

            ProcessResult result = await RunAsync(
                executable,
                $"\"{tempFile}\"",
                timeoutSeconds: 180,
                cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? result.Output
                        : result.Error);
            }

            return result.Output;
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static string BuildPayload(
        MutationSummary summary,
        string repository,
        string scope)
    {
        var payload = new
        {
            Repository = repository,
            Scope = scope,
            Score = summary.Score,
            Total = summary.Total,
            Killed = summary.Killed,
            Survived = summary.Survived,
            TimedOut = summary.TimedOut,
            NoCoverage = summary.NoCoverage,
            Survivors = Array.ConvertAll(
                new System.Collections.Generic.List<SurvivingMutant>(summary.Survivors).ToArray(),
                s => new
                {
                    s.File,
                    s.Line,
                    s.Mutator,
                    s.Description
                })
        };

        return JsonConvert.SerializeObject(payload);
    }

    private static async Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executable),
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
                "MutationQaSummaryTool could not be started.");
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        Task processExitTask = WaitForExitAsync(process);
        Task timeoutTask = Task.Delay(
            TimeSpan.FromSeconds(timeoutSeconds),
            cancellationToken);

        Task completedTask = await Task.WhenAny(processExitTask, timeoutTask);

        if (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (completedTask != processExitTask)
        {
            TryKill(process);
            throw new TimeoutException(
                $"MutationQaSummaryTool timed out after {timeoutSeconds} seconds.");
        }

        await processExitTask;

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static Task WaitForExitAsync(Process process)
    {
        var completionSource = new TaskCompletionSource<object>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        process.Exited += (_, _) => completionSource.TrySetResult(null);

        if (process.HasExited)
        {
            completionSource.TrySetResult(null);
        }

        return completionSource.Task;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
            // Preserve cancellation or timeout result.
        }
    }
}
