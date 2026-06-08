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

// Pipeline step 2 — schema-driven field extraction via Azure Content Understanding.
// Mirrors Python map_handler.py: prefers API-supplied CU sidecar; falls back to live
// custom-analyzer call using the schema envelope from the Schema Vault container.
// Parses CU contents[0].fields into a dict compatible with EvaluateStep's expected shape.
public sealed class MapStep(
    IContentUnderstandingClient cuClient,
    IBlobStorageService blobService,
    IOptions<ContentProcessorOptions> options,
    ILogger<MapStep> logger) : IPipelineStep
{
    public string StepName => "map";

    private static readonly ActivitySource ActivitySource = new("ClaimsIntelligence.ContentProcessor");
    private readonly ContentProcessorOptions _options = options.Value;

    public async Task<DataPipeline> ExecuteAsync(DataPipeline pipeline, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("contentprocessor.map");

        var sourceFile = pipeline.GetSourceFiles().FirstOrDefault()
            ?? throw new InvalidOperationException("No source file found in pipeline.");

        var processId = pipeline.PipelineStatus.ProcessId ?? pipeline.ProcessId;
        var schemaId = pipeline.PipelineStatus.SchemaId
            ?? throw new InvalidOperationException("SchemaId is required for MapStep.");

        activity?.SetTag("process_id", processId);
        activity?.SetTag("document_name", sourceFile.Name);
        activity?.SetTag("pipeline_stage", StepName);
        activity?.SetTag("schema_id", schemaId);

        if (sourceFile.MimeType is not (MimeTypes.Pdf or MimeTypes.ImageJpeg or MimeTypes.ImagePng))
            throw new InvalidOperationException($"Unsupported source MIME type for MapStep: {sourceFile.MimeType}");

        var schemaEnvelopeJson = await blobService.DownloadTextAsync(
            $"{_options.ConfigurationContainer}/Schemas/{schemaId}",
            "schema.json",
            cancellationToken);

        var analyzerPayload = JsonDocument.Parse(schemaEnvelopeJson).RootElement;
        var expectedFieldNames = ExtractExpectedFieldNames(analyzerPayload);

        string className = analyzerPayload.TryGetProperty("className", out var cn)
            ? cn.GetString() ?? schemaId
            : schemaId;

        JsonElement? cuPayload = null;
        string analyzerId = string.Empty;
        try
        {
            var sidecarBlobName = $"{processId}/{sourceFile.Name}.cu.json";
            var sidecarJson = await blobService.DownloadTextAsync(
                _options.ProcessesContainer, sidecarBlobName, cancellationToken);
            cuPayload = JsonDocument.Parse(sidecarJson).RootElement;
            analyzerId = "<api-router-passthrough>";
            logger.LogInformation(
                "MapStep: using CU sidecar for class={ClassName} process={ProcessId} file={FileName}",
                className, processId, sourceFile.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(
                "MapStep: no usable CU sidecar for process={ProcessId} file={FileName} — {Reason}",
                processId, sourceFile.Name, ex.Message);
        }

        if (!cuPayload.HasValue)
        {
            analyzerId = await cuClient.EnsureFieldAnalyzerAsync(
                className, analyzerPayload, cancellationToken);

            logger.LogInformation(
                "MapStep: CU extraction class={ClassName} analyzer={AnalyzerId} mime={MimeType}",
                className, analyzerId, sourceFile.MimeType);

            var fileBytes = await blobService.DownloadAsync(
                _options.ProcessesContainer,
                $"{processId}/{sourceFile.Name}",
                cancellationToken);

            cuPayload = await cuClient.AnalyzeAndWaitAsync(
                analyzerId, fileBytes, cancellationToken: cancellationToken);
        }

        var (parsedDict, cuFieldConfidences) = ParseCuResponse(cuPayload.Value, expectedFieldNames);

        double overallConfidence = cuFieldConfidences.Count > 0
            ? cuFieldConfidences.Values.Average()
            : 0.0;

        // Shape EvaluateStep expects — mirrors the GPT response envelope so evaluate works
        // on both the CU path and any future GPT fallback without branching.
        var responseDict = new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(parsedDict),
                        parsed = parsedDict
                    },
                    logprobs = (object?)null
                }
            },
            usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0, input_tokens = 0 },
            _cu_field_confidences = cuFieldConfidences,
            _cu_analyzer_id = analyzerId
        };

        var resultJson = JsonSerializer.Serialize(responseDict);
        var resultFile = pipeline.AddFile("gpt_output.json", ArtifactType.SchemaMappedData);
        resultFile.AddLogEntry(StepName, $"CU custom-analyzer extraction complete (analyzer={analyzerId}).");

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

    private static List<string> ExtractExpectedFieldNames(JsonElement analyzerPayload)
    {
        if (analyzerPayload.TryGetProperty("fieldSchema", out var fs)
            && fs.TryGetProperty("fields", out var fields)
            && fields.ValueKind == JsonValueKind.Object)
        {
            return fields.EnumerateObject().Select(p => p.Name).ToList();
        }
        return [];
    }

    private static (Dictionary<string, object?> Values, Dictionary<string, double> Confidences)
        ParseCuResponse(JsonElement cuPayload, IReadOnlyList<string> expectedFieldNames)
    {
        var values = new Dictionary<string, object?>();
        var confidences = new Dictionary<string, double>();

        var resultBlock = cuPayload.TryGetProperty("result", out var rb) ? rb : cuPayload;

        JsonElement fields = default;
        bool foundFields = false;

        if (resultBlock.TryGetProperty("contents", out var contents)
            && contents.GetArrayLength() > 0)
        {
            var first = contents[0];

            if (first.TryGetProperty("fields", out var directFields)
                && directFields.ValueKind == JsonValueKind.Object)
            {
                fields = directFields;
                foundFields = true;
            }
            else if (first.TryGetProperty("segments", out var segments)
                     && segments.GetArrayLength() > 0
                     && segments[0].TryGetProperty("fields", out var segFields)
                     && segFields.ValueKind == JsonValueKind.Object)
            {
                fields = segFields;
                foundFields = true;
            }
        }

        if (!foundFields)
            return (values, confidences);

        foreach (var fieldName in expectedFieldNames)
        {
            if (!fields.TryGetProperty(fieldName, out var fieldEl))
                continue;

            object? value = null;
            double confidence = 0.0;

            if (fieldEl.TryGetProperty("confidence", out var confEl))
                confidence = confEl.GetDouble();

            foreach (var valueProp in new[] { "valueString", "valueNumber", "valueDate",
                                              "valueTime", "valueBoolean", "value" })
            {
                if (fieldEl.TryGetProperty(valueProp, out var valEl))
                {
                    value = valEl.ValueKind switch
                    {
                        JsonValueKind.String => valEl.GetString(),
                        JsonValueKind.Number => valEl.TryGetDouble(out var d) ? (object?)d : valEl.GetRawText(),
                        JsonValueKind.True or JsonValueKind.False => valEl.GetBoolean(),
                        _ => valEl.GetRawText()
                    };
                    break;
                }
            }

            values[fieldName] = value;
            confidences[fieldName] = confidence;
        }

        return (values, confidences);
    }
}
