using Microsoft.EntityFrameworkCore;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetRegistry(ObjectStorageAssetDbContext objectStorageAssetDbContext)
    : IObjectAssetRegistry
{
    private readonly ObjectStorageAssetDbContext _objectStorageAssetDbContext = objectStorageAssetDbContext;

    public async Task<ObjectAssetDescriptor?> GetDescriptorAsync(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        if (assetId == Guid.Empty)
        {
            throw new InvalidOperationException("Asset descriptor reads require a non-empty asset id.");
        }

        var asset = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == assetId && x.Status == ObjectAssetStatus.Active,
                cancellationToken)
            .ConfigureAwait(false);

        return asset is null ? null : MapDescriptor(asset);
    }

    public async Task<IReadOnlyDictionary<Guid, ObjectAssetDescriptor>> GetDescriptorsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        if (assetIds.Count == 0)
        {
            return new Dictionary<Guid, ObjectAssetDescriptor>();
        }

        var keys = assetIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (keys.Length == 0)
        {
            return new Dictionary<Guid, ObjectAssetDescriptor>();
        }

        var assets = await _objectStorageAssetDbContext.ObjectAssets
            .AsNoTracking()
            .Where(x => x.Status == ObjectAssetStatus.Active && keys.Contains(x.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return assets.ToDictionary(x => x.Id, MapDescriptor);
    }

    public Task<ObjectAssetDescriptorRegistrationResult> RegisterDescriptorAsync(
        ObjectAssetDescriptorRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RegisterDescriptorCoreAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<ObjectAssetDescriptorRegistrationResult>> RegisterDescriptorsAsync(
        IReadOnlyCollection<ObjectAssetDescriptorRegistrationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            return [];
        }

        var results = new List<ObjectAssetDescriptorRegistrationResult>(requests.Count);
        foreach (var request in requests)
        {
            results.Add(await RegisterDescriptorCoreAsync(request, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<ObjectAssetDescriptorRegistrationResult> RegisterDescriptorCoreAsync(
        ObjectAssetDescriptorRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateDescriptor(request.Descriptor);

        var existingAsset = await _objectStorageAssetDbContext.ObjectAssets
            .FindAsync([request.Descriptor.AssetId], cancellationToken)
            .ConfigureAwait(false);

        if (existingAsset is null)
        {
            var asset = ObjectAsset.CreateRegisteredDescriptor(
                request.Descriptor.AssetId,
                request.Descriptor.Bucket,
                request.Descriptor.ObjectKey,
                request.Descriptor.FileName,
                request.Descriptor.ContentType,
                request.Descriptor.Length,
                request.Descriptor.Hash,
                request.Descriptor.CreatedAtUtc,
                request.Descriptor.ExpiresAtUtc,
                request.OwnershipMode);

            _objectStorageAssetDbContext.ObjectAssets.Add(asset);
            await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new ObjectAssetDescriptorRegistrationResult
            {
                AssetId = asset.Id,
                Outcome = ObjectAssetDescriptorRegistrationOutcome.Added,
                OwnershipMode = asset.OwnershipMode,
                Descriptor = MapDescriptor(asset)
            };
        }

        var existingDescriptor = MapDescriptor(existingAsset);
        var conflictFields = DetectConflicts(existingAsset, existingDescriptor, request);
        if (conflictFields.Count == 0)
        {
            return new ObjectAssetDescriptorRegistrationResult
            {
                AssetId = existingAsset.Id,
                Outcome = ObjectAssetDescriptorRegistrationOutcome.IgnoredIdentical,
                OwnershipMode = existingAsset.OwnershipMode,
                Descriptor = existingDescriptor
            };
        }

        return new ObjectAssetDescriptorRegistrationResult
        {
            AssetId = existingAsset.Id,
            Outcome = ObjectAssetDescriptorRegistrationOutcome.RejectedConflict,
            OwnershipMode = existingAsset.OwnershipMode,
            Descriptor = existingDescriptor,
            ConflictFields = conflictFields,
            ConflictMessage = $"Descriptor registration conflicted for asset '{existingAsset.Id}'."
        };
    }

    private static IReadOnlyList<string> DetectConflicts(
        ObjectAsset existingAsset,
        ObjectAssetDescriptor existingDescriptor,
        ObjectAssetDescriptorRegistrationRequest request)
    {
        var conflicts = new List<string>();
        var descriptor = request.Descriptor;

        if (existingAsset.Status != ObjectAssetStatus.Active)
        {
            conflicts.Add(nameof(existingAsset.Status));
        }

        if (!string.Equals(existingDescriptor.FileName, descriptor.FileName, StringComparison.Ordinal))
        {
            conflicts.Add(nameof(ObjectAssetDescriptor.FileName));
        }

        if (!string.Equals(existingDescriptor.ContentType, descriptor.ContentType, StringComparison.Ordinal))
        {
            conflicts.Add(nameof(ObjectAssetDescriptor.ContentType));
        }

        if (existingDescriptor.Length != descriptor.Length)
        {
            conflicts.Add(nameof(ObjectAssetDescriptor.Length));
        }

        if (!string.Equals(existingDescriptor.Hash, NormalizeString(descriptor.Hash), StringComparison.Ordinal))
        {
            conflicts.Add(nameof(ObjectAssetDescriptor.Hash));
        }

        if (!string.Equals(existingDescriptor.Bucket, descriptor.Bucket, StringComparison.Ordinal))
        {
            conflicts.Add(nameof(ObjectAssetDescriptor.Bucket));
        }

        if (!string.Equals(existingDescriptor.ObjectKey, descriptor.ObjectKey, StringComparison.Ordinal))
        {
            conflicts.Add(nameof(ObjectAssetDescriptor.ObjectKey));
        }

        if (existingDescriptor.CreatedAtUtc != descriptor.CreatedAtUtc)
        {
            conflicts.Add(nameof(ObjectAssetDescriptor.CreatedAtUtc));
        }

        if (existingDescriptor.ExpiresAtUtc != descriptor.ExpiresAtUtc)
        {
            conflicts.Add(nameof(ObjectAssetDescriptor.ExpiresAtUtc));
        }

        if (existingAsset.OwnershipMode != request.OwnershipMode)
        {
            conflicts.Add(nameof(ObjectAssetDescriptorRegistrationRequest.OwnershipMode));
        }

        return conflicts;
    }

    private static ObjectAssetDescriptor MapDescriptor(ObjectAsset asset)
    {
        return new ObjectAssetDescriptor
        {
            AssetId = asset.Id,
            FileName = asset.OriginalFileName,
            ContentType = asset.ContentType,
            Length = asset.SizeBytes,
            Hash = asset.Sha256,
            Bucket = asset.BucketName,
            ObjectKey = asset.ObjectKey,
            CreatedAtUtc = asset.CreatedAtUtc,
            ExpiresAtUtc = asset.ExpiresAtUtc,
            DescriptorVersion = ObjectAssetDescriptor.CurrentVersion
        };
    }

    private static void ValidateDescriptor(ObjectAssetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.AssetId == Guid.Empty)
        {
            throw new InvalidOperationException("Asset descriptor registration requires a non-empty asset id.");
        }

        if (descriptor.DescriptorVersion != ObjectAssetDescriptor.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Descriptor version '{descriptor.DescriptorVersion}' is not supported.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.FileName))
        {
            throw new InvalidOperationException("Descriptor FileName is required.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.ContentType))
        {
            throw new InvalidOperationException("Descriptor ContentType is required.");
        }

        if (descriptor.Length < 0)
        {
            throw new InvalidOperationException("Descriptor Length cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.Bucket))
        {
            throw new InvalidOperationException("Descriptor Bucket is required.");
        }

        if (string.IsNullOrWhiteSpace(descriptor.ObjectKey))
        {
            throw new InvalidOperationException("Descriptor ObjectKey is required.");
        }
    }

    private static string NormalizeString(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
