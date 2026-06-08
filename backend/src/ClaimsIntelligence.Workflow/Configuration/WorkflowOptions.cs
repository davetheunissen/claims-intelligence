namespace ClaimsIntelligence.Workflow.Configuration;

/// <summary>
/// Configuration record for the Workflow worker service.
/// Bind from the "Workflow" section in appsettings.json / environment variables.
/// </summary>
public record WorkflowOptions
{
    public const string SectionName = "Workflow";

    // Queue settings
    public string ClaimProcessQueueName { get; init; } = "claim-process-queue";
    public string DeadLetterQueueName { get; init; } = "claim-process-dead-letter-queue";

    /// <summary>Visibility timeout applied when dequeuing a message (minutes).</summary>
    public int VisibilityTimeoutMinutes { get; init; } = 30;

    /// <summary>How long to sleep between polls when the queue is empty (seconds).</summary>
    public int PollIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// Maximum lifetime of the visibility-renewal loop (minutes).
    /// After this duration the message may become visible again so the
    /// dead-letter path can take effect.
    /// </summary>
    public int MessageTimeoutMinutes { get; init; } = 25;

    /// <summary>Maximum number of dequeue attempts before dead-lettering.</summary>
    public int MaxReceiveAttempts { get; init; } = 3;

    /// <summary>
    /// Shortened visibility timeout applied to a failed message so it
    /// becomes available for retry sooner (seconds).
    /// </summary>
    public int RetryVisibilityDelaySeconds { get; init; } = 5;

    // Model / AI settings
    public string InferenceModelName { get; init; } = "gpt-5.1";

    // RAI feature flag
    public bool RaiEnabled { get; init; } = true;

    // DocumentProcess polling
    public int DocumentPollIntervalSeconds { get; init; } = 5;
    public int DocumentPollTimeoutSeconds { get; init; } = 600;

    // Gap-rules loading
    /// <summary>
    /// Local path to the directory containing *.yaml gap-rule files.
    /// Defaults to the GapRules sub-folder next to the assembly.
    /// </summary>
    public string GapRulesPath { get; init; } = "GapRules";
}
