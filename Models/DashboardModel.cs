using System.Collections.ObjectModel;

namespace ABB.Analyze.VisualStudio.Models;

internal sealed class DashboardModel
{
    public string Repository { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Status { get; set; } = "Ready";
    public string Risk { get; set; } = "NOT REVIEWED";
    public string RiskColour { get; set; } = "#5C6370";
    public string Summary { get; set; } = "No review has been run.";
    public string Metadata { get; set; } = string.Empty;
    public string RawJson { get; set; } = string.Empty;
    public ObservableCollection<string> Reasons { get; } = new();
    public ObservableCollection<string> AffectedFiles { get; } = new();
    public ObservableCollection<FindingRow> Findings { get; } = new();
    public ObservableCollection<TestRow> Tests { get; } = new();
}
