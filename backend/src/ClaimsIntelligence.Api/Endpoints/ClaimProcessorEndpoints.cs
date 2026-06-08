namespace ClaimsIntelligence.Api.Endpoints;

public static class ClaimProcessorEndpoints
{
    private const string ClaimQueueName = "claim-process-queue";
    private const string ClaimsBlobContainer = "claims";
    private const string Tag = "claimprocessor";

    public static IEndpointRouteBuilder MapClaimProcessorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/claimprocessor").WithTags(Tag);

        // PUT /claimprocessor/claims — create claim container
        group.MapPut("/claims",
            async (
                [FromBody] ClaimCreateRequest request,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var claimId = Guid.NewGuid().ToString();
                var claim = new ClaimProcess
                {
                    Id = claimId,
                    SchemaSetId = request.SchemaCollectionId,
                    Status = ClaimSteps.Pending
                };
                await db.InsertAsync(claim, ct);
                await blob.EnsureContainerExistsAsync(ClaimsBlobContainer, ct);

                return Results.Ok(new
                {
                    claim_id = claimId,
                    schemaset_id = request.SchemaCollectionId,
                    status = "created"
                });
            })
            .WithName("CreateClaimContainer")
            .WithSummary("Create a claim batch");

        // GET /claimprocessor/claims/{claim_id}/manifest
        group.MapGet("/claims/{claimId}/manifest",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { status = "failed", message = $"Claim '{claimId}' not found." });

