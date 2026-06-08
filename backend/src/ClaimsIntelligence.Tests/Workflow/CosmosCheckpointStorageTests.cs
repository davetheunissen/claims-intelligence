using ClaimsIntelligence.Workflow.Checkpoint;
using Microsoft.Azure.Cosmos;

namespace ClaimsIntelligence.Tests.Workflow;

/// <summary>
/// Unit tests for CosmosCheckpointStorage — checkpoint read/write/clear logic.
/// Mirrors Python test_cosmos_checkpoint_storage.py intent.
/// </summary>
public class CosmosCheckpointStorageTests
{
    private readonly Mock<ICosmosService<ClaimProcess>> _cosmos = new();
    private readonly Mock<ILogger<CosmosCheckpointStorage>> _logger = new();

    private CosmosCheckpointStorage CreateSut() =>
        new(_cosmos.Object, _logger.Object);

    private static ClaimProcess BuildClaim(string id, string? processComment = null) =>
        new()
        {
            Id = id,
            ProcessComment = processComment ?? string.Empty
        };

    [Fact]
    public async Task GetCompletedExecutorsAsync_NullDocument_ReturnsEmptySet()
    {
        _cosmos.Setup(c => c.GetByIdAsync("claim-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClaimProcess?)null);

        var result = await CreateSut().GetCompletedExecutorsAsync("claim-1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCompletedExecutorsAsync_WithCheckpoints_ReturnsCompletedNames()
    {
        var claim = BuildClaim("claim-2", "checkpoint:DocumentProcess\ncheckpoint:Rai");
        _cosmos.Setup(c => c.GetByIdAsync("claim-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var result = await CreateSut().GetCompletedExecutorsAsync("claim-2");

        result.Should().Contain("DocumentProcess");
        result.Should().Contain("Rai");
        result.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetCompletedExecutorsAsync_IsCaseInsensitive()
    {
        var claim = BuildClaim("claim-3", "checkpoint:documentprocess");
        _cosmos.Setup(c => c.GetByIdAsync("claim-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var result = await CreateSut().GetCompletedExecutorsAsync("claim-3");

        result.Should().Contain("documentprocess");
        result.Any(s => s.Equals("documentprocess", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public async Task MarkExecutorCompleteAsync_AddsCheckpointMarker()
    {
        var claim = BuildClaim("claim-4", string.Empty);
        _cosmos.Setup(c => c.GetByIdAsync("claim-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _cosmos.Setup(c => c.PatchAsync("claim-4", It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().MarkExecutorCompleteAsync("claim-4", "Summarize");

        _cosmos.Verify(c => c.PatchAsync(
            "claim-4",
            It.IsAny<IReadOnlyList<PatchOperation>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MarkExecutorCompleteAsync_AlreadyMarked_DoesNotPatchAgain()
    {
        var claim = BuildClaim("claim-5", "checkpoint:Summarize");
        _cosmos.Setup(c => c.GetByIdAsync("claim-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        await CreateSut().MarkExecutorCompleteAsync("claim-5", "Summarize");

        // Should NOT patch if checkpoint already present
        _cosmos.Verify(c => c.PatchAsync(
            "claim-5",
            It.IsAny<IReadOnlyList<PatchOperation>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ClearAsync_NullDocument_DoesNothing()
    {
        _cosmos.Setup(c => c.GetByIdAsync("claim-6", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClaimProcess?)null);

        await CreateSut().ClearAsync("claim-6");

        _cosmos.Verify(c => c.PatchAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PatchOperation>>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ClearAsync_RemovesCheckpointLinesPreservesOtherContent()
    {
        var claim = BuildClaim("claim-7", "some-user-comment\ncheckpoint:DocumentProcess\ncheckpoint:Rai");
        _cosmos.Setup(c => c.GetByIdAsync("claim-7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _cosmos.Setup(c => c.PatchAsync("claim-7", It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().ClearAsync("claim-7");

        _cosmos.Verify(c => c.PatchAsync(
            "claim-7",
            It.IsAny<IReadOnlyList<PatchOperation>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
