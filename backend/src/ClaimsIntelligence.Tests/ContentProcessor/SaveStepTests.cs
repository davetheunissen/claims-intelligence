using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.ContentProcessor.Pipeline;

namespace ClaimsIntelligence.Tests.ContentProcessor;

/// <summary>
/// Unit tests for SaveStep — persistence of ContentProcess to Cosmos + blob artifacts.
/// </summary>
public class SaveStepTests
{
    private readonly Mock<IBlobStorageService> _blobService = new();
    private readonly Mock<ICosmosService<ContentProcess>> _cosmosService = new();
    private readonly Mock<ILogger<SaveStep>> _logger = new();

    private readonly IOptions<ContentProcessorOptions> _options =
        Options.Create(new ContentProcessorOptions
        {
            ProcessesContainer = "cps-processes"
        });

    private SaveStep CreateSut() =>
        new(_blobService.Object, _cosmosService.Object, _options, _logger.Object);

    private static string BuildEvaluateJson(double overallConfidence = 0.88, int total = 2, int zeros = 0)
    {
        return JsonSerializer.Serialize(new
        {
            extracted_result = new { claimantName = "Alice", amount = 1000.0 },
            confidence = new
            {
                overall_confidence = overallConfidence,
                min_extracted_field_confidence = 0.80,
                total_evaluated_fields_count = total,
                zero_confidence_fields_count = zeros,
                field_confidences = new { claimantName = 0.95, amount = 0.80 }
            },
            comparison_result = new { items = new object[] { } },
            prompt_tokens = 100,
            completion_tokens = 50,
            execution_time = 0
        });
    }

    private static string BuildMapJson() =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new { content = "{}", parsed = new { } },
                    logprobs = (object?)null
                }
            },
            usage = new { prompt_tokens = 50, completion_tokens = 25, total_tokens = 75, input_tokens = 0 },
            _cu_field_confidences = new { },
            _cu_analyzer_id = "test-analyzer"
        });

    private static DataPipeline BuildPipelineWithAllSteps(string processId = "proc-save")
    {
        var pipeline = new DataPipeline { ProcessId = processId };
        pipeline.PipelineStatus.ProcessId = processId;
        pipeline.PipelineStatus.ActiveStep = "save";

        pipeline.Files.Add(new FileDetails
        {
            ProcessId = processId,
            Name = "claim.pdf",
            MimeType = MimeTypes.Pdf,
            ArtifactType = ArtifactType.SourceContent
        });
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = processId,
            Name = "gpt_output.json",
            ArtifactType = ArtifactType.SchemaMappedData,
            ProcessedBy = "map"
        });
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = processId,
            Name = "evaluate_output.json",
            ArtifactType = ArtifactType.ScoreMergedData,
            ProcessedBy = "evaluate"
        });

        return pipeline;
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_UpsertsContentProcessToCosmos()
    {
        const string processId = "proc-save-1";
        var pipeline = BuildPipelineWithAllSteps(processId);
        var evaluateJson = BuildEvaluateJson(0.88);
        var mapJson = BuildMapJson();

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/gpt_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapJson);

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/evaluate_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluateJson);

        _cosmosService
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentProcess?)null);

        ContentProcess? upserted = null;
        _cosmosService
            .Setup(c => c.UpsertAsync(It.IsAny<ContentProcess>(), It.IsAny<CancellationToken>()))
            .Callback<ContentProcess, CancellationToken>((cp, _) => upserted = cp)
            .Returns(Task.CompletedTask);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        upserted.Should().NotBeNull();
        upserted!.ProcessId.Should().Be(processId);
        upserted.EntityScore.Should().BeApproximately(0.88, 0.001);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesFileNameFromExistingCosmosRecord()
    {
        const string processId = "proc-save-preserve";
        var pipeline = BuildPipelineWithAllSteps(processId);
        var evaluateJson = BuildEvaluateJson();
        var mapJson = BuildMapJson();

        var existing = new ContentProcess
        {
            Id = processId,
            ProcessId = processId,
            FileName = "original-claim.pdf"
        };

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/gpt_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapJson);

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/evaluate_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluateJson);

        _cosmosService
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        ContentProcess? upserted = null;
        _cosmosService
            .Setup(c => c.UpsertAsync(It.IsAny<ContentProcess>(), It.IsAny<CancellationToken>()))
            .Callback<ContentProcess, CancellationToken>((cp, _) => upserted = cp)
            .Returns(Task.CompletedTask);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        upserted!.FileName.Should().Be("original-claim.pdf");
    }

    [Fact]
    public async Task ExecuteAsync_MissingMapFile_ThrowsInvalidOperationException()
    {
        var pipeline = new DataPipeline { ProcessId = "proc-nomap" };
        pipeline.PipelineStatus.ProcessId = "proc-nomap";
        pipeline.PipelineStatus.ActiveStep = "save";
        pipeline.Files.Add(new FileDetails
        {
            ProcessId = "proc-nomap",
            Name = "claim.pdf",
            MimeType = MimeTypes.Pdf,
            ArtifactType = ArtifactType.SourceContent
        });
        // No map or evaluate file

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().ExecuteAsync(pipeline, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_AddsStepResultAndOutputFiles()
    {
        const string processId = "proc-save-step";
        var pipeline = BuildPipelineWithAllSteps(processId);

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/gpt_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMapJson());

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/evaluate_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildEvaluateJson());

        _cosmosService
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentProcess?)null);

        _cosmosService
            .Setup(c => c.UpsertAsync(It.IsAny<ContentProcess>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        result.Files.Should().Contain(f => f.Name == "step_outputs.json" && f.ArtifactType == ArtifactType.SavedContent);
        result.Files.Should().Contain(f => f.Name == "save_output.json" && f.ArtifactType == ArtifactType.SavedContent);
        result.PipelineStatus.GetStepResult("save").Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ComputesSchemaScore_FromEvaluateFields()
    {
        const string processId = "proc-schema-score";
        var pipeline = BuildPipelineWithAllSteps(processId);

        // 4 total fields, 1 zero-confidence -> schema score = 3/4 = 0.75
        var evaluateJson = BuildEvaluateJson(overallConfidence: 0.7, total: 4, zeros: 1);

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/gpt_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildMapJson());

        _blobService
            .Setup(b => b.DownloadTextAsync("cps-processes", $"{processId}/evaluate_output.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(evaluateJson);

        _cosmosService
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentProcess?)null);

        ContentProcess? upserted = null;
        _cosmosService
            .Setup(c => c.UpsertAsync(It.IsAny<ContentProcess>(), It.IsAny<CancellationToken>()))
            .Callback<ContentProcess, CancellationToken>((cp, _) => upserted = cp)
            .Returns(Task.CompletedTask);

        _blobService
            .Setup(b => b.UploadTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().ExecuteAsync(pipeline, CancellationToken.None);

        upserted!.SchemaScore.Should().BeApproximately(0.75, 0.001);
    }
}
