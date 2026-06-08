using Azure.Storage.Queues.Models;

namespace ClaimsIntelligence.Infrastructure.Queue;

public interface IQueueStorageService
{
    Task EnsureQueueExistsAsync(string queueName, CancellationToken cancellationToken = default);

    Task SendMessageAsync(string queueName, string content, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueMessage>> ReceiveMessagesAsync(
        string queueName,
        int maxMessages = 1,
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default);

    Task DeleteMessageAsync(string queueName, string messageId, string popReceipt, CancellationToken cancellationToken = default);

    /// <summary>Returns the updated pop receipt after extending visibility.</summary>
    Task<string?> UpdateMessageVisibilityAsync(
        string queueName,
        string messageId,
        string popReceipt,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default);

    Task<int?> GetApproximateMessageCountAsync(string queueName, CancellationToken cancellationToken = default);
}
