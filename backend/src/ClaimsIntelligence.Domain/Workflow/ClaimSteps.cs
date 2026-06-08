namespace ClaimsIntelligence.Domain.Workflow;

public enum ClaimSteps
{
    Pending,
    DocumentProcessing,
    RaiAnalysis,
    Summarizing,
    GapAnalysis,
    Failed,
    Completed
}
