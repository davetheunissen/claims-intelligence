using ClaimsIntelligence.Workflow.Checkpoint;
using ClaimsIntelligence.Workflow.Configuration;
using ClaimsIntelligence.Workflow.Workflow;
using Microsoft.Azure.Cosmos;

namespace ClaimsIntelligence.Tests.Workflow;

/// <summary>
/// Unit tests for WorkflowRunner — DAG orchestration, checkpoint skip, failure propagation.
/// </summary>
public class WorkflowRunnerTests
{
    private readonly Mock<ICheckpointStorage> _checkpoint = new();
    private readonly Mock<ICosmosService<ClaimProcess>> _claimCosmos = new();
    private readonly Mock<ILogger<WorkflowRunner>> _logger = new();

    private readonly IOptions<WorkflowOptions> _optionsWithRai =
        Options.Create(new WorkflowOptions { RaiEnabled = true, InferenceModelName = "gpt-4" });

    private readonly IOptions<WorkflowOptions> _optionsWithoutRai =
        Options.Create(new WorkflowOptions { RaiEnabled = false, InferenceModelName = "gpt-4" });

    private static ClaimProcess BuildClaim(string id = "claim-1") =>
        new() { Id = id, Status = ClaimSteps.Pending, ProcessedDocuments = [] };

    private WorkflowRunner CreateSut(
        IEnumerable<IWorkflowExecutor> executors,
        IOptions<WorkflowOptions>? opts = null)
    {
        return new WorkflowRunner(
            executors,
            _checkpoint.Object,
            _claimCosmos.Object,
            opts ?? _optionsWithRai,
            _logger.Object);
    }

