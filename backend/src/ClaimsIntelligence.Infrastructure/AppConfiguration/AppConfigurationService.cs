using Azure.Data.AppConfiguration;
using Microsoft.Extensions.Logging;

namespace ClaimsIntelligence.Infrastructure.AppConfiguration;

public class AppConfigurationService(ConfigurationClient client, ILogger<AppConfigurationService> logger) : IAppConfigurationService
{
    public async Task<IReadOnlyDictionary<string, string?>> GetAllSettingsAsync(CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string?>();

        await foreach (var setting in client.GetConfigurationSettingsAsync(new SettingSelector(), cancellationToken))
        {
            results[setting.Key] = setting.Value;
        }

        return results;
    }

    public async Task<string?> GetSettingAsync(string key, string? label = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.GetConfigurationSettingAsync(key, label, cancellationToken);
            return response.Value.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            logger.LogDebug("App Configuration key not found: {Key}", key);
            return null;
        }
    }
}
