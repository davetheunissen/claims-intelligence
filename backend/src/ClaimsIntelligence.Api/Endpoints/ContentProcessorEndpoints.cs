namespace ClaimsIntelligence.Api.Endpoints;

public static class ContentProcessorEndpoints
{
    private const string ExtractQueueName = "content-pipeline-extract-queue";
    private const string ProcessesBlobContainer = "processes";
    private const string Tag = "contentprocessor";

    public static IEndpointRouteBuilder MapContentProcessorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contentprocessor").WithTags(Tag);

        // POST /contentprocessor/processed — list (paginated)
        group.MapPost("/processed",
            async (
                PagingRequest pageRequest,
                ICosmosService<ContentProcess> db,
                CancellationToken ct) =>
            {
                var all = await db.GetAllAsync("processedTime", descending: true, ct);
                var total = all.Count;
                var totalPages = pageRequest.PageSize > 0
                    ? (total + pageRequest.PageSize - 1) / pageRequest.PageSize
                    : 1;
                var skip = (pageRequest.PageNumber - 1) * pageRequest.PageSize;
                var items = all.Skip(skip).Take(pageRequest.PageSize).ToList();
                return Results.Ok(new
                {
                    total_count = total,
                    total_pages = totalPages,
                    current_page = pageRequest.PageNumber,
                    page_size = pageRequest.PageSize,
                    items
                });
            })
            .WithName("ListProcessedContents")
            .WithSummary("List processed contents (paginated)")
            .Accepts<PagingRequest>("application/json");

