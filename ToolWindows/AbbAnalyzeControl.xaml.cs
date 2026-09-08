using ABB.Analyze.VisualStudio.Models;
using ABB.Analyze.VisualStudio.Services;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ABB.Analyze.VisualStudio.ToolWindows;

public partial class AbbAnalyzeControl : UserControl
{
    private DashboardModel _model = new();
    private CancellationTokenSource? _operationCancellation;

    public AbbAnalyzeControl()
    {
        InitializeComponent();

        Apply(_model);

        SetMutationViewState(
            MutationViewState.NotRun);

        ViewComboBox.SelectedIndex =
            0;

        Loaded +=
            AbbAnalyzeControl_Loaded;
    }

    public void RunLocalCheck() => StartAnalysis(false);
    public void RunCopilotAnalysis() => StartAnalysis(true);

    private void LocalButton_Click(object sender, RoutedEventArgs e) => RunLocalCheck();
    private void CopilotButton_Click(object sender, RoutedEventArgs e) => RunCopilotAnalysis();
    private void CancelButton_Click(object sender, RoutedEventArgs e) => _operationCancellation?.Cancel();
    private void CopyJsonButton_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrWhiteSpace(_model.RawJson)) Clipboard.SetText(_model.RawJson); }
    private void CopySummaryButton_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(BuildMarkdownSummary());
    private string _selectedModel = string.Empty;
    private bool _modelsLoaded;
    private MutationSummary? _mutationSummary;
    private bool _mutationRunInProgress;


    internal enum MutationViewState
    {
        NotRun,
        Running,
        Completed
    }

    private void StartAnalysis(bool copilot)
    {
        if (_operationCancellation != null) return;
        ThreadHelper.JoinableTaskFactory.RunAsync(async delegate { await ExecuteAnalysisAsync(copilot); })
            .FileAndForget(copilot ? "ABBAnalyze/Copilot" : "ABBAnalyze/Check");
    }

    private void ViewComboBox_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
    {
        if (Tabs == null ||
            ViewComboBox.SelectedItem is not ComboBoxItem selectedItem)
        {
            return;
        }

        if (!int.TryParse(
                selectedItem.Tag?.ToString(),
                out int selectedIndex))
        {
            return;
        }

        if (selectedIndex < 0 ||
            selectedIndex >= Tabs.Items.Count)
        {
            return;
        }

        Tabs.SelectedIndex =
            selectedIndex;
    }

    private async void AbbAnalyzeControl_Loaded(
    object sender,
    RoutedEventArgs e)
    {
        if (_modelsLoaded)
        {
            return;
        }

        try
        {
            await LoadAvailableModelsAsync();
        }
        catch (Exception ex)
        {
            OperationStatus.Text =
                "Model loading failed: " +
                ex.Message;
        }
    }

    private async Task LoadAvailableModelsAsync()
    {
        RefreshModelsButton.IsEnabled = false;
        ModelComboBox.IsEnabled = false;

        try
        {
            string repository =
                GitRepositoryService.GetCurrentRepository();

            using var cancellation =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(30));

            ProcessResult processResult =
                await QuackRunner.GetModelsAsync(
                    repository,
                    cancellation.Token);

            if (processResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(
                        processResult.Error)
                        ? processResult.Output
                        : processResult.Error);
            }

            List<CopilotModelInfo> availableModels = ParseModels(processResult.Output);

            ModelComboBox.ItemsSource =
                availableModels;

            string currentModel =
                ParseCurrentModel(processResult.Output);

            string preferredModel =
                !string.IsNullOrWhiteSpace(_selectedModel)
                    ? _selectedModel
                    : currentModel;

            ModelComboBox.SelectedValue =
                preferredModel;

            if (ModelComboBox.SelectedItem == null &&
                availableModels.Count > 0)
            {
                ModelComboBox.SelectedIndex = 0;
            }

            if (ModelComboBox.SelectedItem is CopilotModelInfo selectedModel)
            {
                _selectedModel =
                    selectedModel.Id;
            }

            _modelsLoaded = true;

            OperationStatus.Text =
                availableModels.Count == 0
                    ? "No reachable Copilot models were returned."
                    : $"{availableModels.Count} Copilot model(s) available.";

            if (ModelComboBox.SelectedItem == null &&
                availableModels.Count > 0)
            {
                ModelComboBox.SelectedIndex = 0;
            }

            _modelsLoaded = true;

            OperationStatus.Text =
                availableModels.Count == 0
                    ? "No selectable Copilot models were returned."
                    : $"{availableModels.Count} Copilot model(s) available.";
        }
        catch (Exception exception)
        {
            /*
             * Model discovery should not break Copilot analysis.
             * Leaving the selected model empty makes QUACK use its default.
             */
            LoadDefaultModel();

            OperationStatus.Text =
                "Model discovery unavailable. " +
                "QUACK will use its default model. " +
                exception.Message;
            System.Diagnostics.Debug.WriteLine(exception);
        }
        finally
        {
            RefreshModelsButton.IsEnabled = true;
            ModelComboBox.IsEnabled = true;
        }
    }

    private static string ParseCurrentModel(
    string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        string[] lines =
            output.Split(
                new[]
                {
                "\r\n",
                "\n"
                },
                StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            string trimmedLine =
                line.Trim();

            if (!trimmedLine.StartsWith(
                    "Completion model:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value =
                trimmedLine
                    .Substring(
                        "Completion model:".Length)
                    .Trim();

            int sourceInformationIndex =
                value.IndexOf(
                    " (",
                    StringComparison.Ordinal);

            if (sourceInformationIndex >= 0)
            {
                value =
                    value.Substring(
                        0,
                        sourceInformationIndex);
            }

            return value.Trim();
        }

        return string.Empty;
    }

    private static List<CopilotModelInfo> ParseModels(
    string output)
    {
        var models =
            new List<CopilotModelInfo>();

        string[] lines =
            output.Split(
                new[]
                {
                "\r\n",
                "\n"
                },
                StringSplitOptions.RemoveEmptyEntries);

        foreach (string line in lines)
        {
            if (!line.StartsWith(
                    "Reachable models:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value =
                line.Substring(
                    "Reachable models:".Length)
                .Trim();

            string[] modelNames =
                value.Split(
                    new[] { ',' },
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (string model in modelNames)
            {
                string trimmed =
                    model.Trim();

                models.Add(
                    new CopilotModelInfo
                    {
                        Id = trimmed,
                        DisplayName = trimmed,
                        Provider = "Copilot",
                        Available = true
                    });
            }
        }

        return models;
    }

    private void LoadDefaultModel()
    {
        var models =
            new List<CopilotModelInfo>
            {
            new CopilotModelInfo
            {
                Id = string.Empty,
                DisplayName = "QUACK Default",
                Provider = string.Empty,
                Available = true
            }
            };

        ModelComboBox.ItemsSource =
            models;

        ModelComboBox.SelectedIndex =
            0;

        _selectedModel =
            string.Empty;
    }

    private static CopilotModelList DeserializeModelList(
    string json)
    {
        using var stream =
            new MemoryStream(
                Encoding.UTF8.GetBytes(json));

        var serializer =
            new DataContractJsonSerializer(
                typeof(CopilotModelList));

        return (CopilotModelList)(
            serializer.ReadObject(stream)
            ?? throw new InvalidDataException(
                "QUACK returned an empty model list."));
    }

    private async Task ExecuteAnalysisAsync(
    bool copilot)
    {
        _operationCancellation =
            new CancellationTokenSource();

        SetBusy(
            true,
            copilot
                ? "Analyzing with Copilot..."
                : "Running local check...");

        try
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            string repository =
                GitRepositoryService.GetCurrentRepository();

            string branch =
                GitRepositoryService.GetCurrentBranch(
                    repository);

            await OfferToStageMissingTestFilesAsync(
                repository);

            ProcessResult process;

            if (copilot)
            {
                process =
                    await QuackRunner.AnalyzeAsync(
                        repository,
                        _selectedModel,
                        _operationCancellation.Token);
            }
            else
            {
                process =
                    await QuackRunner.CheckAsync(
                        repository,
                        _operationCancellation.Token);
            }

            if (string.IsNullOrWhiteSpace(process.Output))
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(process.Error)
                        ? "QUACK did not return any output."
                        : process.Error);
            }

            QuackCheckResult result =
                JsonServices.Deserialize(
                    process.Output);

            DashboardModel next =
                JsonServices.Build(
                    result,
                    branch,
                    process.Output,
                    repository);

            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            _model =
                next;

            Apply(_model);

            OperationStatus.Text =
                copilot
                    ? BuildCopilotCompletedMessage()
                    : "Local check completed.";
        }
        catch (OperationCanceledException)
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            OperationStatus.Text =
                "Operation cancelled. Previous review preserved.";
        }
        catch (Exception exception)
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            OperationStatus.Text =
                "Error: " +
                exception.Message;
        }
        finally
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            _operationCancellation?.Dispose();

            _operationCancellation =
                null;

            SetBusy(
                false,
                "Ready");
        }
    }

    private async Task OfferToStageMissingTestFilesAsync(
    string repository)
    {
        await ThreadHelper.JoinableTaskFactory
            .SwitchToMainThreadAsync();

        IReadOnlyList<string> unstagedTestFiles;

        try
        {
            unstagedTestFiles =
                TestFileStagingService.FindUnstagedTestFiles(
                    repository);
        }
        catch
        {
            // If git detection fails for any reason, fall through and let
            // QUACK run against whatever is currently staged.
            return;
        }

        if (unstagedTestFiles.Count == 0)
        {
            return;
        }

        string fileList =
            string.Join(
                Environment.NewLine,
                unstagedTestFiles);

        MessageBoxResult stageResult =
            MessageBox.Show(
                "The following existing test file(s) match staged source changes " +
                "but are not staged themselves. QUACK only analyzes staged changes, " +
                "so this can cause a false \"Coverage gap\" result:" +
                Environment.NewLine + Environment.NewLine +
                fileList +
                Environment.NewLine + Environment.NewLine +
                "Stage these test file(s) now before running the analysis?",
                "ABB AI Code Analyzer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (stageResult != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (string testFile in unstagedTestFiles)
        {
            GitRepositoryService.StageFile(
                repository,
                testFile);
        }

        OperationStatus.Text =
            "Staged missing test file(s) before analysis.";
    }

    private string BuildCopilotCompletedMessage()
    {
        if (string.IsNullOrWhiteSpace(_selectedModel))
        {
            return
                "Copilot analysis completed using the QUACK default model.";
        }

        return
            $"Copilot analysis completed using {_selectedModel}.";
    }

    private void ModelComboBox_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
    {
        if (ModelComboBox.SelectedItem
            is CopilotModelInfo selectedModel)
        {
            _selectedModel =
                selectedModel.Id;

            OperationStatus.Text =
                $"Selected model: {selectedModel.DisplayName}";
        }
    }

    private async void RefreshModelsButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        _modelsLoaded = false;

        await LoadAvailableModelsAsync();
    }

    private async void RunTest_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext
            is not TestRow row)
        {
            return;
        }

        string command =
            TestCommandService.NormalizeCommand(
                row.TestOrCommand);

        bool canRun =
        row.IsUserModified ||
        TestCommandService.IsApproved(command);

        if (!canRun)
        {
            MessageBox.Show(
                "The command is not executable.",
                "ABB AI Code Analyzer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        OperationStatus.Text =
            "Running test command...";

        try
        {
            string repository =
                GitRepositoryService.GetCurrentRepository();

            string result =
                await TestCommandService.RunAsync(
                    command,
                    repository,
                    CancellationToken.None);

            TestResultsText.Text =
                result;

            SelectView(3);

            OperationStatus.Text =
                "Test execution completed.";
        }
        catch (Exception exception)
        {
            TestResultsText.Text =
                exception.ToString();

            SelectView(3);

            OperationStatus.Text =
                "Test execution failed.";
        }
    }

    private void SelectView(
    int selectedIndex)
    {
        if (selectedIndex < 0 ||
            selectedIndex >= Tabs.Items.Count)
        {
            return;
        }

        Tabs.SelectedIndex =
            selectedIndex;

        if (ViewComboBox != null &&
            selectedIndex < ViewComboBox.Items.Count)
        {
            ViewComboBox.SelectedIndex =
                selectedIndex;
        }
    }


    private void OpenTestSource_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (!(((FrameworkElement)sender)
              .DataContext
              is TestRow test))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                test.SourceFile))
        {
            OperationStatus.Text =
                "This test item contains an executable command, " +
                "but no source-file path.";

            return;
        }

        OpenSourceFile(
            test.SourceFile);
    }

    private void CopyTestCommand_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TestRow row)
        {
            string value = string.IsNullOrWhiteSpace(row.TestOrCommand) ? row.SourceFile : row.TestOrCommand;
            if (!string.IsNullOrWhiteSpace(value)) Clipboard.SetText(value);
        }
    }
    private void FindingsGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!(FindingsGrid.SelectedItem
              is FindingRow finding))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(
                finding.File))
        {
            OperationStatus.Text =
                "This Copilot finding does not contain " +
                "a source-file location.";

            return;
        }

        OpenSourceFile(
            finding.File,
            finding.Line);
    }

    private void AffectedFilesList_DoubleClick(
    object sender,
    System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!(AffectedFilesList.SelectedItem is string path))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            OperationStatus.Text =
                "No affected-file path is available.";

            return;
        }

        OpenSourceFile(path);
    }

    private string BuildMarkdownSummary()
    {
        var text = new StringBuilder();
        text.AppendLine("## ABB AI Code Analyzer Review").AppendLine();
        text.AppendLine($"**Risk:** {_model.Risk}  ");
        text.AppendLine($"**Repository:** {_model.Repository}  ").AppendLine();
        text.AppendLine("### Summary").AppendLine().AppendLine(_model.Summary).AppendLine();
        if (_model.Reasons.Count > 0)
        {
            text.AppendLine("### Reasons").AppendLine();
            foreach (string reason in _model.Reasons) text.AppendLine("- " + reason);
            text.AppendLine();
        }
        if (_model.Tests.Count > 0)
        {
            text.AppendLine("### Test Guidance").AppendLine();
            foreach (TestRow test in _model.Tests) text.AppendLine($"- **{test.DisplayStatus}:** {test.SourceFile} {test.TestOrCommand}".Trim());
        }
        return text.ToString();
    }

    private void Apply(DashboardModel model)
    {
        RepositoryText.Text = $"Repository: {model.Repository}  [{model.Branch}]  |  {model.Status}";
        RiskText.Text = model.Risk;
        RiskBadge.Background = (Brush)new BrushConverter().ConvertFromString(model.RiskColour);
        SummaryText.Text = model.Summary;
        ReasonsList.ItemsSource = model.Reasons;
        AffectedFilesList.ItemsSource = model.AffectedFiles;
        MetadataText.Text = model.Metadata;
        FindingsGrid.ItemsSource = model.Findings;
        FindingsGrid.Visibility = model.Findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FindingsEmpty.Visibility = model.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TestsGrid.ItemsSource = model.Tests;
        TestsGrid.Visibility = model.Tests.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TestsEmpty.Visibility = model.Tests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RawText.Text = model.RawJson;
    }

    private void SetBusy(
    bool busy,
    string status)
    {
        LocalButton.IsEnabled =
            !busy;

        CopilotButton.IsEnabled =
            !busy;

        CancelButton.IsEnabled =
            busy;

        RefreshModelsButton.IsEnabled =
            !busy;

        ModelComboBox.IsEnabled =
            !busy;

        ViewComboBox.IsEnabled =
            true;

        GeneratePrButton.IsEnabled =
            !busy;

        if (_operationCancellation == null)
        {
            RunMutationButton.IsEnabled =
                !busy;
        }

        OperationStatus.Text =
            status;
    }

    private void GeneratePrDescription_Click(
    object sender,
    RoutedEventArgs e)
    {
        string description =
            PrDescriptionService.Generate(
                _model,
                _mutationSummary);

        Clipboard.SetText(description);
        OperationStatus.Text =
            "PR description copied to clipboard.";
    }

    private async void RunMutationTesting_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (_mutationRunInProgress)
        {
            OperationStatus.Text =
                "Mutation testing is already running.";

            return;
        }

        if (_operationCancellation != null)
        {
            OperationStatus.Text =
                "Another operation is already running.";

            return;
        }

        _mutationRunInProgress =
            true;

        _operationCancellation =
            new CancellationTokenSource();

        DateTime startedAt =
            DateTime.Now;

        SetMutationViewState(
            MutationViewState.Running);

        // Mutation Score is view index 5.
        SelectView(5);

        SetBusy(
            true,
            "Discovering the test project and starting Stryker.NET...");

        // SetBusy may already disable this button, but setting it
        // explicitly prevents a second mutation run.
        RunMutationButton.IsEnabled =
            false;

        RunMutationButton.ToolTip =
            "Mutation testing is currently running.";

        try
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync(
                    _operationCancellation.Token);

            string repository =
                GitRepositoryService.GetCurrentRepository();

            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new InvalidOperationException(
                    "No Git repository is currently available.");
            }

            OperationStatus.Text =
                "Running Stryker.NET mutation testing...";

            MutationSummary summary =
                await MutationService.RunAsync(
                    repository,
                    _operationCancellation.Token);

            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync(
                    _operationCancellation.Token);

            TimeSpan duration =
                DateTime.Now - startedAt;

            _mutationSummary =
                summary;

            MutationScoreText.Text =
                summary.Status;

            MutationStatsText.Text =
                $"Killed: {summary.Killed} | " +
                $"Survived: {summary.Survived} | " +
                $"Timed out: {summary.TimedOut} | " +
                $"No coverage: {summary.NoCoverage} | " +
                $"Ignored: {summary.Ignored} | " +
                $"Compile errors: {summary.CompileErrors} | " +
                $"Duration: {FormatDuration(duration)}";

            MutationStatsText.ToolTip =
                string.IsNullOrWhiteSpace(summary.ReportPath)
                    ? null
                    : summary.ReportPath;

            SurvivorsGrid.ItemsSource =
                summary.Survivors;

            SetMutationViewState(
                MutationViewState.Completed);

            SelectView(5);

            OperationStatus.Text =
                $"Mutation testing completed. " +
                $"Score: {summary.Score:0.00}%. " +
                $"Surviving mutants: {summary.Survived}.";
        }
        catch (OperationCanceledException)
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            SetMutationViewState(
                _mutationSummary == null
                    ? MutationViewState.NotRun
                    : MutationViewState.Completed);

            OperationStatus.Text =
                "Mutation testing was cancelled. " +
                "The previous mutation result has been preserved.";
        }
        catch (Exception exception)
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            SetMutationViewState(
                _mutationSummary == null
                    ? MutationViewState.NotRun
                    : MutationViewState.Completed);

            TestResultsText.Text =
                exception.ToString();

            SelectView(3);

            OperationStatus.Text =
                "Mutation testing failed: " +
                exception.Message;
        }
        finally
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            _operationCancellation?.Dispose();

            _operationCancellation =
                null;

            _mutationRunInProgress =
                false;

            SetBusy(
                false,
                OperationStatus.Text);

            RunMutationButton.IsEnabled =
                true;

            RunMutationButton.ToolTip =
                _mutationSummary == null
                    ? "Run Mutation Testing"
                    : "Run Mutation Testing Again";

            UpdateGeneratePrButtonState();
        }
    }

    private async void RunMutationTestingOnModified_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (_mutationRunInProgress)
        {
            OperationStatus.Text =
                "Mutation testing is already running.";

            return;
        }

        if (_operationCancellation != null)
        {
            OperationStatus.Text =
                "Another operation is already running.";

            return;
        }

        _mutationRunInProgress =
            true;

        _operationCancellation =
            new CancellationTokenSource();

        DateTime startedAt =
            DateTime.Now;

        SetMutationViewState(
            MutationViewState.Running);

        // Mutation Score is view index 5.
        SelectView(5);

        SetBusy(
            true,
            "Discovering modified files and starting Stryker.NET...");

        // Set it explicitly to prevent a second mutation run.
        RunMutationOnModifiedButton.IsEnabled =
            false;

        RunMutationOnModifiedButton.ToolTip =
            "Mutation testing is currently running.";

        try
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync(
                    _operationCancellation.Token);

            string repository =
                GitRepositoryService.GetCurrentRepository();

            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new InvalidOperationException(
                    "No Git repository is currently available.");
            }

            string[] modifiedFiles =
                GitRepositoryService.GetModifiedFiles(repository);

            if (modifiedFiles.Length == 0)
            {
                throw new InvalidOperationException(
                    "No modified or staged files found in the current repository.");
            }

            OperationStatus.Text =
                $"Discovering test mappings for {modifiedFiles.Length} modified file(s)...";

            // Get mapped test commands from QUACK analysis
            string[] mappedTestCommands = await GetMappedTestCommandsAsync(
                repository,
                modifiedFiles,
                _operationCancellation.Token);

            if (mappedTestCommands.Length == 0)
            {
                OperationStatus.Text =
                    $"No test mappings found for modified files. Running mutations on {modifiedFiles.Length} file(s) without test filtering...";
            }
            else
            {
                OperationStatus.Text =
                    $"Running Stryker.NET with {mappedTestCommands.Length} mapped test(s)...";
            }

            OperationStatus.Text =
                "Scoped mutation files: " +
                string.Join(", ", modifiedFiles.Take(5)) +
                (modifiedFiles.Length > 5 ? " ..." : string.Empty) +
                $" | mapped tests: {mappedTestCommands.Length}";

            MutationSummary summary =
                await MutationService.RunAsync(
                    repository,
                    modifiedFiles,
                    mappedTestCommands,
                    _operationCancellation.Token);

            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync(
                    _operationCancellation.Token);

            TimeSpan duration =
                DateTime.Now - startedAt;

            _mutationSummary =
                summary;

            MutationScoreText.Text =
                summary.Status;

            MutationStatsText.Text =
                $"Killed: {summary.Killed} | " +
                $"Survived: {summary.Survived} | " +
                $"Timed out: {summary.TimedOut} | " +
                $"No coverage: {summary.NoCoverage} | " +
                $"Ignored: {summary.Ignored} | " +
                $"Compile errors: {summary.CompileErrors} | " +
                $"Duration: {FormatDuration(duration)}";

            MutationStatsText.ToolTip =
                string.IsNullOrWhiteSpace(summary.ReportPath)
                    ? null
                    : summary.ReportPath;

            SurvivorsGrid.ItemsSource =
                summary.Survivors;

            SetMutationViewState(
                MutationViewState.Completed);

            SelectView(5);

            bool cacheHit =
                !string.IsNullOrWhiteSpace(summary.ReportPath) &&
                summary.ReportPath.StartsWith("[CACHE HIT]", StringComparison.Ordinal);

            OperationStatus.Text =
                cacheHit
                    ? $"Mutation testing reused cached result for scoped files. Score: {summary.Score:0.00}%. Surviving mutants: {summary.Survived}."
                    : $"Mutation testing on modified files completed. Score: {summary.Score:0.00}%. Surviving mutants: {summary.Survived}.";
        }
        catch (OperationCanceledException)
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            SetMutationViewState(
                _mutationSummary == null
                    ? MutationViewState.NotRun
                    : MutationViewState.Completed);

            OperationStatus.Text =
                "Mutation testing was cancelled. " +
                "The previous mutation result has been preserved.";
        }
        catch (Exception exception)
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            SetMutationViewState(
                _mutationSummary == null
                    ? MutationViewState.NotRun
                    : MutationViewState.Completed);

            TestResultsText.Text =
                exception.ToString();

            SelectView(3);

            OperationStatus.Text =
                "Mutation testing failed: " +
                exception.Message;
        }
        finally
        {
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            _operationCancellation?.Dispose();

            _operationCancellation =
                null;

            _mutationRunInProgress =
                false;

            SetBusy(
                false,
                OperationStatus.Text);

            RunMutationOnModifiedButton.IsEnabled =
                true;

            RunMutationOnModifiedButton.ToolTip =
                _mutationSummary == null
                    ? "Run Mutation Testing on Modified Files"
                    : "Run Mutation Testing on Modified Files Again";

            UpdateGeneratePrButtonState();
        }
    }

    private async Task<string[]> GetMappedTestCommandsAsync(
        string repository,
        string[] modifiedFiles,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get QUACK analysis to find test mappings
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            ProcessResult checkResult = await QuackRunner.CheckAsync(repository, cancellationToken);

            if (checkResult.ExitCode != 0 || string.IsNullOrWhiteSpace(checkResult.Output))
            {
                return Array.Empty<string>();
            }

            QuackCheckResult result = JsonServices.Deserialize(checkResult.Output);

            if (result?.TestGuidance == null || result.TestGuidance.Count == 0)
            {
                return Array.Empty<string>();
            }

            // Map source files to their test commands
            var modifiedFileSet = new HashSet<string>(
                modifiedFiles.Select(f => 
                    RemoveExtension(Path.GetFileName(f))),
                StringComparer.OrdinalIgnoreCase);

            var mappedTests = new List<string>();

            foreach (var testGuidance in result.TestGuidance)
            {
                // Check if this test guidance is for one of our modified files
                string sourceFileName = RemoveExtension(Path.GetFileName(testGuidance.SourceFile));

                if (modifiedFileSet.Contains(sourceFileName))
                {
                    // Extract test method/class name from the test command
                    string testFilter = ExtractTestFilter(testGuidance.TestOrCommand);
                    if (!string.IsNullOrWhiteSpace(testFilter))
                    {
                        mappedTests.Add(testFilter);
                    }
                }
            }

            return mappedTests.ToArray();
        }
        catch
        {
            // If we can't get test mappings, continue without them
            return Array.Empty<string>();
        }
    }

    private static string RemoveExtension(string fileName)
    {
        if (fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.Substring(0, fileName.Length - 3);
        }
        return fileName;
    }

    private static string ExtractTestFilter(string testOrCommand)
    {
        // Extract test name from command like:
        // "dotnet test /path/to/Test.csproj --filter "FullyQualifiedName~TestClass.TestMethod""
        if (string.IsNullOrWhiteSpace(testOrCommand))
        {
            return string.Empty;
        }

        // Look for --filter parameter
        var match = Regex.Match(
            testOrCommand,
            @"--filter\s+[""']?([^""']+)[""']?");

        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // If no filter found, try to extract test class/method name
        // Look for .Test.csproj pattern and assume test class name
        if (testOrCommand.Contains(".Test"))
        {
            // Extract the test file/class name
            var testMatch = Regex.Match(
                testOrCommand,
                @"Test[s]?\.csproj");
            if (testMatch.Success)
            {
                return testOrCommand;
            }
        }

        return testOrCommand;
    }

    private void UpdateGeneratePrButtonState()
    {
        bool hasReview =
            _model != null &&
            !string.IsNullOrWhiteSpace(
                _model.Summary);

        GeneratePrButton.IsEnabled =
            hasReview &&
            _operationCancellation == null;
    }

    private static string FormatDuration(
    TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(
                @"hh\:mm\:ss");
        }

        return duration.ToString(
            @"mm\:ss");
    }

    private void SurvivorsGrid_DoubleClick(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SurvivorsGrid.SelectedItem is
            SurvivingMutant mutant)
        {
            FileNavigationService.Open(
                _model.Repository,
                mutant.File,
                mutant.Line);
        }
    }

    private void OpenSourceFile(
    string relativePath,
    int line = 0)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        FileNavigationResult result =
            FileNavigationService.Open(
                _model.Repository,
                relativePath,
                line);

        if (result.Success)
        {
            OperationStatus.Text =
                line > 0
                    ? $"Opened {Path.GetFileName(result.Path)} at line {line}."
                    : $"Opened {Path.GetFileName(result.Path)}.";

            return;
        }

        OperationStatus.Text =
            result.ErrorMessage;
    }

    private void TestCommand_TextChanged(
    object sender,
    TextChangedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext
            is not TestRow row)
        {
            return;
        }

        bool modified =
            !string.Equals(
                row.TestOrCommand,
                row.OriginalTestOrCommand,
                StringComparison.Ordinal);

        row.IsUserModified =
            modified;

        if (modified)
        {
            row.CanRun = true;

            row.Status =
                "custom_command";

            row.DisplayStatus =
                "Custom Command";
        }
    }

    private void SetMutationViewState(
    MutationViewState state)
    {
        MutationEmptyPanel.Visibility =
            state == MutationViewState.NotRun
                ? Visibility.Visible
                : Visibility.Collapsed;

        MutationRunningPanel.Visibility =
            state == MutationViewState.Running
                ? Visibility.Visible
                : Visibility.Collapsed;

        MutationResultPanel.Visibility =
            state == MutationViewState.Completed
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

}
