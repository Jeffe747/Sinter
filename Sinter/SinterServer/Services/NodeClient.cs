using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SinterServer.Models;

namespace SinterServer.Services;

public interface INodeClient
{
    Task<NodeStatusResponse> GetStatusAsync(string nodeUrl, CancellationToken cancellationToken);
    Task<RemoteActionResult> ReloadDaemonAsync(string nodeUrl, string apiKey, CancellationToken cancellationToken);
    Task<RemoteActionResult> StartServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken);
    Task<RemoteActionResult> StopServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken);
    Task<RemoteActionResult> EnableServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken);
    Task<RemoteActionResult> DisableServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken);
    Task<RemoteActionResult> DeployApplicationAsync(string nodeUrl, string apiKey, object request, CancellationToken cancellationToken);
    Task<RemoteActionResult> RestartApplicationServiceAsync(string nodeUrl, string apiKey, string appName, CancellationToken cancellationToken);
    Task<RemoteActionResult> UninstallApplicationAsync(string nodeUrl, string apiKey, string appName, CancellationToken cancellationToken);
    Task<RemoteFileView> GetServiceUnitAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken);
    Task<RemoteFileView> GetServiceOverrideAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken);
    Task<RemoteActionResult> UpdateServiceUnitAsync(string nodeUrl, string apiKey, string serviceName, UpdateRemoteFileRequest request, CancellationToken cancellationToken);
    Task<RemoteActionResult> UpdateServiceOverrideAsync(string nodeUrl, string apiKey, string serviceName, UpdateRemoteFileRequest request, CancellationToken cancellationToken);
}

