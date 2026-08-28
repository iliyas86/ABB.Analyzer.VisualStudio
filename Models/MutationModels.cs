using System.Collections.ObjectModel;

namespace ABB.Analyze.VisualStudio.Models;

internal sealed class MutationSummary
{
    public double Score { get; set; }
    public int Total { get; set; }
    public int Killed { get; set; }
    public int Survived { get; set; }
    public int TimedOut { get; set; }
    public int NoCoverage { get; set; }
    public int Ignored { get; set; }
    public int CompileErrors { get; set; }
    public string ReportPath { get; set; } = string.Empty;
    public string Status => Total == 0 ? "No scored mutants" : $"{Score:0.0}%";
    public ObservableCollection<SurvivingMutant> Survivors { get; } = new();
}

internal sealed class SurvivingMutant
{
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Mutator { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Location => Line > 0 ? $"{System.IO.Path.GetFileName(File)}:{Line}" : System.IO.Path.GetFileName(File);
}
