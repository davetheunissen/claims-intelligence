namespace ClaimsIntelligence.Infrastructure.AppConfiguration;

public interface IAppConfigurationService
{
    /// <summary>Returns all key-value pairs from App Configuration.</summary>
    Task<IReadOnlyDictionary<string, string?>> GetAllSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the value for a single key, or null if not found.</summary>
    Task<string?> GetSettingAsync(string key, string? label = null, CancellationToken cancellationToken = default);
}