public sealed class NodeClient(IHttpClientFactory httpClientFactory) : INodeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<NodeStatusResponse> GetStatusAsync(string nodeUrl, CancellationToken cancellationToken)
    {
        using var client = CreateClient(nodeUrl, apiKey: null);
        var response = await client.GetAsync("api/status", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new NodeStatusResponse(null, await ReadErrorSummaryAsync(response, cancellationToken), [], []);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        NodeCapabilities? capabilities = root.TryGetProperty("capabilities", out var capabilitiesElement)
            ? JsonSerializer.Deserialize<NodeCapabilities>(capabilitiesElement.GetRawText(), JsonOptions)
            : null;
        NodeEnvironment? environment = root.TryGetProperty("environment", out var environmentElement)
            ? JsonSerializer.Deserialize<NodeEnvironment>(environmentElement.GetRawText(), JsonOptions)
            : null;
        NodeTelemetry? telemetry = root.TryGetProperty("telemetry", out var telemetryElement)
            ? JsonSerializer.Deserialize<NodeTelemetry>(telemetryElement.GetRawText(), JsonOptions)
            : null;
        var services = root.TryGetProperty("services", out var servicesRaw) && servicesRaw.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<IReadOnlyList<NodeServiceInventoryItem>>(servicesRaw.GetRawText(), JsonOptions) ?? []
            : [];
        var managedApplications = root.TryGetProperty("managedApplications", out var appsRaw) && appsRaw.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<IReadOnlyList<NodeManagedApplicationInventoryItem>>(appsRaw.GetRawText(), JsonOptions) ?? []
            : [];
        var servicesCount = services.Count;
        var appsCount = managedApplications.Count;

        return new NodeStatusResponse(
            new NodeSnapshot(
                root.TryGetProperty("hostname", out var hostnameElement) ? hostnameElement.GetString() : null,
                root.TryGetProperty("osDescription", out var osElement) ? osElement.GetString() : null,
                root.TryGetProperty("processArchitecture", out var archElement) ? archElement.GetString() : null,
                root.TryGetProperty("frameworkDescription", out var frameworkElement) ? frameworkElement.GetString() : null,
                capabilities,
                environment,
                root.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null,
                root.TryGetProperty("uptime", out var uptimeElement) ? uptimeElement.GetString() : null,
                telemetry,
                servicesCount,
                appsCount),
            "Online",
            services,
            managedApplications);
    }

    public Task<RemoteActionResult> ReloadDaemonAsync(string nodeUrl, string apiKey, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Post, "api/system/daemon-reload", null, cancellationToken, "Requested daemon reload.");
    }

    public Task<RemoteActionResult> StartServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Post, $"api/services/{Uri.EscapeDataString(serviceName)}/start", null, cancellationToken, "Service start requested.");
    }

    public Task<RemoteActionResult> StopServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Post, $"api/services/{Uri.EscapeDataString(serviceName)}/stop", null, cancellationToken, "Service stop requested.");
    }

    public Task<RemoteActionResult> EnableServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Post, $"api/services/{Uri.EscapeDataString(serviceName)}/enable", null, cancellationToken, "Service enable requested.");
    }

    public Task<RemoteActionResult> DisableServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Post, $"api/services/{Uri.EscapeDataString(serviceName)}/disable", null, cancellationToken, "Service disable requested.");
    }

    public Task<RemoteActionResult> DeployApplicationAsync(string nodeUrl, string apiKey, object request, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Post, "api/apps/deploy", request, cancellationToken, "Deployment requested.");
    }

    public Task<RemoteActionResult> RestartApplicationServiceAsync(string nodeUrl, string apiKey, string appName, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Post, $"api/apps/{Uri.EscapeDataString(appName)}/restart", null, cancellationToken, "Service restart requested.");
    }

    public Task<RemoteActionResult> UninstallApplicationAsync(string nodeUrl, string apiKey, string appName, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Delete, $"api/apps/{Uri.EscapeDataString(appName)}", null, cancellationToken, "Uninstall requested.");
    }

    public async Task<RemoteFileView> GetServiceUnitAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken)
    {
        using var client = CreateClient(nodeUrl, apiKey);
        var response = await client.GetAsync($"api/services/{Uri.EscapeDataString(serviceName)}/unit", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return new RemoteFileView(response.IsSuccessStatusCode ? content : string.Empty, response.IsSuccessStatusCode ? "Available" : await ReadErrorSummaryAsync(response, content, cancellationToken));
    }

    public async Task<RemoteFileView> GetServiceOverrideAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken)
    {
        using var client = CreateClient(nodeUrl, apiKey);
        var response = await client.GetAsync($"api/services/{Uri.EscapeDataString(serviceName)}/override", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return new RemoteFileView(response.IsSuccessStatusCode ? content : string.Empty, response.IsSuccessStatusCode ? "Available" : await ReadErrorSummaryAsync(response, content, cancellationToken));
    }

    public Task<RemoteActionResult> UpdateServiceUnitAsync(string nodeUrl, string apiKey, string serviceName, UpdateRemoteFileRequest request, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Put, $"api/services/{Uri.EscapeDataString(serviceName)}/unit", request, cancellationToken, "Service unit updated.");
    }

    public Task<RemoteActionResult> UpdateServiceOverrideAsync(string nodeUrl, string apiKey, string serviceName, UpdateRemoteFileRequest request, CancellationToken cancellationToken)
    {
        return CallOperationAsync(nodeUrl, apiKey, HttpMethod.Put, $"api/services/{Uri.EscapeDataString(serviceName)}/override", request, cancellationToken, "Service override updated.");
    }

    private async Task<RemoteActionResult> CallOperationAsync(string nodeUrl, string apiKey, HttpMethod method, string relativePath, object? payload, CancellationToken cancellationToken, string fallbackSummary)
    {
        using var client = CreateClient(nodeUrl, apiKey);
        using var request = new HttpRequestMessage(method, relativePath);
        if (payload is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (string.Equals(response.Content.Headers.ContentType?.MediaType, "application/x-ndjson", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadNdjsonAsync(response, cancellationToken, fallbackSummary);
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return new RemoteActionResult(response.IsSuccessStatusCode ? "Success" : "Error", response.IsSuccessStatusCode ? (string.IsNullOrWhiteSpace(text) ? fallbackSummary : text) : await ReadErrorSummaryAsync(response, text, cancellationToken), []);
    }

    private static async Task<RemoteActionResult> ReadNdjsonAsync(HttpResponseMessage response, CancellationToken cancellationToken, string fallbackSummary)
    {
        var events = new List<RemoteEvent>();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var evt = JsonSerializer.Deserialize<RemoteEvent>(line, JsonOptions);
            if (evt is not null)
            {
                events.Add(evt);
            }
        }

        var last = events.LastOrDefault();
        var status = response.IsSuccessStatusCode && last?.Type != "error" ? "Success" : "Error";
        return new RemoteActionResult(status, last?.Message ?? fallbackSummary, events);
    }

    private HttpClient CreateClient(string nodeUrl, string? apiKey)
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(EnsureTrailingSlash(nodeUrl));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-Sinter-Key", apiKey);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task<string> ReadErrorSummaryAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return await ReadErrorSummaryAsync(response, body, cancellationToken);
    }

    private static Task<string> ReadErrorSummaryAsync(HttpResponseMessage response, string body, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var errorElement))
                {
                    var message = errorElement.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return Task.FromResult(message);
                    }
                }
            }
            catch (JsonException)
            {
            }

            return Task.FromResult(body);
        }

        return Task.FromResult($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}