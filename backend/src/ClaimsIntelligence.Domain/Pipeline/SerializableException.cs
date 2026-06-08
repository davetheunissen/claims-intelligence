namespace ClaimsIntelligence.Domain.Pipeline;

public record SerializableException(
    string? ExceptionType,
    string? ExceptionMessage,
    string? ExceptionDetails,
    string? StackTrace,
    string? InnerException
)
{
    public static SerializableException FromException(Exception ex) => new(
        ex.GetType().Name,
        ex.Message,
        ex.ToString(),
        ex.StackTrace,
        ex.InnerException?.Message
    );
}
