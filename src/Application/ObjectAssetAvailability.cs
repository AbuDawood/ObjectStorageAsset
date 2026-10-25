using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

public sealed record ObjectAssetAvailabilityResult(Guid AssetId, ObjectAssetContentAvailability Availability,
    DateTimeOffset? CheckedAtUtc = null, DateTimeOffset? MissingSinceUtc = null, ObjectAssetMissingReason? MissingReason = null);

public interface IObjectAssetAvailabilityService
{
    Task<ObjectAssetAvailabilityResult?> GetAsync(Guid assetId, CancellationToken cancellationToken = default);
    /// <summary>Explicitly contacts storage, including for a previously missing asset.</summary>
    Task<ObjectAssetAvailabilityResult?> RecheckAsync(Guid assetId, CancellationToken cancellationToken = default);
    /// <summary>Archives a confirmed missing active asset without deleting storage or its stable identity.</summary>
    Task<bool> ArchiveMissingAsync(Guid assetId, CancellationToken cancellationToken = default);
}

public sealed class ObjectAssetContentMissingException(ObjectAssetAvailabilityResult result, Exception? innerException = null)
    : IOException($"Content for asset '{result.AssetId}' is missing.", innerException)
{
    public ObjectAssetAvailabilityResult Result { get; } = result;
}

/// <summary>Providers throw this only for a confirmed absent object or requested version.</summary>
public sealed class ObjectStorageObjectNotFoundException(string bucketName, string objectKey, string? versionId = null,
    ObjectAssetMissingReason reason = ObjectAssetMissingReason.ObjectNotFound, Exception? innerException = null)
    : IOException("The requested storage object or version does not exist.", innerException)
{
    public string BucketName { get; } = bucketName;
    public string ObjectKey { get; } = objectKey;
    public string? VersionId { get; } = versionId;
    public ObjectAssetMissingReason Reason { get; } = reason;
}
