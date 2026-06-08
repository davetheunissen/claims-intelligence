using Microsoft.Azure.Cosmos;

namespace ClaimsIntelligence.Infrastructure.Cosmos;

public interface ICosmosService<T>
{
    Task InsertAsync(T document, CancellationToken ct = default);
    Task<List<T>> GetAllAsync(string? orderByField = null, bool descending = false, CancellationToken ct = default);
    Task<T?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<T?> FindOneAsync(string fieldPath, object value, CancellationToken ct = default);
    Task<List<T>> FindByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default);
    Task UpsertAsync(T document, CancellationToken ct = default);
    Task PatchAsync(string id, IReadOnlyList<PatchOperation> patches, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
