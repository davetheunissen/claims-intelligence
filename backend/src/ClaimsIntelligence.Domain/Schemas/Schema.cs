namespace ClaimsIntelligence.Domain.Schemas;

public class Schema
{
    public string Id { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime? CreatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
}
