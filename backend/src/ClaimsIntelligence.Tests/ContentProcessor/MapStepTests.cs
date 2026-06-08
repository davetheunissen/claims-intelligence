using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.ContentProcessor.Pipeline;

namespace ClaimsIntelligence.Tests.ContentProcessor;

/// <summary>
/// Unit tests for MapStep — schema-driven field extraction via CU custom analyzer.
/// Tests the sidecar path, live CU path, and field parsing.
/// </summary>
public class MapStepTests
{
    private readonly Mock<IContentUnderstandingClient> _cuClient = new();
    private readonly Mock<IBlobStorageService> _blobService = new();
    private readonly Mock<ILogger<MapStep>> _logger = new();

    private readonly IOptions<ContentProcessorOptions> _options =
        Options.Create(new ContentProcessorOptions
        {
            ProcessesContainer = "cps-processes",
            ConfigurationContainer = "cps-configuration"
        });

    private MapStep CreateSut() =>
        new(_cuClient.Object, _blobService.Object, _options, _logger.Object);

    private static DataPipeline BuildPipeline(string processId = "proc-1", string schemaId = "schema-1")
    {
        var pipeline = new DataPipeline { ProcessId = processId };
        pipeline.PipelineStatus.ProcessId = processId;
        pipeline.PipelineStatus.SchemaId = schemaId;
        pipeline.PipelineStatus.ActiveStep = "map";
        pipeline.Files.Add(new FileDetails
        {
            Id = Guid.NewGuid().ToString(),
            ProcessId = processId,
            Name = "claim.pdf",
            MimeType = MimeTypes.Pdf,
            ArtifactType = ArtifactType.SourceContent
        });
        return pipeline;
    }

    private static string BuildSchemaJson(string className = "ClaimForm") =>
        JsonSerializer.Serialize(new
        {
            className,
            fieldSchema = new
            {
                fields = new
                {
                    claimantName = new { type = "string" },
                    incidentDate = new { type = "date" }
                }
            }
        });

    [Fact]
    public async Task ExecuteAsync_WithSidecar_UsesSidecarAndSkipsLiveCuCall()
    {
        var pipeline = BuildPipeline();

        _blobService
            .Setup(b => b.DownloadTextAsync(
                It.IsAny<string>(), "schema.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSchemaJson());

        var sidecarPayload = JsonSerializer.Serialize(new
        {
            result = new
            {
                contents = new[]
                {
                    new
                    {
                        fields = new
                        {
                            claimantName = new { valueString = "Jane Smith", confidence = 0.95 },
                            incidentDate = new { valueDate = "2024-01-15", confidence = 0.90 }
                        }
                    }
                }
            }
        });

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", "proc-1/claim.pdf.cu.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sidecarPayload);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        _cuClient.Verify(c => c.EnsureFieldAnalyzerAsync(
            It.IsAny<string>(), It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.Files.Should().Contain(f =>
            f.ArtifactType == ArtifactType.SchemaMappedData &&
            f.Name == "gpt_output.json");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutSidecar_CallsLiveCuAnalyzer()
    {
        var pipeline = BuildPipeline();

        _blobService
            .Setup(b => b.DownloadTextAsync(
                It.IsAny<string>(), "schema.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSchemaJson());

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", "proc-1/claim.pdf.cu.json", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Blob not found"));

        _cuClient
            .Setup(c => c.EnsureFieldAnalyzerAsync("ClaimForm", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("analyzer-abc");

        _blobService
            .Setup(b => b.DownloadAsync("cps-processes", "proc-1/claim.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        var cuResponse = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            result = new
            {
                contents = new[]
                {
                    new
                    {
                        fields = new
                        {
                            claimantName = new { valueString = "John Doe", confidence = 0.88 }
                        }
                    }
                }
            }
        })).RootElement;

        _cuClient
            .Setup(c => c.AnalyzeAndWaitAsync(
                "analyzer-abc", It.IsAny<byte[]>(),
                It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cuResponse);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        _cuClient.Verify(c => c.EnsureFieldAnalyzerAsync("ClaimForm", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Once);
        _cuClient.Verify(c => c.AnalyzeAndWaitAsync("analyzer-abc", It.IsAny<byte[]>(), It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MissingSchemaId_ThrowsInvalidOperationException()
    {
        var pipeline = new DataPipeline { ProcessId = "proc-1" };
        pipeline.PipelineStatus.ProcessId = "proc-1";
        pipeline.PipelineStatus.ActiveStep = "map";
        // SchemaId intentionally omitted
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = "proc-1",
            Name = "claim.pdf",
            MimeType = MimeTypes.Pdf,
            ArtifactType = ArtifactType.SourceContent
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().ExecuteAsync(pipeline, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedMimeType_ThrowsInvalidOperationException()
    {
        var pipeline = new DataPipeline { ProcessId = "proc-1" };
        pipeline.PipelineStatus.ProcessId = "proc-1";
        pipeline.PipelineStatus.SchemaId = "schema-1";
        pipeline.PipelineStatus.ActiveStep = "map";
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = "proc-1",
            Name = "document.txt",
            MimeType = MimeTypes.PlainText,
            ArtifactType = ArtifactType.SourceContent
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().ExecuteAsync(pipeline, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_AddsSuccessStepResult()
    {
        var pipeline = BuildPipeline();

        _blobService
            .Setup(b => b.DownloadTextAsync(It.IsAny<string>(), "schema.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSchemaJson());

        var sidecarPayload = JsonSerializer.Serialize(new
        {
            result = new
            {
                contents = new[]
                {
                    new { fields = new { claimantName = new { valueString = "Jane", confidence = 0.9 } } }
                }
            }
        });

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", "proc-1/claim.pdf.cu.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sidecarPayload);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        var stepResult = result.PipelineStatus.GetStepResult("map");
        stepResult.Should().NotBeNull();
        var json = JsonSerializer.Serialize(stepResult!.Result);
        json.Should().Contain("success");
    }
}
