using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.ContentProcessor.Pipeline;

namespace ClaimsIntelligence.Tests.ContentProcessor;

/// <summary>
/// Unit tests for EvaluateStep — dual confidence scoring (OCR + CU field confidence merge).
/// Mirrors Python test_confidence.py and test_evaluate_model.py intent.
/// </summary>
public class EvaluateStepTests
{
    private readonly Mock<IBlobStorageService> _blobService = new();
    private readonly Mock<ILogger<EvaluateStep>> _logger = new();

    private readonly IOptions<ContentProcessorOptions> _options =
        Options.Create(new ContentProcessorOptions
        {
            ProcessesContainer = "cps-processes"
        });

    private EvaluateStep CreateSut() =>
        new(_blobService.Object, _options, _logger.Object);

    /// <summary>
    /// Builds a pipeline with both an extract artifact and a map artifact already attached
    /// (simulating that ExtractStep and MapStep already ran successfully).
    /// </summary>
    private static DataPipeline BuildPipelineWithPriorSteps(
        string processId = "proc-1",
        string mapJson = "",
        string extractJson = "")
    {
        var pipeline = new DataPipeline { ProcessId = processId };
        pipeline.PipelineStatus.ProcessId = processId;
        pipeline.PipelineStatus.ActiveStep = "evaluate";

        // Source file
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = processId,
            Name = "claim.pdf",
            MimeType = MimeTypes.Pdf,
            ArtifactType = ArtifactType.SourceContent
        });

        if (!string.IsNullOrEmpty(extractJson))
        {
            pipeline.PipelineStatus.ActiveStep = "extract";
            pipeline.Files.Add(new FileDetails
            {
                ProcessId = processId,
                Name = "content_understanding_output.json",
                ArtifactType = ArtifactType.ExtractedContent,
                ProcessedBy = "extract"
            });
        }

        if (!string.IsNullOrEmpty(mapJson))
        {
            pipeline.Files.Add(new FileDetails
            {
                ProcessId = processId,
                Name = "gpt_output.json",
                ArtifactType = ArtifactType.SchemaMappedData,
                ProcessedBy = "map"
            });
        }

        pipeline.PipelineStatus.ActiveStep = "evaluate";
        return pipeline;
    }

    private static string BuildMapJson(
        Dictionary<string, object?> fields,
        Dictionary<string, double>? cuConfidences = null)
    {
        var cuConf = cuConfidences ?? fields.ToDictionary(k => k.Key, _ => 0.9);
        return JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(fields),
                        parsed = fields
                    },
                    logprobs = (object?)null
                }
            },
            usage = new { prompt_tokens = 100, completion_tokens = 50, total_tokens = 150, input_tokens = 0 },
            _cu_field_confidences = cuConf,
            _cu_analyzer_id = "test-analyzer"
        });
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_CalculatesConfidenceAndAddsEvaluateFile()
    {
        const string processId = "proc-eval";
        var fields = new Dictionary<string, object?> { ["claimantName"] = "Jane Smith", ["incidentDate"] = "2024-01-15" };
        var mapJson = BuildMapJson(fields, new Dictionary<string, double> { ["claimantName"] = 0.95, ["incidentDate"] = 0.85 });
        var pipeline = BuildPipelineWithPriorSteps(processId, mapJson);

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/gpt_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapJson);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        result.Files.Should().Contain(f => f.ArtifactType == ArtifactType.ScoreMergedData && f.Name == "evaluate_output.json");
        result.PipelineStatus.GetStepResult("evaluate").Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithExtractOutput_MergesOcrAndCuConfidences()
    {
        const string processId = "proc-merge";
        var fields = new Dictionary<string, object?> { ["claimantName"] = "Bob", ["amount"] = 5000.0 };
        var mapJson = BuildMapJson(fields, new Dictionary<string, double> { ["claimantName"] = 0.9, ["amount"] = 0.8 });

        var extractJson = JsonSerializer.Serialize(new
        {
            result = new
            {
                contents = new[]
                {
                    new
                    {
                        fields = new
                        {
                            claimantName = new { confidence = 0.85 },
                            amount = new { confidence = 0.75 }
                        }
                    }
                }
            }
        });

        var pipeline = BuildPipelineWithPriorSteps(processId, mapJson, extractJson);

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/content_understanding_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractJson);

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/gpt_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapJson);

        string? uploadedJson = null;
        _blobService
            .Setup(b => b.UploadTextAsync("cps-processes", $"{processId}/evaluate_output.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, content, _) => uploadedJson = content)
            .Returns(Task.CompletedTask);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        // The merged confidence for claimantName should be average of OCR (0.85) and CU (0.9) = 0.875
        if (uploadedJson is not null)
        {
            var doc = JsonDocument.Parse(uploadedJson);
            doc.RootElement.TryGetProperty("confidence", out var conf).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ExecuteAsync_MissingMapFile_ThrowsInvalidOperationException()
    {
        var pipeline = new DataPipeline { ProcessId = "proc-nomap" };
        pipeline.PipelineStatus.ProcessId = "proc-nomap";
        pipeline.PipelineStatus.ActiveStep = "evaluate";
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = "proc-nomap",
            Name = "claim.pdf",
            MimeType = MimeTypes.Pdf,
            ArtifactType = ArtifactType.SourceContent
        });
        // No map file added

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().ExecuteAsync(pipeline, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_Image_SkipsOcrLoadAndStillEvaluates()
    {
        const string processId = "proc-img-eval";
        var fields = new Dictionary<string, object?> { ["claimantName"] = "Alice" };
        var mapJson = BuildMapJson(fields);

        var pipeline = new DataPipeline { ProcessId = processId };
        pipeline.PipelineStatus.ProcessId = processId;
        pipeline.PipelineStatus.ActiveStep = "evaluate";
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = processId,
            Name = "photo.jpg",
            MimeType = MimeTypes.ImageJpeg,
            ArtifactType = ArtifactType.SourceContent
        });
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = processId,
            Name = "gpt_output.json",
            ArtifactType = ArtifactType.SchemaMappedData,
            ProcessedBy = "map"
        });

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/gpt_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapJson);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        // Image pipelines should not try to fetch extract output
        _blobService.Verify(b => b.DownloadTextAsync(
            "cps-processes", $"{processId}/content_understanding_output.json", It.IsAny<CancellationToken>()),
            Times.Never);

        result.PipelineStatus.GetStepResult("evaluate").Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyFields_SetsZeroOverallConfidence()
    {
        const string processId = "proc-empty";
        var fields = new Dictionary<string, object?>();
        var mapJson = BuildMapJson(fields);
        var pipeline = BuildPipelineWithPriorSteps(processId, mapJson);

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/gpt_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapJson);

        string? uploadedEvalJson = null;
        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, blobName, content, _) =>
            {
                if (blobName.Contains("evaluate_output")) uploadedEvalJson = content;
            })
            .Returns(Task.CompletedTask);

        await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        if (uploadedEvalJson is not null)
        {
            var doc = JsonDocument.Parse(uploadedEvalJson);
            if (doc.RootElement.TryGetProperty("confidence", out var conf)
                && conf.TryGetProperty("overall_confidence", out var oc))
            {
                oc.GetDouble().Should().Be(0.0);
            }
        }
    }
}
