using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;

namespace ClaimsIntelligence.Infrastructure.Queue;

public class QueueStorageService(QueueServiceClient client, ILogger<QueueStorageService> logger) : IQueueStorageService
{
    private QueueClient GetQueueClient(string queueName) => client.GetQueueClient(queueName);

    public async Task EnsureQueueExistsAsync(string queueName, CancellationToken cancellationToken = default)
    {
        await GetQueueClient(queueName).CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task SendMessageAsync(string queueName, string content, CancellationToken cancellationToken = default)
    {
        await GetQueueClient(queueName).SendMessageAsync(content, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<QueueMessage>> ReceiveMessagesAsync(
        string queueName,
        int maxMessages = 1,
        TimeSpan? visibilityTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var response = await GetQueueClient(queueName).ReceiveMessagesAsync(
            maxMessages,
            visibilityTimeout,
            cancellationToken);

        return response.Value;
    }

    public async Task DeleteMessageAsync(string queueName, string messageId, string popReceipt, CancellationToken cancellationToken = default)
    {
        await GetQueueClient(queueName).DeleteMessageAsync(messageId, popReceipt, cancellationToken);
    }

    public async Task<string?> UpdateMessageVisibilityAsync(
        string queueName,
        string messageId,
        string popReceipt,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
    {
        var response = await GetQueueClient(queueName).UpdateMessageAsync(
            messageId,
            popReceipt,
            visibilityTimeout: visibilityTimeout,
            cancellationToken: cancellationToken);

        return response.Value.PopReceipt;
    }

    public async Task<int?> GetApproximateMessageCountAsync(string queueName, CancellationToken cancellationToken = default)
    {
        try
        {
            var props = await GetQueueClient(queueName).GetPropertiesAsync(cancellationToken);
            return props.Value.ApproximateMessagesCount;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get queue properties for {Queue}", queueName);
            return null;
        }
    }
}
