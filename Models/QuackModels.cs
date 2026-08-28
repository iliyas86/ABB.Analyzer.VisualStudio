using ABB.Analyze.VisualStudio.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace ABB.Analyze.VisualStudio.Models;

[DataContract]
internal sealed class QuackCheckResult
{
    [DataMember(Name = "repository")] public string Repository { get; set; } = string.Empty;
    [DataMember(Name = "blocked")] public bool Blocked { get; set; }
    [DataMember(Name = "findings")] public List<QuackFinding> Findings { get; set; } = new();
    [DataMember(Name = "testGuidance")] public List<QuackTestGuidance> TestGuidance { get; set; } = new();
    [DataMember(Name = "aiReview")] public QuackAiReview? AiReview { get; set; }
    [DataMember(Name = "aiError")] public string? AiError { get; set; }
}

[DataContract]
internal sealed class QuackFinding
{
    [DataMember(Name = "severity")] public string Severity { get; set; } = string.Empty;
    [DataMember(Name = "source")] public string Source { get; set; } = string.Empty;
    [DataMember(Name = "rule")] public string Rule { get; set; } = string.Empty;
    [DataMember(Name = "file")] public string File { get; set; } = string.Empty;
    [DataMember(Name = "line")] public int Line { get; set; }
    [DataMember(Name = "message")] public string Message { get; set; } = string.Empty;
}

[DataContract]
internal sealed class QuackTestGuidance
{
    [DataMember(Name = "sourceFile")] public string SourceFile { get; set; } = string.Empty;
    [DataMember(Name = "testOrCommand")] public string TestOrCommand { get; set; } = string.Empty;
    [DataMember(Name = "status")] public string Status { get; set; } = string.Empty;
    [DataMember(Name = "recommendation")] public string Recommendation { get; set; } = string.Empty;
}

[DataContract]
internal sealed class QuackAiReview
{
    [DataMember(Name = "available")] public bool Available { get; set; }
    [DataMember(Name = "model")] public string Model { get; set; } = string.Empty;
    [DataMember(Name = "risk")] public string Risk { get; set; } = string.Empty;
    [DataMember(Name = "summary")] public string Summary { get; set; } = string.Empty;
    [DataMember(Name = "reasons")] public List<string> Reasons { get; set; } = new();
    [DataMember(Name = "testsToRun")] public List<string> TestsToRun { get; set; } = new();
    [DataMember(Name = "missingTests")] public List<string> MissingTests { get; set; } = new();
    [DataMember(Name = "reviewedAtUtc")] public string ReviewedAtUtc { get; set; } = string.Empty;
}

internal sealed class FindingRow
{
    public string Severity { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public string Location => Line > 0 ? $"{System.IO.Path.GetFileName(File)}:{Line}" : System.IO.Path.GetFileName(File);
    public string Message { get; set; } = string.Empty;
}

internal sealed class TestRow : INotifyPropertyChanged
{
    private string _sourceFile =
        string.Empty;

    private string _testOrCommand =
        string.Empty;

    private string _originalTestOrCommand =
        string.Empty;

    private string _status =
        string.Empty;

    private string _displayStatus =
        string.Empty;

    private string _recommendation =
        string.Empty;

    private bool _canRun;

    private bool _isUserModified;

    public event PropertyChangedEventHandler?
        PropertyChanged;

    public string SourceFile
    {
        get => _sourceFile;

        set
        {
            if (_sourceFile == value)
            {
                return;
            }

            _sourceFile =
                value ?? string.Empty;

            OnPropertyChanged();
        }
    }

    public string TestOrCommand
    {
        get => _testOrCommand;

        set
        {
            string newValue =
                value ?? string.Empty;

            if (_testOrCommand == newValue)
            {
                return;
            }

            _testOrCommand =
                newValue;

            IsUserModified =
                !string.Equals(
                    TestCommandService.NormalizeCommand(
                        _testOrCommand),
                    TestCommandService.NormalizeCommand(
                        OriginalTestOrCommand),
                    StringComparison.Ordinal);

            /*
             * Enable Run only when the edited text is still
             * a valid supported command.
             *
             * This is safer than enabling Run for arbitrary text.
             */
            CanRun =
                TestCommandService.IsApproved(
                    _testOrCommand);

            if (IsUserModified)
            {
                Status =
                    CanRun
                        ? "custom_command"
                        : "custom_value";

                DisplayStatus =
                    CanRun
                        ? "Custom command"
                        : "Edited value";
            }

            OnPropertyChanged();
        }
    }

    public string OriginalTestOrCommand
    {
        get => _originalTestOrCommand;

        set
        {
            if (_originalTestOrCommand == value)
            {
                return;
            }

            _originalTestOrCommand =
                value ?? string.Empty;

            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;

        set
        {
            if (_status == value)
            {
                return;
            }

            _status =
                value ?? string.Empty;

            OnPropertyChanged();
        }
    }

    public string DisplayStatus
    {
        get => _displayStatus;

        set
        {
            if (_displayStatus == value)
            {
                return;
            }

            _displayStatus =
                value ?? string.Empty;

            OnPropertyChanged();
        }
    }

    public string Recommendation
    {
        get => _recommendation;

        set
        {
            if (_recommendation == value)
            {
                return;
            }

            _recommendation =
                value ?? string.Empty;

            OnPropertyChanged();
        }
    }

    public bool CanRun
    {
        get => _canRun;

        set
        {
            if (_canRun == value)
            {
                return;
            }

            _canRun =
                value;

            OnPropertyChanged();
        }
    }

    public bool IsUserModified
    {
        get => _isUserModified;

        set
        {
            if (_isUserModified == value)
            {
                return;
            }

            _isUserModified =
                value;

            OnPropertyChanged();
        }
    }

    public bool CanOpen =>
        !string.IsNullOrWhiteSpace(
            SourceFile);

    public bool CanCopy =>
        !string.IsNullOrWhiteSpace(
            TestOrCommand) ||
        !string.IsNullOrWhiteSpace(
            SourceFile);

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}
