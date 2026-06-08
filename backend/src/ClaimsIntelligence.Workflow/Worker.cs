using System.Text;
using System.Text.Json;
using ClaimsIntelligence.Infrastructure.Queue;
using ClaimsIntelligence.Workflow.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClaimsIntelligence.Workflow;

/// <summary>
/// Long-running BackgroundService that polls <c>claim-process-queue</c> and
/// dispatches each message to <see cref="WorkflowRunner"/>.
///
/// Message lifecycle:
///   1. Dequeue with configurable visibility timeout.
///   2. Visibility-renewal loop runs concurrently at ~60% of timeout interval.
///   3. On success: delete message.
///   4. On transient failure (attempt &lt; maxReceiveAttempts): shorten visibility for retry.
///   5. On final failure or <see cref="WorkflowExecutorFailedException"/>: dead-letter + delete.
///
/// Polly exponential back-off + jitter on queue polling (max 5 retries).
///
/// Ported from: Python <c>services/queue_service.py</c>.
/// </summary>
public sealed class Worker(
    IQueueStorageService queue,
    IServiceScopeFactory scopeFactory,
    IOptions<WorkflowOptions> options,
    ResiliencePipelineProvider<string> polly,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly WorkflowOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Worker started. Queue={Queue}, VisibilityTimeout={Timeout}min",
            _options.ClaimProcessQueueName,
            _options.VisibilityTimeoutMinutes);

        // Ensure both queues exist (idempotent).
        await queue.EnsureQueueExistsAsync(_options.ClaimProcessQueueName, stoppingToken);
        await queue.EnsureQueueExistsAsync(_options.DeadLetterQueueName, stoppingToken);

        var pollPipeline = polly.GetPipeline("workflow-queue-poll");
        var visibilityTimeout = TimeSpan.FromMinutes(_options.VisibilityTimeoutMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await pollPipeline.ExecuteAsync(
                    async ct => await queue.ReceiveMessagesAsync(
                        _options.ClaimProcessQueueName,
                        maxMessages: 1,
                        visibilityTimeout: visibilityTimeout,
                        cancellationToken: ct),
                    stoppingToken);

                if (messages.Count == 0)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(_options.PollIntervalSeconds),
                        stoppingToken);
                    continue;
                }

                var msg = messages[0];

                string claimProcessId;
                try
                {
                    claimProcessId = ParseClaimProcessId(msg.MessageText);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex, "Malformed queue message {MessageId}; dead-lettering immediately",
                        msg.MessageId);
                    await DeadLetterAsync(
                        msg.MessageId, msg.PopReceipt, msg.MessageText,
                        "<unknown>", ex.Message, stoppingToken);
                    await queue.DeleteMessageAsync(
                        _options.ClaimProcessQueueName,
                        msg.MessageId, msg.PopReceipt, stoppingToken);
                    continue;
                }

                logger.LogInformation(
                    "Dequeued message for claim {ClaimId} (messageId={MessageId}, attempt={Attempt})",
                    claimProcessId, msg.MessageId, msg.DequeueCount);

                // Start visibility-renewal loop.
                using var renewCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var renewTask = RenewVisibilityLoopAsync(
                    msg.MessageId, msg.PopReceipt, claimProcessId, renewCts.Token);

                bool success = false;
                bool irrecoverableFailure = false;
                string failureReason = string.Empty;

                try
                {
                    // Resolve WorkflowRunner (and its transient executors) in a fresh scope
                    // per message so each claim gets brand-new executor instances.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner>();
                    await runner.RunAsync(claimProcessId, stoppingToken);
                    success = true;
                }
                catch (WorkflowExecutorFailedException ex)
                {
                    irrecoverableFailure = true;
                    failureReason = ex.Message;
                    logger.LogError(
                        ex,
                        "WorkflowExecutorFailedException for claim {ClaimId}; dead-lettering",
                        claimProcessId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "Worker cancelled while processing claim {ClaimId}", claimProcessId);
                    renewCts.Cancel();
                    await renewTask;
                    return;
                }
                catch (Exception ex)
                {
                    failureReason = ex.Message;
                    logger.LogError(
                        ex, "Unhandled exception for claim {ClaimId}", claimProcessId);
                }

                // Stop renewal loop before touching the pop receipt.
                renewCts.Cancel();
                await renewTask;

                if (success)
                {
                    await queue.DeleteMessageAsync(
                        _options.ClaimProcessQueueName,
                        msg.MessageId, msg.PopReceipt, stoppingToken);
                    logger.LogInformation(
                        "Message deleted after successful processing of claim {ClaimId}",
                        claimProcessId);
                }
                else if (irrecoverableFailure || msg.DequeueCount >= _options.MaxReceiveAttempts)
                {
                    await DeadLetterAsync(
                        msg.MessageId, msg.PopReceipt, msg.MessageText,
                        claimProcessId, failureReason, stoppingToken);
                    await queue.DeleteMessageAsync(
                        _options.ClaimProcessQueueName,
                        msg.MessageId, msg.PopReceipt, stoppingToken);
                }
                else
                {
                    // Retry — shorten visibility so the message reappears sooner.
                    logger.LogWarning(
                        "Retryable failure for claim {ClaimId} (attempt {Attempt}/{Max})",
                        claimProcessId, msg.DequeueCount, _options.MaxReceiveAttempts);
                    await queue.UpdateMessageVisibilityAsync(
                        _options.ClaimProcessQueueName,
                        msg.MessageId, msg.PopReceipt,
                        TimeSpan.FromSeconds(_options.RetryVisibilityDelaySeconds),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in Worker poll loop; will retry after delay");
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.PollIntervalSeconds * 2),
                    stoppingToken);
            }
        }

        logger.LogInformation("Worker stopped.");
    }

    /// <summary>
    /// Renews the message visibility at ~60% of the configured timeout so
    /// long-running jobs don't lose their lease.
    /// </summary>
    private async Task RenewVisibilityLoopAsync(
        string messageId,
        string popReceipt,
        string claimProcessId,
        CancellationToken ct)
    {
        var visibilitySeconds = _options.VisibilityTimeoutMinutes * 60;
        var sleepSeconds = Math.Max(10, (int)(visibilitySeconds * 0.6));
        var maxLifetimeSeconds = Math.Max(
            visibilitySeconds,
            _options.MessageTimeoutMinutes * 60);

        var started = DateTimeOffset.UtcNow;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleepSeconds), ct);

                if ((DateTimeOffset.UtcNow - started).TotalSeconds >= maxLifetimeSeconds)
                {
                    logger.LogWarning(
                        "[Renewal] Max lifetime reached for claim {ClaimId}; stopping renewal",
                        claimProcessId);
                    return;
                }

                try
                {
                    var newReceipt = await queue.UpdateMessageVisibilityAsync(
                        _options.ClaimProcessQueueName,
                        messageId, popReceipt,
                        TimeSpan.FromSeconds(visibilitySeconds),
                        ct);

                    if (newReceipt is not null)
                        popReceipt = newReceipt;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        ex, "[Renewal] Failed to renew visibility for claim {ClaimId}",
                        claimProcessId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancellation.
        }
    }

    private async Task DeadLetterAsync(
        string messageId,
        string popReceipt,
        string originalContent,
        string claimProcessId,
        string reason,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            claim_process_id = claimProcessId,
            failure_reason = reason,
            message_id = messageId,
            original_content = originalContent,
            dead_lettered_at = DateTimeOffset.UtcNow.ToString("O")
        });

        try
        {
            await queue.SendMessageAsync(_options.DeadLetterQueueName, payload, ct);
            logger.LogWarning(
                "Dead-lettered message {MessageId} for claim {ClaimId}: {Reason}",
                messageId, claimProcessId, reason);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send to dead-letter queue for message {MessageId}; " +
                "extending visibility to avoid silent loss", messageId);

            // Fallback: keep the message visible so it won't be silently lost.
            try
            {
                await queue.UpdateMessageVisibilityAsync(
                    _options.ClaimProcessQueueName,
                    messageId, popReceipt,
                    TimeSpan.FromSeconds(Math.Max(60, _options.RetryVisibilityDelaySeconds)),
                    ct);
            }
            catch (Exception extEx)
            {
                logger.LogError(
                    extEx,
                    "Also failed to extend visibility for message {MessageId}", messageId);
            }
        }
    }

    /// <summary>
    /// Parses <c>claim_process_id</c> from the queue message body.
    /// Handles both raw JSON and Base64-encoded JSON (Azure Queue default encoding).
    /// </summary>
    private static string ParseClaimProcessId(string messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            throw new ArgumentException("Queue message content is empty");

        var text = messageText.Trim();

        // Try Base64 decode first (Azure SDK may base64-encode by default).
        if (!text.StartsWith('{'))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(text));
                if (decoded.TrimStart().StartsWith('{'))
                    text = decoded.Trim();
            }
            catch { /* not base64 — proceed with original */ }
        }

        if (!text.StartsWith('{'))
            throw new FormatException(
                $"Queue message must be JSON with 'claim_process_id'. Got: {text[..Math.Min(50, text.Length)]}");

        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("claim_process_id", out var prop))
            throw new FormatException("Queue JSON must include 'claim_process_id'");

        var id = prop.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            throw new FormatException("claim_process_id is empty");

        return id;
    }
}
