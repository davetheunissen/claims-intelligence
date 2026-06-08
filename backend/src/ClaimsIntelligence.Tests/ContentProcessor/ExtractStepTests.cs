using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.ContentProcessor.Pipeline;

namespace ClaimsIntelligence.Tests.ContentProcessor;

/// <summary>
/// Unit tests for ExtractStep — mirrors Python test_pipeline_step_helper.py intent
/// for the extract handler (sidecar path, live CU call, image skip, unsupported skip).
/// </summary>
public class ExtractStepTests
{
    private readonly Mock<IContentUnderstandingClient> _cuClient = new();
    private readonly Mock<IBlobStorageService> _blobService = new();
    private readonly Mock<ILogger<ExtractStep>> _logger = new();

    private readonly IOptions<ContentProcessorOptions> _options =
        Options.Create(new ContentProcessorOptions
        {
            ProcessesContainer = "cps-processes",
            ExtractAnalyzerId = "prebuilt-layout"
        });

    private ExtractStep CreateSut() =>
        new(_cuClient.Object, _blobService.Object, _options, _logger.Object);

    private static DataPipeline BuildPdfPipeline(string processId = "proc-1")
    {
        var pipeline = new DataPipeline { ProcessId = processId };
        pipeline.PipelineStatus.ProcessId = processId;
        pipeline.PipelineStatus.ActiveStep = "extract";
        var sourceFile = new FileDetails
        {
            Id = Guid.NewGuid().ToString(),
            ProcessId = processId,
            Name = "claim.pdf",
            MimeType = MimeTypes.Pdf,
            ArtifactType = ArtifactType.SourceContent
        };
        pipeline.Files.Add(sourceFile);
        return pipeline;
    }

    private static DataPipeline BuildImagePipeline(string mimeType, string processId = "proc-img")
    {
        var pipeline = new DataPipeline { ProcessId = processId };
        pipeline.PipelineStatus.ProcessId = processId;
        pipeline.PipelineStatus.ActiveStep = "extract";
        var sourceFile = new FileDetails
        {
            Id = Guid.NewGuid().ToString(),
            ProcessId = processId,
            Name = "photo.jpg",
            MimeType = mimeType,
            ArtifactType = ArtifactType.SourceContent
        };
        pipeline.Files.Add(sourceFile);
        return pipeline;
    }

    [Fact]
    public async Task ExecuteAsync_ImageJpeg_SkipsExtractionAndReturnsSkippedResult()
    {
        var pipeline = BuildImagePipeline(MimeTypes.ImageJpeg);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        result.PipelineStatus.GetStepResult("extract").Should().NotBeNull();
        var stepResult = result.PipelineStatus.GetStepResult("extract")!;
        var json = JsonSerializer.Serialize(stepResult.Result);
        json.Should().Contain("skipped");
    }

    [Fact]
    public async Task ExecuteAsync_ImagePng_SkipsExtractionAndReturnsSkippedResult()
    {
        var pipeline = BuildImagePipeline(MimeTypes.ImagePng);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        result.PipelineStatus.GetStepResult("extract").Should().NotBeNull();
        var json = JsonSerializer.Serialize(result.PipelineStatus.GetStepResult("extract")!.Result);
        json.Should().Contain("skipped");
    }

    [Fact]
    public async Task ExecuteAsync_Pdf_UsesExistingSidecarWhenPresent()
    {
        var pipeline = BuildPdfPipeline();
        var sidecarJson = JsonSerializer.Serialize(new
        {
            result = new
            {
                contents = new[]
                {
                    new { markdown = "# Claim document content" }
                }
            }
        });

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", "proc-1/claim.pdf.cu.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sidecarJson);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        // CU client should NOT be called when sidecar is available
        _cuClient.Verify(c => c.AnalyzeAndWaitAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(),
            It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        result.PipelineStatus.GetStepResult("extract")!.Should().NotBeNull();
        var json = JsonSerializer.Serialize(result.PipelineStatus.GetStepResult("extract")!.Result);
        json.Should().Contain("success");
    }

    [Fact]
    public async Task ExecuteAsync_Pdf_CallsLiveCuWhenNoSidecar()
    {
        var pipeline = BuildPdfPipeline();

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", "proc-1/claim.pdf.cu.json", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Blob not found"));

        _blobService
            .Setup(b => b.DownloadAsync("cps-processes", "proc-1/claim.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // PDF magic bytes

        var cuResultJson = JsonSerializer.Serialize(new
        {
            result = new
            {
                contents = new[]
                {
                    new { markdown = "# Claim document content" }
                }
            }
        });
        var cuResult = JsonDocument.Parse(cuResultJson).RootElement;

        _cuClient
            .Setup(c => c.AnalyzeAndWaitAsync(
                "prebuilt-layout", It.IsAny<byte[]>(),
                It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cuResult);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        _cuClient.Verify(c => c.AnalyzeAndWaitAsync(
            "prebuilt-layout", It.IsAny<byte[]>(),
            It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var json = JsonSerializer.Serialize(result.PipelineStatus.GetStepResult("extract")!.Result);
        json.Should().Contain("success");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedMimeType_SkipsWithUnsupportedReason()
    {
        var pipeline = new DataPipeline { ProcessId = "proc-txt" };
        pipeline.PipelineStatus.ProcessId = "proc-txt";
        pipeline.PipelineStatus.ActiveStep = "extract";
        pipeline.Files.Add(new FileDetails
        {
            Id = Guid.NewGuid().ToString(),
            ProcessId = "proc-txt",
            Name = "document.txt",
            MimeType = MimeTypes.PlainText,
            ArtifactType = ArtifactType.SourceContent
        });

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        var json = JsonSerializer.Serialize(result.PipelineStatus.GetStepResult("extract")!.Result);
        json.Should().Contain("skipped");
    }

    [Fact]
    public async Task ExecuteAsync_Pdf_AddsExtractedContentFileToFiles()
    {
        var pipeline = BuildPdfPipeline();

        var sidecarJson = JsonSerializer.Serialize(new
        {
            result = new
            {
                contents = new[]
                {
                    new { markdown = "# Document content" }
                }
            }
        });

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", "proc-1/claim.pdf.cu.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sidecarJson);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        result.Files.Should().Contain(f =>
            f.ArtifactType == ArtifactType.ExtractedContent &&
            f.Name == "content_understanding_output.json");
    }
}
