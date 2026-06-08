using System.Net;
using System.Net.Http.Json;
using ClaimsIntelligence.Api.Models.Requests;
using Microsoft.Azure.Cosmos;

namespace ClaimsIntelligence.Tests.Api;

/// <summary>
/// Integration tests for /claimprocessor endpoints.
/// </summary>
public class ClaimProcessorEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    public ClaimProcessorEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateClaim_ValidRequest_Returns200WithClaimId()
    {
        _factory.ClaimProcessCosmos
            .Setup(c => c.InsertAsync(It.IsAny<ClaimProcess>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory.BlobService
            .Setup(b => b.EnsureContainerExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new ClaimCreateRequest("schemaset-abc");
        var response = await _client.PutAsJsonAsync("/claimprocessor/claims", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("claim_id");
        body.Should().Contain("created");
    }

    [Fact]
    public async Task GetClaimStatus_PendingClaim_Returns200()
    {
        const string claimId = "claim-pending";
        var claim = new ClaimProcess
        {
            Id = claimId,
            Status = ClaimSteps.Pending
        };

        _factory.ClaimProcessCosmos
            .Setup(c => c.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var response = await _client.GetAsync($"/claimprocessor/claims/{claimId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Pending");
    }

    [Fact]
    public async Task GetClaimStatus_CompletedClaim_Returns302()
    {
        const string claimId = "claim-completed";
        var claim = new ClaimProcess
        {
            Id = claimId,
            Status = ClaimSteps.Completed
        };

        _factory.ClaimProcessCosmos
            .Setup(c => c.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        // Use a non-redirecting client — the endpoint returns 302 via Results.Json
        // (no Location header), and the default client's redirect handler would crash
        // on a null URI.
        var noRedirectClient = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        var response = await noRedirectClient.GetAsync($"/claimprocessor/claims/{claimId}/status");

        ((int)response.StatusCode).Should().Be(302);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Completed");
    }

    [Fact]
    public async Task GetClaimStatus_NotFound_Returns404()
    {
        const string claimId = "claim-notfound";

        _factory.ClaimProcessCosmos
            .Setup(c => c.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClaimProcess?)null);

        var response = await _client.GetAsync($"/claimprocessor/claims/{claimId}/status");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StartClaimProcess_ExistingClaim_UpdatesAndEnqueues()
    {
        const string claimId = "claim-enqueue";
        var existingClaim = new ClaimProcess { Id = claimId, Status = ClaimSteps.Pending };

        _factory.ClaimProcessCosmos
            .Setup(c => c.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingClaim);

        _factory.ClaimProcessCosmos
            .Setup(c => c.PatchAsync(claimId, It.IsAny<IReadOnlyList<PatchOperation>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory.QueueService
            .Setup(q => q.EnsureQueueExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory.QueueService
            .Setup(q => q.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new ClaimProcessRequest(claimId);
        var response = await _client.PostAsJsonAsync("/claimprocessor/claims", request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.QueueService.Verify(q => q.SendMessageAsync(
            "claim-process-queue", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetClaimDetails_ExistingClaim_Returns200WithData()
    {
        const string claimId = "claim-details";
        var claim = new ClaimProcess
        {
            Id = claimId,
            ProcessName = "FNOL Claim",
            Status = ClaimSteps.Summarizing
        };

        _factory.ClaimProcessCosmos
            .Setup(c => c.GetByIdAsync(claimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var response = await _client.GetAsync($"/claimprocessor/claims/{claimId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("FNOL Claim");
    }
}
