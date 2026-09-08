namespace ABB.Analyze.VisualStudio.Models;

/// <summary>
/// Describes a test case mapped to a source file via QUACK test guidance.
/// </summary>
internal sealed class TestCaseInfo
{
    public string SourceFile { get; set; } = string.Empty;

    public string Recommendation { get; set; } = string.Empty;

    public string TestFilter { get; set; } = string.Empty;

    public string TestProjectPath { get; set; } = string.Empty;

    public string FullyQualifiedName { get; set; } = string.Empty;

    public string TestClassName { get; set; } = string.Empty;
}
