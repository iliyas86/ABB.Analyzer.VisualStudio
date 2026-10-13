using ABB.Analyze.VisualStudio.Models;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ABB.Analyze.VisualStudio.Services;

internal static class JsonServices
{
    public static QuackCheckResult Deserialize(string json)
    {
        string extracted = ExtractJsonObject(json);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(extracted));
        var serializer = new DataContractJsonSerializer(typeof(QuackCheckResult));
        return (QuackCheckResult)(serializer.ReadObject(stream)
            ?? throw new InvalidDataException("QUACK returned empty JSON."));
    }

    private static QuackWatchResult DeserializeWatch(string json)
    {
        string extracted = ExtractJsonObject(json);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(extracted));
        var serializer = new DataContractJsonSerializer(typeof(QuackWatchResult));
        return (QuackWatchResult)(serializer.ReadObject(stream)
            ?? throw new InvalidDataException("QUACK returned empty JSON."));
    }

    /// <summary>
    /// "watch --once --json" writes a progress spinner (e.g. "reviewing
    /// changes...") to the same stream before the JSON payload. This strips
    /// any such preamble/trailing noise by extracting the outermost JSON
    /// object delimited by the first '{' and the matching last '}'.
    /// </summary>
    private static string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return text;
        }

        return text.Substring(start, end - start + 1);
    }

    /// <summary>
    /// "watch --once --json" performs the AI review but does not evaluate the
    /// local quality gate; "check --json" evaluates the gate (blocked,
    /// findings, testGuidance) but never runs AI. This copies the AI review
    /// from the watch output onto the check result so neither is lost.
    /// </summary>
    public static string MergeAiReview(string checkJson, string watchJson)
    {
        QuackCheckResult checkResult = Deserialize(checkJson);
        QuackWatchResult watchResult = DeserializeWatch(watchJson);

        checkResult.AiReview = watchResult.AiReview;

        var serializer = new DataContractJsonSerializer(typeof(QuackCheckResult));
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, checkResult);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static DashboardModel Build(QuackCheckResult result, string branch, string rawJson, string repository = null, System.Collections.Generic.IEnumerable<string> changedFiles = null)
    {
        var model = new DashboardModel
        {
            Repository = result.Repository,
            Branch = branch,
            Status = result.Blocked ? "Blocked" : "Completed",
            RawJson = rawJson
        };

        if (changedFiles != null)
        {
            foreach (string file in changedFiles) AddUnique(model.AffectedFiles, file);
        }

        QuackAiReview? ai = result.AiReview;
        if (ai?.Available == true)
        {
            model.Risk = ai.Risk.ToUpperInvariant();
            model.RiskColour = model.Risk == "HIGH" ? "#C62828" : model.Risk == "MEDIUM" ? "#F9A825" : "#2E7D32";
            model.Summary = string.IsNullOrWhiteSpace(ai.Summary) ? "AI review completed." : ai.Summary;
            model.Metadata = $"Model: {ai.Model} | Reviewed: {FormatTimestamp(ai.ReviewedAtUtc)}";
            foreach (string reason in ai.Reasons)
            {
                model.Reasons.Add(reason);
                model.Findings.Add(new FindingRow
                {
                    Severity = model.Risk,
                    Source = "Copilot",
                    Rule = InferRule(reason),
                    File = ai.MissingTests.FirstOrDefault() ?? string.Empty,
                    Message = reason
                });
            }
            foreach (string file in ai.MissingTests) AddUnique(model.AffectedFiles, file);
            foreach (string command in ai.TestsToRun) AddTest(model, new QuackTestGuidance
            {
                TestOrCommand = command,
                Status = "ready_to_run",
                Recommendation = "Run the AI-suggested test."
            }, repository);
        }
        else if (!string.IsNullOrWhiteSpace(result.AiError))
        {
            model.Risk = "AI UNAVAILABLE";
            model.RiskColour = "#6B7280";
            model.Summary = result.AiError;
        }
        else if (ai != null)
        {
            // The AI review ran but did not come back as available and no
            // explicit error was reported - surface that instead of leaving
            // the stale "No review has been run." default in place.
            model.Risk = "AI UNAVAILABLE";
            model.RiskColour = "#6B7280";
            model.Summary = "AI review did not return a summary for this run.";
        }

        foreach (QuackFinding item in result.Findings)
        {
            model.Findings.Add(new FindingRow
            {
                Severity = item.Severity,
                Source = "Local",
                Rule = item.Rule,
                File = item.File,
                Line = item.Line,
                Message = item.Message
            });
            AddUnique(model.AffectedFiles, item.File);
        }

        foreach (QuackTestGuidance item in result.TestGuidance)
        {
            AddTest(model, item, repository);
            AddUnique(model.AffectedFiles, item.SourceFile);
        }

        if (ai?.Available != true && string.IsNullOrWhiteSpace(result.AiError))
        {
            model.Summary = result.Findings.Count > 0
                ? $"{result.Findings.Count} local finding(s) detected. AI review was not available."
                : result.TestGuidance.Count > 0
                    ? "No blocking findings. Review test guidance. AI review was not available."
                    : "No blocking findings were detected. AI review was not available.";
        }

        return model;
    }

    private static void AddTest(
    DashboardModel model,
    QuackTestGuidance item,
    string repository = null)
    {
        string normalizedValue =
            TestCommandService.NormalizeCommand(
                item.TestOrCommand);

        normalizedValue =
            BuildExecutableTestCommand(
                normalizedValue,
                item.Status);

        bool isRunnableCommand =
            TestCommandService.IsApproved(
                normalizedValue);

        string effectiveStatus;
        string recommendation = item.Recommendation;

        if (isRunnableCommand)
        {
            effectiveStatus =
                "ready_to_run";
        }
        else if (string.Equals(
                     item.Status,
                     "ready_to_run",
                     StringComparison.OrdinalIgnoreCase))
        {
            effectiveStatus =
                "ai_suggested_test";
        }
        else if (string.Equals(
                     item.Status,
                     "no_tests_found",
                     StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(repository) &&
                 TryFindExistingTestFile(
                     repository,
                     item.SourceFile,
                     out string existingTestFile))
        {
            // QUACK only analyzes staged diffs, so an existing but unmodified test
            // file is invisible to it. Independently verify test-file existence by
            // naming convention to avoid a false "Coverage gap".
            effectiveStatus =
                "existing_test_found";

            recommendation =
                $"An existing test file was found: {existingTestFile}. " +
                "QUACK only analyzes staged diffs, so this unchanged test file was not detected automatically.";

            // QUACK never populated TestOrCommand for this row (it never saw the
            // test file), so synthesize a runnable command from the located file
            // instead of leaving the Command column blank.
            string synthesizedCommand =
                ExistingTestFileLocator.BuildTestCommand(
                    repository,
                    existingTestFile);

            if (!string.IsNullOrWhiteSpace(synthesizedCommand))
            {
                normalizedValue =
                    TestCommandService.NormalizeCommand(synthesizedCommand);

                isRunnableCommand =
                    TestCommandService.IsApproved(normalizedValue);
            }
        }
        else
        {
            effectiveStatus =
                item.Status;
        }

        model.Tests.Add(
            new TestRow
            {
                SourceFile =
                    item.SourceFile,

                TestOrCommand =
                    normalizedValue,

                OriginalTestOrCommand =
                    normalizedValue,

                Status =
                    effectiveStatus,

                DisplayStatus =
                    FriendlyStatus(
                        effectiveStatus),

                Recommendation =
                    recommendation,

                CanRun =
                    isRunnableCommand,

                IsUserModified =
                    false
            });
    }

    private static bool TryFindExistingTestFile(
    string repository,
    string sourceFile,
    out string existingTestFile)
    {
        existingTestFile =
            ExistingTestFileLocator.FindExistingTestFile(
                repository,
                sourceFile);

        return !string.IsNullOrWhiteSpace(existingTestFile);
    }


    private static string BuildExecutableTestCommand(
    string value,
    string status)
    {
        string normalizedValue =
            TestCommandService.NormalizeCommand(value);

        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return string.Empty;
        }

        // The value is already a complete supported command.
        // Do not add "dotnet test" a second time.
        if (StartsWithSupportedCommand(normalizedValue))
        {
            return normalizedValue;
        }

        bool isMarkedReadyToRun =
            string.Equals(
                status,
                "ready_to_run",
                StringComparison.OrdinalIgnoreCase);

        if (!isMarkedReadyToRun)
        {
            return normalizedValue;
        }

        bool containsDotNetProject =
            normalizedValue.IndexOf(
                ".csproj",
                StringComparison.OrdinalIgnoreCase) >= 0;

        if (!containsDotNetProject)
        {
            return normalizedValue;
        }

        string executableCommand =
            "dotnet test " + normalizedValue;

        return TestCommandService.NormalizeCommand(
            executableCommand);
    }

    private static bool StartsWithSupportedCommand(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return
            string.Equals(
                value,
                "dotnet test",
                StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(
                "dotnet test ",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                "pytest",
                StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(
                "pytest ",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                "python -m pytest",
                StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith(
                "python -m pytest ",
                StringComparison.OrdinalIgnoreCase);
    }


    private static string FriendlyStatus(
    string status)
    {
        return status switch
        {
            "no_tests_found" =>
                "Coverage gap",

            "existing_test_found" =>
                "Test exists (unstaged diff)",

            "ready_to_run" =>
                "Ready to run",

            "mapped" =>
                "Test mapped",

            "changed_test" =>
                "Changed test",

            "ai_suggested_test" =>
                "AI-suggested test",

            _ =>
                string.IsNullOrWhiteSpace(status)
                    ? "Unknown"
                    : status.Replace(
                        '_',
                        ' ')
        };
    }

    private static string FormatTimestamp(string value) =>
        DateTime.TryParse(value, out DateTime parsed) ? parsed.ToLocalTime().ToString("dd MMM yyyy, hh:mm:ss tt") : value;

    private static void AddUnique(System.Collections.ObjectModel.ObservableCollection<string> list, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !list.Contains(value)) list.Add(value);
    }

    private static string InferRule(string text)
    {
        if (text.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("coverage", StringComparison.OrdinalIgnoreCase) >= 0) return "AI-COVERAGE";
        if (text.IndexOf("null", StringComparison.OrdinalIgnoreCase) >= 0) return "AI-NULLABILITY";
        if (text.IndexOf("contract", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("interface", StringComparison.OrdinalIgnoreCase) >= 0) return "AI-CONTRACT";
        return "AI-REVIEW";
    }
}
