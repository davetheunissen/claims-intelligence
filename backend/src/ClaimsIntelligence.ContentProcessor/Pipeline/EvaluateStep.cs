using System.Diagnostics;
using System.Text.Json;
using ClaimsIntelligence.ContentProcessor.Configuration;
using ClaimsIntelligence.Domain.Interfaces;
using ClaimsIntelligence.Domain.Pipeline;
using ClaimsIntelligence.Infrastructure.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimsIntelligence.ContentProcessor.Pipeline;

// Pipeline step 3 — dual confidence scoring. Mirrors Python evaluate_handler.py:
// merges per-field CU OCR confidence (from ExtractStep) with custom-analyzer confidence
// stored in _cu_field_confidences (or uniform 1.0 for legacy GPT logprob paths).
public sealed class EvaluateStep(
    IBlobStorageService blobService,
    IOptions<ContentProcessorOptions> options,
    ILogger<EvaluateStep> logger) : IPipelineStep
{
    public string StepName => "evaluate";

    private static readonly ActivitySource ActivitySource = new("ClaimsIntelligence.ContentProcessor");
    private const double DefaultConfidenceThreshold = 0.8;
    private readonly ContentProcessorOptions _options = options.Value;

    public async Task<DataPipeline> ExecuteAsync(DataPipeline pipeline, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("contentprocessor.evaluate");

        var sourceFile = pipeline.GetSourceFiles().FirstOrDefault()
            ?? throw new InvalidOperationException("No source file found in pipeline.");

        var processId = pipeline.PipelineStatus.ProcessId ?? pipeline.ProcessId;

        activity?.SetTag("process_id", processId);
        activity?.SetTag("document_name", sourceFile.Name);
        activity?.SetTag("pipeline_stage", StepName);

        Dictionary<string, double>? cuOcrConfidences = null;
        bool isImage = sourceFile.MimeType is MimeTypes.ImageJpeg or MimeTypes.ImagePng;

        if (!isImage)
        {
            var extractFile = pipeline.Files
                .FirstOrDefault(f => f.ProcessedBy == "extract"
                                  && f.ArtifactType == ArtifactType.ExtractedContent);
            if (extractFile?.Name is not null)
            {
                try
                {
                    var extractJson = await blobService.DownloadTextAsync(
                        _options.ProcessesContainer,
                        $"{processId}/{extractFile.Name}",
                        cancellationToken);

                    cuOcrConfidences = ParseOcrFieldConfidences(extractJson);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(
                        "EvaluateStep: could not load extract output for process={ProcessId} — {Reason}",
                        processId, ex.Message);
                }
            }
        }

        var mapFile = pipeline.Files
            .FirstOrDefault(f => f.ProcessedBy == "map"
                              && f.ArtifactType == ArtifactType.SchemaMappedData)
            ?? throw new InvalidOperationException(
                "MapStep output file not found. Ensure MapStep ran before EvaluateStep.");

        var mapJson = await blobService.DownloadTextAsync(
            _options.ProcessesContainer,
            $"{processId}/{mapFile.Name}",
            cancellationToken);

        var mapDoc = JsonDocument.Parse(mapJson).RootElement;

        var parsedMessage = mapDoc
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("parsed");

        var extractedFields = ParseFieldValues(parsedMessage);

        Dictionary<string, double> fieldConfidences;
        if (mapDoc.TryGetProperty("_cu_field_confidences", out var cuFcEl)
            && cuFcEl.ValueKind == JsonValueKind.Object)
        {
            fieldConfidences = ParseDoubleDict(cuFcEl);
        }
        else
        {
            fieldConfidences = extractedFields.Keys.ToDictionary(k => k, _ => 1.0);
            logger.LogDebug("EvaluateStep: no _cu_field_confidences found, using placeholder confidence 1.0");
        }

        var mergedConfidences = MergeConfidences(cuOcrConfidences, fieldConfidences, extractedFields.Keys);

        var scores = mergedConfidences.Values.ToList();
        double overallConfidence = scores.Count > 0 ? scores.Average() : 0.0;
        double minConfidence = scores.Count > 0 ? scores.Min() : 0.0;
        int zeroConfidenceCount = scores.Count(s => s == 0.0);
        int totalFields = scores.Count;

        double schemaScore = totalFields == 0
            ? 0.0
            : Math.Round((double)(totalFields - zeroConfidenceCount) / totalFields, 3);

        var comparisonItems = BuildComparisonItems(extractedFields, mergedConfidences, DefaultConfidenceThreshold);

        int promptTokens = mapDoc.TryGetProperty("usage", out var usage)
            && usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
        int completionTokens = mapDoc.TryGetProperty("usage", out var usage2)
            && usage2.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;

        var evaluationResult = new
        {
            extracted_result = extractedFields,
            confidence = new
            {
                overall_confidence = overallConfidence,
                min_extracted_field_confidence = minConfidence,
                total_evaluated_fields_count = totalFields,
                zero_confidence_fields_count = zeroConfidenceCount,
                field_confidences = mergedConfidences
            },
            comparison_result = new { items = comparisonItems },
            prompt_tokens = promptTokens,
            completion_tokens = completionTokens,
            execution_time = 0
        };

        var resultJson = JsonSerializer.Serialize(evaluationResult);
        var resultFile = pipeline.AddFile("evaluate_output.json", ArtifactType.ScoreMergedData);
        resultFile.AddLogEntry(StepName, "Evaluation Result has been added.");

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

        logger.LogInformation(
            "EvaluateStep: process={ProcessId} overall={Overall:F3} schema={Schema:F3} fields={Fields}",
            processId, overallConfidence, schemaScore, totalFields);

        return pipeline;
    }

    private static Dictionary<string, double>? ParseOcrFieldConfidences(string extractJson)
    {
        var doc = JsonDocument.Parse(extractJson).RootElement;
        var resultBlock = doc.TryGetProperty("result", out var rb) ? rb : doc;

        if (!resultBlock.TryGetProperty("contents", out var contents)
            || contents.GetArrayLength() == 0)
            return null;

        var first = contents[0];
        JsonElement fields = default;
        bool found = false;

        if (first.TryGetProperty("fields", out var df) && df.ValueKind == JsonValueKind.Object)
        {
            fields = df;
            found = true;
        }
        else if (first.TryGetProperty("segments", out var segs)
                 && segs.GetArrayLength() > 0
                 && segs[0].TryGetProperty("fields", out var sf)
                 && sf.ValueKind == JsonValueKind.Object)
        {
            fields = sf;
            found = true;
        }

        if (!found) return null;

        var result = new Dictionary<string, double>();
        foreach (var prop in fields.EnumerateObject())
        {
            if (prop.Value.TryGetProperty("confidence", out var c))
                result[prop.Name] = c.GetDouble();
        }

        return result.Count > 0 ? result : null;
    }

    private static Dictionary<string, object?> ParseFieldValues(JsonElement parsedEl)
    {
        var dict = new Dictionary<string, object?>();
        if (parsedEl.ValueKind != JsonValueKind.Object) return dict;

        foreach (var prop in parsedEl.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetDouble(out var d) ? d : (object?)prop.Value.GetRawText(),
                JsonValueKind.True or JsonValueKind.False => prop.Value.GetBoolean(),
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText()
            };
        }

        return dict;
    }

    private static Dictionary<string, double> ParseDoubleDict(JsonElement el)
    {
        var dict = new Dictionary<string, double>();
        foreach (var prop in el.EnumerateObject())
        {
            if (prop.Value.TryGetDouble(out var d))
                dict[prop.Name] = d;
        }
        return dict;
    }

    private static Dictionary<string, double> MergeConfidences(
        Dictionary<string, double>? ocrConf,
        Dictionary<string, double> mapConf,
        IEnumerable<string> allFieldNames)
    {
        var merged = new Dictionary<string, double>();
        foreach (var field in allFieldNames)
        {
            double ocr = 0.0;
            bool hasOcr = ocrConf?.TryGetValue(field, out ocr) == true;
            bool hasMap = mapConf.TryGetValue(field, out double map);

            merged[field] = (hasOcr, hasMap) switch
            {
                (true, true) => (ocr + map) / 2.0,
                (true, false) => ocr,
                (false, true) => map,
                _ => 0.0
            };
        }
        return merged;
    }

    private static List<object> BuildComparisonItems(
        Dictionary<string, object?> extracted,
        Dictionary<string, double> confidences,
        double threshold)
    {
        return extracted.Select(kv =>
        {
            var conf = confidences.GetValueOrDefault(kv.Key, 0.0);
            return (object)new
            {
                field_name = kv.Key,
                extracted_value = kv.Value,
                confidence = conf,
                meets_threshold = conf >= threshold
            };
        }).ToList();
    }
}
