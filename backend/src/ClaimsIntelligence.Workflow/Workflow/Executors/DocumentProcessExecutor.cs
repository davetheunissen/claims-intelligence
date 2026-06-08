using System.Diagnostics;

namespace ClaimsIntelligence.Workflow.Workflow.Executors;

/// <summary>
/// Stage 1 — Document Processing.
///
/// Polls Cosmos DB for each document in the claim until it reaches a terminal
/// status (Completed / Error) and updates <see cref="ClaimProcess.ProcessedDocuments"/>.
///
/// Emits an OpenTelemetry span named <c>workflow.documentprocess</c>.
///
/// Ported from: Python <c>steps/document_process/executor/document_process_executor.py</c>.
/// </summary>
public sealed class DocumentProcessExecutor(
    ICosmosService<ClaimProcess> claimCosmos,
    ICosmosService<ContentProcess> contentCosmos,
    IOptions<WorkflowOptions> options,
    ResiliencePipelineProvider<string> polly,
    ILogger<DocumentProcessExecutor> logger) : IWorkflowExecutor
{
    private static readonly ActivitySource ActivitySource =
        new("ClaimsIntelligence.Workflow");

    public string ExecutorName => "DocumentProcess";

    private readonly WorkflowOptions _options = options.Value;

    /// <inheritdoc/>
    public async Task<ClaimProcess> ExecuteAsync(
        ClaimProcess claim,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("workflow.documentprocess");
        activity?.SetTag("claim_process_id", claim.Id);

        logger.LogInformation(
            "[DocumentProcess] Starting document processing for claim {ClaimId}",
            claim.Id);

        await UpdateStatusAsync(claim.Id, ClaimSteps.DocumentProcessing, cancellationToken);

        var persisted = await claimCosmos.GetByIdAsync(claim.Id, cancellationToken);

        if (persisted is null)
            throw new WorkflowExecutorFailedException(
                ExecutorName, $"ClaimProcess '{claim.Id}' not found in Cosmos DB.");

        var pollPipeline = polly.GetPipeline("workflow-http");
        var pollInterval = TimeSpan.FromSeconds(_options.DocumentPollIntervalSeconds);
        var pollTimeout = TimeSpan.FromSeconds(_options.DocumentPollTimeoutSeconds);

        List<ContentProcess> updatedDocs = [];
        foreach (var doc in persisted.ProcessedDocuments)
        {
            if (doc.Status is "Completed" or "Error")
            {
                updatedDocs.Add(doc);
                continue;
            }

            logger.LogInformation(
                "[DocumentProcess] Polling document {ProcessId} ({FileName})",
                doc.ProcessId, doc.FileName);

            var finalDoc = await PollDocumentCompletionAsync(
                doc, pollPipeline, pollInterval, pollTimeout, cancellationToken);
            updatedDocs.Add(finalDoc);
        }

        await claimCosmos.PatchAsync(claim.Id, [
            PatchOperation.Set("/processedDocuments", updatedDocs),
            PatchOperation.Set("/status", (int)ClaimSteps.DocumentProcessing)
        ], cancellationToken);

        claim.ProcessedDocuments = updatedDocs;

        logger.LogInformation(
            "[DocumentProcess] Completed. {Count} documents processed for claim {ClaimId}",
            updatedDocs.Count, claim.Id);

        return claim;
    }

    private async Task<ContentProcess> PollDocumentCompletionAsync(
        ContentProcess doc,
        Polly.ResiliencePipeline pollPipeline,
        TimeSpan interval,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ContentProcess.Id == ProcessId (set during submit)
            var current = await pollPipeline.ExecuteAsync(
                async ct => await contentCosmos.GetByIdAsync(doc.ProcessId, ct),
                cancellationToken);

            if (current is not null)
            {
                doc.Status = current.Status;
                doc.EntityScore = current.EntityScore;
                doc.SchemaScore = current.SchemaScore;
                doc.ProcessedTime = current.ProcessedTime;

                if (doc.Status is "Completed" or "Error")
                    return doc;
            }

            await Task.Delay(interval, cancellationToken);
        }

        logger.LogWarning(
            "[DocumentProcess] Poll timeout for document {ProcessId}; marking Error",
            doc.ProcessId);

        doc.Status = "Error";
        return doc;
    }

    private async Task UpdateStatusAsync(string claimId, ClaimSteps status, CancellationToken ct)
    {
        await claimCosmos.PatchAsync(claimId,
            [PatchOperation.Set("/status", (int)status)], ct);
    }
}
