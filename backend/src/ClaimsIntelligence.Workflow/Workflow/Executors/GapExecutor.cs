using System.Diagnostics;
using System.Text.Json;
using Azure.AI.Inference;
using ClaimsIntelligence.Workflow.GapRules;

namespace ClaimsIntelligence.Workflow.Workflow.Executors;

/// <summary>
/// Stage 4 — Gap / Discrepancy Analysis.
///
/// Loads YAML gap rules via <see cref="GapRuleLoader"/>, sends extracted document
/// fields to <see cref="IAzureInferenceService"/> for evaluation, and writes the
/// structured gap report to <see cref="ClaimProcess.ProcessGaps"/>.
///
/// Emits an OpenTelemetry span named <c>workflow.gap</c>.
///
/// Ported from: Python <c>steps/gap_analysis/executor/gap_executor.py</c>.
/// </summary>
public sealed class GapExecutor(
    IAzureInferenceService inference,
    ICosmosService<ClaimProcess> claimCosmos,
    GapRuleLoader ruleLoader,
    IOptions<WorkflowOptions> options,
    ILogger<GapExecutor> logger) : IWorkflowExecutor
{
    private readonly WorkflowOptions _options = options.Value;
    private static readonly ActivitySource ActivitySource =
        new("ClaimsIntelligence.Workflow");

    public string ExecutorName => "Gap";

    private static readonly string SystemPromptTemplate = """
        You are an expert insurance claims adjuster performing a FNOL (First Notice of Loss)
        gap and discrepancy analysis.

        You will be given:
        1. A YAML DSL that defines the required documents, discrepancy checks, and
           observation triggers for an auto insurance FNOL claim.
        2. The extracted content from the submitted claim documents.

        Your task is to evaluate the claim against every rule in the DSL and produce a
        structured JSON report.

        RULES DSL:
        {{RULES_DSL}}

        OUTPUT FORMAT (strictly JSON):
        {
          "required_document_gaps": [
            {
              "rule_id": "REQ-...",
              "name": "...",
              "severity": "high|medium|low|info",
              "gap_found": true,
              "details": "..."
            }
          ],
          "discrepancy_findings": [
            {
              "rule_id": "DISC-...",
              "field": "...",
              "severity": "critical|high|medium|low",
              "conflict_found": true,
              "details": "..."
            }
          ],
          "observations": [
            {
              "rule_id": "OBS-...",
              "name": "...",
              "severity": "medium|info",
              "condition_met": true,
              "details": "..."
            }
          ],
          "overall_assessment": "..."
        }

        Be thorough and conservative. Only report gap_found/conflict_found/condition_met=true
        when there is clear evidence from the documents. For any rule where the required
        evidence is absent, still include the entry with gap_found=false.
        """;

    /// <inheritdoc/>
    public async Task<ClaimProcess> ExecuteAsync(
        ClaimProcess claim,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("workflow.gap");
        activity?.SetTag("claim_process_id", claim.Id);

        logger.LogInformation(
            "[Gap] Running gap/discrepancy analysis for claim {ClaimId}", claim.Id);

        await UpdateStatusAsync(claim.Id, ClaimSteps.GapAnalysis, cancellationToken);

        var rulesYaml = ruleLoader.LoadRawYaml();
        var systemPrompt = SystemPromptTemplate.Replace("{{RULES_DSL}}", rulesYaml);
        var userMessage = BuildAnalysisRequest(claim);

        List<ChatRequestMessage> messages =
        [
            new ChatRequestSystemMessage(systemPrompt),
            new ChatRequestUserMessage(userMessage)
        ];

        var response = await inference.CompleteAsync(
            _options.InferenceModelName,
            messages,
            cancellationToken: cancellationToken);

        var gapText = (response.Content ?? string.Empty).Trim();

        logger.LogInformation(
            "[Gap] Gap analysis completed ({Length} chars) for claim {ClaimId}",
            gapText.Length, claim.Id);

        if (!TryExtractJson(gapText, out var validJson))
        {
            logger.LogError(
                "[Gap] Gap analysis returned non-JSON output for claim {ClaimId}; refusing to persist",
                claim.Id);
            return claim;
        }

        await claimCosmos.PatchAsync(claim.Id,
            [PatchOperation.Set("/processGaps", validJson)],
            cancellationToken);

        claim.ProcessGaps = validJson;
        return claim;
    }

    private static string BuildAnalysisRequest(ClaimProcess claim)
    {
        var inventory = string.Join(
            "\n",
            claim.ProcessedDocuments.Select(d =>
                $"- {d.FileName}: status={d.Status}, mime_type={d.MimeType ?? "unknown"}"));

        var documents = string.Join(
            "\n\n",
            claim.ProcessedDocuments
                .Where(d => d.Status is "Completed")
                .Select(d =>
                    $"Document: {d.FileName} ({d.MimeType})\n" +
                    $"Status: {d.Status}\n" +
                    $"EntityScore: {d.EntityScore:F4}\n" +
                    $"SchemaScore: {d.SchemaScore:F4}"));

        return $"""
            Authoritative document inventory from intake classification:
            {inventory}

            Now analyze the following document extracts:

            {documents}
            """;
    }

    private static bool TryExtractJson(string text, out string json)
    {
        json = text;
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException) { }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var candidate = text[start..(end + 1)];
            try
            {
                using var _ = JsonDocument.Parse(candidate);
                json = candidate;
                return true;
            }
            catch (JsonException) { }
        }

        return false;
    }

    private async Task UpdateStatusAsync(string claimId, ClaimSteps status, CancellationToken ct)
    {
        await claimCosmos.PatchAsync(claimId,
            [PatchOperation.Set("/status", (int)status)], ct);
    }
}
