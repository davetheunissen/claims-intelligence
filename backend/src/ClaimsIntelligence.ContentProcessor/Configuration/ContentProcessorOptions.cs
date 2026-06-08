namespace ClaimsIntelligence.ContentProcessor.Configuration;

public record ContentProcessorOptions
{
    public const string SectionName = "ContentProcessor";

    public string ExtractQueueName { get; init; } = "content-pipeline-extract-queue";
    public string MapQueueName { get; init; } = "content-pipeline-map-queue";
    public string EvaluateQueueName { get; init; } = "content-pipeline-evaluate-queue";
    public string SaveQueueName { get; init; } = "content-pipeline-save-queue";
    public string ProcessesContainer { get; init; } = "cps-processes";
    public string ConfigurationContainer { get; init; } = "cps-configuration";
    public string ExtractAnalyzerId { get; init; } = "prebuilt-layout";
    public int QueuePollingIntervalSeconds { get; init; } = 5;
    public int MessageVisibilityTimeoutSeconds { get; init; } = 300;
    public int MaxMessagesPerPoll { get; init; } = 1;
    public int MaxRetryAttempts { get; init; } = 5;
    public double RetryBaseDelaySeconds { get; init; } = 2.0;
}
