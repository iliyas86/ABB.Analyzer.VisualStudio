using ABB.Analyze.VisualStudio.Models;
using ABB.Analyze.VisualStudio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ABB.Analyze.VisualStudio.Tests;

[TestClass]
public class MutationCsvExportServiceTests
{
    [TestMethod]
    public void BuildCsv_ExportsSummaryAndSurvivingMutantDetails()
    {
        var summary = new MutationSummary
        {
            Score = 82.5,
            Total = 40,
            Killed = 33,
            Survived = 7,
            TimedOut = 1,
            NoCoverage = 2,
            Ignored = 3,
            CompileErrors = 1
        };
        summary.Survivors.Add(new SurvivingMutant
        {
            File = "src/Query.cs",
            Line = 28,
            Mutator = "EqualityMutator",
            Description = "Changed == to !=, \"survived\"",
            AnalysisStatus = "Review"
        });

        string csv = MutationCsvExportService.BuildCsv(
            summary,
            "C:\\repo",
            "Modified files");

        StringAssert.Contains(csv, "Mutation Score (%)");
        StringAssert.Contains(csv, "\"82.50\"");
        StringAssert.Contains(csv, "\"Surviving mutant\"");
        StringAssert.Contains(csv, "\"src/Query.cs\"");
        StringAssert.Contains(csv, "\"\"survived\"\"");
        StringAssert.Contains(csv, "\"Review\"");
    }

    [TestMethod]
    public void BuildCsv_EmitsSummaryWhenThereAreNoSurvivorsAndEscapesFormulaText()
    {
        var summary = new MutationSummary
        {
            Score = 100,
            Total = 5,
            Killed = 5
        };

        string csv = MutationCsvExportService.BuildCsv(
            summary,
            "=HYPERLINK(\"https://example.invalid\")",
            "Entire solution");

        StringAssert.Contains(csv, "\"Summary\"");
        StringAssert.Contains(csv, "'=HYPERLINK(");
        Assert.AreEqual(2, csv.Split(new[] { '\n' }).Length - 1);
    }
}