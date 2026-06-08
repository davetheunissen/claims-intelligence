namespace ClaimsIntelligence.Api.Models.Requests;

/// <summary>A single policy guidance document to seed into the AI Search index.</summary>
public record PolicyIndexSeedDocument(
    string SourceFilename,
    string Content,
    string? Section = null);

/// <summary>Request body for seeding the advisory claims-handling guidance Search index.</summary>
public record PolicyIndexSeedRequest(
    string? IndexName,
    List<PolicyIndexSeedDocument> Documents);

/// <summary>One authoritative member-held auto-policy contract document.</summary>
public record MemberPolicySeedDocument(
    string PolicyNumber,
    string SourceFilename,
    string Content,
    string FormVersion = "",
    string Carrier = "",
    string State = "",
    string EffectiveDate = "",
    string ExpirationDate = "",
    string Status = "",
    List<string>? NamedInsureds = null,
    List<string>? ExcludedDrivers = null,
    List<string>? Vins = null,
    List<string>? Endorsements = null);

/// <summary>Request body for seeding the member auto-policy Search index.</summary>
public record MemberPolicySeedRequest(
    string? IndexName,
    List<MemberPolicySeedDocument> Documents);

/// <summary>Request body for acknowledging or un-acknowledging a fraud finding.</summary>
public record FraudAckRequest(string FindingId, bool Acknowledged, string? Note = null);

/// <summary>Snapshot of a recommendation verdict captured at disposition time.</summary>
public record DispositionSnapshotRequest(
    string Verdict,
    double Confidence,
    string Rationale = "",
    List<string>? FollowUps = null,
    string? MemberPolicyNumber = null,
    List<string>? GuidanceSectionIds = null);

/// <summary>Request body for recording a claim disposition decision.</summary>
public record DispositionRequest(
    string Decision,
    DispositionSnapshotRequest Snapshot,
    string? Note = null);

/// <summary>Request body for SIU handoff export.</summary>
public record SiuHandoffRequest(
    DispositionSnapshotRequest Snapshot,
    string? Note = null);

/// <summary>Request body for saving a summary markdown.</summary>
public record SummaryUpdateRequest(string Markdown);

/// <summary>Request body for queueing an email for delivery.</summary>
public record EmailSendRequest(
    string? To,
    string? Cc,
    string? Subject,
    string? Body);
