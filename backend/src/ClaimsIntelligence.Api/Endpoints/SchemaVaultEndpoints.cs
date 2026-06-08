namespace ClaimsIntelligence.Api.Endpoints;

public static class SchemaVaultEndpoints
{
    private const string SchemasBlobContainer = "schemas";
    private const string Tag = "schemavault";

    public static IEndpointRouteBuilder MapSchemaVaultEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/schemavault").WithTags(Tag);

        // GET /schemavault/ — list all schemas
        group.MapGet("/",
            async (
                ICosmosService<Schema> db,
                CancellationToken ct) =>
            {
                var schemas = await db.GetAllAsync(ct: ct);
                return Results.Ok(schemas);
            })
            .WithName("ListRegisteredSchemas")
            .WithSummary("List registered schemas");

        // POST /schemavault/json — register schema (v2 JSON-native)
        group.MapPost("/json",
            async (
                SchemaVaultRegisterJsonRequest request,
                ICosmosService<Schema> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                if (request.FieldSchema is null || !request.FieldSchema.ContainsKey("fields"))
                    return Results.BadRequest(new { detail = "FieldSchema must be a dict with a non-empty 'fields' object." });

                var schemaId = Guid.NewGuid().ToString();
                var safeClass = new string(request.ClassName
                    .Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_')
                    .ToArray()).Trim('_');
                if (string.IsNullOrEmpty(safeClass)) safeClass = "schema";
                var fileName = $"{safeClass}.json";

                var analyzerEnvelope = new Dictionary<string, object?>
                {
                    ["baseAnalyzerId"] = request.BaseAnalyzerId ?? "prebuilt-document",
                    ["description"] = request.Description ?? $"Auto-generated extractor for {request.ClassName}.",
                    ["config"] = new { returnDetails = true },
                    ["fieldSchema"] = request.FieldSchema,
                    ["models"] = new { completion = request.CompletionModel ?? "gpt-4.1-mini" }
                };

                var envelopeJson = JsonSerializer.Serialize(analyzerEnvelope);
                var envelopeBytes = System.Text.Encoding.UTF8.GetBytes(envelopeJson);

                await blob.EnsureContainerExistsAsync(SchemasBlobContainer, ct);
                await blob.UploadAsync(SchemasBlobContainer, $"{schemaId}/{fileName}", envelopeBytes, "application/json", ct);

                var schema = new Schema
                {
                    Id = schemaId,
                    ClassName = request.ClassName,
                    Description = request.Description ?? string.Empty,
                    FileName = fileName,
                    ContentType = "application/json",
                    CreatedOn = DateTime.UtcNow
                };
                await db.InsertAsync(schema, ct);

                return Results.Ok(schema);
            })
            .WithName("RegisterSchemaJson")
            .WithSummary("Register a schema (Schema Vault v2 / JSON-native)");

        // DELETE /schemavault/ — unregister a schema
        group.MapDelete("/",
            async (
                [FromBody] SchemaVaultUnregisterRequest request,
                ICosmosService<Schema> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var schema = await db.GetByIdAsync(request.SchemaId, ct);
                if (schema is null)
                    return Results.NotFound(new { detail = $"Schema '{request.SchemaId}' not found." });

                try
                {
                    await foreach (var blobName in blob.ListBlobNamesAsync(SchemasBlobContainer, $"{request.SchemaId}/", ct))
                    {
                        await blob.DeleteBlobAsync(SchemasBlobContainer, blobName, ct);
                    }
                }
                catch { }

                await db.DeleteAsync(request.SchemaId, ct);

                return Results.Ok(new SchemaVaultUnregisterResponse("Success", schema.Id, schema.ClassName, schema.FileName));
            })
            .WithName("UnregisterSchema")
            .WithSummary("Unregister a schema");

        // GET /schemavault/schemas/{schema_id} — download schema file
        group.MapGet("/schemas/{schemaId}",
            async (
                string schemaId,
                ICosmosService<Schema> db,
                IBlobStorageService blob,
                CancellationToken ct) =>
            {
                var schema = await db.GetByIdAsync(schemaId, ct);
                if (schema is null)
                    return Results.NotFound(new { detail = $"Schema '{schemaId}' not found." });

                try
                {
                    var bytes = await blob.DownloadAsync(SchemasBlobContainer, $"{schemaId}/{schema.FileName}", ct);
                    return Results.File(bytes, schema.ContentType, schema.FileName);
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to download schema file: {ex.Message}", statusCode: 500);
                }
            })
            .WithName("DownloadSchemaFile")
            .WithSummary("Download schema file");

        return app;
    }
}
