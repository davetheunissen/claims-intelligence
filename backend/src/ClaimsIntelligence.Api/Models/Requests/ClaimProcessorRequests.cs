namespace ClaimsIntelligence.Api.Models.Requests;

/// <summary>Request body for creating a new claim batch container.</summary>
public record ClaimCreateRequest(string SchemaCollectionId);

/// <summary>Request body for adding a file to an existing claim batch.</summary>
public record ClaimFileAddRequest(string ClaimId, string MetadataId, string SchemaId);

/// <summary>Request body for triggering claim processing.</summary>
public record ClaimProcessRequest(string ClaimProcessId);

/// <summary>Request body for adding a comment to a claim process.</summary>
public record ClaimCommentRequest(string Comment);
