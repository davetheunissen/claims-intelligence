namespace ClaimsIntelligence.Domain.Pipeline;

public class PipelineStatus
{
    public bool Completed { get; set; }
    public string? ProcessId { get; set; }
    public string? MetadataId { get; set; }
    public string? SchemaId { get; set; }
    public string? CreationTime { get; set; }
    public string? LastUpdatedTime { get; set; }
    public string? ActiveStep { get; set; }
    public List<string> Steps { get; set; } = [];
    public List<string> RemainingSteps { get; set; } = [];
    public List<string> CompletedSteps { get; set; } = [];
    public List<StepResult> ProcessResults { get; set; } = [];
    public SerializableException? Exception { get; set; }

    public void UpdateStep()
    {
        if (ActiveStep is null) return;
        LastUpdatedTime = DateTimeOffset.UtcNow.ToString("O");
        MoveToNextStep(ActiveStep);
    }

    public void AddStepResult(StepResult result)
    {
        var index = ProcessResults.FindIndex(r => r.StepName == result.StepName);
        if (index >= 0)
            ProcessResults[index] = result;
        else
            ProcessResults.Add(result);
    }

    public StepResult? GetStepResult(string stepName)
        => ProcessResults.FirstOrDefault(r => r.StepName == stepName);

    public StepResult? GetPreviousStepResult()
    {
        var previous = CompletedSteps.LastOrDefault();
        return previous is null ? null : GetStepResult(previous);
    }

    private void MoveToNextStep(string stepName)
    {
        if (!CompletedSteps.Contains(stepName))
            CompletedSteps.Add(stepName);
        RemainingSteps.Remove(stepName);
        if (RemainingSteps.Count == 0)
            Completed = true;
    }
}
