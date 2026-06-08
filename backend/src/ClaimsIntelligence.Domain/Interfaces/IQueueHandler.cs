namespace ClaimsIntelligence.Domain.Interfaces;

public interface IQueueHandler
{
    Task HandleAsync(string messageBody, CancellationToken cancellationToken);
}
