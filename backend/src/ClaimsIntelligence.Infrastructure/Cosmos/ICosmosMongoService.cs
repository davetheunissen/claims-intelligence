using MongoDB.Driver;

namespace ClaimsIntelligence.Infrastructure.Cosmos;

public interface ICosmosMongoService<T>
{
    Task InsertAsync(T document, CancellationToken cancellationToken = default);

    Task<List<T>> FindAsync(
        FilterDefinition<T> filter,
        SortDefinition<T>? sort = null,
        CancellationToken cancellationToken = default);

    Task<T?> FindOneAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default);

    Task<UpdateResult> UpdateAsync(
        FilterDefinition<T> filter,
        UpdateDefinition<T> update,
        CancellationToken cancellationToken = default);

    /// <summary>Atomic upsert: $set on match, $setOnInsert (if provided) on insert only.</summary>
    Task<UpdateResult> UpsertAsync(
        FilterDefinition<T> filter,
        UpdateDefinition<T> setFields,
        UpdateDefinition<T>? setOnInsert = null,
        CancellationToken cancellationToken = default);

    Task<DeleteResult> DeleteAsync(FilterDefinition<T> filter, CancellationToken cancellationToken = default);
}
