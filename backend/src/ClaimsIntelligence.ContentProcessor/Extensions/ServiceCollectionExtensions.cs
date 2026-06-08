using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.ContentProcessor.Pipeline;
using ClaimsIntelligence.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsIntelligence.ContentProcessor.Extensions;

/// <summary>
/// DI registration for the ContentProcessor pipeline steps and configuration.
/// Call after <c>AddInfrastructure()</c> and <c>AddCosmosCollection&lt;ContentProcess&gt;()</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ContentProcessorOptions"/> and the four pipeline steps.
    /// </summary>
    public static IServiceCollection AddContentProcessor(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind options from "ContentProcessor" config section.
        services.Configure<ContentProcessorOptions>(
            configuration.GetSection(ContentProcessorOptions.SectionName));

        // Register the four pipeline steps in execution order.
        // All are registered as transient — one instance per pipeline execution.
        services.AddTransient<IPipelineStep, ExtractStep>();
        services.AddTransient<IPipelineStep, MapStep>();
        services.AddTransient<IPipelineStep, EvaluateStep>();
        services.AddTransient<IPipelineStep, SaveStep>();

        // Named individual registrations so Worker can resolve them by type.
        services.AddTransient<ExtractStep>();
        services.AddTransient<MapStep>();
        services.AddTransient<EvaluateStep>();
        services.AddTransient<SaveStep>();

        return services;
    }
}