                try
                {
                    var manifest = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/manifest.json", ct);
                    return Results.Content(manifest, "application/json");
                }
                catch
                {
                    return Results.Ok(claim);
                }
            })
            .WithName("GetClaimManifest")
            .WithSummary("Get claim batch details");

        // DELETE /claimprocessor/claims/{claim_id}
        group.MapDelete("/claims/{claimId}",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { status = "failed", message = $"Claim process with ID {claimId} not found." });

                try
                {
                    await foreach (var blobName in blob.ListBlobNamesAsync(ClaimsBlobContainer, $"{claimId}/", ct))
                    {
                        await blob.DeleteBlobAsync(ClaimsBlobContainer, blobName, ct);
                    }
                }
                catch { }

                await db.DeleteAsync(claimId, ct);

                return Results.Ok(new
                {
                    status = "success",
                    message = $"Claim process with ID : '{claimId}' and its container have been deleted."
                });
            })
            .WithName("DeleteClaimContainer")
            .WithSummary("Delete a claim batch");

        // POST /claimprocessor/claims/{claim_id}/files — upload file to claim
        group.MapPost("/claims/{claimId}/files",
            async (
                string claimId,
                [FromForm] string? data,
                IFormFile file,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                IConfiguration config,
                CancellationToken ct) =>
            {
                ClaimFileAddRequest? req = null;
                if (!string.IsNullOrWhiteSpace(data))
                {
                    req = JsonSerializer.Deserialize<ClaimFileAddRequest>(data,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }

                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { status = "failed", message = "No file uploaded." });

                if (req?.ClaimId is not null && req.ClaimId != claimId)
                    return Results.BadRequest(new { status = "failed", message = "Path claim_id must match data.ClaimId." });

                var maxMb = config.GetValue<double>("Api:MaxFileSizeMb", 20);
                if (file.Length > maxMb * 1024 * 1024)
                    return Results.BadRequest(new { status = "failed", message = $"File exceeds {maxMb} MB limit." });

                var safeFilename = Path.GetFileName(file.FileName);
                await blob.EnsureContainerExistsAsync(ClaimsBlobContainer, ct);
                using var stream = file.OpenReadStream();
                await blob.UploadAsync(ClaimsBlobContainer, $"{claimId}/{safeFilename}", stream, file.ContentType, ct);

                return Results.Ok(new
                {
                    batch_id = claimId,
                    file_name = safeFilename,
                    size = file.Length,
                    mime_type = file.ContentType
                });
            })
            .WithName("AddFileToClaim")
            .WithSummary("Upload a file to a claim")
            .DisableAntiforgery();

        // POST /claimprocessor/claims — submit claim batch for processing
        group.MapPost("/claims",
            async (
                ClaimProcessRequest request,
                ICosmosService<ClaimProcess> db,
                IQueueStorageService queue,
                CancellationToken ct) =>
            {
                var existing = await db.GetByIdAsync(request.ClaimProcessId, ct);

                if (existing is not null)
                {
                    await db.PatchAsync(request.ClaimProcessId, [
                        PatchOperation.Set("/status", (int)ClaimSteps.Pending),
                        PatchOperation.Set("/processName", "Waiting for processing")
                    ], ct);
                }
                else
                {
                    await db.InsertAsync(new ClaimProcess
                    {
                        Id = request.ClaimProcessId,
                        ProcessName = "Waiting for processing",
                        Status = ClaimSteps.Pending
                    }, ct);
                }

                var queueMessage = JsonSerializer.Serialize(new { claim_process_id = request.ClaimProcessId });
                await queue.EnsureQueueExistsAsync(ClaimQueueName, ct);
                await queue.SendMessageAsync(ClaimQueueName, queueMessage, ct);

                var location = $"/claimprocessor/claims/{request.ClaimProcessId}/status";
                return Results.Accepted(location, new
                {
                    status = "success",
                    message = $"claim id '{request.ClaimProcessId}' has been submitted for processing.",
                    location
                });
            })
            .WithName("StartClaimProcess")
            .WithSummary("Submit claim batch for processing");

        // POST /claimprocessor/claims/processed — list (paginated)
        group.MapPost("/claims/processed",
            async (
                PagingRequest pageRequest,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var all = await db.GetAllAsync("processTime", descending: true, ct);
                var total = all.Count;
                var totalPages = pageRequest.PageSize > 0
                    ? (total + pageRequest.PageSize - 1) / pageRequest.PageSize
                    : 1;
                var skip = (pageRequest.PageNumber - 1) * pageRequest.PageSize;
                var items = all.Skip(skip).Take(pageRequest.PageSize).ToList();
                return Results.Ok(new PaginatedClaimProcessResponse(total, totalPages, pageRequest.PageNumber, pageRequest.PageSize, items));
            })
            .WithName("ListClaimProcesses")
            .WithSummary("List claim batch processes (paginated)");

        // GET /claimprocessor/claims/{claim_id}/status
        group.MapGet("/claims/{claimId}/status",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);

                if (claim is null)
                    return Results.NotFound(new
                    {
                        status = "Not Found",
                        message = $"Claim process with ID {claimId} not found."
                    });

                var statusStr = claim.Status.ToString();

                if (claim.Status == ClaimSteps.Completed)
                    return Results.Json(new
                    {
                        status = statusStr,
                        message = $"Claim Batch '{claimId}' has been completed.",
                        location = $"/claimprocessor/claims/{claimId}"
                    }, statusCode: 302);

                if (claim.Status == ClaimSteps.Failed)
                    return Results.Json(new
                    {
                        status = statusStr,
                        message = "Workflow execution failed.",
                        location = $"/claimprocessor/claims/{claimId}"
                    }, statusCode: 302);

                return Results.Ok(new
                {
                    status = statusStr,
                    message = $"Claim Batch '{claimId}' is in progress."
                });
            })
            .WithName("GetClaimStatus")
            .WithSummary("Get claim batch processing status");

        // DELETE /claimprocessor/claims/{claim_id}/process — delete process record only
        group.MapDelete("/claims/{claimId}/process",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { status = "failed", message = $"Claim Batch process with ID {claimId} not found." });

                await db.DeleteAsync(claimId, ct);
                return Results.Ok(new { status = "success", message = $"Claim process with ID {claimId} has been deleted." });
            })
            .WithName("DeleteClaimProcess")
            .WithSummary("Delete claim batch process");

        // POST /claimprocessor/claims/{claim_id}/comment
        group.MapPost("/claims/{claimId}/comment",
            async (
                string claimId,
                ClaimCommentRequest request,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { status = "failed", message = $"Claim process with ID {claimId} not found." });

                await db.PatchAsync(claimId, [PatchOperation.Set("/processComment", request.Comment)], ct);

                return Results.Ok(new { status = "success", message = $"Comment added to Claim Batch process with ID {claimId}." });
            })
            .WithName("AddCommentToClaim")
            .WithSummary("Add comment to claim batch process");

        // GET /claimprocessor/claims/{claim_id}
        group.MapGet("/claims/{claimId}",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { status = "failed", message = $"Claim Batch process with ID {claimId} not found." });

                return Results.Ok(new { status = "success", data = claim });
            })
            .WithName("GetClaimDetails")
            .WithSummary("Get claim batch process details");

        return app;
    }
}
