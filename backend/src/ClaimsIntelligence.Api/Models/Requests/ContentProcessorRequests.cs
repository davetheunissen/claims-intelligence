namespace ClaimsIntelligence.Api.Models.Requests;

/// <summary>Request body for single-file content processing submission.</summary>
public record ContentProcessorSubmitRequest(
    string? MetadataId,
    string? SchemaId);

/// <summary>Pagination parameters (1-based page number).</summary>
public record PagingRequest(int PageNumber, int PageSize);

/// <summary>Request body for overwriting the processed result.</summary>
public record ContentResultUpdateRequest(string ProcessId, Dictionary<string, object> ModifiedResult);

/// <summary>Request body for attaching or updating a user comment.</summary>
public record ContentCommentUpdateRequest(string ProcessId, string Comment);
