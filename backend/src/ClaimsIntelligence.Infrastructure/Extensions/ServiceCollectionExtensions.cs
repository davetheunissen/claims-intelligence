using Azure.Data.AppConfiguration;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.AI.Inference;
using ClaimsIntelligence.Infrastructure.AppConfiguration;
using ClaimsIntelligence.Infrastructure.Blob;
using ClaimsIntelligence.Infrastructure.ContentUnderstanding;
using ClaimsIntelligence.Infrastructure.Cosmos;
using ClaimsIntelligence.Infrastructure.OpenAI;
using ClaimsIntelligence.Infrastructure.Queue;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimsIntelligence.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Azure infrastructure services. Bind from "Azure" config section.
    ///
    /// Required keys:
    ///   Azure:BlobStorageAccountUrl          — https://{account}.blob.core.windows.net
    ///   Azure:QueueStorageAccountUrl         — https://{account}.queue.core.windows.net
    ///   Azure:CosmosEndpoint                 — https://{account}.documents.azure.com:443/
    ///   Azure:CosmosDatabaseName             — target database name
    ///   Azure:ContentUnderstandingEndpoint   — https://{resource}.cognitiveservices.azure.com
    ///   Azure:AzureInferenceEndpoint         — AI Foundry endpoint
    ///   Azure:AppConfigurationEndpoint       — https://{name}.azconfig.io
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var cfg = configuration.GetSection("Azure");
        var credential = new DefaultAzureCredential();

        // --- Blob Storage ---
        services.AddSingleton(_ =>
            new BlobServiceClient(new Uri(cfg["BlobStorageAccountUrl"]!), credential));
        services.AddSingleton<IBlobStorageService, BlobStorageService>();

        // --- Queue Storage ---
        services.AddSingleton(_ =>
            new QueueServiceClient(new Uri(cfg["QueueStorageAccountUrl"]!), credential));
        services.AddSingleton<IQueueStorageService, QueueStorageService>();

        // --- Cosmos DB (NoSQL API) ---
        // Partition key path is /id for all containers. Callers register per-container services
        // via AddCosmosCollection<T>() after calling AddInfrastructure().
        services.AddSingleton(_ => new CosmosClient(
            cfg["CosmosEndpoint"]!,
            credential,
            new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            }));
        services.AddSingleton(sp =>
            sp.GetRequiredService<CosmosClient>().GetDatabase(cfg["CosmosDatabaseName"]));

        // --- Content Understanding ---
        services.AddHttpClient("ContentUnderstanding");
        services.Configure<ContentUnderstandingOptions>(cfg.GetSection("ContentUnderstanding"));
        services.AddSingleton<Azure.Core.TokenCredential>(_ => credential);
        services.AddSingleton<IContentUnderstandingClient, ContentUnderstandingClient>();

        // --- Azure AI Inference ---
        services.AddSingleton(_ =>
            new ChatCompletionsClient(
                new Uri(cfg["AzureInferenceEndpoint"]!),
                credential));
        services.AddSingleton<IAzureInferenceService, AzureInferenceService>();

        // --- App Configuration ---
        services.AddSingleton(_ =>
            new ConfigurationClient(
                new Uri(cfg["AppConfigurationEndpoint"]!),
                credential));
        services.AddSingleton<IAppConfigurationService, AppConfigurationService>();

        return services;
    }

    /// <summary>
    /// Registers ICosmosService&lt;T&gt; backed by the named Cosmos container.
    /// The container must already exist in the database with partition key path /id.
    /// Call once per document type after AddInfrastructure().
    /// </summary>
    public static IServiceCollection AddCosmosCollection<T>(
        this IServiceCollection services,
        string containerName)
    {
        services.AddSingleton<ICosmosService<T>>(sp =>
        {
            var db = sp.GetRequiredService<Database>();
            return new CosmosService<T>(db.GetContainer(containerName));
        });

        return services;
    }
}
