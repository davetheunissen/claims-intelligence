using System.Net;
using System.Reflection;
using Microsoft.Azure.Cosmos;

namespace ClaimsIntelligence.Infrastructure.Cosmos;

public class CosmosService<T>(Container container) : ICosmosService<T>
{
    private static readonly PropertyInfo IdProperty =
        typeof(T).GetProperty("Id")
        ?? throw new InvalidOperationException($"Type {typeof(T).Name} must have a string 'Id' property.");

    private static string GetId(T document) =>
        (string)(IdProperty.GetValue(document)
            ?? throw new InvalidOperationException($"{typeof(T).Name}.Id is null."));

    public async Task InsertAsync(T document, CancellationToken ct = default)
    {
        await container.CreateItemAsync(document, new PartitionKey(GetId(document)), cancellationToken: ct);
    }

    public async Task<List<T>> GetAllAsync(string? orderByField = null, bool descending = false, CancellationToken ct = default)
    {
        var sql = orderByField is not null
            ? $"SELECT * FROM c ORDER BY c.{orderByField} {(descending ? "DESC" : "ASC")}"
            : "SELECT * FROM c";
        return await RunQueryAsync(new QueryDefinition(sql), ct);
    }

    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await container.ReadItemAsync<T>(id, new PartitionKey(id), cancellationToken: ct);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    public async Task<T?> FindOneAsync(string fieldPath, object value, CancellationToken ct = default)
    {
        var query = new QueryDefinition($"SELECT * FROM c WHERE c.{fieldPath} = @value")
            .WithParameter("@value", value);
        var results = await RunQueryAsync(query, ct);
        return results.Count > 0 ? results[0] : default;
    }

    public async Task<List<T>> FindByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var idArray = ids.ToArray();
        if (idArray.Length == 0) return [];
        var paramNames = idArray.Select((_, i) => $"@id{i}").ToArray();
        var qd = new QueryDefinition($"SELECT * FROM c WHERE c.id IN ({string.Join(",", paramNames)})");
        for (int i = 0; i < idArray.Length; i++)
            qd = qd.WithParameter(paramNames[i], idArray[i]);
        return await RunQueryAsync(qd, ct);
    }

    public async Task UpsertAsync(T document, CancellationToken ct = default)
    {
        await container.UpsertItemAsync(document, new PartitionKey(GetId(document)), cancellationToken: ct);
    }

    public async Task PatchAsync(string id, IReadOnlyList<PatchOperation> patches, CancellationToken ct = default)
    {
        await container.PatchItemAsync<T>(id, new PartitionKey(id), patches, cancellationToken: ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        try
        {
            await container.DeleteItemAsync<T>(id, new PartitionKey(id), cancellationToken: ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone — treat as success
        }
    }

    private async Task<List<T>> RunQueryAsync(QueryDefinition query, CancellationToken ct)
    {
        var results = new List<T>();
        using var iterator = container.GetItemQueryIterator<T>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            results.AddRange(page);
        }
        return results;
    }
}
