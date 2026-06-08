using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ClaimsIntelligence.Workflow.GapRules;

/// <summary>
/// Loads YAML gap-rule files from the local GapRules directory (or a configurable path).
///
/// The YAML DSL format is defined by <see cref="GapRuleSet"/>.
/// Rules are validated on load — an empty or invalid file throws <see cref="InvalidOperationException"/>.
///
/// Ported from: Python <c>steps/gap_analysis/executor/gap_executor.py</c>
/// (<c>_load_prompt_and_rules</c> / YAML loading logic).
/// </summary>
public sealed class GapRuleLoader(IOptions<WorkflowOptions> options, ILogger<GapRuleLoader> logger)
{
    private readonly WorkflowOptions _options = options.Value;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads and deserialises all <c>*.yaml</c> files from the configured
    /// <see cref="WorkflowOptions.GapRulesPath"/> directory.
    /// </summary>
    /// <returns>
    /// List of <see cref="GapRuleSet"/> instances, one per file.
    /// </returns>
    public IReadOnlyList<GapRuleSet> LoadAll()
    {
        var baseDir = Path.IsPathRooted(_options.GapRulesPath)
            ? _options.GapRulesPath
            : Path.Combine(AppContext.BaseDirectory, _options.GapRulesPath);

        if (!Directory.Exists(baseDir))
        {
            logger.LogWarning(
                "GapRules directory not found at {Path}; returning empty rule set", baseDir);
            return [];
        }

        List<GapRuleSet> results = [];
        foreach (var file in Directory.EnumerateFiles(baseDir, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var text = File.ReadAllText(file).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    logger.LogWarning("Gap rules file is empty: {File}", file);
                    continue;
                }

                var ruleSet = Deserializer.Deserialize<GapRuleSet>(text);
                results.Add(ruleSet);
                logger.LogInformation(
                    "Loaded gap rules: {RuleSetId} v{Version} from {File}",
                    ruleSet.RuleSetId, ruleSet.Version, file);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load gap rules from '{file}': {ex.Message}", ex);
            }
        }

        return results;
    }

    /// <summary>
    /// Loads the first <c>*.yaml</c> file found and returns its raw text content
    /// for injection into an AI prompt (the <c>{{RULES_DSL}}</c> placeholder pattern
    /// from the Python executor).
    /// </summary>
    public string LoadRawYaml()
    {
        var baseDir = Path.IsPathRooted(_options.GapRulesPath)
            ? _options.GapRulesPath
            : Path.Combine(AppContext.BaseDirectory, _options.GapRulesPath);

        if (!Directory.Exists(baseDir))
        {
            logger.LogWarning(
                "GapRules directory not found at {Path}; rules DSL will be empty", baseDir);
            return string.Empty;
        }

        var file = Directory
            .EnumerateFiles(baseDir, "*.yaml", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (file is null)
        {
            logger.LogWarning("No *.yaml files found in GapRules directory {Path}", baseDir);
            return string.Empty;
        }

        var text = File.ReadAllText(file).Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Gap rules file is empty: {file}");

        return text;
    }
}
