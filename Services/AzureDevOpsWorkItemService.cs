using ABB.Analyze.VisualStudio.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ABB.Analyze.VisualStudio.Services;

internal sealed class AzureDevOpsWorkItemService : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private string _personalAccessToken = string.Empty;

    public bool IsConnected => !string.IsNullOrWhiteSpace(_personalAccessToken);

    public void ConnectWithPersonalAccessToken(string personalAccessToken)
    {
        if (string.IsNullOrWhiteSpace(personalAccessToken))
        {
            throw new ArgumentException("Enter an Azure DevOps personal access token.", nameof(personalAccessToken));
        }

        _personalAccessToken = personalAccessToken.Trim();
    }

    public async Task<AzureDevOpsWorkItem> GetWorkItemAsync(
        string organization,
        string project,
        int workItemId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_personalAccessToken))
        {
            throw new InvalidOperationException("Connect to Azure DevOps before fetching a work item.");
        }

        if (!Regex.IsMatch(organization ?? string.Empty, @"^[A-Za-z0-9-]+$") ||
            string.IsNullOrWhiteSpace(project) ||
            project.Contains("/") ||
            workItemId <= 0)
        {
            throw new ArgumentException("Enter a valid organization, project, and positive work item ID.");
        }

        string url =
            $"https://dev.azure.com/{organization}/{Uri.EscapeDataString(project.Trim())}" +
            $"/_apis/wit/workitems/{workItemId}?$expand=all&api-version=7.1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Basic", CreateBasicAuthorizationParameter(_personalAccessToken));

        using HttpResponseMessage response =
            await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string detail = (int)response.StatusCode == 401
                ? "Check that the PAT is valid, not expired or revoked, and scoped to the selected organization."
                : (int)response.StatusCode == 403
                    ? "Check that the PAT has Work Items (Read) scope and its owner can read this item."
                    : (int)response.StatusCode == 404
                        ? "Check the organization, project, and work item ID."
                        : "Check your Azure DevOps connection and try again.";

            throw new InvalidOperationException(
                $"Azure DevOps returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}");
        }

        string json = await response.Content.ReadAsStringAsync();
        return AzureDevOpsWorkItemParser.Parse(json);
    }

    internal static string CreateBasicAuthorizationParameter(string personalAccessToken) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + personalAccessToken));

    public void Dispose()
    {
        _personalAccessToken = string.Empty;
        _httpClient.Dispose();
    }
}