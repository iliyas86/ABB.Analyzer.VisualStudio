using System.Text;
using System.Text.Json;
using GitHub.Copilot;

namespace MutationQaSummaryTool;

/// <summary>
/// Standalone .NET 8 helper invoked out-of-process by the .NET Framework 4.7.2
/// Visual Studio extension. Reads a mutation summary JSON file, builds a QA prompt
/// from the surviving mutants, sends it to GitHub Copilot via the Copilot SDK, and
/// prints the plain-text response to stdout so the caller can capture it.
///
/// Usage: MutationQaSummaryTool.exe <path-to-mutation-summary.json>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: MutationQaSummaryTool <path-to-mutation-summary.json>");
            return 1;
        }

        var inputPath = args[0];
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            return 1;
        }

        MutationSummaryInput? summary;
        try
        {
            var json = await File.ReadAllTextAsync(inputPath);
            summary = JsonSerializer.Deserialize<MutationSummaryInput>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse mutation summary JSON: {ex.Message}");
            return 1;
        }

        if (summary is null || summary.Survivors.Count == 0)
        {
            Console.Error.WriteLine("No surviving mutants found in the supplied summary.");
            return 1;
        }

        var prompt = BuildPrompt(summary);

        try
        {
            var response = await SendToCopilotAsync(prompt);
            Console.WriteLine(response);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Copilot request failed: {ex.Message}");
            return 2;
        }
    }

    private static string BuildPrompt(MutationSummaryInput summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are reviewing results from Stryker.NET mutation testing.");
        sb.AppendLine("The following mutants survived, meaning the existing test suite did not catch these code changes.");
        sb.AppendLine("For each group of related survivors, identify the missing test scenario(s), the expected behavior that should be verified, and any production risk this gap represents.");
        sb.AppendLine("Conclude with a prioritized summary of the most important missing test cases to add first.");
        sb.AppendLine();
        sb.AppendLine($"Repository: {summary.Repository}");
        sb.AppendLine($"Scope: {summary.Scope}");
        sb.AppendLine($"Mutation score: {summary.Score}% ({summary.Killed} killed / {summary.Survived} survived / {summary.Total} total)");
        sb.AppendLine();
        sb.AppendLine("Surviving mutants:");
        foreach (var survivor in summary.Survivors)
        {
            sb.AppendLine($"- {survivor.File}:{survivor.Line} [{survivor.Mutator}] {survivor.Description}");
        }

        return sb.ToString();
    }

    private static async Task<string> SendToCopilotAsync(string prompt)
    {
        await using var client = new CopilotClient(new CopilotClientOptions());
        await client.StartAsync();

        await using var session = await client.CreateSessionAsync(new SessionConfig());

        var response = await session.SendAndWaitAsync(
            new MessageOptions { Prompt = prompt },
            TimeSpan.FromMinutes(5));

        return response.Data?.Content ?? string.Empty;
    }
}
