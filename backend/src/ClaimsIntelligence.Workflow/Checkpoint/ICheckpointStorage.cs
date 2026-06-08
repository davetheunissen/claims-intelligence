namespace ClaimsIntelligence.Workflow.Checkpoint;

/// <summary>
/// Persists and retrieves per-claim executor-completion state so that a
/// restarted WorkflowRunner can skip stages that already succeeded.
/// </summary>
public interface ICheckpointStorage
{
    /// <summary>
    /// Returns the set of executor names that have already completed for
    /// the given <paramref name="claimProcessId"/>.
    /// </summary>
    Task<IReadOnlySet<string>> GetCompletedExecutorsAsync(
        string claimProcessId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that <paramref name="executorName"/> completed successfully
    /// for the given <paramref name="claimProcessId"/>.
    /// </summary>
    Task MarkExecutorCompleteAsync(
        string claimProcessId,
        string executorName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all checkpoint state for <paramref name="claimProcessId"/>
    /// (called on final success or dead-letter).
    /// </summary>
    Task ClearAsync(string claimProcessId, CancellationToken cancellationToken = default);
}
