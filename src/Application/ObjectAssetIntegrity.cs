using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

public enum Sha256FailureBehavior { Warning = 0, Guard = 1 }
[Flags]
public enum Sha256VerificationTriggers { ExplicitOnly = 0, OnRead = 1, AfterUpload = 2 }

public sealed class ObjectAssetIntegrityOptions
{
    public Sha256FailureBehavior FailureBehavior { get; set; } = Sha256FailureBehavior.Warning;
    public Sha256VerificationTriggers Triggers { get; set; } = Sha256VerificationTriggers.ExplicitOnly;
    public TimeSpan EvidenceRetention { get; set; } = TimeSpan.FromDays(30);
}

public sealed record ObjectAssetIntegrityResult(Guid AssetId, Sha256ValidationStatus Status,
    string ExpectedHash, string? ActualHash, DateTimeOffset AttemptedAtUtc,
    DateTimeOffset? CompletedAtUtc, string? ObjectVersion, string? Error);

public interface IObjectAssetIntegrityService
{
    Task<ObjectAssetIntegrityResult> VerifyAsync(Guid assetId, CancellationToken cancellationToken = default);
}

public sealed class ObjectAssetIntegrityException(ObjectAssetIntegrityResult result)
    : IOException($"SHA-256 verification for asset '{result.AssetId}' returned {result.Status}.")
{
    public ObjectAssetIntegrityResult Result { get; } = result;
}
