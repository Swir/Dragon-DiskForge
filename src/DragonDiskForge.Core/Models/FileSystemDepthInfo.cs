namespace DragonDiskForge.Core.Models;

/// <summary>
/// Additional bounded filesystem evidence that is intentionally deeper than basic recognition.
/// It never implies a writable or traversable filesystem by itself.
/// </summary>
public sealed record FileSystemDepthInfo(
    IReadOnlyList<ImageIdentityEvidence> Identity,
    IReadOnlyList<ImageHealthFinding> HealthFindings)
{
    public bool HasIdentity => Identity.Count > 0;
    public bool HasHealthFindings => HealthFindings.Count > 0;
}
