namespace ClaimsIntelligence.Domain.Pipeline;

public class StepResult
{
    public string ProcessId { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public object? Result { get; set; }
    public string? Elapsed { get; set; }
    public SerializableException? Exception { get; set; }
}
