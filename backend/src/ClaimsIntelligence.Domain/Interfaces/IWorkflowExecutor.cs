using ClaimsIntelligence.Domain.Workflow;

namespace ClaimsIntelligence.Domain.Interfaces;

public interface IWorkflowExecutor
{
    string ExecutorName { get; }
    Task<ClaimProcess> ExecuteAsync(ClaimProcess claim, CancellationToken cancellationToken);
}
