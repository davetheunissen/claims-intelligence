using Azure.AI.Inference;
using ClaimsIntelligence.Workflow.Configuration;
using ClaimsIntelligence.Workflow.GapRules;
using ClaimsIntelligence.Workflow.Workflow;
using ClaimsIntelligence.Workflow.Workflow.Executors;
using Microsoft.Azure.Cosmos;
using System.ClientModel.Primitives;

namespace ClaimsIntelligence.Tests.Workflow;

/// <summary>
/// Unit tests for the 4 workflow executors:
/// RaiExecutor, SummarizeExecutor, GapExecutor, DocumentProcessExecutor.
/// </summary>
public class RaiExecutorTests
{
    private readonly Mock<IAzureInferenceService> _inference = new();
    private readonly Mock<ICosmosService<ClaimProcess>> _claimCosmos = new();
    private readonly Mock<ILogger<RaiExecutor>> _logger = new();

    private readonly IOptions<WorkflowOptions> _options =
        Options.Create(new WorkflowOptions { InferenceModelName = "gpt-4" });

    private RaiExecutor CreateSut() =>
        new(_inference.Object, _claimCosmos.Object, _options, _logger.Object);

    private static ClaimProcess BuildClaim(string id = "claim-rai") =>
        new()
        {
            Id = id,
            ProcessedDocuments =
            [
                new ContentProcess { FileName = "form.pdf", Status = "Completed", EntityScore = 0.9 }
            ]
        };

    private static ChatCompletions BuildSafeResponse() =>
        BuildChatResponse("""{"overall_safe": true, "summary": "All safe"}""");

    private static ChatCompletions BuildUnsafeResponse() =>
        BuildChatResponse("""{"overall_safe": false, "summary": "Unsafe content detected"}""");

    private static ChatCompletions BuildChatResponse(string content)
    {
        // ChatCompletions cannot be directly instantiated — use AzureAI.Models.Test or reflection
        // We reflect to create a mock-friendly wrapper
        return AzureAIInferenceTestHelper.CreateChatCompletions(content);
    }

    [Fact]
    public async Task ExecuteAsync_SafeContent_ReturnsClaim()
    {
        var claim = BuildClaim();

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _inference.Setup(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSafeResponse());

        var result = await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        result.Should().BeSameAs(claim);
    }

