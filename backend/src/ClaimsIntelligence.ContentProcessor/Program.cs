using Azure.Monitor.OpenTelemetry.Exporter;
using ClaimsIntelligence.ContentProcessor;
using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.ContentProcessor.Extensions;
using ClaimsIntelligence.Domain.Workflow;
using ClaimsIntelligence.Infrastructure.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;

var builder = Host.CreateApplicationBuilder(args);

// ── Infrastructure (Blob, Queue, Cosmos, CU, OpenAI, App Config) ────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ── Cosmos collection for ContentProcess ─────────────────────────────────────
builder.Services.AddCosmosCollection<ContentProcess>("processes");

// ── ContentProcessor pipeline steps + options ─────────────────────────────────
builder.Services.AddContentProcessor(builder.Configuration);

// ── Polly resilience pipeline ─────────────────────────────────────────────────
// Exponential back-off with jitter, max 5 retries before giving up and
// routing the message to the dead-letter queue.
var processorOptions = builder.Configuration
    .GetSection(ContentProcessorOptions.SectionName)
    .Get<ContentProcessorOptions>() ?? new ContentProcessorOptions();

builder.Services.AddResiliencePipeline("content-processor", pipelineBuilder =>
{
    pipelineBuilder.AddRetry(new Polly.Retry.RetryStrategyOptions
    {
        MaxRetryAttempts = processorOptions.MaxRetryAttempts,
        Delay = TimeSpan.FromSeconds(processorOptions.RetryBaseDelaySeconds),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        OnRetry = static args =>
        {
            // Polly does not expose ILogger in static callbacks; use console as fallback.
            Console.WriteLine(
                $"[Polly] Retry attempt {args.AttemptNumber + 1} " +
                $"after {args.RetryDelay.TotalMilliseconds:F0}ms. " +
                $"Exception: {args.Outcome.Exception?.Message}");
            return ValueTask.CompletedTask;
        }
    });
});

// ── Hosted worker service ─────────────────────────────────────────────────────
builder.Services.AddHostedService<Worker>();

// ── OpenTelemetry tracing ─────────────────────────────────────────────────────
var appInsightsConnStr = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["Azure:ApplicationInsightsConnectionString"];

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("ClaimsIntelligence.ContentProcessor"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("ClaimsIntelligence.ContentProcessor")
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrEmpty(appInsightsConnStr))
        {
            // AddAzureMonitorTraceExporter is on TracerProviderBuilder
            // (Azure.Monitor.OpenTelemetry.Exporter package).
            tracing.AddAzureMonitorTraceExporter(o =>
                o.ConnectionString = appInsightsConnStr);
        }
        else
        {
            // Export to OTLP collector when running in dev/container without App Insights.
            tracing.AddOtlpExporter();
        }
    });

var host = builder.Build();
await host.RunAsync();
