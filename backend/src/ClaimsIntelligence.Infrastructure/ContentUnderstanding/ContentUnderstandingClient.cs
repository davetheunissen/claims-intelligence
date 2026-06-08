using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimsIntelligence.Infrastructure.ContentUnderstanding;

public class ContentUnderstandingOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2025-11-01";
}

public class ContentUnderstandingClient(
    IHttpClientFactory httpClientFactory,
    TokenCredential credential,
    IOptions<ContentUnderstandingOptions> options,
    ILogger<ContentUnderstandingClient> logger) : IContentUnderstandingClient
{
    private static readonly string[] CognitiveServicesScopes = ["https://cognitiveservices.azure.com/.default"];
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly string _endpoint = options.Value.Endpoint.TrimEnd('/');
    private readonly string _apiVersion = options.Value.ApiVersion;

    private HttpClient CreateClient() => httpClientFactory.CreateClient("ContentUnderstanding");

    private async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(CognitiveServicesScopes),
            cancellationToken);
        return token.Token;
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url);
        var token = await GetBearerTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-ms-useragent", "cps-contentunderstanding/client");
        return request;
    }

    private string AnalyzerUrl(string analyzerId)
        => $"{_endpoint}/contentunderstanding/analyzers/{analyzerId}?api-version={_apiVersion}";

    private string AnalyzerListUrl()
        => $"{_endpoint}/contentunderstanding/analyzers?api-version={_apiVersion}";

    private string AnalyzeUrl(string analyzerId)
        => $"{_endpoint}/contentunderstanding/analyzers/{analyzerId}:analyze?api-version={_apiVersion}";

    private string AnalyzeBinaryUrl(string analyzerId)
        => $"{_endpoint}/contentunderstanding/analyzers/{analyzerId}:analyzeBinary?api-version={_apiVersion}";

    public async Task<JsonElement> GetAllAnalyzersAsync(CancellationToken cancellationToken = default)
    {
        using var http = CreateClient();
        var request = await BuildRequestAsync(HttpMethod.Get, AnalyzerListUrl(), cancellationToken);
        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(content).RootElement.Clone();
    }

    public async Task<JsonElement> GetAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient();
        var request = await BuildRequestAsync(HttpMethod.Get, AnalyzerUrl(analyzerId), cancellationToken);
        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(content).RootElement.Clone();
    }

    public async Task DeleteAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient();
        var request = await BuildRequestAsync(HttpMethod.Delete, AnalyzerUrl(analyzerId), cancellationToken);
        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Analyzer {AnalyzerId} deleted", analyzerId);
    }

    public async Task<string> EnsureFieldAnalyzerAsync(
        string className,
        JsonElement analyzerPayload,
        CancellationToken cancellationToken = default)
    {
        var analyzerId = BuildAnalyzerId(className, analyzerPayload);
        var url = AnalyzerUrl(analyzerId);

        // Fast path: check if the analyzer already exists (hash-keyed, so existence == same schema).
        try
        {
            using var checkHttp = CreateClient();
            var checkRequest = await BuildRequestAsync(HttpMethod.Get, url, cancellationToken);
            var checkResponse = await checkHttp.SendAsync(checkRequest, cancellationToken);
            if (checkResponse.IsSuccessStatusCode)
            {
                logger.LogDebug("Analyzer {AnalyzerId} already exists", analyzerId);
                return analyzerId;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "CU GET analyzer check failed; will attempt PUT");
        }

        using var http = CreateClient();
        var request = await BuildRequestAsync(HttpMethod.Put, url, cancellationToken);
        request.Content = new StringContent(analyzerPayload.GetRawText(), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode == 409)
        {
            logger.LogDebug("Analyzer {AnalyzerId} already exists (409 race)", analyzerId);
            return analyzerId;
        }

        response.EnsureSuccessStatusCode();

        var operationLocation = response.Headers.Contains("Operation-Location")
            ? response.Headers.GetValues("Operation-Location").FirstOrDefault()
            : null;

        if (operationLocation is not null)
        {
            await PollResultAsync(operationLocation, TimeSpan.FromSeconds(180), TimeSpan.FromSeconds(1), cancellationToken);
        }

        logger.LogInformation("Content Understanding analyzer '{AnalyzerId}' ready", analyzerId);
        return analyzerId;
    }

    public async Task<JsonElement> AnalyzeAndWaitAsync(
        string analyzerId,
        byte[] fileBytes,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var operationLocation = await BeginAnalyzeAsync(analyzerId, fileBytes, cancellationToken);
        return await PollResultAsync(operationLocation, timeout, pollInterval, cancellationToken);
    }

    public async Task<JsonElement> AnalyzeUrlAndWaitAsync(
        string analyzerId,
        string url,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var operationLocation = await BeginAnalyzeUrlAsync(analyzerId, url, cancellationToken);
        return await PollResultAsync(operationLocation, timeout, pollInterval, cancellationToken);
    }

    public async Task<string> BeginAnalyzeAsync(string analyzerId, byte[] fileBytes, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient();
        var request = await BuildRequestAsync(HttpMethod.Post, AnalyzeBinaryUrl(analyzerId), cancellationToken);
        request.Content = new ByteArrayContent(fileBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("CU analyze started for analyzer {AnalyzerId}", analyzerId);

        return ExtractOperationLocation(response)
            ?? throw new InvalidOperationException("Operation-Location header missing from CU analyze response");
    }

    public async Task<string> BeginAnalyzeUrlAsync(string analyzerId, string url, CancellationToken cancellationToken = default)
    {
        using var http = CreateClient();
        var request = await BuildRequestAsync(HttpMethod.Post, AnalyzeUrl(analyzerId), cancellationToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { url }),
            Encoding.UTF8,
            "application/json");

        var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ExtractOperationLocation(response)
            ?? throw new InvalidOperationException("Operation-Location header missing from CU analyze response");
    }

    public async Task<JsonElement> PollResultAsync(
        string operationLocation,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout ?? DefaultTimeout);
        var interval = pollInterval ?? DefaultPollInterval;
        var operationId = operationLocation.Split('/').LastOrDefault()?.Split('?').FirstOrDefault() ?? "unknown";

        while (true)
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException($"CU operation {operationId} did not complete within {timeout ?? DefaultTimeout}");

            await Task.Delay(interval, cancellationToken);

            using var http = CreateClient();
            var request = await BuildRequestAsync(HttpMethod.Get, operationLocation, cancellationToken);
            var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString()?.ToLowerInvariant()
                : null;

            if (status is "succeeded" or "completed")
            {
                logger.LogInformation("CU operation {OperationId} succeeded", operationId);
                return root.Clone();
            }

            if (status is "failed" or "canceled")
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetRawText() : json;
                throw new InvalidOperationException($"CU operation {operationId} {status}: {error}");
            }

            logger.LogDebug("CU operation {OperationId} in progress (status={Status})", operationId, status);
        }
    }

    private static string? ExtractOperationLocation(HttpResponseMessage response)
        => response.Headers.Contains("Operation-Location")
            ? response.Headers.GetValues("Operation-Location").FirstOrDefault()
            : null;

    private static string BuildAnalyzerId(string className, JsonElement payload)
    {
        var canonical = JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(payload.GetRawText()),
            new JsonSerializerOptions { WriteIndented = false });

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hash8 = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();

        var safeName = new string(
            className.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray())
            .Trim('_')
            .ToLowerInvariant();

        return $"cps_extract_{safeName}_v{hash8}";
    }
}
