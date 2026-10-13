namespace MutationQaSummaryTool;

/// <summary>
/// Minimal mirror of ABB.Analyze.VisualStudio's mutation summary models, used only
/// for deserializing the JSON payload written by the VSIX before invoking this tool.
/// </summary>
public sealed class MutationSummaryInput
{
    public string? Repository { get; set; }
    public string? Scope { get; set; }
    public double Score { get; set; }
    public int Total { get; set; }
    public int Killed { get; set; }
    public int Survived { get; set; }
    public int TimedOut { get; set; }
    public int NoCoverage { get; set; }
    public List<SurvivingMutantInput> Survivors { get; set; } = new();
}

public sealed class SurvivingMutantInput
{
    public string? File { get; set; }
    public int Line { get; set; }
    public string? Mutator { get; set; }
    public string? Description { get; set; }
}