        // POST /contentprocessor/submit — submit file
        group.MapPost("/submit",
            async (
                [FromForm] string? data,
                IFormFile file,
                IQueueStorageService queue,
                ICosmosService<ContentProcess> db,
                IBlobStorageService blob,
                IConfiguration config,
                CancellationToken ct) =>
            {
                ContentProcessorSubmitRequest? req = null;
                if (!string.IsNullOrWhiteSpace(data))
                {
                    req = JsonSerializer.Deserialize<ContentProcessorSubmitRequest>(data,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { status = "failed", message = "No file uploaded." });

                var maxMb = config.GetValue<double>("Api:MaxFileSizeMb", 20);
                if (file.Length > maxMb * 1024 * 1024)
                    return Results.BadRequest(new { status = "failed", message = $"File exceeds {maxMb} MB limit." });

                var processId = Guid.NewGuid().ToString();
                var safeFilename = Path.GetFileName(file.FileName);

                await blob.EnsureContainerExistsAsync(ProcessesBlobContainer, ct);
                using var stream = file.OpenReadStream();
                await blob.UploadAsync(ProcessesBlobContainer, $"{processId}/{safeFilename}", stream, file.ContentType, ct);

                var process = new ContentProcess
                {
                    Id = processId,
                    ProcessId = processId,
                    FileName = safeFilename,
                    MimeType = file.ContentType,
                    Status = "processing"
                };
                await db.InsertAsync(process, ct);

                var queueMessage = JsonSerializer.Serialize(new
                {
                    process_id = processId,
                    schema_id = req?.SchemaId,
                    metadata_id = req?.MetadataId,
                    file_name = safeFilename
                });
                await queue.EnsureQueueExistsAsync(ExtractQueueName, ct);
                await queue.SendMessageAsync(ExtractQueueName, queueMessage, ct);

                var statusUrl = $"/contentprocessor/status/{processId}";
                return Results.Accepted(statusUrl, new
                {
                    message = $"File '{safeFilename}' received and is being processed.",
                    process_id = processId,
                    status_url = statusUrl
                });
            })
            .WithName("SubmitFileForProcessing")
            .WithSummary("Submit a file for processing")
            .DisableAntiforgery();

        // GET /contentprocessor/status/{process_id}
        group.MapGet("/status/{processId}",
            async (
                string processId,
                ICosmosService<ContentProcess> db,
                CancellationToken ct) =>
            {
                var process = await db.GetByIdAsync(processId, ct);

                if (process is null)
                    return Results.NotFound(new
                    {
                        status = "failed",
                        process_id = processId,
                        file_name = "",
                        message = $"Processing of file with Process ID '{processId}' not found."
                    });

                if (process.Status == "Completed")
                    return Results.Json(new
                    {
                        status = "completed",
                        process_id = processId,
                        file_name = process.FileName,
                        message = $"Processing of file '{process.FileName}' with Process ID '{processId}' is completed.",
                        resource_url = $"/contentprocessor/processed/{processId}"
                    }, statusCode: 302);

                if (process.Status == "Error")
                    return Results.Json(new
                    {
                        status = "failed",
                        process_id = processId,
                        file_name = process.FileName,
                        message = $"Processing of file '{process.FileName}' with Process ID '{processId}' has failed."
                    }, statusCode: 500);

                return Results.Ok(new
                {
                    status = process.Status,
                    process_id = processId,
                    file_name = process.FileName,
                    message = $"Processing of file '{process.FileName}' with Process ID '{processId}' is still in progress."
                });
            })
            .WithName("GetProcessingStatus")
            .WithSummary("Get file processing status");

        // GET /contentprocessor/processed/{process_id}
        group.MapGet("/processed/{processId}",
            async (
                string processId,
                ICosmosService<ContentProcess> db,
                CancellationToken ct) =>
            {
                var process = await db.GetByIdAsync(processId, ct);

                if (process is null)
                    return Results.NotFound(new
                    {
                        status = "failed",
                        process_id = processId,
                        file_name = "",
                        message = $"Processing of file with Process ID '{processId}' not found."
                    });

                return Results.Ok(process);
            })
            .WithName("GetProcessedContent")
            .WithSummary("Get processed content result");

        // GET /contentprocessor/processed/{process_id}/steps
        group.MapGet("/processed/{processId}/steps",
            async (
                string processId,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                try
                {
                    var json = await blob.DownloadTextAsync(ProcessesBlobContainer, $"{processId}/step_outputs.json", ct);
                    var steps = JsonSerializer.Deserialize<object>(json);
                    return Results.Ok(steps);
                }
                catch
                {
                    return Results.NotFound(new
                    {
                        status = "failed",
                        process_id = processId,
                        file_name = "",
                        message = $"Step outputs for Process ID '{processId}' not found."
                    });
                }
            })
            .WithName("GetProcessedSteps")
            .WithSummary("Get processed step outputs");

        // PUT /contentprocessor/processed/{process_id}
        group.MapPut("/processed/{processId}",
            async (
                string processId,
                [FromBody] JsonElement body,
                ICosmosService<ContentProcess> db,
                CancellationToken ct) =>
            {
                var process = await db.GetByIdAsync(processId, ct);
                if (process is null)
                    return Results.NotFound(new { status = "failed", message = $"Process ID '{processId}' not found." });

                if (body.TryGetProperty("modified_result", out var modifiedResultEl))
                {
                    await db.PatchAsync(processId, [PatchOperation.Set("/modified_result", modifiedResultEl.ToString())], ct);
                }
                else if (body.TryGetProperty("comment", out var commentEl))
                {
                    await db.PatchAsync(processId, [PatchOperation.Set("/comment", commentEl.GetString() ?? "")], ct);
                }
                else
                {
                    return Results.BadRequest(new { status = "failed", message = "Request body must contain 'modified_result' or 'comment'." });
                }

                return Results.Ok(new
                {
                    status = "success",
                    message = $"Processing of file with Process ID '{processId}' updated."
                });
            })
            .WithName("UpdateProcessedResult")
            .WithSummary("Update processed result or comment");

        // GET /contentprocessor/processed/files/{process_id} — stream original file
        group.MapGet("/processed/files/{processId}",
            async (
                string processId,
                ICosmosService<ContentProcess> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var process = await db.GetByIdAsync(processId, ct);

                if (process is null)
                    return Results.NotFound(new
                    {
                        status = "failed",
                        message = $"Process ID '{processId}' not found."
                    });

                try
                {
                    var bytes = await blob.DownloadAsync(ProcessesBlobContainer, $"{processId}/{process.FileName}", ct);
                    var contentType = process.MimeType ?? "application/octet-stream";
                    return Results.File(bytes, contentType, process.FileName);
                }
                catch
                {
                    return Results.NotFound(new { status = "failed", message = "File not found in blob storage." });
                }
            })
            .WithName("StreamOriginalFile")
            .WithSummary("Stream original uploaded file");

        // DELETE /contentprocessor/processed/{process_id}
        group.MapDelete("/processed/{processId}",
            async (
                string processId,
                ICosmosService<ContentProcess> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var process = await db.GetByIdAsync(processId, ct);

                if (process is null)
                    return Results.Ok(new ContentResultDeleteResponse(processId, "Failed", "This record no longer exists. Please refresh."));

                try
                {
                    await foreach (var blobName in blob.ListBlobNamesAsync(ProcessesBlobContainer, $"{processId}/", ct))
                    {
                        await blob.DeleteBlobAsync(ProcessesBlobContainer, blobName, ct);
                    }
                }
                catch
                {
                    // Best-effort blob cleanup
                }

                await db.DeleteAsync(processId, ct);

                return Results.Ok(new ContentResultDeleteResponse(processId, "Success", ""));
            })
            .WithName("DeleteProcessedContent")
            .WithSummary("Delete processed content result");

        return app;
    }
}
