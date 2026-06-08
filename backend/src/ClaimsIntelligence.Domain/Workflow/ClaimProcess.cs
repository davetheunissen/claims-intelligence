namespace ClaimsIntelligence.Domain.Workflow;

public class ClaimProcess
{
    public string Id { get; set; } = string.Empty;
    public string ProcessName { get; set; } = "First Notice of Loss";
    public string SchemaSetId { get; set; } = string.Empty;
    public string? MetadataId { get; set; }
    public List<ContentProcess> ProcessedDocuments { get; set; } = [];
    public ClaimSteps Status { get; set; } = ClaimSteps.DocumentProcessing;
    public string ProcessSummary { get; set; } = string.Empty;
    public string ProcessGaps { get; set; } = string.Empty;
    public string ProcessComment { get; set; } = string.Empty;
    public string ProcessTime { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string ProcessedTime { get; set; } = string.Empty;
}
