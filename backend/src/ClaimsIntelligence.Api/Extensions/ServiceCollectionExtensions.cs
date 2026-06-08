using Azure.Monitor.OpenTelemetry.AspNetCore;
using ClaimsIntelligence.Infrastructure.Extensions;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ClaimsIntelligence.Api.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the API layer services: infrastructure, Cosmos collections,
    /// Swagger, health checks, CORS, and OpenTelemetry.
    /// </summary>
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        // Infrastructure services (Blob, Queue, Cosmos, CU, Inference, AppConfig)
        services.AddInfrastructure(configuration);

        // Cosmos NoSQL containers (partition key path: /id)
        services.AddCosmosCollection<ContentProcess>("processes");
        services.AddCosmosCollection<ClaimProcess>("claimprocesses");
        services.AddCosmosCollection<Schema>("schemas");
        services.AddCosmosCollection<SchemaSet>("schemasets");

        // Swagger / OpenAPI
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new()
            {
                Title = "Claims Intelligence API",
                Version = "v1",
                Description = "ASP.NET Core 10 port of the ContentProcessorAPI gateway."
            });
        });

        // Health checks
        services.AddHealthChecks();

        // CORS — allow all in development; tighten in production via config
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        });

        // OpenTelemetry tracing
        var otel = services.AddOpenTelemetry();
        otel.ConfigureResource(r => r.AddService("ClaimsIntelligence.Api"));
        otel.WithTracing(t => t.AddAspNetCoreInstrumentation());

        // Azure Monitor exporter — only wire up if connection string is present
        var connectionString = configuration["Azure:ApplicationInsightsConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddOpenTelemetry().UseAzureMonitor(o => o.ConnectionString = connectionString);
        }

        return services;
    }
}
