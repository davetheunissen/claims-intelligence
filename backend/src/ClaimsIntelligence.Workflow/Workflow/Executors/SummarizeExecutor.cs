using System.Diagnostics;
using Azure.AI.Inference;

namespace ClaimsIntelligence.Workflow.Workflow.Executors;

/// <summary>
/// Stage 3 — Cross-document AI Summary.
///
/// Calls <see cref="IAzureInferenceService"/> to generate a consolidated narrative
/// summary of all extracted document content and writes the result to
/// <see cref="ClaimProcess.ProcessSummary"/>.
///
/// Emits an OpenTelemetry span named <c>workflow.summarize</c>.
///
/// Ported from: Python <c>steps/summarize/executor/summarize_executor.py</c>.
/// </summary>
public sealed class SummarizeExecutor(
    IAzureInferenceService inference,
    ICosmosService<ClaimProcess> claimCosmos,
    IOptions<WorkflowOptions> options,
    ILogger<SummarizeExecutor> logger) : IWorkflowExecutor
{
    private static readonly ActivitySource ActivitySource =
        new("ClaimsIntelligence.Workflow");

    public string ExecutorName => "Summarize";

    private readonly WorkflowOptions _options = options.Value;

    private static readonly string SystemPrompt = """
        You are an expert insurance claims analyst. Your task is to produce a concise,
        structured summary of a multi-document insurance claim.

        Given the extracted content from one or more claim documents (claim forms, police
        reports, repair estimates, damage photos), produce a summary that covers:

        1. **Incident Overview** — What happened, when, and where.
        2. **Parties Involved** — Insured, claimant, third parties, witnesses.
        3. **Vehicle / Property** — Make, model, year, VIN, damage description.
        4. **Financial Estimate** — Loss amount, repair estimate, deductible if known.
        5. **Supporting Documentation** — What documents are present and their key findings.
        6. **Outstanding Items** — Anything that appears missing or requires follow-up.

        Be factual. Do not infer information not present in the documents.
        Format the summary in clear, adjuster-friendly prose (2-4 paragraphs).
        """;

    /// <inheritdoc/>
    public async Task<ClaimProcess> ExecuteAsync(
        ClaimProcess claim,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("workflow.summarize");
        activity?.SetTag("claim_process_id", claim.Id);

        logger.LogInformation(
            "[Summarize] Generating cross-document summary for claim {ClaimId}", claim.Id);

        await UpdateStatusAsync(claim.Id, ClaimSteps.Summarizing, cancellationToken);

        var docContent = BuildUserMessage(claim);
        if (string.IsNullOrWhiteSpace(docContent))
        {
            logger.LogWarning(
                "[Summarize] No document content available for claim {ClaimId}; skipping summarization",
                claim.Id);
            return claim;
        }

        List<ChatRequestMessage> messages =
        [
            new ChatRequestSystemMessage(SystemPrompt),
            new ChatRequestUserMessage(
                "Now summarize the following document extracts:\n\n" + docContent)
        ];

        var response = await inference.CompleteAsync(
            _options.InferenceModelName,
            messages,
            cancellationToken: cancellationToken);

        var summary = response.Content ?? string.Empty;

        logger.LogInformation(
            "[Summarize] Summary generated ({Length} chars) for claim {ClaimId}",
            summary.Length, claim.Id);

        await claimCosmos.PatchAsync(claim.Id,
            [PatchOperation.Set("/processSummary", summary)],
            cancellationToken);

        claim.ProcessSummary = summary;
        return claim;
    }

    private static string BuildUserMessage(ClaimProcess claim)
    {
        if (claim.ProcessedDocuments.Count == 0)
            return string.Empty;

        return string.Join(
            "\n\n---\n\n",
            claim.ProcessedDocuments
                .Where(d => d.Status is "Completed")
                .Select(d => $"Document: {d.FileName}\nMimeType: {d.MimeType}\nScore: {d.EntityScore:F2}"));
    }

    private async Task UpdateStatusAsync(string claimId, ClaimSteps status, CancellationToken ct)
    {
        await claimCosmos.PatchAsync(claimId,
            [PatchOperation.Set("/status", (int)status)], ct);
    }
}
