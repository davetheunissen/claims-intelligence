using System.Net;
using System.Net.Http.Json;
using ClaimsIntelligence.Api.Models.Requests;

namespace ClaimsIntelligence.Tests.Api;

/// <summary>
/// Integration tests for /contentprocessor endpoints using an in-process test server.
/// All Azure dependencies are replaced with mocks via ApiTestFactory.
/// </summary>
public class ContentProcessorEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;
    private readonly HttpClient _client;

    public ContentProcessorEndpointsTests(ApiTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetStatus_ExistingProcessingRecord_Returns200WithStatus()
    {
        const string processId = "test-process-1";
        var process = new ContentProcess
        {
            Id = processId,
            ProcessId = processId,
            FileName = "claim.pdf",
            Status = "processing"
        };

        _factory.ContentProcessCosmos
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(process);

        var response = await _client.GetAsync($"/contentprocessor/status/{processId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(processId);
        body.Should().Contain("processing");
    }

    [Fact]
    public async Task GetStatus_CompletedProcess_Returns302WithResourceUrl()
    {
        const string processId = "test-complete-1";
        var process = new ContentProcess
        {
            Id = processId,
            ProcessId = processId,
            FileName = "claim.pdf",
            Status = "Completed"
        };

        _factory.ContentProcessCosmos
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(process);

        var response = await _client.GetAsync($"/contentprocessor/status/{processId}");

        ((int)response.StatusCode).Should().Be(302);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("completed");
    }

    [Fact]
    public async Task GetStatus_NotFound_Returns404()
    {
        const string processId = "missing-process";

        _factory.ContentProcessCosmos
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentProcess?)null);

        var response = await _client.GetAsync($"/contentprocessor/status/{processId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListProcessed_ReturnsPagedResults()
    {
        var processes = new List<ContentProcess>
        {
            new() { Id = "p1", ProcessId = "p1", FileName = "doc1.pdf", Status = "Completed" },
            new() { Id = "p2", ProcessId = "p2", FileName = "doc2.pdf", Status = "Completed" }
        };

        _factory.ContentProcessCosmos
            .Setup(c => c.GetAllAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processes);

        var request = new PagingRequest(1, 10);
        var response = await _client.PostAsJsonAsync("/contentprocessor/processed", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("total_count");
        body.Should().Contain("2");
    }

    [Fact]
    public async Task GetProcessedContent_ExistingRecord_Returns200()
    {
        const string processId = "proc-get";
        var process = new ContentProcess
        {
            Id = processId,
            ProcessId = processId,
            FileName = "form.pdf",
            Status = "Completed"
        };

        _factory.ContentProcessCosmos
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(process);

        var response = await _client.GetAsync($"/contentprocessor/processed/{processId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetProcessedContent_MissingRecord_Returns404()
    {
        const string processId = "proc-missing";

        _factory.ContentProcessCosmos
            .Setup(c => c.GetByIdAsync(processId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentProcess?)null);

        var response = await _client.GetAsync($"/contentprocessor/processed/{processId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
