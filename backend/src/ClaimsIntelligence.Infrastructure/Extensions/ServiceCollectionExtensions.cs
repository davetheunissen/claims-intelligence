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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace ClaimsIntelligence.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Azure infrastructure services. Bind from "Azure" config section.
    ///
    /// Required keys:
    ///   Azure:BlobStorageAccountUrl      — https://{account}.blob.core.windows.net
    ///   Azure:QueueStorageAccountUrl     — https://{account}.queue.core.windows.net
    ///   Azure:CosmosConnectionString     — Cosmos DB Mongo API connection string
    ///   Azure:CosmosDatabaseName         — target database name
    ///   Azure:ContentUnderstandingEndpoint — https://{resource}.cognitiveservices.azure.com
    ///   Azure:AzureInferenceEndpoint     — AI Foundry endpoint
    ///   Azure:AppConfigurationEndpoint   — https://{name}.azconfig.io
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var cfg = configuration.GetSection("Azure");

        // --- Blob Storage ---
        services.AddSingleton(_ =>
            new BlobServiceClient(new Uri(cfg["BlobStorageAccountUrl"]!), new DefaultAzureCredential()));
        services.AddSingleton<IBlobStorageService, BlobStorageService>();

        // --- Queue Storage ---
        services.AddSingleton(_ =>
            new QueueServiceClient(new Uri(cfg["QueueStorageAccountUrl"]!), new DefaultAzureCredential()));
        services.AddSingleton<IQueueStorageService, QueueStorageService>();

        // --- Cosmos DB (Mongo API) ---
        // Callers register ICosmosMongoService<T> by adding the typed collection:
        //   services.AddCosmosCollection<ContentProcess>("processes");
        services.AddSingleton(new MongoClient(cfg["CosmosConnectionString"]));
        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<MongoClient>().GetDatabase(cfg["CosmosDatabaseName"]));

        // --- Content Understanding ---
        services.AddHttpClient("ContentUnderstanding");
        services.Configure<ContentUnderstandingOptions>(cfg.GetSection("ContentUnderstanding"));
        services.AddSingleton<Azure.Core.TokenCredential>(_ => new DefaultAzureCredential());
        services.AddSingleton<IContentUnderstandingClient, ContentUnderstandingClient>();

        // --- Azure AI Inference ---
        services.AddSingleton(_ =>
            new ChatCompletionsClient(
                new Uri(cfg["AzureInferenceEndpoint"]!),
                new DefaultAzureCredential()));
        services.AddSingleton<IAzureInferenceService, AzureInferenceService>();

        // --- App Configuration ---
        services.AddSingleton(_ =>
            new ConfigurationClient(
                new Uri(cfg["AppConfigurationEndpoint"]!),
                new DefaultAzureCredential()));
        services.AddSingleton<IAppConfigurationService, AppConfigurationService>();

        return services;
    }

    /// <summary>
    /// Registers ICosmosMongoService&lt;T&gt; backed by the named Cosmos collection.
    /// Call once per document type after AddInfrastructure().
    /// </summary>
    public static IServiceCollection AddCosmosCollection<T>(
        this IServiceCollection services,
        string collectionName)
    {
        services.AddSingleton<ICosmosMongoService<T>>(sp =>
        {
            var db = sp.GetRequiredService<IMongoDatabase>();
            return new CosmosMongoService<T>(db.GetCollection<T>(collectionName));
        });

        return services;
    }
}
