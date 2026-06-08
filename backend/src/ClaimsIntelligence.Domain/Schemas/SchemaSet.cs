namespace ClaimsIntelligence.Domain.Schemas;

public class SchemaSet
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> SchemaIds { get; set; } = [];
    public DateTime? CreatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
}
