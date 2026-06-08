namespace ClaimsIntelligence.Workflow.Checkpoint;

/// <summary>
/// Cosmos DB (NoSQL API) backed checkpoint store.
///
/// Checkpoint state is stored directly on the <see cref="ClaimProcess"/> document
/// in the <c>ProcessComment</c> field using "checkpoint:{executorName}" markers.
/// This avoids a separate container while keeping fault-tolerant restart state
/// co-located with the claim.
///
/// Ported from: Python <c>libs/agent_framework/cosmos_checkpoint_storage.py</c>.
/// </summary>
public sealed class CosmosCheckpointStorage(
    ICosmosService<ClaimProcess> cosmos,
    ILogger<CosmosCheckpointStorage> logger) : ICheckpointStorage
{
    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> GetCompletedExecutorsAsync(
        string claimProcessId,
        CancellationToken cancellationToken = default)
    {
        var doc = await cosmos.GetByIdAsync(claimProcessId, cancellationToken);

        if (doc is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(doc.ProcessComment))
        {
            foreach (var line in doc.ProcessComment.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("checkpoint:", StringComparison.OrdinalIgnoreCase))
                    completed.Add(trimmed["checkpoint:".Length..].Trim());
            }
        }

        return completed;
    }

    /// <inheritdoc/>
    public async Task MarkExecutorCompleteAsync(
        string claimProcessId,
        string executorName,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Checkpoint: marking executor {Executor} complete for claim {ClaimId}",
            executorName, claimProcessId);

        var doc = await cosmos.GetByIdAsync(claimProcessId, cancellationToken);

        var existing = doc?.ProcessComment ?? string.Empty;
        var marker = $"checkpoint:{executorName}";

        if (existing.Contains(marker, StringComparison.OrdinalIgnoreCase))
            return;

        var newComment = string.IsNullOrWhiteSpace(existing)
            ? marker
            : existing + "\n" + marker;

        await cosmos.PatchAsync(claimProcessId,
            [PatchOperation.Set("/processComment", newComment)],
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task ClearAsync(string claimProcessId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Checkpoint: clearing checkpoints for claim {ClaimId}", claimProcessId);

        var doc = await cosmos.GetByIdAsync(claimProcessId, cancellationToken);
        if (doc is null) return;

        var lines = (doc.ProcessComment ?? string.Empty)
            .Split('\n')
            .Where(l => !l.Trim().StartsWith("checkpoint:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var cleaned = string.Join('\n', lines).Trim();
        await cosmos.PatchAsync(claimProcessId,
            [PatchOperation.Set("/processComment", cleaned)],
            cancellationToken);
    }
}
