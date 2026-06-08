using System.Net;
using System.Net.Http.Json;
using ClaimsIntelligence.Api.Models.Requests;
using ClaimsIntelligence.Domain.Schemas;

namespace ClaimsIntelligence.Tests.Api;

/// <summary>
/// Integration tests for /schemavault endpoints.
/// </summary>
public class SchemaVaultEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    public SchemaVaultEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListSchemas_ReturnsAllSchemas()
    {
        var schemas = new List<Schema>
        {
            new() { Id = "s1", ClassName = "ClaimForm", FileName = "ClaimForm.json" },
            new() { Id = "s2", ClassName = "PoliceReport", FileName = "PoliceReport.json" }
        };

        _factory.SchemaCosmos
            .Setup(c => c.GetAllAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemas);

        var response = await _client.GetAsync("/schemavault/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ClaimForm");
        body.Should().Contain("PoliceReport");
    }

    [Fact]
    public async Task RegisterSchemaJson_ValidRequest_Returns200WithSchemaId()
    {
        _factory.BlobService
            .Setup(b => b.EnsureContainerExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory.BlobService
            .Setup(b => b.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory.SchemaCosmos
            .Setup(c => c.InsertAsync(It.IsAny<Schema>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new SchemaVaultRegisterJsonRequest(
            ClassName: "TestSchema",
            Description: "Test",
            FieldSchema: new Dictionary<string, object> { ["fields"] = new { claimantName = new { type = "string" } } },
            BaseAnalyzerId: "prebuilt-document",
            CompletionModel: "gpt-4");

        var response = await _client.PostAsJsonAsync("/schemavault/json", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("TestSchema");
    }

    [Fact]
    public async Task RegisterSchemaJson_MissingFields_Returns400()
    {
        var request = new SchemaVaultRegisterJsonRequest(
            ClassName: "BadSchema",
            Description: null!,
            FieldSchema: null!); // Missing fields

        var response = await _client.PostAsJsonAsync("/schemavault/json", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DownloadSchemaFile_ExistingSchema_Returns200WithFile()
    {
        const string schemaId = "schema-dl";
        var schema = new Schema
        {
            Id = schemaId,
            ClassName = "ClaimForm",
            FileName = "ClaimForm.json",
            ContentType = "application/json"
        };

        _factory.SchemaCosmos
            .Setup(c => c.GetByIdAsync(schemaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schema);

        _factory.BlobService
            .Setup(b => b.DownloadAsync(It.IsAny<string>(), $"{schemaId}/ClaimForm.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes("""{"fieldSchema":{"fields":{}}}"""));

        var response = await _client.GetAsync($"/schemavault/schemas/{schemaId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DownloadSchemaFile_NotFound_Returns404()
    {
        const string schemaId = "schema-missing";

        _factory.SchemaCosmos
            .Setup(c => c.GetByIdAsync(schemaId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Schema?)null);

        var response = await _client.GetAsync($"/schemavault/schemas/{schemaId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
