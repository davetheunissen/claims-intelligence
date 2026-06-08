namespace ClaimsIntelligence.Workflow.GapRules;

// ---------------------------------------------------------------------------
// Root
// ---------------------------------------------------------------------------

/// <summary>
/// Root model for the FNOL gap-rules YAML DSL.
/// Ported from <c>fnol_gap_rules.dsl.yaml</c>.
/// </summary>
public record GapRuleSet
{
    public int DslVersion { get; init; }
    public string RuleSetId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public Dictionary<string, DocumentTypeEntry> DocumentTypes { get; init; } = [];
    public Dictionary<string, InputField> Inputs { get; init; } = [];
    public List<RequiredDocumentRule> RequiredDocuments { get; init; } = [];
    public List<DiscrepancyCheck> DiscrepancyChecks { get; init; } = [];
    public List<ObservationTrigger> ObservationTriggers { get; init; } = [];
}

// ---------------------------------------------------------------------------
// Document types
// ---------------------------------------------------------------------------

public record DocumentTypeEntry
{
    public string RequirementType { get; init; } = string.Empty;
    public string Schema { get; init; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Inputs
// ---------------------------------------------------------------------------

public record InputField
{
    public string Type { get; init; } = string.Empty;
    public List<string>? Allowed { get; init; }
    public string Meaning { get; init; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Required document rules
// ---------------------------------------------------------------------------

public record RequiredDocumentRule
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string When { get; init; } = string.Empty;
    public List<DocumentRequirement> Require { get; init; } = [];
    public string Severity { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
}

public record DocumentRequirement
{
    public string Type { get; init; } = string.Empty;
    public int MinCount { get; init; } = 1;
}

// ---------------------------------------------------------------------------
// Discrepancy checks
// ---------------------------------------------------------------------------

public record DiscrepancyCheck
{
    public string Id { get; init; } = string.Empty;
    public string Field { get; init; } = string.Empty;
    public List<string> Sources { get; init; } = [];
    public string CheckType { get; init; } = "conflict";
    public string Severity { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
    public double? Tolerance { get; init; }
}

// ---------------------------------------------------------------------------
// Observation triggers
// ---------------------------------------------------------------------------

public record ObservationTrigger
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string When { get; init; } = string.Empty;
    public string? Check { get; init; }
    public string Severity { get; init; } = string.Empty;
    public string Rationale { get; init; } = string.Empty;
}
