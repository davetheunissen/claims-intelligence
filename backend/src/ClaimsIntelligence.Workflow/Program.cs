using Azure.Monitor.OpenTelemetry.Exporter;
using ClaimsIntelligence.Domain.Workflow;
using ClaimsIntelligence.Infrastructure.Extensions;
using ClaimsIntelligence.Workflow;
using ClaimsIntelligence.Workflow.Extensions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// --- Infrastructure (Azure services: Blob, Queue, Cosmos, OpenAI, AppConfig) ---
builder.Services.AddInfrastructure(builder.Configuration);

// --- Cosmos collections ---
builder.Services.AddCosmosCollection<ClaimProcess>("claimprocesses");
builder.Services.AddCosmosCollection<ContentProcess>("processes");

// --- Workflow layer (executors, WorkflowRunner, Polly, checkpoint) ---
builder.Services.AddWorkflow(builder.Configuration);

// --- Background worker ---
builder.Services.AddHostedService<Worker>();

// --- OpenTelemetry ---
var serviceName = "ClaimsIntelligence.Workflow";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("ClaimsIntelligence.Workflow")
            .AddOtlpExporter();

        var appInsightsConnStr = builder.Configuration["Azure:ApplicationInsightsConnectionString"];
        if (!string.IsNullOrWhiteSpace(appInsightsConnStr))
        {
            tracing.AddAzureMonitorTraceExporter(o =>
                o.ConnectionString = appInsightsConnStr);
        }
    });

var host = builder.Build();
await host.RunAsync();
