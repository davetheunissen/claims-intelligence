using MongoDB.Driver;

namespace ClaimsIntelligence.Infrastructure.Cosmos;

public class CosmosMongoService<T>(IMongoCollection<T> collection) : ICosmosMongoService<T>
{
    public async Task InsertAsync(T document, CancellationToken cancellationToken = default)
    {
        await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
    }

    public async Task<List<T>> FindAsync(
        FilterDefinition<T> filter,
        SortDefinition<T>? sort = null,
        CancellationToken cancellationToken = default)
    {
        var query = collection.Find(filter);
        if (sort is not null)
            query = query.Sort(sort);
        return await query.ToListAsync(cancellationToken);
    }

    public async Task<T?> FindOneAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default)
    {
        return await collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UpdateResult> UpdateAsync(
        FilterDefinition<T> filter,
        UpdateDefinition<T> update,
        CancellationToken cancellationToken = default)
    {
        return await collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    public async Task<UpdateResult> UpsertAsync(
        FilterDefinition<T> filter,
        UpdateDefinition<T> setFields,
        UpdateDefinition<T>? setOnInsert = null,
        CancellationToken cancellationToken = default)
    {
        UpdateDefinition<T> update = Builders<T>.Update.Combine(setFields);

        if (setOnInsert is not null)
            update = Builders<T>.Update.Combine(update, Builders<T>.Update.SetOnInsert("_setOnInsert_marker", 1));

        // Build a proper combined update with $set and optionally $setOnInsert.
        // MongoDB.Driver merges Combine'd definitions into a single document.
        if (setOnInsert is not null)
            update = Builders<T>.Update.Combine(setFields, setOnInsert);

        var options = new UpdateOptions { IsUpsert = true };
        return await collection.UpdateOneAsync(filter, update, options, cancellationToken);
    }

    public async Task<DeleteResult> DeleteAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default)
    {
        return await collection.DeleteOneAsync(filter, cancellationToken);
    }
}
