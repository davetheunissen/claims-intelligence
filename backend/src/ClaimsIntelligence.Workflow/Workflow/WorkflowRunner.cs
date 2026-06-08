using System.Diagnostics;
using ClaimsIntelligence.Workflow.Checkpoint;

namespace ClaimsIntelligence.Workflow.Workflow;

/// <summary>
/// DAG orchestrator that runs the 4-stage claim workflow in sequence:
///
///   DocumentProcess → [Rai (optional)] → Summarize → Gap
///
/// Before each executor, the checkpoint storage is consulted to skip stages
/// that already completed on a prior run (fault tolerance). After each
/// executor, the checkpoint is written so restarts can resume mid-workflow.
///
/// Ported from: Python <c>steps/claim_processor.py</c> (<c>ClaimProcessor._init_workflow</c>).
/// </summary>
public sealed class WorkflowRunner(
    IEnumerable<IWorkflowExecutor> executors,
    ICheckpointStorage checkpoint,
    ICosmosService<ClaimProcess> claimCosmos,
    IOptions<WorkflowOptions> options,
    ILogger<WorkflowRunner> logger)
{
    private static readonly ActivitySource ActivitySource =
        new("ClaimsIntelligence.Workflow");

    private readonly WorkflowOptions _options = options.Value;

    /// <summary>
    /// Runs the full 4-stage workflow for <paramref name="claimProcessId"/>.
    /// Throws <see cref="WorkflowExecutorFailedException"/> if any stage fails
    /// irrecoverably.
    /// </summary>
    public async Task RunAsync(string claimProcessId, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("workflow.claim_process");
        activity?.SetTag("claim_process_id", claimProcessId);

        logger.LogInformation("WorkflowRunner starting for claim {ClaimId}", claimProcessId);

        var claim = await claimCosmos.GetByIdAsync(claimProcessId, cancellationToken)
                    ?? throw new WorkflowExecutorFailedException(
                        "WorkflowRunner",
                        $"ClaimProcess '{claimProcessId}' not found in Cosmos DB.");

        var completed = await checkpoint.GetCompletedExecutorsAsync(claimProcessId, cancellationToken);

        var pipeline = BuildPipeline();

        foreach (var executor in pipeline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (completed.Contains(executor.ExecutorName))
            {
                logger.LogInformation(
                    "[WorkflowRunner] Skipping already-completed stage {Stage} for claim {ClaimId}",
                    executor.ExecutorName, claimProcessId);
                continue;
            }

            logger.LogInformation(
                "[WorkflowRunner] Executing stage {Stage} for claim {ClaimId}",
                executor.ExecutorName, claimProcessId);

            try
            {
                claim = await executor.ExecuteAsync(claim, cancellationToken);
            }
            catch (WorkflowExecutorFailedException)
            {
                await UpdateStatusAsync(claimProcessId, ClaimSteps.Failed, cancellationToken);
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await UpdateStatusAsync(claimProcessId, ClaimSteps.Failed, cancellationToken);
                throw new WorkflowExecutorFailedException(executor.ExecutorName, ex.Message, ex);
            }

            await checkpoint.MarkExecutorCompleteAsync(claimProcessId, executor.ExecutorName, cancellationToken);
        }

        await UpdateStatusAsync(claimProcessId, ClaimSteps.Completed, cancellationToken);
        await SetProcessedTimeAsync(claimProcessId, cancellationToken);
        await checkpoint.ClearAsync(claimProcessId, cancellationToken);

        logger.LogInformation("WorkflowRunner completed successfully for claim {ClaimId}", claimProcessId);
    }

    private IEnumerable<IWorkflowExecutor> BuildPipeline()
    {
        string[] ordered = ["DocumentProcess", "Rai", "Summarize", "Gap"];

        foreach (var name in ordered)
        {
            if (name == "Rai" && !_options.RaiEnabled)
            {
                logger.LogDebug("[WorkflowRunner] RAI stage disabled; skipping.");
                continue;
            }

            var executor = executors.FirstOrDefault(e =>
                string.Equals(e.ExecutorName, name, StringComparison.OrdinalIgnoreCase));

            if (executor is null)
            {
                logger.LogWarning("[WorkflowRunner] Executor '{Name}' not registered; skipping.", name);
                continue;
            }

            yield return executor;
        }
    }

    private async Task UpdateStatusAsync(string claimId, ClaimSteps status, CancellationToken ct)
    {
        await claimCosmos.PatchAsync(claimId,
            [PatchOperation.Set("/status", (int)status)], ct);
    }

    private async Task SetProcessedTimeAsync(string claimId, CancellationToken ct)
    {
        await claimCosmos.PatchAsync(claimId,
            [PatchOperation.Set("/processedTime", DateTimeOffset.UtcNow.ToString("O"))], ct);
    }
}
