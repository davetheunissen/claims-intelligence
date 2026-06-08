namespace ClaimsIntelligence.Workflow.Workflow;

/// <summary>
/// Thrown when an executor fails irrecoverably during workflow execution.
/// Signals the Worker to dead-letter the queue message immediately
/// without further retries.
///
/// Ported from: Python <c>steps/claim_processor.py</c>
/// (<c>WorkflowExecutorFailedException</c>).
/// </summary>
public sealed class WorkflowExecutorFailedException : Exception
{
    public string ExecutorName { get; }

    public WorkflowExecutorFailedException(string executorName, string message)
        : base($"Executor '{executorName}' failed: {message}")
    {
        ExecutorName = executorName;
    }

    public WorkflowExecutorFailedException(string executorName, string message, Exception inner)
        : base($"Executor '{executorName}' failed: {message}", inner)
    {
        ExecutorName = executorName;
    }
}
