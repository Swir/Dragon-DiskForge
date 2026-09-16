namespace DragonDiskForge.Core.Models;

/// <summary>
/// Result of the deliberately narrow UDF root-directory traversal path.
/// Traversal is read-only and is exposed only when a validated Type 1 partition map
/// can be translated to bounded physical image bytes.
/// </summary>
public sealed record UdfTraversalInfo(
    bool Traversed,
    int RootEntryCount,
    IReadOnlyList<string> RootEntryNames,
    IReadOnlyList<ImageIdentityEvidence> Identity,
    IReadOnlyList<ImageHealthFinding> HealthFindings);
