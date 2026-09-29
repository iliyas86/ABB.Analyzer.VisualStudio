using ABB.Analyze.VisualStudio.Models;
using ABB.Analyze.VisualStudio.Services;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
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
    private readonly AzureDevOpsWorkItemService _azureDevOpsWorkItemService = new();
    private AzureDevOpsWorkItem? _azureDevOpsWorkItem;
    private List<StoryCheckpoint> _checkpoints = new();
    private string _checkpointEvaluationModel = string.Empty;

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
            await LoadAvailableModelsAsync(showDiscoveryErrors: false);
        }
        catch (Exception ex)
        {
            OperationStatus.Text =
                "Model loading failed: " +
                ex.Message;
        }
    }

    private async Task LoadAvailableModelsAsync(bool showDiscoveryErrors = true)
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
            if (availableModels.Count == 0)
            {
                LoadDefaultModel();
                OperationStatus.Text =
                    "Default model set. Select Refresh Models to load available Copilot models.";
            }
            else
            {
                OperationStatus.Text =
                    $"{availableModels.Count} Copilot model(s) loaded. Refresh Models to reload the list.";
            }
        }
        catch (Exception exception)
        {
            /*
             * Model discovery should not break Copilot analysis.
             * Leaving the selected model empty makes QUACK use its default.
             */
            LoadDefaultModel();

            OperationStatus.Text = showDiscoveryErrors
                ? "Default model set. Select Refresh Models to load available Copilot models. " + exception.Message
                : "Default model set. Select Refresh Models to load available Copilot models.";
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
                    process.Output);

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

        await LoadAvailableModelsAsync(showDiscoveryErrors: true);
    }

    private void ConnectAzureDevOpsButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        SetOperationComplete(ConnectAzureDevOpsCheck, false);
        try
        {
            _azureDevOpsWorkItemService.ConnectWithPersonalAccessToken(
                AzureDevOpsPatBox.Password);
            AzureDevOpsPatBox.Clear();
            FetchWorkItemButton.IsEnabled = true;
            SetOperationComplete(ConnectAzureDevOpsCheck, true);
            AzureDevOpsStatusText.Text = "PAT accepted for this Visual Studio session. Fetch a work item to validate access.";
        }
        catch (Exception exception)
        {
            SetOperationComplete(ConnectAzureDevOpsCheck, false);
            AzureDevOpsPatBox.Clear();
            AzureDevOpsStatusText.Text = "Could not connect: " + exception.Message;
        }
    }

    private async void FetchWorkItemButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (!int.TryParse(AzureDevOpsWorkItemIdText.Text, out int workItemId) || workItemId <= 0)
        {
            AzureDevOpsStatusText.Text = "Enter a positive work item ID.";
            return;
        }

        SetOperationComplete(FetchWorkItemCheck, false);
        FetchWorkItemButton.IsEnabled = false;
        AzureDevOpsStatusText.Text = "Fetching work item...";

        try
        {
            _azureDevOpsWorkItem = await _azureDevOpsWorkItemService.GetWorkItemAsync(
                AzureDevOpsOrganizationText.Text.Trim(),
                AzureDevOpsProjectText.Text.Trim(),
                workItemId,
                CancellationToken.None);

            WorkItemHeaderText.Text =
                $"{_azureDevOpsWorkItem.Type} {_azureDevOpsWorkItem.Id}: {_azureDevOpsWorkItem.Title}";
            WorkItemDescriptionText.Text = _azureDevOpsWorkItem.Description;
            WorkItemCriteriaText.Text =
                !string.IsNullOrWhiteSpace(_azureDevOpsWorkItem.AcceptanceCriteria)
                    ? _azureDevOpsWorkItem.AcceptanceCriteria
                    : _azureDevOpsWorkItem.ReproSteps;
                    SetOperationComplete(FetchWorkItemCheck, true);
                    SetOperationComplete(CreateCheckpointsCheck, false);
                    SetOperationComplete(DraftCheckpointsCheck, false);
                    SetOperationComplete(EvaluateCheckpointsCheck, false);
                    SetOperationComplete(LoadCurrentDiffCheck, false);
            CreateCheckpointsButton.IsEnabled = true;
            _checkpoints.Clear();
            CheckpointsGrid.ItemsSource = null;
            _checkpointEvaluationModel = string.Empty;
            UpdateCheckpointActions();
            UpdateCheckpointGateSummary();
            AzureDevOpsStatusText.Text = "Work item loaded. Review its criteria, then build the checkpoint list.";
        }
        catch (Exception exception)
        {
            SetOperationComplete(FetchWorkItemCheck, false);
            AzureDevOpsStatusText.Text = "Work item fetch failed: " + exception.Message;
        }
        finally
        {
            FetchWorkItemButton.IsEnabled = true;
        }
    }

    private void CreateCheckpointsButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (_azureDevOpsWorkItem == null)
        {
            return;
        }

        SetOperationComplete(CreateCheckpointsCheck, false);
        SetOperationComplete(DraftCheckpointsCheck, false);
        SetOperationComplete(EvaluateCheckpointsCheck, false);
        _checkpoints =
            AzureDevOpsWorkItemParser.CreateCheckpoints(_azureDevOpsWorkItem);

        _checkpointEvaluationModel = string.Empty;
        CheckpointsGrid.ItemsSource = _checkpoints;
        CheckpointsGrid.Items.Refresh();
        UpdateCheckpointActions();
        UpdateCheckpointGateSummary();
        SetOperationComplete(CreateCheckpointsCheck, _checkpoints.Count > 0);

        AzureDevOpsStatusText.Text =
            CheckpointsGrid.Items.Count == 0
                ? "No acceptance criteria, repro steps, or description text was available to turn into checkpoints."
                : $"Created {CheckpointsGrid.Items.Count} editable checkpoints. They have not been evaluated.";
    }

    private async void DraftCheckpointsButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (_azureDevOpsWorkItem == null || _operationCancellation != null)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            "The work-item title and text will be sent to GitHub Copilot to draft candidate gates. Continue?",
            "Draft Checkpoints with Copilot",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetOperationComplete(DraftCheckpointsCheck, false);
        SetOperationComplete(EvaluateCheckpointsCheck, false);
        _operationCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _operationCancellation.Token;
        string model = string.IsNullOrWhiteSpace(_selectedModel) ? "auto" : _selectedModel;
        AzureDevOpsStatusText.Text = $"Drafting candidate gates with Copilot ({model})...";
        SetBusy(true, "Drafting checkpoints with Copilot...");

        try
        {
            string prompt = CheckpointEvaluationService.BuildCheckpointDraftPrompt(_azureDevOpsWorkItem);
            string response = await CopilotCliRunner.RunPromptAsync(prompt, model, cancellationToken);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            _checkpoints = CheckpointEvaluationService.ParseCheckpointDraft(response);
            _checkpointEvaluationModel = string.Empty;
            CheckpointsGrid.ItemsSource = _checkpoints;
            CheckpointsGrid.Items.Refresh();
            UpdateCheckpointActions();
            UpdateCheckpointGateSummary();
            SetOperationComplete(DraftCheckpointsCheck, _checkpoints.Count > 0);
            AzureDevOpsStatusText.Text = _checkpoints.Count == 0
                ? "Copilot could not infer concrete gates. Add criteria or use the editable checkpoint builder."
                : $"Copilot drafted {_checkpoints.Count} gates. Review and edit them before evaluation.";
        }
        catch (OperationCanceledException)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetOperationComplete(DraftCheckpointsCheck, false);
            AzureDevOpsStatusText.Text = "Checkpoint drafting cancelled.";
        }
        catch (Exception exception)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetOperationComplete(DraftCheckpointsCheck, false);
            AzureDevOpsStatusText.Text = "Could not draft checkpoints: " + exception.Message;
        }
        finally
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            SetBusy(false, "Ready");
            UpdateCheckpointActions();
        }
    }

    private async void LoadCurrentDiffButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        SetOperationComplete(LoadCurrentDiffCheck, false);
        SetOperationComplete(EvaluateCheckpointsCheck, false);
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string repository = GitRepositoryService.GetCurrentRepository();
            AzureDevOpsStatusText.Text = "Loading saved Git changes...";

            string diff = await Task.Run(
                () => GitCommandService.GetWorkingTreeDiff(repository));

            SetCurrentDiffPreview(diff);
            ResetCheckpointEvaluations();
            SetOperationComplete(LoadCurrentDiffCheck, true);

            AzureDevOpsStatusText.Text = "Current saved Git diff loaded. Run Copilot evaluation to update the gate.";
        }
        catch (Exception exception)
        {
            SetOperationComplete(LoadCurrentDiffCheck, false);
            AzureDevOpsStatusText.Text = "Could not load Git changes: " + exception.Message;
        }
    }

    private async void EvaluateCheckpointsButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        if (_checkpoints.Count == 0 || _azureDevOpsWorkItem == null || _operationCancellation != null)
        {
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            "Copilot will compare the work-item checkpoints with the current saved Git diff using the selected model. The work-item text and changed code will be sent to GitHub Copilot. It will assign Met, Not met, or Inconclusive and provide evidence for each result. Review the evidence before treating the gate as a decision. Continue?",
            "Confirm Copilot Checkpoint Evaluation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetOperationComplete(EvaluateCheckpointsCheck, false);
        _operationCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _operationCancellation.Token;
        string model = string.IsNullOrWhiteSpace(_selectedModel) ? "auto" : _selectedModel;

        ResetCheckpointEvaluations();
        CheckpointGateSummaryText.Text = "EVALUATING...";
        CheckpointGateSummaryText.Foreground = Brushes.Goldenrod;
        AzureDevOpsStatusText.Text = $"Evaluating checkpoints with Copilot ({model})...";
        SetBusy(true, "Copilot checkpoint evaluation running...");

        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            string repository = GitRepositoryService.GetCurrentRepository();

            string diff = await Task.Run(
                () => GitCommandService.GetWorkingTreeDiff(repository),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(diff))
            {
                throw new InvalidOperationException(
                    "No staged, unstaged, or untracked changes were found to evaluate.");
            }

            SetCurrentDiffPreview(diff);
            string prompt = CheckpointEvaluationService.BuildPrompt(
                _azureDevOpsWorkItem,
                _checkpoints,
                diff);

            string response = await CopilotCliRunner.RunPromptAsync(
                prompt,
                model,
                cancellationToken);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            CheckpointEvaluationService.ApplyResponse(response, _checkpoints, diff);
            CheckpointsGrid.Items.Refresh();
            _checkpointEvaluationModel = model;
            UpdateCheckpointGateSummary();
            SetOperationComplete(EvaluateCheckpointsCheck, true);
            AzureDevOpsStatusText.Text =
                $"Evaluation complete using {model}. Review the evidence before relying on the gate.";
        }
        catch (OperationCanceledException)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetOperationComplete(EvaluateCheckpointsCheck, false);
            ResetCheckpointEvaluations();
            AzureDevOpsStatusText.Text = "Copilot evaluation cancelled; checkpoints remain not evaluated.";
        }
        catch (Exception exception)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetOperationComplete(EvaluateCheckpointsCheck, false);
            ResetCheckpointEvaluations();
            AzureDevOpsStatusText.Text = "Copilot evaluation failed: " + exception.Message;
        }
        finally
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            SetBusy(false, "Ready");
            UpdateCheckpointActions();
        }
    }

    private void CheckpointsGrid_CellEditEnding(
    object sender,
    DataGridCellEditEndingEventArgs e)
    {
        SetOperationComplete(EvaluateCheckpointsCheck, false);
        _checkpointEvaluationModel = string.Empty;
        CheckpointGateSummaryText.Text = "REVIEW REQUIRED | checkpoint edited; re-evaluate";
        CheckpointGateSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(230, 180, 80));
        AzureDevOpsStatusText.Text =
            "Checkpoint edited. Run Copilot evaluation again to refresh the gate.";
    }

    private void SetCurrentDiffPreview(string diff)
    {
        const int maximumPreviewLength = 120000;
        CurrentDiffText.Text = diff.Length > maximumPreviewLength
            ? diff.Substring(0, maximumPreviewLength) + Environment.NewLine + "[Preview truncated]"
            : string.IsNullOrWhiteSpace(diff)
                ? "No saved staged, unstaged, or untracked changes were found."
                : diff;
    }

    private void ResetCheckpointEvaluations()
    {
        _checkpointEvaluationModel = string.Empty;
        foreach (StoryCheckpoint checkpoint in _checkpoints)
        {
            checkpoint.ResetEvaluation();
        }

        CheckpointsGrid.Items.Refresh();
        UpdateCheckpointGateSummary();
    }

    private void UpdateCheckpointGateSummary()
    {
        string summary = CheckpointEvaluationService.GetGateSummary(_checkpoints);
        CheckpointGateSummaryText.Text = string.IsNullOrWhiteSpace(_checkpointEvaluationModel)
            ? summary
            : $"{summary} | {_checkpointEvaluationModel}";
        CheckpointGateSummaryText.Foreground = summary.StartsWith("GATE PASSED", StringComparison.Ordinal)
            ? new SolidColorBrush(Color.FromRgb(115, 201, 145))
            : summary.StartsWith("GATE FAILED", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.FromRgb(240, 113, 120))
                : summary.StartsWith("REVIEW REQUIRED", StringComparison.Ordinal)
                    ? new SolidColorBrush(Color.FromRgb(230, 180, 80))
                    : Brushes.Gray;
    }

    private void UpdateCheckpointActions()
    {
        CreateCheckpointsButton.IsEnabled = _azureDevOpsWorkItem != null;
        DraftCheckpointsButton.IsEnabled =
            _operationCancellation == null && _azureDevOpsWorkItem != null;
        EvaluateCheckpointsButton.IsEnabled =
            _operationCancellation == null &&
            _azureDevOpsWorkItem != null &&
            _checkpoints.Count > 0;
    }

    private static void SetOperationComplete(
    System.Windows.Controls.TextBlock indicator,
    bool completed)
    {
        indicator.Visibility = completed
            ? Visibility.Visible
            : Visibility.Collapsed;
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

        ConnectAzureDevOpsButton.IsEnabled = !busy;
        FetchWorkItemButton.IsEnabled = !busy && _azureDevOpsWorkItemService.IsConnected;
        CreateCheckpointsButton.IsEnabled = !busy && _azureDevOpsWorkItem != null;
        DraftCheckpointsButton.IsEnabled = !busy && _azureDevOpsWorkItem != null;
        LoadCurrentDiffButton.IsEnabled = !busy;

        EvaluateCheckpointsButton.IsEnabled =
            !busy && _azureDevOpsWorkItem != null && _checkpoints.Count > 0;

        if (_operationCancellation == null)
        {
            RunMutationButton.IsEnabled =
                !busy;

            RunMutationChangedButton.IsEnabled =
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

    private void RunMutationTesting_Click(
    object sender,
    RoutedEventArgs e) => RunMutationTestingAsync(changedFilesOnly: false);

    private void RunMutationTestingChanged_Click(
    object sender,
    RoutedEventArgs e) => RunMutationTestingAsync(changedFilesOnly: true);

    private async void RunMutationTestingAsync(bool changedFilesOnly)
    {
        if (_operationCancellation != null)
        {
            OperationStatus.Text =
                "Another operation is already running.";

            return;
        }

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
            changedFilesOnly
                ? "Discovering modified files and starting Stryker.NET..."
                : "Discovering the test project and starting Stryker.NET...");

        // SetBusy may already disable these buttons, but setting them
        // explicitly prevents a second mutation run.
        RunMutationButton.IsEnabled =
            false;

        RunMutationChangedButton.IsEnabled =
            false;

        RunMutationButton.ToolTip =
            "Mutation testing is currently running.";

        RunMutationChangedButton.ToolTip =
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

            if (changedFilesOnly &&
                GitRepositoryService.GetChangedSourceFiles(repository).Count == 0)
            {
                throw new InvalidOperationException(
                    "No changed C# source files detected in the Git repository. " +
                    "Make changes to a .cs file or commit them to your branch.");
            }

            OperationStatus.Text =
                changedFilesOnly
                    ? "Running Stryker.NET mutation testing on modified files..."
                    : "Running Stryker.NET mutation testing...";

            _model.Repository =
                repository;

            MutationSummary summary =
                await MutationService.RunAsync(
                    repository,
                    _operationCancellation.Token,
                    changedFilesOnly);

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
                $"Mutation testing completed{(changedFilesOnly ? " for modified files" : string.Empty)}. " +
                $"Score: {summary.Score:0.00}%. " +
                $"Surviving mutants: {summary.Survived}.";

            if (summary.Survivors.Count > 0)
            {
                OperationStatus.Text =
                    "Analyzing surviving mutant descriptions...";

                await AnalyzeMutationDescriptionsAsync(
                    repository,
                    summary.Survivors,
                    _operationCancellation.Token);

                OperationStatus.Text =
                    $"Mutation testing completed{(changedFilesOnly ? " for modified files" : string.Empty)}. " +
                    $"Score: {summary.Score:0.00}%. " +
                    $"Surviving mutants: {summary.Survived}.";
            }
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

            SetBusy(
                false,
                OperationStatus.Text);

            RunMutationButton.IsEnabled =
                true;

            RunMutationChangedButton.IsEnabled =
                true;

            RunMutationButton.ToolTip =
                _mutationSummary == null
                    ? "Run Mutation Testing (Entire Solution)"
                    : "Run Mutation Testing Again (Entire Solution)";

            RunMutationChangedButton.ToolTip =
                _mutationSummary == null
                    ? "Run Mutation Testing (Modified Files Only)"
                    : "Run Mutation Testing Again (Modified Files Only)";

            UpdateGeneratePrButtonState();
        }
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

    private async Task AnalyzeMutationDescriptionsAsync(
        string repository,
        IEnumerable<SurvivingMutant> mutants,
        CancellationToken cancellationToken)
    {
        foreach (SurvivingMutant mutant in mutants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mutant.IsAnalyzing = true;
            mutant.AnalysisStatus = "Analyzing...";

            try
            {
                MutationAnalysisResult result =
                    await MutationAnalysisService.AnalyzeAsync(
                        repository,
                        mutant,
                        cancellationToken);

                mutant.Description = result.Description;
                mutant.AnalysisStatus = "Analyzed";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                mutant.Description =
                    "Unable to analyze this mutation: " + exception.Message;
                mutant.AnalysisStatus = "Analysis failed";
            }
            finally
            {
                mutant.IsAnalyzing = false;
            }
        }
    }

    private void SurvivorsGrid_PreviewMouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e)
    {
        var row =
            FindAncestor<DataGridRow>(
                e.OriginalSource as DependencyObject);

        if (row?.Item is not SurvivingMutant mutant)
        {
            return;
        }

        string repository =
            string.IsNullOrWhiteSpace(_model.Repository)
                ? GitRepositoryService.GetCurrentRepository()
                : _model.Repository;

        FileNavigationResult result =
            FileNavigationService.Open(
                repository,
                mutant.File,
                mutant.Line);

        OperationStatus.Text =
            result.Success
                ? $"Opened {Path.GetFileName(result.Path)} at line {mutant.Line}."
                : result.ErrorMessage;
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
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
