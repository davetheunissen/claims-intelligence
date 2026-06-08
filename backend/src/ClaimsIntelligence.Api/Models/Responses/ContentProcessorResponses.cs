namespace ClaimsIntelligence.Api.Models.Responses;

/// <summary>Response returned after successful file submission.</summary>
public record ContentProcessorSubmitResponse(
    string Message,
    string ProcessId,
    string StatusUrl);

/// <summary>Response returned after a delete operation.</summary>
public record ContentResultDeleteResponse(
    string ProcessId,
    string Status,
    string Message);

/// <summary>Paginated list of content processes.</summary>
public record PaginatedContentProcessResponse(
    int TotalCount,
    int TotalPages,
    int CurrentPage,
    int PageSize,
    List<object> Items);

/// <summary>Status response for a content process.</summary>
public record ContentProcessStatusResponse(
    string Status,
    string ProcessId,
    string FileName,
    string Message,
    string? ResourceUrl = null);
