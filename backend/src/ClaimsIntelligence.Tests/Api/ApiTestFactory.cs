using ClaimsIntelligence.Domain.Schemas;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClaimsIntelligence.Tests.Api;

/// <summary>
/// WebApplicationFactory that replaces all Azure infrastructure services with in-memory mocks
/// so integration tests run without any Azure dependencies.
/// </summary>
public class ApiTestFactory : WebApplicationFactory<Program>
{
    public Mock<ICosmosService<ContentProcess>> ContentProcessCosmos { get; } = new();
    public Mock<ICosmosService<ClaimProcess>> ClaimProcessCosmos { get; } = new();
    public Mock<ICosmosService<Schema>> SchemaCosmos { get; } = new();
    public Mock<ICosmosService<SchemaSet>> SchemaSetCosmos { get; } = new();
    public Mock<IBlobStorageService> BlobService { get; } = new();
    public Mock<IQueueStorageService> QueueService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace all real infrastructure services with mocks
            services.RemoveAll<ICosmosService<ContentProcess>>();
            services.RemoveAll<ICosmosService<ClaimProcess>>();
            services.RemoveAll<ICosmosService<Schema>>();
            services.RemoveAll<ICosmosService<SchemaSet>>();
            services.RemoveAll<IBlobStorageService>();
            services.RemoveAll<IQueueStorageService>();

            // Remove Cosmos/Azure SDK clients that require real endpoints
            services.RemoveAll(typeof(Microsoft.Azure.Cosmos.CosmosClient));
            services.RemoveAll(typeof(Microsoft.Azure.Cosmos.Database));
            services.RemoveAll(typeof(Azure.Storage.Blobs.BlobServiceClient));
            services.RemoveAll(typeof(Azure.Storage.Queues.QueueServiceClient));
            services.RemoveAll(typeof(Azure.AI.Inference.ChatCompletionsClient));
            services.RemoveAll<ClaimsIntelligence.Infrastructure.ContentUnderstanding.IContentUnderstandingClient>();
            services.RemoveAll<ClaimsIntelligence.Infrastructure.OpenAI.IAzureInferenceService>();
            services.RemoveAll<ClaimsIntelligence.Infrastructure.AppConfiguration.IAppConfigurationService>();
            services.RemoveAll(typeof(Azure.Data.AppConfiguration.ConfigurationClient));

            // Register mocks
            services.AddSingleton(ContentProcessCosmos.Object);
            services.AddSingleton(ClaimProcessCosmos.Object);
            services.AddSingleton(SchemaCosmos.Object);
            services.AddSingleton(SchemaSetCosmos.Object);
            services.AddSingleton(BlobService.Object);
            services.AddSingleton(QueueService.Object);
        });
    }
}