    [Fact]
    public async Task ExecuteAsync_UnsafeContent_ThrowsWorkflowExecutorFailedException()
    {
        var claim = BuildClaim();

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _inference.Setup(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildUnsafeResponse());

        var ex = await Assert.ThrowsAsync<WorkflowExecutorFailedException>(
            () => CreateSut().ExecuteAsync(claim, CancellationToken.None));

        ex.ExecutorName.Should().Be("Rai");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_DefaultsToSafeAndReturnsClaim()
    {
        var claim = BuildClaim();

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _inference.Setup(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildChatResponse("This is not valid JSON at all"));

        // Invalid JSON => defaults to safe
        var result = await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        result.Should().BeSameAs(claim);
    }

    [Fact]
    public async Task ExecuteAsync_NoDocuments_SkipsInferenceAndReturnsClaim()
    {
        var claim = new ClaimProcess { Id = "claim-empty-docs", ProcessedDocuments = [] };

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        result.Should().BeSameAs(claim);
        _inference.Verify(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

public class SummarizeExecutorTests
{
    private readonly Mock<IAzureInferenceService> _inference = new();
    private readonly Mock<ICosmosService<ClaimProcess>> _claimCosmos = new();
    private readonly Mock<ILogger<SummarizeExecutor>> _logger = new();

    private readonly IOptions<WorkflowOptions> _options =
        Options.Create(new WorkflowOptions { InferenceModelName = "gpt-4" });

    private SummarizeExecutor CreateSut() =>
        new(_inference.Object, _claimCosmos.Object, _options, _logger.Object);

    private static ClaimProcess BuildClaim(string id = "claim-sum", bool withDocs = true) =>
        new()
        {
            Id = id,
            ProcessedDocuments = withDocs
                ? [new ContentProcess { FileName = "form.pdf", Status = "Completed", EntityScore = 0.9, MimeType = "application/pdf" }]
                : []
        };

    [Fact]
    public async Task ExecuteAsync_HappyPath_WritesSummaryToClaim()
    {
        var claim = BuildClaim();
        const string summaryText = "This is a comprehensive claim summary.";

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _inference.Setup(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AzureAIInferenceTestHelper.CreateChatCompletions(summaryText));

        var result = await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        result.ProcessSummary.Should().Be(summaryText);
        _claimCosmos.Verify(c => c.PatchAsync(
            claim.Id,
            It.IsAny<IReadOnlyList<PatchOperation>>(),
            It.IsAny<CancellationToken>()),
            Times.AtLeast(2)); // Status update + summary update
    }

    [Fact]
    public async Task ExecuteAsync_NoCompletedDocuments_SkipsInference()
    {
        var claim = new ClaimProcess
        {
            Id = "claim-no-complete",
            ProcessedDocuments = [new ContentProcess { FileName = "form.pdf", Status = "Error", EntityScore = 0.0 }]
        };

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        result.Should().BeSameAs(claim);
        _inference.Verify(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyDocumentList_SkipsInference()
    {
        var claim = BuildClaim(withDocs: false);

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        _inference.Verify(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

public class GapExecutorTests
{
    private readonly Mock<IAzureInferenceService> _inference = new();
    private readonly Mock<ICosmosService<ClaimProcess>> _claimCosmos = new();
    private readonly Mock<ILogger<GapExecutor>> _logger = new();
    private readonly Mock<ILogger<GapRuleLoader>> _ruleLogger = new();

    private readonly IOptions<WorkflowOptions> _options;

    public GapExecutorTests()
    {
        // Point to a temp dir with no yaml files (GapRuleLoader returns empty string)
        var tempDir = Path.Combine(Path.GetTempPath(), "gap-rules-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        // Create a minimal YAML rule file
        File.WriteAllText(Path.Combine(tempDir, "rules.yaml"), """
            rule_set_id: FNOL-RULES-TEST
            version: "1.0"
            description: Test rules
            required_documents: []
            discrepancy_checks: []
            observations: []
            """);

        _options = Options.Create(new WorkflowOptions
        {
            InferenceModelName = "gpt-4",
            GapRulesPath = tempDir
        });
    }

    private GapExecutor CreateSut() =>
        new(_inference.Object, _claimCosmos.Object,
            new GapRuleLoader(_options, _ruleLogger.Object),
            _options, _logger.Object);

    private static ClaimProcess BuildClaim(string id = "claim-gap") =>
        new()
        {
            Id = id,
            ProcessedDocuments =
            [
                new ContentProcess { FileName = "police-report.pdf", Status = "Completed", MimeType = "application/pdf", EntityScore = 0.85, SchemaScore = 0.9 }
            ]
        };

    [Fact]
    public async Task ExecuteAsync_ValidJsonResponse_WritesGapsToClaim()
    {
        var claim = BuildClaim();
        var gapJson = """{"required_document_gaps": [], "discrepancy_findings": [], "observations": [], "overall_assessment": "No gaps found."}""";

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _inference.Setup(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AzureAIInferenceTestHelper.CreateChatCompletions(gapJson));

        var result = await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        result.ProcessGaps.Should().Be(gapJson);
    }

    [Fact]
    public async Task ExecuteAsync_JsonEmbeddedInText_ExtractsAndStores()
    {
        var claim = BuildClaim();
        var gapJson = """{"required_document_gaps": [], "discrepancy_findings": [], "observations": [], "overall_assessment": "OK"}""";
        var wrappedResponse = $"Here is the analysis:\n\n{gapJson}\n\nEnd of analysis.";

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _inference.Setup(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AzureAIInferenceTestHelper.CreateChatCompletions(wrappedResponse));

        var result = await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        result.ProcessGaps.Should().NotBeEmpty();
        result.ProcessGaps.Should().Contain("required_document_gaps");
    }

    [Fact]
    public async Task ExecuteAsync_NonJsonResponse_DoesNotPersistGaps()
    {
        var claim = BuildClaim();

        _claimCosmos.Setup(c => c.PatchAsync(claim.Id, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _inference.Setup(i => i.CompleteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<ChatRequestMessage>>(),
            It.IsAny<ChatCompletionsOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AzureAIInferenceTestHelper.CreateChatCompletions("I cannot analyze this claim."));

        var result = await CreateSut().ExecuteAsync(claim, CancellationToken.None);

        // Non-JSON response should not persist
        result.ProcessGaps.Should().BeEmpty();
    }
}

/// <summary>
/// Helper to create ChatCompletions instances for tests.
/// Azure.AI.Inference 1.0.0-beta.5 has internal constructors, so we use
/// ModelReaderWriter to deserialize from a JSON payload.
/// </summary>
internal static class AzureAIInferenceTestHelper
{
    public static ChatCompletions CreateChatCompletions(string content)
    {
        // Escape the content for embedding in JSON
        var escapedContent = System.Text.Json.JsonSerializer.Serialize(content);

        var json = $$"""
            {
              "id": "test-{{Guid.NewGuid():N}}",
              "created": 1700000000,
              "model": "gpt-4",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": {{escapedContent}}
                  },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 10,
                "completion_tokens": 10,
                "total_tokens": 20
              }
            }
            """;

        var data = BinaryData.FromString(json);
        return ModelReaderWriter.Read<ChatCompletions>(data)!;
    }
}