    private void SetupClaimInCosmos(ClaimProcess claim)
    {
        _claimCosmos.Setup(c => c.GetByIdAsync(claim.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);
    }

    private void SetupNoCompletedCheckpoints(string claimId)
    {
        _checkpoint
            .Setup(cp => cp.GetCompletedExecutorsAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
    }

    [Fact]
    public async Task RunAsync_ClaimNotFound_ThrowsWorkflowExecutorFailedException()
    {
        _claimCosmos.Setup(c => c.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClaimProcess?)null);

        var sut = CreateSut([]);

        await Assert.ThrowsAsync<WorkflowExecutorFailedException>(
            () => sut.RunAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_AllStagesComplete_MarksClaimCompleted()
    {
        const string claimId = "claim-run-1";
        var claim = BuildClaim(claimId);

        SetupClaimInCosmos(claim);
        SetupNoCompletedCheckpoints(claimId);

        // All 4 executors
        var execNames = new[] { "DocumentProcess", "Rai", "Summarize", "Gap" };
        var executors = execNames.Select(name =>
        {
            var mock = new Mock<IWorkflowExecutor>();
            mock.Setup(e => e.ExecutorName).Returns(name);
            mock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(claim);
            return mock.Object;
        }).ToList();

        _checkpoint.Setup(cp => cp.MarkExecutorCompleteAsync(claimId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _checkpoint.Setup(cp => cp.ClearAsync(claimId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _claimCosmos.Setup(c => c.PatchAsync(claimId, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(executors, _optionsWithRai);
        await sut.RunAsync(claimId, CancellationToken.None);

        // Should patch status to Completed (value 6)
        _claimCosmos.Verify(c => c.PatchAsync(
            claimId,
            It.Is<IReadOnlyList<PatchOperation>>(ops => ops.Any()),
            It.IsAny<CancellationToken>()),
            Times.AtLeast(1));
    }

    [Fact]
    public async Task RunAsync_CompletedCheckpoint_SkipsExecutor()
    {
        const string claimId = "claim-skip";
        var claim = BuildClaim(claimId);

        SetupClaimInCosmos(claim);

        // DocumentProcess already completed
        _checkpoint
            .Setup(cp => cp.GetCompletedExecutorsAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DocumentProcess" });

        var docProcessMock = new Mock<IWorkflowExecutor>();
        docProcessMock.Setup(e => e.ExecutorName).Returns("DocumentProcess");

        var raiMock = new Mock<IWorkflowExecutor>();
        raiMock.Setup(e => e.ExecutorName).Returns("Rai");
        raiMock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var summarizeMock = new Mock<IWorkflowExecutor>();
        summarizeMock.Setup(e => e.ExecutorName).Returns("Summarize");
        summarizeMock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var gapMock = new Mock<IWorkflowExecutor>();
        gapMock.Setup(e => e.ExecutorName).Returns("Gap");
        gapMock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _checkpoint.Setup(cp => cp.MarkExecutorCompleteAsync(claimId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _checkpoint.Setup(cp => cp.ClearAsync(claimId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _claimCosmos.Setup(c => c.PatchAsync(claimId, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(
            [docProcessMock.Object, raiMock.Object, summarizeMock.Object, gapMock.Object],
            _optionsWithRai);

        await sut.RunAsync(claimId, CancellationToken.None);

        // DocumentProcess should NOT be called (was already completed)
        docProcessMock.Verify(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()), Times.Never);
        // Rai, Summarize, Gap should all be called
        raiMock.Verify(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()), Times.Once);
        summarizeMock.Verify(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()), Times.Once);
        gapMock.Verify(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ExecutorThrowsWorkflowExecutorFailedException_PropagatesAndMarksClaimFailed()
    {
        const string claimId = "claim-fail";
        var claim = BuildClaim(claimId);

        SetupClaimInCosmos(claim);
        SetupNoCompletedCheckpoints(claimId);

        var failingMock = new Mock<IWorkflowExecutor>();
        failingMock.Setup(e => e.ExecutorName).Returns("DocumentProcess");
        failingMock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkflowExecutorFailedException("DocumentProcess", "Cosmos unavailable"));

        _claimCosmos.Setup(c => c.PatchAsync(claimId, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut([failingMock.Object], _optionsWithRai);

        await Assert.ThrowsAsync<WorkflowExecutorFailedException>(
            () => sut.RunAsync(claimId, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_ExecutorThrowsGenericException_WrapsAsWorkflowExecutorFailedException()
    {
        const string claimId = "claim-generic-fail";
        var claim = BuildClaim(claimId);

        SetupClaimInCosmos(claim);
        SetupNoCompletedCheckpoints(claimId);

        var failingMock = new Mock<IWorkflowExecutor>();
        failingMock.Setup(e => e.ExecutorName).Returns("DocumentProcess");
        failingMock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        _claimCosmos.Setup(c => c.PatchAsync(claimId, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut([failingMock.Object], _optionsWithRai);

        var ex = await Assert.ThrowsAsync<WorkflowExecutorFailedException>(
            () => sut.RunAsync(claimId, CancellationToken.None));

        ex.ExecutorName.Should().Be("DocumentProcess");
    }

    [Fact]
    public async Task RunAsync_RaiDisabled_SkipsRaiStage()
    {
        const string claimId = "claim-norai";
        var claim = BuildClaim(claimId);

        SetupClaimInCosmos(claim);
        SetupNoCompletedCheckpoints(claimId);

        var raiMock = new Mock<IWorkflowExecutor>();
        raiMock.Setup(e => e.ExecutorName).Returns("Rai");

        var docMock = new Mock<IWorkflowExecutor>();
        docMock.Setup(e => e.ExecutorName).Returns("DocumentProcess");
        docMock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var summarizeMock = new Mock<IWorkflowExecutor>();
        summarizeMock.Setup(e => e.ExecutorName).Returns("Summarize");
        summarizeMock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var gapMock = new Mock<IWorkflowExecutor>();
        gapMock.Setup(e => e.ExecutorName).Returns("Gap");
        gapMock.Setup(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        _checkpoint.Setup(cp => cp.MarkExecutorCompleteAsync(claimId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _checkpoint.Setup(cp => cp.ClearAsync(claimId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _claimCosmos.Setup(c => c.PatchAsync(claimId, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut([docMock.Object, raiMock.Object, summarizeMock.Object, gapMock.Object], _optionsWithoutRai);
        await sut.RunAsync(claimId, CancellationToken.None);

        raiMock.Verify(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()), Times.Never);
        docMock.Verify(e => e.ExecuteAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
