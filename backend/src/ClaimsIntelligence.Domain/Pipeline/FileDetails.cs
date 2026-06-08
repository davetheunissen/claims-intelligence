namespace ClaimsIntelligence.Domain.Pipeline;

public class FileDetails
{
    public string? Id { get; set; }
    public string ProcessId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public long? Size { get; set; }
    public string? MimeType { get; set; }
    public ArtifactType? ArtifactType { get; set; }
    public string? ProcessedBy { get; set; }
    public List<PipelineLogEntry> LogEntries { get; set; } = [];

    public FileDetails AddLogEntry(string source, string message)
    {
        LogEntries.Add(PipelineLogEntry.Create(source, message));
        return this;
    }
}
