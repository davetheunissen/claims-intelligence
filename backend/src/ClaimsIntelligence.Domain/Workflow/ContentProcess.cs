namespace ClaimsIntelligence.Domain.Workflow;

public class ContentProcess
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProcessId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public double EntityScore { get; set; }
    public double SchemaScore { get; set; }
    public string? Status { get; set; }
    public string ProcessedTime { get; set; } = string.Empty;
}
