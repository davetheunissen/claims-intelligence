using System.Diagnostics;
using System.Text.Json;
using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.Domain.Interfaces;
using ClaimsIntelligence.Domain.Pipeline;
using ClaimsIntelligence.Domain.Workflow;
using ClaimsIntelligence.Infrastructure.Blob;
using ClaimsIntelligence.Infrastructure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimsIntelligence.ContentProcessor.Pipeline;

// Pipeline step 4 — final persistence. Mirrors Python save_handler.py:
// reads extract/map/evaluate outputs, computes aggregate scores, upserts ContentProcess
// to Cosmos DB, and writes step_outputs.json + save_output.json artifacts.
public sealed class SaveStep(
    IBlobStorageService blobService,
    ICosmosService<ContentProcess> cosmosService,
    IOptions<ContentProcessorOptions> options,
    ILogger<SaveStep> logger) : IPipelineStep
{
    public string StepName => "save";

    private static readonly ActivitySource ActivitySource = new("ClaimsIntelligence.ContentProcessor");
    private readonly ContentProcessorOptions _options = options.Value;

    public async Task<DataPipeline> ExecuteAsync(DataPipeline pipeline, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("contentprocessor.save");

        var sourceFile = pipeline.GetSourceFiles().FirstOrDefault()
            ?? throw new InvalidOperationException("No source file found in pipeline.");

        var processId = pipeline.PipelineStatus.ProcessId ?? pipeline.ProcessId;

        activity?.SetTag("process_id", processId);
        activity?.SetTag("document_name", sourceFile.Name);
        activity?.SetTag("pipeline_stage", StepName);

        bool isImage = sourceFile.MimeType is MimeTypes.ImageJpeg or MimeTypes.ImagePng;

        string? extractJson = null;
        if (!isImage)
        {
            var extractFile = pipeline.Files
                .FirstOrDefault(f => f.ProcessedBy == "extract"
                                  && f.ArtifactType == ArtifactType.ExtractedContent);
            if (extractFile?.Name is not null)
            {
                try
                {
                    extractJson = await blobService.DownloadTextAsync(
                        _options.ProcessesContainer,
                        $"{processId}/{extractFile.Name}",
                        cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("SaveStep: could not load extract output — {Reason}", ex.Message);
                }
            }
        }

        var mapFile = pipeline.Files
            .FirstOrDefault(f => f.ProcessedBy == "map"
                              && f.ArtifactType == ArtifactType.SchemaMappedData)
            ?? throw new InvalidOperationException("MapStep output not found.");

        var mapJson = await blobService.DownloadTextAsync(
            _options.ProcessesContainer,
            $"{processId}/{mapFile.Name}",
            cancellationToken);

        var evaluateFile = pipeline.Files
            .FirstOrDefault(f => f.ProcessedBy == "evaluate"
                              && f.ArtifactType == ArtifactType.ScoreMergedData)
            ?? throw new InvalidOperationException("EvaluateStep output not found.");

        var evaluateJson = await blobService.DownloadTextAsync(
            _options.ProcessesContainer,
            $"{processId}/{evaluateFile.Name}",
            cancellationToken);

        var evalDoc = JsonDocument.Parse(evaluateJson).RootElement;

        double entityScore = 0.0;
        double schemaScore = 0.0;
        double minEntityScore = 0.0;

        if (evalDoc.TryGetProperty("confidence", out var conf))
        {
            if (conf.TryGetProperty("overall_confidence", out var oc))
                entityScore = oc.GetDouble();
            if (conf.TryGetProperty("min_extracted_field_confidence", out var mc))
                minEntityScore = mc.GetDouble();
            if (conf.TryGetProperty("total_evaluated_fields_count", out var total)
                && conf.TryGetProperty("zero_confidence_fields_count", out var zeros))
            {
                int t = total.GetInt32(), z = zeros.GetInt32();
                schemaScore = t == 0 ? 0.0 : Math.Round((double)(t - z) / t, 3);
            }
        }

        string processedTime = SummarizeProcessedTime(pipeline.PipelineStatus.ProcessResults);

        // Read existing record to preserve FileName set during initial insert
        var existing = await cosmosService.GetByIdAsync(processId, cancellationToken);

        var contentProcess = new ContentProcess
        {
            Id = processId,
            ProcessId = processId,
            FileName = existing?.FileName ?? sourceFile.Name ?? string.Empty,
            MimeType = sourceFile.MimeType,
            EntityScore = entityScore,
            SchemaScore = schemaScore,
            Status = pipeline.PipelineStatus.Completed ? "Completed" : StepName,
            ProcessedTime = processedTime
        };

        await cosmosService.UpsertAsync(contentProcess, cancellationToken);

        logger.LogInformation(
            "SaveStep: upserted ContentProcess process={ProcessId} file={FileName} entity={Entity:F3} schema={Schema:F3}",
            processId, sourceFile.Name, entityScore, schemaScore);

        var stepOutputs = BuildStepOutputs(pipeline, extractJson, mapJson, evaluateJson);

        var historyFile = pipeline.AddFile("step_outputs.json", ArtifactType.SavedContent);
        historyFile.AddLogEntry(StepName,
            "Process Output has been added. This file should be deserialized to Step_Outputs[].");

        await blobService.UploadTextAsync(
            _options.ProcessesContainer,
            $"{processId}/{historyFile.Name}",
            JsonSerializer.Serialize(stepOutputs),
            cancellationToken);

        var saveResult = new
        {
            process_id = processId,
            status = contentProcess.Status,
            entity_score = entityScore,
            schema_score = schemaScore,
            min_extracted_entity_score = minEntityScore,
            processed_time = processedTime,
            file_name = sourceFile.Name
        };

        var resultFile = pipeline.AddFile("save_output.json", ArtifactType.SavedContent);
        resultFile.AddLogEntry(StepName, "Save Result has been added.");

        await blobService.UploadTextAsync(
            _options.ProcessesContainer,
            $"{processId}/{resultFile.Name}",
            JsonSerializer.Serialize(saveResult),
            cancellationToken);

        pipeline.PipelineStatus.AddStepResult(new StepResult
        {
            ProcessId = processId,
            StepName = StepName,
            Result = new { result = resultFile.Name }
        });

        return pipeline;
    }

    private static string SummarizeProcessedTime(IEnumerable<StepResult> stepResults)
    {
        double totalSeconds = 0.0;
        foreach (var step in stepResults)
        {
            if (step.Elapsed is not null && TimeSpan.TryParse(step.Elapsed, out var ts))
                totalSeconds += ts.TotalSeconds;
        }

        var span = TimeSpan.FromSeconds(totalSeconds);
        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}.{span.Milliseconds:D3}";
    }

    private static List<object> BuildStepOutputs(
        DataPipeline pipeline,
        string? extractJson,
        string mapJson,
        string evaluateJson)
    {
        var outputs = new List<object>();

        var extractResult = extractJson is not null
            ? (object?)JsonDocument.Parse(extractJson).RootElement
            : new { result = "skipped", reason = "Content type is image, skipping extraction." };

        var extractStepResult = pipeline.PipelineStatus.GetStepResult("extract");
        outputs.Add(new
        {
            step_name = "extract",
            processed_time = extractStepResult?.Elapsed ?? string.Empty,
            step_result = extractResult
        });

        var mapStepResult = pipeline.PipelineStatus.GetStepResult("map");
        outputs.Add(new
        {
            step_name = "map",
            processed_time = mapStepResult?.Elapsed ?? string.Empty,
            step_result = JsonDocument.Parse(mapJson).RootElement
        });

        var evalStepResult = pipeline.PipelineStatus.GetStepResult("evaluate");
        outputs.Add(new
        {
            step_name = "evaluate",
            processed_time = evalStepResult?.Elapsed ?? string.Empty,
            step_result = JsonDocument.Parse(evaluateJson).RootElement
        });

        return outputs;
    }
}
