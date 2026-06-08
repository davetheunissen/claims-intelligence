namespace ClaimsIntelligence.Api.Models.Responses;

/// <summary>Paginated list of claim processes.</summary>
public record PaginatedClaimProcessResponse(
    int TotalCount,
    int TotalPages,
    int CurrentPage,
    int PageSize,
    List<ClaimProcess> Items);

/// <summary>Response returned after submitting a claim for processing.</summary>
public record ClaimProcessSubmitResponse(
    string Status,
    string Message,
    string Location);

/// <summary>Response returned after creating a claim container.</summary>
public record ClaimCreateResponse(
    string ClaimId,
    string SchemaSetId,
    string Status);
