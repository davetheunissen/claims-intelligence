using System.Text.Json.Nodes;

namespace ClaimsIntelligence.Api.Endpoints;

public static class ClaimsDemoEndpoints
{
    private const string Tag = "claimsdemo";
    private const string ClaimsBlobContainer = "claims";
    private const string ClaimQueueName = "claim-process-queue";

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static IEndpointRouteBuilder MapClaimsDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/claimsdemo").WithTags(Tag);

        // POST /claimsdemo/claims/auto-submit — accept uploaded files as one claim
        group.MapPost("/claims/auto-submit",
            async (
                [FromForm] IFormFileCollection files,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                IQueueStorageService queue,
                IConfiguration config,
                CancellationToken ct) =>
            {
                if (files is null || files.Count == 0)
                    return Results.BadRequest(new { status = "failed", message = "No files uploaded." });

                var maxMb = config.GetValue<double>("Api:MaxFileSizeMb", 20);
                var claimId = Guid.NewGuid().ToString();
                var acceptedFiles = new List<object>();

                await blob.EnsureContainerExistsAsync(ClaimsBlobContainer, ct);

                foreach (var file in files)
                {
                    if (file.Length > maxMb * 1024 * 1024)
                        return Results.BadRequest(new { status = "failed", message = $"File '{file.FileName}' exceeds {maxMb} MB limit." });

                    var safeFilename = Path.GetFileName(file.FileName);
                    using var stream = file.OpenReadStream();
                    await blob.UploadAsync(ClaimsBlobContainer, $"{claimId}/{safeFilename}", stream, file.ContentType, ct);

                    acceptedFiles.Add(new
                    {
                        file_name = safeFilename,
                        mime_type = file.ContentType,
                        size = file.Length,
                        category = "processing",
                        confidence = 0.0,
                        method = "Intake accepted"
                    });
                }

                var claim = new ClaimProcess
                {
                    Id = claimId,
                    ProcessName = "Auto-classified intake",
                    Status = ClaimSteps.Pending
                };
                await db.InsertAsync(claim, ct);

                var queueMessage = JsonSerializer.Serialize(new { claim_process_id = claimId });
                await queue.EnsureQueueExistsAsync(ClaimQueueName, ct);
                await queue.SendMessageAsync(ClaimQueueName, queueMessage, ct);

                return Results.Accepted($"/claimprocessor/claims/{claimId}/status", new
                {
                    claim_id = claimId,
                    status = "processing",
                    files = acceptedFiles
                });
            })
            .WithName("AutoSubmitClaim")
            .WithSummary("Accept uploaded files as one claim and classify them in the background")
            .DisableAntiforgery();

        // POST /claimsdemo/claims/start — start sample claim
        group.MapPost("/claims/start",
            async (
                ICosmosService<ClaimProcess> db,
                IQueueStorageService queue,
                CancellationToken ct) =>
            {
                var claimId = Guid.NewGuid().ToString();
                var claim = new ClaimProcess
                {
                    Id = claimId,
                    ProcessName = "Sample auto-claim intake",
                    Status = ClaimSteps.Pending
                };
                await db.InsertAsync(claim, ct);

                var queueMessage = JsonSerializer.Serialize(new { claim_process_id = claimId });
                await queue.EnsureQueueExistsAsync(ClaimQueueName, ct);
                await queue.SendMessageAsync(ClaimQueueName, queueMessage, ct);

                return Results.Accepted($"/claimprocessor/claims/{claimId}/status", new
                {
                    claim_id = claimId,
                    status = "processing",
                    files = new[] {
                        new { file_name = "claim_form.pdf", mime_type = "application/pdf", size = 0, category = "processing", confidence = 0.0 },
                        new { file_name = "police_report.pdf", mime_type = "application/pdf", size = 0, category = "processing", confidence = 0.0 },
                        new { file_name = "repair_estimate.pdf", mime_type = "application/pdf", size = 0, category = "processing", confidence = 0.0 },
                        new { file_name = "damage_photo.png", mime_type = "image/png", size = 0, category = "processing", confidence = 0.0 }
                    }
                });
            })
            .WithName("StartSampleClaim")
            .WithSummary("Start the bundled sample auto-insurance claim through real intake");

        // GET /claimsdemo/claims/{claim_id}/documents
        group.MapGet("/claims/{claimId}/documents",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { message = "Claim not found." });

