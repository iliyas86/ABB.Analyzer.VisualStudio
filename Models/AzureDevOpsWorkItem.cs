using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ABB.Analyze.VisualStudio.Models;

internal sealed class AzureDevOpsWorkItem
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AcceptanceCriteria { get; set; } = string.Empty;
    public string ReproSteps { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

internal sealed class StoryCheckpoint : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string _status = "Not evaluated";
    private string _evidence = string.Empty;

    public int Number { get; set; }
    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            ResetEvaluation();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public string Source { get; set; } = string.Empty;
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public string Evidence
    {
        get => _evidence;
        private set
        {
            if (_evidence == value)
            {
                return;
            }

            _evidence = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Evidence)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyEvaluation(string status, string evidence)
    {
        Status = status;
        Evidence = evidence;
    }

    public void ResetEvaluation()
    {
        Status = "Not evaluated";
        Evidence = string.Empty;
    }
}

internal static class AzureDevOpsWorkItemParser
{
    public static AzureDevOpsWorkItem Parse(string json)
    {
        JObject payload = JObject.Parse(json);
        JObject fields = payload["fields"] as JObject ?? new JObject();

        return new AzureDevOpsWorkItem
        {
            Id = (int?)payload["id"] ?? 0,
            Type = ReadField(fields, "System.WorkItemType"),
            Title = ReadField(fields, "System.Title"),
            Description = ToPlainText(ReadField(fields, "System.Description")),
            AcceptanceCriteria = ToPlainText(ReadField(fields, "Microsoft.VSTS.Common.AcceptanceCriteria")),
            ReproSteps = ToPlainText(ReadField(fields, "Microsoft.VSTS.TCM.ReproSteps")),
            Url = (string?)payload["_links"]?["html"]?["href"] ?? (string?)payload["url"] ?? string.Empty
        };
    }

    public static List<StoryCheckpoint> CreateCheckpoints(AzureDevOpsWorkItem workItem)
    {
        string source;
        string sourceName;

        if (!string.IsNullOrWhiteSpace(workItem.AcceptanceCriteria))
        {
            source = workItem.AcceptanceCriteria;
            sourceName = "Acceptance criteria";
        }
        else if (!string.IsNullOrWhiteSpace(workItem.ReproSteps))
        {
            source = workItem.ReproSteps;
            sourceName = "Repro steps";
        }
        else
        {
            source = workItem.Description;
            sourceName = "Description";
        }

        return source
            .Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Replace(line.Trim(), @"^(?:[-*•]|\d+[.)])\s*", string.Empty).Trim())
            .Where(line => line.Length > 0)
            .Select((line, index) => new StoryCheckpoint
            {
                Number = index + 1,
                Text = line,
                Source = sourceName
            })
            .ToList();
    }

    private static string ReadField(JObject fields, string name) =>
        (string?)fields[name] ?? string.Empty;

    private static string ToPlainText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string text = Regex.Replace(value, @"<(script|style)\b[^>]*>.*?</\1>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<\s*(br\s*/?|/p|/div|/li|/h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = text.Replace('\u00a0', ' ');

        return string.Join(
            "\n",
            text.Split(new[] { '\r', '\n' })
                .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
                .Where(line => line.Length > 0));
    }
}