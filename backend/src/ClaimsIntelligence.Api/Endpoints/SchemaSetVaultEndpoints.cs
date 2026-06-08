namespace ClaimsIntelligence.Api.Endpoints;

public static class SchemaSetVaultEndpoints
{
    private const string Tag = "schemasetvault";

    public static IEndpointRouteBuilder MapSchemaSetVaultEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/schemasetvault").WithTags(Tag);

        // GET /schemasetvault/ — list all schema sets
        group.MapGet("/",
            async (
                ICosmosService<SchemaSet> db,
                CancellationToken ct) =>
            {
                var sets = await db.GetAllAsync(ct: ct);
                return Results.Ok(sets);
            })
            .WithName("ListSchemaSets")
            .WithSummary("List schema sets");

        // POST /schemasetvault/ — create a schema set
        group.MapPost("/",
            async (
                SchemaSetCreateRequest request,
                ICosmosService<SchemaSet> db,
                CancellationToken ct) =>
            {
                var schemaSet = new SchemaSet
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = request.Name,
                    Description = request.Description,
                    SchemaIds = [],
                    CreatedOn = DateTime.UtcNow
                };
                await db.InsertAsync(schemaSet, ct);
                return Results.Ok(schemaSet);
            })
            .WithName("CreateSchemaSet")
            .WithSummary("Create a schema set");

        // GET /schemasetvault/{schemaset_id}
        group.MapGet("/{schemaSetId}",
            async (
                string schemaSetId,
                ICosmosService<SchemaSet> db,
                CancellationToken ct) =>
            {
                var schemaSet = await db.GetByIdAsync(schemaSetId, ct);
                if (schemaSet is null)
                    return Results.NotFound(new { detail = "Schema Set not found" });
                return Results.Ok(schemaSet);
            })
            .WithName("GetSchemaSetById")
            .WithSummary("Get schema set details");

        // DELETE /schemasetvault/{schemaset_id}
        group.MapDelete("/{schemaSetId}",
            async (
                string schemaSetId,
                ICosmosService<SchemaSet> db,
                CancellationToken ct) =>
            {
                var existing = await db.GetByIdAsync(schemaSetId, ct);
                if (existing is null)
                    return Results.NotFound(new { detail = "Schema Set not found" });

                await db.DeleteAsync(schemaSetId, ct);
                return Results.Ok(new { status = "success", schemaset_id = schemaSetId });
            })
            .WithName("DeleteSchemaSet")
            .WithSummary("Delete a schema set");

        // GET /schemasetvault/{schemaset_id}/schemas — list schemas in set
        group.MapGet("/{schemaSetId}/schemas",
            async (
                string schemaSetId,
                ICosmosService<SchemaSet> dbSets,
                ICosmosService<Schema> dbSchemas,
                CancellationToken ct) =>
            {
                var schemaSet = await dbSets.GetByIdAsync(schemaSetId, ct);
                if (schemaSet is null)
                    return Results.NotFound(new { detail = $"Schema set '{schemaSetId}' not found." });

                if (schemaSet.SchemaIds.Count == 0)
                    return Results.Ok(Array.Empty<Schema>());

                var schemas = await dbSchemas.FindByIdsAsync(schemaSet.SchemaIds, ct);
                return Results.Ok(schemas);
            })
            .WithName("ListSchemasInSet")
            .WithSummary("List schemas in a schema set");

        // POST /schemasetvault/{schemaset_id}/schemas — add schema to set
        group.MapPost("/{schemaSetId}/schemas",
            async (
                string schemaSetId,
                SchemaSetAddSchemaRequest request,
                ICosmosService<SchemaSet> dbSets,
                ICosmosService<Schema> dbSchemas,
                CancellationToken ct) =>
            {
                var schemaSet = await dbSets.GetByIdAsync(schemaSetId, ct);
                if (schemaSet is null)
                    return Results.NotFound(new { detail = $"Schema set '{schemaSetId}' not found." });

                var schema = await dbSchemas.GetByIdAsync(request.SchemaId, ct);
                if (schema is null)
                    return Results.NotFound(new { detail = $"Schema '{request.SchemaId}' not found." });

                if (!schemaSet.SchemaIds.Contains(request.SchemaId))
                {
                    schemaSet.SchemaIds.Add(request.SchemaId);
                    await dbSets.PatchAsync(schemaSetId, [
                        PatchOperation.Set("/schemaIds", schemaSet.SchemaIds),
                        PatchOperation.Set("/updatedOn", DateTime.UtcNow)
                    ], ct);
                }

                return Results.Ok(schemaSet);
            })
            .WithName("AddSchemaToSet")
            .WithSummary("Add a schema to a schema set");

        // DELETE /schemasetvault/{schemaset_id}/schemas/{schema_id} — remove schema from set
        group.MapDelete("/{schemaSetId}/schemas/{schemaId}",
            async (
                string schemaSetId,
                string schemaId,
                ICosmosService<SchemaSet> dbSets,
                CancellationToken ct) =>
            {
                var schemaSet = await dbSets.GetByIdAsync(schemaSetId, ct);
                if (schemaSet is null)
                    return Results.NotFound(new { detail = $"Schema set '{schemaSetId}' not found." });

                if (!schemaSet.SchemaIds.Contains(schemaId))
                    return Results.NotFound(new { detail = $"Schema '{schemaId}' not found in set '{schemaSetId}'." });

                schemaSet.SchemaIds.Remove(schemaId);
                await dbSets.PatchAsync(schemaSetId, [
                    PatchOperation.Set("/schemaIds", schemaSet.SchemaIds),
                    PatchOperation.Set("/updatedOn", DateTime.UtcNow)
                ], ct);

                return Results.Ok(schemaSet);
            })
            .WithName("RemoveSchemaFromSet")
            .WithSummary("Remove a schema from a schema set");

        return app;
    }
}
