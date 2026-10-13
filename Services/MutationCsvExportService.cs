using ABB.Analyze.VisualStudio.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ABB.Analyze.VisualStudio.Services;

internal static class MutationCsvExportService
{
    private static readonly string[] Headers =
    {
        "Record Type",
        "Repository",
        "Scope",
        "Mutation Score (%)",
        "Total Mutants",
        "Killed",
        "Survived",
        "Timed Out",
        "No Coverage",
        "Ignored",
        "Compile Errors",
        "File",
        "Line",
        "Mutator",
        "Description",
        "Analysis"
    };

    public static string BuildCsv(
        MutationSummary summary,
        string repository,
        string scope)
    {
        var output = new StringBuilder();
        AppendRow(output, Headers);

        IEnumerable<SurvivingMutant?> survivors = summary.Survivors.Count > 0
            ? summary.Survivors.Cast<SurvivingMutant?>()
            : new SurvivingMutant?[] { null };

        foreach (SurvivingMutant? mutant in survivors)
        {
            AppendRow(output, new[]
            {
                mutant == null ? "Summary" : "Surviving mutant",
                repository,
                scope,
                summary.Score.ToString("0.00", CultureInfo.InvariantCulture),
                summary.Total.ToString(CultureInfo.InvariantCulture),
                summary.Killed.ToString(CultureInfo.InvariantCulture),
                summary.Survived.ToString(CultureInfo.InvariantCulture),
                summary.TimedOut.ToString(CultureInfo.InvariantCulture),
                summary.NoCoverage.ToString(CultureInfo.InvariantCulture),
                summary.Ignored.ToString(CultureInfo.InvariantCulture),
                summary.CompileErrors.ToString(CultureInfo.InvariantCulture),
                mutant?.File ?? string.Empty,
                mutant?.Line.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                mutant?.Mutator ?? string.Empty,
                mutant?.Description ?? string.Empty,
                mutant?.AnalysisStatus ?? string.Empty
            });
        }

        return output.ToString();
    }

    private static void AppendRow(StringBuilder output, IEnumerable<string> fields)
    {
        output.AppendLine(string.Join(",", fields.Select(EscapeField)));
    }

    private static string EscapeField(string value)
    {
        string safeValue = value ?? string.Empty;
        if (safeValue.Length > 0 && "=+-@\t\r".IndexOf(safeValue[0]) >= 0)
        {
            safeValue = "'" + safeValue;
        }

        return "\"" + safeValue.Replace("\"", "\"\"") + "\"";
    }
}