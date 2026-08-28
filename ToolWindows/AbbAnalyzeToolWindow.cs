using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace ABB.Analyze.VisualStudio.ToolWindows;

[Guid(WindowGuidString)]
public sealed class AbbAnalyzeToolWindow : ToolWindowPane
{
    public const string WindowGuidString = "9fe44b2e-2d07-4f44-b4f4-d0e8bb1665f9";
    public AbbAnalyzeToolWindow() : base(null) { Caption = "ABB AI Code Analyzer"; Content = new AbbAnalyzeControl(); }
    internal AbbAnalyzeControl View => (AbbAnalyzeControl)Content;
}
