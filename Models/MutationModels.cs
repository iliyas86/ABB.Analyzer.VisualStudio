using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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

internal sealed class SurvivingMutant : INotifyPropertyChanged
{
    private string _description = string.Empty;
    private string _analysisStatus = string.Empty;
    private bool _isAnalyzing;

    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Mutator { get; set; } = string.Empty;

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public string AnalysisStatus
    {
        get => _analysisStatus;
        set => SetField(ref _analysisStatus, value);
    }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        set => SetField(ref _isAnalyzing, value);
    }

    public string Location => Line > 0 ? $"{System.IO.Path.GetFileName(File)}:{Line}" : System.IO.Path.GetFileName(File);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
