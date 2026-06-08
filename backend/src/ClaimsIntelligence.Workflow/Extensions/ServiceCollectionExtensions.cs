using ClaimsIntelligence.Domain.Interfaces;
using ClaimsIntelligence.Workflow.Checkpoint;
using ClaimsIntelligence.Workflow.Configuration;
using ClaimsIntelligence.Workflow.GapRules;
using ClaimsIntelligence.Workflow.Workflow;
using ClaimsIntelligence.Workflow.Workflow.Executors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;

namespace ClaimsIntelligence.Workflow.Extensions;

/// <summary>
/// DI extension that wires up all Workflow-layer services.
/// Call after <c>AddInfrastructure()</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflow(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration options.
        services.Configure<WorkflowOptions>(
            configuration.GetSection(WorkflowOptions.SectionName));

        // Gap rule loading.
        services.AddSingleton<GapRuleLoader>();

        // Checkpoint storage — Cosmos-backed.
        services.AddSingleton<ICheckpointStorage, CosmosCheckpointStorage>();

        // Executors — transient (new instance per claim execution).
        services.AddTransient<IWorkflowExecutor, DocumentProcessExecutor>();
        services.AddTransient<IWorkflowExecutor, RaiExecutor>();
        services.AddTransient<IWorkflowExecutor, SummarizeExecutor>();
        services.AddTransient<IWorkflowExecutor, GapExecutor>();

        // DAG orchestrator — transient so each claim execution gets fresh executor instances.
        services.AddTransient<WorkflowRunner>();

        // Polly resilience pipelines.
        services.AddResiliencePipeline("workflow-queue-poll", builder =>
        {
            builder.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromSeconds(60),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                    ex is not OperationCanceledException)
            });
        });

        services.AddResiliencePipeline("workflow-http", builder =>
        {
            builder.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                    ex is not OperationCanceledException &&
                    ex is not WorkflowExecutorFailedException)
            });
        });

        return services;
    }
}
