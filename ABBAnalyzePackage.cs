using ABB.Analyze.VisualStudio.Commands;
using ABB.Analyze.VisualStudio.ToolWindows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ABB.Analyze.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("ABB AI Code Analyzer", "Actionable QUACK review dashboard.", "0.9.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(AbbAnalyzeToolWindow), Style = VsDockStyle.Tabbed, Window = ToolWindowGuids80.Outputwindow, Orientation = ToolWindowOrientation.Bottom, DockedHeight = 220)]
[Guid(PackageGuidString)]
public sealed class ABBAnalyzePackage : AsyncPackage
{
    public const string PackageGuidString = "a03f44e4-8068-4c4a-a502-406f6c80f8f1";

    protected override async Task InitializeAsync(CancellationToken token, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(token, progress);
        await ShowWindowCommand.InitializeAsync(this, token);
        await RunLocalCommand.InitializeAsync(this, token);
        await AnalyzeCommand.InitializeAsync(this, token);
    }

    internal async Task<AbbAnalyzeToolWindow> ShowAnalyzeWindowAsync(CancellationToken token)
    {
        ToolWindowPane pane = await ShowToolWindowAsync(typeof(AbbAnalyzeToolWindow), 0, true, token);
        return pane as AbbAnalyzeToolWindow
            ?? throw new InvalidOperationException("ABB Analyzer window could not be created.");
    }
}
