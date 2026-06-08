using System.Diagnostics;
using System.Text.Json;
using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.Domain.Interfaces;
using ClaimsIntelligence.Domain.Pipeline;
using ClaimsIntelligence.Infrastructure.Blob;
using ClaimsIntelligence.Infrastructure.ContentUnderstanding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimsIntelligence.ContentProcessor.Pipeline;

// Routing logic (mirrors Python extract_handler.py):
// - Images (JPEG/PNG): skip extraction, return "skipped" result.
// - PDFs: attempt CU sidecar blob ({processId}/{fileName}.cu.json) with contents[0].markdown;
//         falls back to live AnalyzeAndWaitAsync call.
// - Other MIME types: skip with "unsupported" reason.
public sealed class ExtractStep(
    IContentUnderstandingClient cuClient,
    IBlobStorageService blobService,
    IOptions<ContentProcessorOptions> options,
    ILogger<ExtractStep> logger) : IPipelineStep
{
    public string StepName => "extract";

    private static readonly ActivitySource ActivitySource = new("ClaimsIntelligence.ContentProcessor");
    private readonly ContentProcessorOptions _options = options.Value;

    public async Task<DataPipeline> ExecuteAsync(DataPipeline pipeline, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("contentprocessor.extract");

        var sourceFile = pipeline.GetSourceFiles().FirstOrDefault()
            ?? throw new InvalidOperationException("No source file found in pipeline.");

        var processId = pipeline.PipelineStatus.ProcessId ?? pipeline.ProcessId;

        activity?.SetTag("process_id", processId);
        activity?.SetTag("document_name", sourceFile.Name);
        activity?.SetTag("pipeline_stage", StepName);

        if (sourceFile.MimeType is MimeTypes.ImageJpeg or MimeTypes.ImagePng)
        {
            logger.LogInformation(
                "ExtractStep: skipping image file {FileName} (process={ProcessId})",
                sourceFile.Name, processId);

            pipeline.PipelineStatus.AddStepResult(new StepResult
            {
                ProcessId = processId,
                StepName = StepName,
                Result = new { result = "skipped", reason = "Content type is image, skipping extraction." }
            });

            return pipeline;
        }

        if (sourceFile.MimeType == MimeTypes.Pdf)
        {
            JsonElement? sidecarPayload = null;
            try
            {
                var sidecarBlobName = $"{processId}/{sourceFile.Name}.cu.json";
                var sidecarJson = await blobService.DownloadTextAsync(
                    _options.ProcessesContainer, sidecarBlobName, cancellationToken);

                var candidate = JsonDocument.Parse(sidecarJson).RootElement;
                var resultBlock = candidate.TryGetProperty("result", out var r) ? r : candidate;
                if (resultBlock.TryGetProperty("contents", out var contents)
                    && contents.GetArrayLength() > 0)
                {
                    var first = contents[0];
                    bool hasMarkdown = first.TryGetProperty("markdown", out _)
                                    || first.TryGetProperty("markdownContent", out _);
                    if (hasMarkdown)
                    {
                        sidecarPayload = candidate;
                        logger.LogInformation(
                            "ExtractStep: using CU sidecar for process={ProcessId} file={FileName}",
                            processId, sourceFile.Name);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(
                    "ExtractStep: no usable CU sidecar for process={ProcessId} file={FileName} — {Reason}",
                    processId, sourceFile.Name, ex.Message);
            }

            JsonElement cuResult;

            if (sidecarPayload.HasValue)
            {
                cuResult = sidecarPayload.Value;
            }
            else
            {
                var fileBytes = await blobService.DownloadAsync(
                    _options.ProcessesContainer,
                    $"{processId}/{sourceFile.Name}",
                    cancellationToken);

                logger.LogInformation(
                    "ExtractStep: calling CU analyzer={AnalyzerId} for process={ProcessId} file={FileName}",
                    _options.ExtractAnalyzerId, processId, sourceFile.Name);

                cuResult = await cuClient.AnalyzeAndWaitAsync(
                    _options.ExtractAnalyzerId,
                    fileBytes,
                    cancellationToken: cancellationToken);
            }

            var resultJson = cuResult.GetRawText();
            var resultFile = pipeline.AddFile("content_understanding_output.json", ArtifactType.ExtractedContent);
            resultFile.AddLogEntry(StepName, "Content Understanding Extraction Result has been added.");

            await blobService.UploadTextAsync(
                _options.ProcessesContainer,
                $"{processId}/{resultFile.Name}",
                resultJson,
                cancellationToken);

            pipeline.PipelineStatus.AddStepResult(new StepResult
            {
                ProcessId = processId,
                StepName = StepName,
                Result = new { result = "success", file_name = resultFile.Name }
            });

            return pipeline;
        }

        logger.LogWarning(
            "ExtractStep: unsupported mime type {MimeType} for process={ProcessId}",
            sourceFile.MimeType, processId);

        pipeline.PipelineStatus.AddStepResult(new StepResult
        {
            ProcessId = processId,
            StepName = StepName,
            Result = new { result = "skipped", reason = "Content type not supported for extraction." }
        });

        return pipeline;
    }
}
