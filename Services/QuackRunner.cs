using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace ABB.Analyze.VisualStudio.Services;

internal sealed class ProcessResult
{
    public ProcessResult(
        int exitCode,
        string output,
        string error)
    {
        ExitCode = exitCode;
        Output = output;
        Error = error;
    }

    public int ExitCode { get; }

    public string Output { get; }

    public string Error { get; }
}

internal static class QuackRunner
{
    public static Task<ProcessResult> CheckAsync(
        string repository,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            "check --json",
            repository,
            timeoutSeconds: 45,
            cancellationToken);
    }

    public static Task<ProcessResult> GetModelsAsync(
        string repository,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            "model",
            repository,
            timeoutSeconds: 30,
            cancellationToken);
    }

    public static async Task<ProcessResult> AnalyzeAsync(
        string repository,
        string selectedModel,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string>?
            environmentVariables = null;

        if (!string.IsNullOrWhiteSpace(selectedModel) &&
            !string.Equals(
                selectedModel,
                "auto",
                StringComparison.OrdinalIgnoreCase))
        {
            environmentVariables =
                new Dictionary<string, string>
                {
                    ["QUACK_MODEL"] =
                        selectedModel
                };
        }

        ProcessResult watchResult =
            await RunAsync(
                "watch --once --json",
                repository,
                timeoutSeconds: 180,
                cancellationToken,
                environmentVariables);

        if (watchResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(
                    watchResult.Error)
                    ? watchResult.Output
                    : watchResult.Error);
        }

        return await CheckAsync(
            repository,
            cancellationToken);
    }

    private static async Task<ProcessResult> RunAsync(
        string arguments,
        string workingDirectory,
        int timeoutSeconds,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>?
            environmentVariables = null)
    {

        string executable =
            FindExecutable(
                workingDirectory);

        var startInfo =
            new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

        startInfo.EnvironmentVariables[
            "PYTHONIOENCODING"] = "utf-8";

        startInfo.EnvironmentVariables[
            "NO_COLOR"] = "1";

        if (environmentVariables != null)
        {
            foreach (KeyValuePair<string, string> variable
                     in environmentVariables)
            {
                startInfo.EnvironmentVariables[
                    variable.Key] = variable.Value;
            }
        }

        using var process =
            new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "QUACK could not be started.");
        }

        Task<string> standardOutputTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> standardErrorTask =
            process.StandardError.ReadToEndAsync();

        Task processExitTask =
            WaitForExitAsync(process);

        Task timeoutTask =
            Task.Delay(
                TimeSpan.FromSeconds(timeoutSeconds),
                cancellationToken);

        Task completedTask =
            await Task.WhenAny(
                processExitTask,
                timeoutTask);

        if (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (completedTask != processExitTask)
        {
            TryKill(process);

            throw new TimeoutException(
                $"QUACK timed out after {timeoutSeconds} seconds.");
        }

        await processExitTask;

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static string FindExecutable(
    string repository)
    {
        string executable =
            QuackLocator.GetExecutable();

        if (File.Exists(executable))
        {
            return executable;
        }

        throw new FileNotFoundException(
            $"Bundled QUACK executable was not found: {executable}");
    }

    private static Task WaitForExitAsync(
        Process process)
    {
        var completionSource =
            new TaskCompletionSource<object?>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        process.Exited +=
            (_, _) =>
                completionSource.TrySetResult(null);

        if (process.HasExited)
        {
            completionSource.TrySetResult(null);
        }

        return completionSource.Task;
    }

    private static void TryKill(
        Process process)
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