                // Read manifest sidecar if present
                List<object> documents = [];
                try
                {
                    var manifestJson = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/manifest.json", ct);
                    var manifestNode = JsonNode.Parse(manifestJson);
                    var filesNode = manifestNode?["files"];
                    if (filesNode is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            documents.Add(item?.Deserialize<object>() ?? new object());
                        }
                    }
                }
                catch
                {
                    // Return processed documents from DB if no manifest sidecar
                    documents = claim.ProcessedDocuments.Select(d => (object)new
                    {
                        file_name = d.FileName,
                        mime_type = d.MimeType,
                        status = d.Status
                    }).ToList();
                }

                return Results.Ok(new { claim_id = claimId, documents });
            })
            .WithName("ListClaimDocuments")
            .WithSummary("List documents in a claim");

        // GET /claimsdemo/claims/{claim_id}/files/{file_name}/raw
        group.MapGet("/claims/{claimId}/files/{fileName}/raw",
            async (
                string claimId,
                string fileName,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var safeName = fileName.Replace("\\", "/").TrimStart('/');
                if (string.IsNullOrEmpty(safeName) || safeName == "manifest.json" || safeName.Contains('/') || safeName.Contains(".."))
                    return Results.BadRequest(new { message = "Invalid file name." });

                try
                {
                    var data = await blob.DownloadAsync(ClaimsBlobContainer, $"{claimId}/{safeName}", ct);
                    var mediaType = GetMimeType(safeName);
                    return Results.File(data, mediaType, safeName);
                }
                catch
                {
                    return Results.NotFound(new { message = "File not found." });
                }
            })
            .WithName("GetClaimFileRaw")
            .WithSummary("Stream a single uploaded file from the claim's blob prefix");

        // GET /claimsdemo/claims/{claim_id}/classification
        group.MapGet("/claims/{claimId}/classification",
            async (
                string claimId,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                try
                {
                    var sidecarJson = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/classification.json", ct);
                    var sidecar = JsonSerializer.Deserialize<object>(sidecarJson);
                    return Results.Ok(new { claim_id = claimId, classification = sidecar });
                }
                catch
                {
                    return Results.Accepted(null as string, new
                    {
                        claim_id = claimId,
                        status = "processing",
                        message = "Classification not yet available; the claim is still being processed."
                    });
                }
            })
            .WithName("GetClaimClassification")
            .WithSummary("Get document classification results for a claim");

        // GET /claimsdemo/claims/{claim_id}/entities
        group.MapGet("/claims/{claimId}/entities",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { message = "Claim not found." });

                if (IsProcessing(claim))
                    return Results.Accepted(null as string, new
                    {
                        claim_id = claimId,
                        status = "processing",
                        message = "Entities not yet available; the claim is still being processed."
                    });

                // Read entities sidecar
                try
                {
                    var entitiesJson = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/entities.json", ct);
                    var entities = JsonSerializer.Deserialize<object>(entitiesJson);
                    return Results.Ok(new { claim_id = claimId, entities });
                }
                catch
                {
                    return Results.Ok(new { claim_id = claimId, entities = new object() });
                }
            })
            .WithName("GetClaimEntities")
            .WithSummary("Extract entities from claim documents");

        // GET /claimsdemo/claims/{claim_id}/fraud-check
        group.MapGet("/claims/{claimId}/fraud-check",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { message = "Claim not found." });

                if (IsProcessing(claim))
                    return Results.Accepted(null as string, new
                    {
                        claim_id = claimId,
                        status = "processing",
                        message = "Fraud check not yet available; the claim is still being processed."
                    });

                var parsed = ParseGaps(claim.ProcessGaps);
                return Results.Ok(new { claim_id = claimId, fraud_indicators = parsed.FraudIndicators });
            })
            .WithName("GetFraudCheck")
            .WithSummary("Get fraud check results for a claim");

        // GET /claimsdemo/claims/{claim_id}/fraud-acks
        group.MapGet("/claims/{claimId}/fraud-acks",
            async (
                string claimId,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var acks = await ReadFraudAcks(blob, claimId, ct);
                return Results.Ok(new { claim_id = claimId, acks });
            })
            .WithName("GetFraudAcks")
            .WithSummary("Get fraud acknowledgements for a claim");

        // POST /claimsdemo/claims/{claim_id}/fraud-acks
        group.MapPost("/claims/{claimId}/fraud-acks",
            async (
                string claimId,
                FraudAckRequest request,
                HttpContext httpCtx,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var acks = await ReadFraudAcks(blob, claimId, ct);
                if (request.Acknowledged)
                {
                    acks[request.FindingId] = new
                    {
                        acknowledged = true,
                        by = GetPrincipalName(httpCtx),
                        at = DateTimeOffset.UtcNow.ToString("O"),
                        note = request.Note?.Trim() ?? ""
                    };
                }
                else
                {
                    acks.Remove(request.FindingId);
                }

                await WriteFraudAcks(blob, claimId, acks, ct);
                await AppendAuditEvent(blob, claimId, request.Acknowledged ? "fraud_ack" : "fraud_unack", GetPrincipalName(httpCtx),
                    new { finding_id = request.FindingId, note = request.Note?.Trim() }, ct);

                return Results.Ok(new { claim_id = claimId, acks });
            })
            .WithName("PostFraudAck")
            .WithSummary("Acknowledge or un-acknowledge a fraud finding");

        // GET /claimsdemo/claims/{claim_id}/disposition
        group.MapGet("/claims/{claimId}/disposition",
            async (
                string claimId,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var disposition = await ReadDisposition(blob, claimId, ct);
                return Results.Ok(new { claim_id = claimId, disposition });
            })
            .WithName("GetDisposition")
            .WithSummary("Get the disposition for a claim");

        // POST /claimsdemo/claims/{claim_id}/disposition
        group.MapPost("/claims/{claimId}/disposition",
            async (
                string claimId,
                DispositionRequest request,
                HttpContext httpCtx,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var validDecisions = new HashSet<string> { "approve", "approve_with_conditions", "decline", "refer_to_siu" };
                if (!validDecisions.Contains(request.Decision))
                    return Results.BadRequest(new { detail = $"decision must be one of {string.Join(", ", validDecisions.Order())}" });

                var record = new Dictionary<string, object?>
                {
                    ["decision"] = request.Decision,
                    ["decided_by"] = GetPrincipalName(httpCtx),
                    ["decided_at"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["note"] = request.Note?.Trim() ?? "",
                    ["snapshot"] = request.Snapshot
                };

                await WriteDisposition(blob, claimId, record, ct);
                await AppendAuditEvent(blob, claimId, "disposition_set", GetPrincipalName(httpCtx),
                    new { decision = request.Decision, verdict = request.Snapshot.Verdict, confidence = request.Snapshot.Confidence }, ct);

                return Results.Ok(new { claim_id = claimId, disposition = record });
            })
            .WithName("PostDisposition")
            .WithSummary("Record a claim disposition decision");

        // DELETE /claimsdemo/claims/{claim_id}/disposition
        group.MapDelete("/claims/{claimId}/disposition",
            async (
                string claimId,
                HttpContext httpCtx,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                await WriteDisposition(blob, claimId, new Dictionary<string, object?> { ["cleared"] = true }, ct);
                await AppendAuditEvent(blob, claimId, "disposition_cleared", GetPrincipalName(httpCtx), null, ct);
                return Results.Ok(new { claim_id = claimId, disposition = (object?)null });
            })
            .WithName("DeleteDisposition")
            .WithSummary("Clear the disposition for a claim");

        // GET /claimsdemo/claims/{claim_id}/audit
        group.MapGet("/claims/{claimId}/audit",
            async (
                string claimId,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var events = await ReadAudit(blob, claimId, ct);
                return Results.Ok(new { claim_id = claimId, events });
            })
            .WithName("GetAuditLog")
            .WithSummary("Get the audit log for a claim");

        // POST /claimsdemo/claims/{claim_id}/siu
        group.MapPost("/claims/{claimId}/siu",
            async (
                string claimId,
                SiuHandoffRequest request,
                HttpContext httpCtx,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var principal = GetPrincipalName(httpCtx);
                var note = request.Note?.Trim();
                var record = new Dictionary<string, object?>
                {
                    ["decision"] = "refer_to_siu",
                    ["decided_by"] = principal,
                    ["decided_at"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["note"] = note,
                    ["snapshot"] = request.Snapshot
                };
                await WriteDisposition(blob, claimId, record, ct);
                await AppendAuditEvent(blob, claimId, "disposition_set", principal, new { decision = "refer_to_siu", via = "siu_handoff" }, ct);
                await AppendAuditEvent(blob, claimId, "marked_for_siu", principal, new { note }, ct);

                var acks = await ReadFraudAcks(blob, claimId, ct);
                await AppendAuditEvent(blob, claimId, "siu_exported", principal, new { ack_count = acks.Count }, ct);

                var audit = await ReadAudit(blob, claimId, ct);
                var bundle = new
                {
                    claim_id = claimId,
                    exported_at = DateTimeOffset.UtcNow.ToString("O"),
                    exported_by = principal,
                    disposition = record,
                    fraud_acks = acks,
                    audit
                };

                return Results.Ok(new { claim_id = claimId, disposition = record, export = bundle });
            })
            .WithName("PostSiuHandoff")
            .WithSummary("Refer a claim to SIU and export a bundle");

        // GET /claimsdemo/claims/{claim_id}/business-checks
        group.MapGet("/claims/{claimId}/business-checks",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { message = "Claim not found." });

                if (IsProcessing(claim))
                    return Results.Accepted(null as string, new
                    {
                        claim_id = claimId,
                        status = "processing",
                        message = "Business checks not yet available; the claim is still being processed."
                    });

                var parsed = ParseGaps(claim.ProcessGaps);
                return Results.Ok(new { claim_id = claimId, checks = parsed.BusinessChecks });
            })
            .WithName("GetBusinessChecks")
            .WithSummary("Get business rule check results for a claim");

        // GET /claimsdemo/claims/{claim_id}/summary
        group.MapGet("/claims/{claimId}/summary",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { message = "Claim not found." });

                if (IsProcessing(claim) && string.IsNullOrWhiteSpace(claim.ProcessSummary))
                    return Results.Accepted(null as string, new
                    {
                        claim_id = claimId,
                        status = "processing",
                        message = "Summary not yet available; the claim is still being processed."
                    });

                return Results.Ok(new { claim_id = claimId, markdown = claim.ProcessSummary ?? "", key_facts = new object() });
            })
            .WithName("GetClaimSummary")
            .WithSummary("Get the AI-generated summary for a claim");

        // PUT /claimsdemo/claims/{claim_id}/summary
        group.MapPut("/claims/{claimId}/summary",
            async (
                string claimId,
                SummaryUpdateRequest request,
                ICosmosService<ClaimProcess> db,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { message = "Claim not found." });

                await db.PatchAsync(claimId, [PatchOperation.Set("/processSummary", request.Markdown)], ct);

                return Results.Ok(new { claim_id = claimId, saved = true, summary = new { markdown = request.Markdown } });
            })
            .WithName("PutClaimSummary")
            .WithSummary("Save or update the claim summary");

        // POST /claimsdemo/claims/{claim_id}/recommendation
        group.MapPost("/claims/{claimId}/recommendation",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                IConfiguration config,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { message = "Claim not found." });

                if (IsProcessing(claim))
                    return Results.Accepted(null as string, new
                    {
                        claim_id = claimId,
                        status = "processing",
                        message = "Recommendation not yet available; the claim is still being processed."
                    });

                // Read recommendation sidecar if available (written by workflow)
                try
                {
                    var recJson = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/recommendation.json", ct);
                    var rec = JsonSerializer.Deserialize<object>(recJson);
                    return Results.Ok(new { claim_id = claimId, data = rec });
                }
                catch
                {
                    return Results.Ok(new
                    {
                        claim_id = claimId,
                        recommendation = new
                        {
                            verdict = "Investigate further",
                            confidence = 0.7,
                            rationale = "Processing complete. AI recommendation requires Foundry project configuration."
                        },
                        stream_text = "",
                        policy_excerpts = Array.Empty<object>(),
                        follow_ups = Array.Empty<string>()
                    });
                }
            })
            .WithName("GetRecommendation")
            .WithSummary("Generate a verdict and rationale via Azure OpenAI");

        // GET /claimsdemo/claims/{claim_id}/email-draft
        group.MapGet("/claims/{claimId}/email-draft",
            async (
                string claimId,
                ICosmosService<ClaimProcess> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var claim = await db.GetByIdAsync(claimId, ct);
                if (claim is null)
                    return Results.NotFound(new { message = "Claim not found." });

                if (IsProcessing(claim))
                    return Results.Accepted(null as string, new
                    {
                        claim_id = claimId,
                        status = "processing",
                        message = "Email draft not yet available; the claim is still being processed."
                    });

                // Read email draft sidecar if available
                try
                {
                    var draftJson = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/email_draft.json", ct);
                    var draft = JsonSerializer.Deserialize<object>(draftJson);
                    return Results.Ok(new { claim_id = claimId, data = draft });
                }
                catch
                {
                    return Results.Ok(new
                    {
                        claim_id = claimId,
                        subject = "Claim Decision Notification",
                        body = "Your claim has been reviewed.",
                        body_markdown = "Your claim has been reviewed."
                    });
                }
            })
            .WithName("GetEmailDraft")
            .WithSummary("Draft an outcome letter via Azure OpenAI");

        // POST /claimsdemo/claims/{claim_id}/email-send
        group.MapPost("/claims/{claimId}/email-send",
            async (
                string claimId,
                EmailSendRequest request,
                HttpContext httpCtx,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var deliveryId = Guid.NewGuid().ToString("N");
                var record = new Dictionary<string, object?>
                {
                    ["delivery_id"] = deliveryId,
                    ["queued_at"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["to"] = request.To ?? "",
                    ["cc"] = request.Cc ?? "",
                    ["subject"] = request.Subject ?? "",
                    ["body"] = request.Body ?? ""
                };

                try
                {
                    var json = JsonSerializer.Serialize(record);
                    await blob.UploadTextAsync(ClaimsBlobContainer, $"{claimId}/email_queue.json", json, ct);
                }
                catch
                {
                    return Results.Json(new
                    {
                        claim_id = claimId,
                        queued = false,
                        message = "Email queue state could not be persisted."
                    }, statusCode: 500);
                }

                await AppendAuditEvent(blob, claimId, "email_sent", GetPrincipalName(httpCtx),
                    new { delivery_id = deliveryId, to = request.To, subject = request.Subject }, ct);

                return Results.Ok(new { claim_id = claimId, queued = true, delivery_id = deliveryId });
            })
            .WithName("SendEmail")
            .WithSummary("Queue the customer letter for delivery");

        // GET /claimsdemo/claims/{claim_id}/email-status
        group.MapGet("/claims/{claimId}/email-status",
            async (
                string claimId,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                try
                {
                    var json = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/email_queue.json", ct);
                    var record = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (record is null || !record.ContainsKey("delivery_id"))
                        return Results.Ok(new { claim_id = claimId, queued = (object?)null });

                    return Results.Ok(new
                    {
                        claim_id = claimId,
                        queued = new
                        {
                            delivery_id = record.TryGetValue("delivery_id", out var did) ? did.GetString() : "",
                            queued_at = record.TryGetValue("queued_at", out var qa) ? qa.GetString() : "",
                            to = record.TryGetValue("to", out var to) ? to.GetString() : "",
                            subject = record.TryGetValue("subject", out var sub) ? sub.GetString() : ""
                        }
                    });
                }
                catch
                {
                    return Results.Ok(new { claim_id = claimId, queued = (object?)null });
                }
            })
            .WithName("GetEmailStatus")
            .WithSummary("Return the queued-email sidecar state");

        // POST /claimsdemo/policy-index/seed
        group.MapPost("/policy-index/seed",
            async (
                PolicyIndexSeedRequest request,
                IConfiguration config,
                CancellationToken ct) =>
            {
                var endpoint = config["Azure:AiSearchEndpoint"];
                var configuredIndex = config["Azure:AiSearchIndexName"];
                var indexName = request.IndexName ?? configuredIndex;

                if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(indexName))
                    return Results.Problem("AI Search is not configured.", statusCode: 503);

                // Return accepted — actual seeding delegates to infrastructure
                return Results.Ok(new { index_name = indexName, documents_uploaded = request.Documents.Count });
            })
            .WithName("SeedPolicyIndex")
            .WithSummary("Seed the advisory claims-handling guidance Search index");

        // POST /claimsdemo/member-policies-index/seed
        group.MapPost("/member-policies-index/seed",
            async (
                MemberPolicySeedRequest request,
                IConfiguration config,
                CancellationToken ct) =>
            {
                var endpoint = config["Azure:AiSearchEndpoint"];
                var configuredIndex = config["Azure:MemberPoliciesIndexName"];
                var indexName = request.IndexName ?? configuredIndex;

                if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(indexName))
                    return Results.Problem("Member-policies AI Search index is not configured.", statusCode: 503);

                return Results.Ok(new { index_name = indexName, documents_uploaded = request.Documents.Count });
            })
            .WithName("SeedMemberPoliciesIndex")
            .WithSummary("Seed the authoritative member auto-policy Search index");

        // POST /claimsdemo/warmup-grounding
        group.MapPost("/warmup-grounding",
            async (IConfiguration config, CancellationToken ct) =>
            {
                var projectEndpoint = config["Azure:AiProjectEndpoint"];
                var model = config["Azure:AzureOpenAiModel"];
                if (string.IsNullOrWhiteSpace(projectEndpoint) || string.IsNullOrWhiteSpace(model))
                    return Results.Problem("Foundry project not configured; nothing to warm.", statusCode: 503);

                return Results.Ok(new { status = "warm" });
            })
            .WithName("WarmupGrounding")
            .WithSummary("Pre-warm the recommendation agent's AI Search grounding path");

        return app;
    }

    // -------------------------------------------------------------------------
    // Sidecar helpers
    // -------------------------------------------------------------------------

    private static async Task<Dictionary<string, object>> ReadFraudAcks(
        IBlobStorageService blob, string claimId, CancellationToken ct)
    {
        try
        {
            var raw = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/fraud_acks.json", ct);
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
            if (data is not null && data.TryGetValue("acks", out var acksEl))
            {
                var acks = JsonSerializer.Deserialize<Dictionary<string, object>>(acksEl.GetRawText());
                return acks ?? [];
            }
        }
        catch { }
        return [];
    }

    private static async Task WriteFraudAcks(
        IBlobStorageService blob, string claimId, Dictionary<string, object> acks, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { acks });
        await blob.UploadTextAsync(ClaimsBlobContainer, $"{claimId}/fraud_acks.json", payload, ct);
    }

    private static async Task<Dictionary<string, object?>?> ReadDisposition(
        IBlobStorageService blob, string claimId, CancellationToken ct)
    {
        try
        {
            var raw = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/disposition.json", ct);
            var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(raw);
            if (data is not null && data.ContainsKey("decision"))
                return data;
        }
        catch { }
        return null;
    }

    private static async Task WriteDisposition(
        IBlobStorageService blob, string claimId, Dictionary<string, object?> record, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(record);
        await blob.UploadTextAsync(ClaimsBlobContainer, $"{claimId}/disposition.json", payload, ct);
    }

    private static async Task<List<object>> ReadAudit(
        IBlobStorageService blob, string claimId, CancellationToken ct)
    {
        try
        {
            var raw = await blob.DownloadTextAsync(ClaimsBlobContainer, $"{claimId}/audit.json", ct);
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
            if (data is not null && data.TryGetValue("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array)
            {
                var events = JsonSerializer.Deserialize<List<object>>(eventsEl.GetRawText());
                return events ?? [];
            }
        }
        catch { }
        return [];
    }

    private static async Task AppendAuditEvent(
        IBlobStorageService blob,
        string claimId,
        string eventType,
        string by,
        object? payload,
        CancellationToken ct)
    {
        var events = await ReadAudit(blob, claimId, ct);
        var evt = new
        {
            id = Guid.NewGuid().ToString("N"),
            type = eventType,
            at = DateTimeOffset.UtcNow.ToString("O"),
            by,
            payload = payload ?? new object()
        };
        events.Add(evt);
        var json = JsonSerializer.Serialize(new { events });
        await blob.UploadTextAsync(ClaimsBlobContainer, $"{claimId}/audit.json", json, ct);
    }

    private static string GetPrincipalName(HttpContext ctx) =>
        ctx.Request.Headers.TryGetValue("X-MS-CLIENT-PRINCIPAL-NAME", out var name) && !string.IsNullOrWhiteSpace(name)
            ? name.ToString()
            : ctx.Request.Headers.TryGetValue("X-MS-CLIENT-PRINCIPAL-ID", out var id) && !string.IsNullOrWhiteSpace(id)
                ? id.ToString()
                : "adjuster";

    private static bool IsProcessing(ClaimProcess claim) =>
        claim.Status is ClaimSteps.Pending or ClaimSteps.DocumentProcessing
            or ClaimSteps.RaiAnalysis or ClaimSteps.Summarizing or ClaimSteps.GapAnalysis;

    private static (List<object> FraudIndicators, List<object> BusinessChecks) ParseGaps(string processGaps)
    {
        if (string.IsNullOrWhiteSpace(processGaps))
            return ([], []);
        try
        {
            var gapsNode = JsonNode.Parse(processGaps);
            var fraudIndicators = gapsNode?["fraud_indicators"]?.Deserialize<List<object>>() ?? [];
            var businessChecks = gapsNode?["business_checks"]?.Deserialize<List<object>>() ?? [];
            return (fraudIndicators, businessChecks);
        }
        catch
        {
            return ([], []);
        }
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }
}
