namespace ClaimsIntelligence.Api.Models.Requests;

/// <summary>Request body for registering a Schema Vault v2 (JSON-native) schema.</summary>
public record SchemaVaultRegisterJsonRequest(
    string ClassName,
    string Description,
    Dictionary<string, object> FieldSchema,
    string? BaseAnalyzerId = "prebuilt-document",
    string? CompletionModel = "gpt-4.1-mini");

/// <summary>Request body for unregistering (deleting) a schema.</summary>
public record SchemaVaultUnregisterRequest(string SchemaId);

/// <summary>Request body for creating a new schema set.</summary>
public record SchemaSetCreateRequest(string Name, string Description);

/// <summary>Request body for adding a schema to a schema set.</summary>
public record SchemaSetAddSchemaRequest(string SchemaId);
