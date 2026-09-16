namespace DragonDiskForge.Core.Models;

public sealed record ImageVerificationInfo(
    string Sha256,
    string Sha512,
    long SizeBytes);
