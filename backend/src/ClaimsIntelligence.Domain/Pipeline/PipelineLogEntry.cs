namespace ClaimsIntelligence.Domain.Pipeline;

public record PipelineLogEntry(
    string Source,
    string Message,
    DateTimeOffset Timestamp
)
{
    public static PipelineLogEntry Create(string source, string message)
        => new(source, message, DateTimeOffset.UtcNow);
}
