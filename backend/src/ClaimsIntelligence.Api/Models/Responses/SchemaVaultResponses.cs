namespace ClaimsIntelligence.Api.Models.Responses;

/// <summary>Response returned after unregistering a schema.</summary>
public record SchemaVaultUnregisterResponse(
    string Status,
    string SchemaId,
    string ClassName,
    string FileName);
