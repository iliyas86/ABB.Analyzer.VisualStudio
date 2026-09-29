using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ABB.Analyze.VisualStudio.Services;

internal static class CopilotCliRunner
{
    public static async Task<string> RunPromptAsync(
        string prompt,
        string model,
        CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(model ?? string.Empty, @"^[A-Za-z0-9._-]+$"))
        {
            throw new ArgumentException("The selected Copilot model ID contains unsupported characters.", nameof(model));
        }

        string cliLoader = FindCliLoader();
        if ((prompt?.Length ?? 0) > 24000)
        {
            throw new InvalidOperationException(
                "The checkpoint prompt is too large for the Copilot CLI. Reduce the changed diff and try again.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "node.exe",
            Arguments = string.Join(
                " ",
                new[]
                {
                    QuoteArgument(cliLoader),
                    "-p",
                    QuoteArgument(prompt ?? string.Empty),
                    "--silent",
                    "--model",
                    model,
                    "--output-format text",
                    "--allow-all-tools",
                    "--disable-builtin-mcps",
                    "--available-tools"
                }),
            WorkingDirectory = Path.GetTempPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.EnvironmentVariables["COPILOT_ALLOW_ALL"] = "false";

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Could not start the Copilot CLI.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception || exception is FileNotFoundException)
        {
            throw new InvalidOperationException(
                "Could not start Node.js to run the GitHub Copilot CLI. Install Node.js and the Copilot CLI, then sign in.",
                exception);
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        Task exitTask = WaitForExitAsync(process);

        Task timeoutTask = Task.Delay(TimeSpan.FromMinutes(3), cancellationToken);
        Task completedTask = await Task.WhenAny(exitTask, timeoutTask);

        if (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (completedTask != exitTask)
        {
            TryKill(process);
            throw new TimeoutException("Copilot checkpoint evaluation timed out after 3 minutes.");
        }

        await exitTask;
        string output = await outputTask;
        string error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "Copilot checkpoint evaluation failed. Check that the Copilot CLI is installed and signed in."
                    : "Copilot checkpoint evaluation failed: " + error.Trim());
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Copilot returned an empty checkpoint evaluation response.");
        }

        return output;
    }

    private static string FindCliLoader()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] candidates =
        {
            Path.Combine(appData, "npm", "node_modules", "@github", "copilot", "npm-loader.js"),
            Path.Combine(appData, "npm", "node_modules", "@github", "copilot", "dist", "npm-loader.js")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "Could not locate the npm-installed GitHub Copilot CLI. Install @github/copilot for this Windows user and sign in.");
    }

    private static string QuoteArgument(string value)
    {
        var quoted = new StringBuilder(value.Length + 2);
        quoted.Append('"');
        int backslashes = 0;

        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                quoted.Append('\\', backslashes * 2 + 1);
                quoted.Append('"');
                backslashes = 0;
                continue;
            }

            quoted.Append('\\', backslashes);
            quoted.Append(character);
            backslashes = 0;
        }

        quoted.Append('\\', backslashes * 2);
        quoted.Append('"');
        return quoted.ToString();
    }

    private static Task WaitForExitAsync(Process process)
    {
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => completion.TrySetResult(null);
        if (process.HasExited)
        {
            completion.TrySetResult(null);
        }

        return completion.Task;
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
        }
    }
}