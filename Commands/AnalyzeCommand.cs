using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using ABB.Analyze.VisualStudio.ToolWindows;

namespace ABB.Analyze.VisualStudio.Commands;

internal sealed class AnalyzeCommand
{
    public const int CommandId = 0x0101;
    public static readonly Guid CommandSet = new("286c58dc-c5bc-4c10-aeb3-e44aeca78d16");
    private readonly ABBAnalyzePackage _package;
    private AnalyzeCommand(ABBAnalyzePackage package, OleMenuCommandService service) { _package = package; service.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId))); }
    public static async Task InitializeAsync(ABBAnalyzePackage package, CancellationToken token) { await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token); var service = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService ?? throw new InvalidOperationException("Command service unavailable."); _ = new AnalyzeCommand(package, service); }
    private void Execute(object sender, EventArgs e) { ThreadHelper.JoinableTaskFactory.RunAsync(async delegate { AbbAnalyzeToolWindow window = await _package.ShowAnalyzeWindowAsync(_package.DisposalToken); await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); window.View.RunCopilotAnalysis(); }).FileAndForget("ABBAnalyze/AnalyzeCommand"); }
}
