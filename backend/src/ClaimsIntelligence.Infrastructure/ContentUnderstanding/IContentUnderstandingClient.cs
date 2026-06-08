using System.Text.Json;

namespace ClaimsIntelligence.Infrastructure.ContentUnderstanding;

public interface IContentUnderstandingClient
{
    // --- Analyzer lifecycle ---

    Task<JsonElement> GetAllAnalyzersAsync(CancellationToken cancellationToken = default);
    Task<JsonElement> GetAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default);
    Task DeleteAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent PUT: creates or verifies a custom field analyzer.
    /// Returns the deterministic analyzer ID (hash-keyed by className + payload).
    /// </summary>
    Task<string> EnsureFieldAnalyzerAsync(
        string className,
        JsonElement analyzerPayload,
        CancellationToken cancellationToken = default);

    // --- Analysis ---

    /// <summary>Submits bytes for analysis and polls until completion. Returns the full CU result payload.</summary>
    Task<JsonElement> AnalyzeAndWaitAsync(
        string analyzerId,
        byte[] fileBytes,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);

    /// <summary>Submits a URL for analysis and polls until completion.</summary>
    Task<JsonElement> AnalyzeUrlAndWaitAsync(
        string analyzerId,
        string url,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default);

    // --- Low-level (begin + poll separately) ---

    Task<string> BeginAnalyzeAsync(string analyzerId, byte[] fileBytes, CancellationToken cancellationToken = default);
    Task<string> BeginAnalyzeUrlAsync(string analyzerId, string url, CancellationToken cancellationToken = default);
    Task<JsonElement> PollResultAsync(string operationLocation, TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default);
}
