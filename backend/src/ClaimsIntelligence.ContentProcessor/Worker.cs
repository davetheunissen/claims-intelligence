using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.ContentProcessor.Pipeline;
using ClaimsIntelligence.Domain.Pipeline;
using ClaimsIntelligence.Infrastructure.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Registry;

namespace ClaimsIntelligence.ContentProcessor;

public sealed class Worker(
    IServiceScopeFactory scopeFactory,
    IQueueStorageService queueService,
    IOptions<ContentProcessorOptions> options,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<Worker> logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("ClaimsIntelligence.ContentProcessor");
    private readonly ContentProcessorOptions _options = options.Value;
    private readonly ResiliencePipeline _resiliencePipeline = pipelineProvider.GetPipeline("content-processor");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueName = _options.ExtractQueueName;
        var deadLetterQueueName = $"{queueName}-dead-letter-queue";

        logger.LogInformation("Worker starting. Polling queue: {QueueName}", queueName);

        await queueService.EnsureQueueExistsAsync(queueName, stoppingToken);
        await queueService.EnsureQueueExistsAsync(deadLetterQueueName, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await queueService.ReceiveMessagesAsync(
                    queueName,
                    _options.MaxMessagesPerPoll,
                    TimeSpan.FromSeconds(_options.MessageVisibilityTimeoutSeconds),
                    stoppingToken);

                if (messages.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.QueuePollingIntervalSeconds), stoppingToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    var rawBody = DecodeMessageBody(message.Body.ToString());
                    DataPipeline? pipeline = null;

                    try
                    {
                        pipeline = JsonSerializer.Deserialize<DataPipeline>(rawBody,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException ex)
                    {
                        logger.LogError(ex, "Failed to deserialize queue message. Sending to dead-letter.");
                        await SendToDeadLetterAsync(deadLetterQueueName, rawBody, $"Deserialization failed: {ex.Message}", stoppingToken);
                        await queueService.DeleteMessageAsync(queueName, message.MessageId, message.PopReceipt, stoppingToken);
                        continue;
                    }

                    if (pipeline is null)
                    {
                        logger.LogWarning("Queue message deserialized to null. Discarding.");
                        await queueService.DeleteMessageAsync(queueName, message.MessageId, message.PopReceipt, stoppingToken);
                        continue;
                    }

                    var processId = pipeline.PipelineStatus.ProcessId ?? pipeline.ProcessId;
                    var docName = pipeline.GetSourceFiles().FirstOrDefault()?.Name ?? "unknown";

                    using var workActivity = ActivitySource.StartActivity("contentprocessor.pipeline");
                    workActivity?.SetTag("process_id", processId);
                    workActivity?.SetTag("document_name", docName);

                    logger.LogInformation("Processing message process={ProcessId} document={DocumentName}", processId, docName);

                    bool succeeded = false;
                    Exception? lastException = null;

                    try
                    {
                        await _resiliencePipeline.ExecuteAsync(
                            async ct => await RunPipelineAsync(pipeline, ct),
                            stoppingToken);
                        succeeded = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lastException = ex;
                        workActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        logger.LogError(ex, "Pipeline failed after all retries for process={ProcessId}", processId);
                    }

                    if (succeeded)
                    {
                        await queueService.DeleteMessageAsync(queueName, message.MessageId, message.PopReceipt, stoppingToken);
                        logger.LogInformation("Pipeline completed for process={ProcessId}", processId);
                    }
                    else
                    {
                        await SendToDeadLetterAsync(deadLetterQueueName, rawBody, lastException?.Message ?? "Unknown error", stoppingToken);
                        await queueService.DeleteMessageAsync(queueName, message.MessageId, message.PopReceipt, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in polling loop. Continuing after delay.");
                await Task.Delay(TimeSpan.FromSeconds(_options.QueuePollingIntervalSeconds * 2), stoppingToken);
            }
        }

        logger.LogInformation("Worker stopped.");
    }

    private async Task RunPipelineAsync(DataPipeline pipeline, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var extract = scope.ServiceProvider.GetRequiredService<ExtractStep>();
        var map = scope.ServiceProvider.GetRequiredService<MapStep>();
        var evaluate = scope.ServiceProvider.GetRequiredService<EvaluateStep>();
        var save = scope.ServiceProvider.GetRequiredService<SaveStep>();

        var sw = Stopwatch.StartNew();

        pipeline.PipelineStatus.ActiveStep = "extract";
        pipeline = await ExecuteStepWithTimingAsync(extract, pipeline, sw, cancellationToken);

        pipeline.PipelineStatus.ActiveStep = "map";
        sw.Restart();
        pipeline = await ExecuteStepWithTimingAsync(map, pipeline, sw, cancellationToken);

        pipeline.PipelineStatus.ActiveStep = "evaluate";
        sw.Restart();
        pipeline = await ExecuteStepWithTimingAsync(evaluate, pipeline, sw, cancellationToken);

        pipeline.PipelineStatus.ActiveStep = "save";
        sw.Restart();
        pipeline = await ExecuteStepWithTimingAsync(save, pipeline, sw, cancellationToken);

        pipeline.PipelineStatus.Completed = true;
    }

    private static async Task<DataPipeline> ExecuteStepWithTimingAsync(
        Domain.Interfaces.IPipelineStep step,
        DataPipeline pipeline,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        sw.Start();
        var result = await step.ExecuteAsync(pipeline, cancellationToken);
        sw.Stop();

        var stepResult = result.PipelineStatus.GetStepResult(step.StepName);
        if (stepResult is not null)
            stepResult.Elapsed = sw.Elapsed.ToString(@"hh\:mm\:ss\.fff");

        return result;
    }

    private static string DecodeMessageBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(body));
        }
        catch
        {
            return body;
        }
    }

    private async Task SendToDeadLetterAsync(string deadLetterQueueName, string originalBody, string errorReason, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                original_message = originalBody,
                error_reason = errorReason,
                dead_lettered_at = DateTimeOffset.UtcNow.ToString("O")
            });

            await queueService.EnsureQueueExistsAsync(deadLetterQueueName, cancellationToken);
            await queueService.SendMessageAsync(deadLetterQueueName, payload, cancellationToken);
            logger.LogWarning("Message sent to dead-letter queue {Queue}. Reason: {Reason}", deadLetterQueueName, errorReason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send message to dead-letter queue {Queue}.", deadLetterQueueName);
        }
    }
}
