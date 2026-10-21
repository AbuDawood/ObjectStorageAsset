using System.Globalization;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed partial class ObjectAssetRegistry
{
    public async Task<ObjectAssetDescriptorV3?> GetDescriptorV3Async(Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await _objectStorageAssetDbContext.PermanentAssets.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == assetId && x.Status == ObjectAssetStatus.Active, cancellationToken);
        if (asset is null) return null;
        if (!asset.OriginInstanceId.HasValue || !asset.OriginAssetId.HasValue)
            throw new InvalidOperationException("Resolve legacy origin or configure LocalInstanceId before v3 export.");
        if (asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("V3 export requires a known SHA-256 digest; legacy v2 export remains available.");
        var authorityId = asset.StorageAuthorityId == Guid.Empty ? options.StorageAuthorityId : asset.StorageAuthorityId;
        if (authorityId == Guid.Empty)
            throw new InvalidOperationException("V3 export requires a configured stable storage authority id.");
        var owner = asset.BindingKind switch
        {
            ObjectAssetBindingKind.ExternalOwner => new ObjectAssetOwnerReference(asset.ExternalOwnerInstanceId!.Value,
                asset.ExternalOwnerType!, asset.ExternalOwnerKeyKind!.Value, asset.ExternalOwnerKey!),
            ObjectAssetBindingKind.LocalOwner when options.LocalInstanceId != Guid.Empty => new ObjectAssetOwnerReference(
                options.LocalInstanceId, asset.OwnerType, asset.OwnerKeyKind, asset.OwnerKeyKind switch
                {
                    ObjectAssetOwnerKeyKind.Int64 => asset.OwnerKeyInt64!.Value.ToString(CultureInfo.InvariantCulture),
                    ObjectAssetOwnerKeyKind.Guid => asset.OwnerKeyGuid!.Value.ToString("D"),
                    _ => asset.OwnerKeyText!
                }),
            _ => throw new InvalidOperationException("V3 export requires an explicit owner and stable OSA instance id.")
        };
        return new ObjectAssetDescriptorV3
        {
            AssetId = asset.Id,
            FileName = asset.OriginalFileName,
            ContentType = asset.ContentType,
            Length = asset.SizeBytes,
            Hash = asset.Sha256,
            CreatedAtUtc = asset.CreatedAtUtc,
            Origin = new(asset.OriginInstanceId.Value, asset.OriginAssetId.Value),
            Owner = owner,
            Storage = new(authorityId, asset.BucketName, asset.ObjectKey, asset.ProviderVersionId),
            Naming = asset.NamingProfileId is null ? null : new(asset.NamingProfileId, asset.NamingProfileVersion!.Value, asset.NamingDefinitionHash!),
            Metadata = ObjectAssetMetadataBagSerializer.Deserialize(asset.CustomMetadataJson)
        };
    }

    public async Task<ObjectAssetDescriptorRegistrationResult> RegisterDescriptorV3Async(ObjectAssetDescriptorV3 descriptor,
        ObjectAssetOwnershipMode ownershipMode = ObjectAssetOwnershipMode.Referenced, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(descriptor.Hash);
        ArgumentNullException.ThrowIfNull(descriptor.Storage);
        ArgumentNullException.ThrowIfNull(descriptor.Owner);
        ArgumentNullException.ThrowIfNull(descriptor.Origin);
        if (descriptor.DescriptorVersion != 3 || descriptor.Hash.Length != 64 || !descriptor.Hash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("V3 requires descriptor version 3 and a SHA-256 hex digest.");
        if (descriptor.Origin.InstanceId == Guid.Empty || descriptor.Origin.AssetId == Guid.Empty)
            throw new InvalidOperationException("V3 requires a complete immutable origin reference.");
        if (descriptor.Storage.AuthorityId == Guid.Empty)
            throw new InvalidOperationException("V3 requires an explicit portable storage authority id; adapt legacy peers using their configured context.");
        var legacy = new ObjectAssetDescriptor
        {
            AssetId = descriptor.AssetId,
            FileName = descriptor.FileName,
            ContentType = descriptor.ContentType,
            Length = descriptor.Length,
            Hash = descriptor.Hash,
            CreatedAtUtc = descriptor.CreatedAtUtc,
            Bucket = descriptor.Storage.Bucket,
            ObjectKey = descriptor.Storage.ObjectKey,
            Metadata = descriptor.Metadata
        };
        ValidateDescriptor(legacy);
        ValidateOwner(descriptor.Owner);
        var owner = descriptor.Owner.Normalize();
        _ = storageResolver.Resolve(descriptor.Storage.AuthorityId);
        var request = new ObjectAssetDescriptorRegistrationRequest { Descriptor = legacy, OwnershipMode = ownershipMode };
        var existing = await _objectStorageAssetDbContext.FindAssetAsync(descriptor.AssetId, true, cancellationToken);
        var asset = existing ?? ObjectAsset.CreateRegisteredDescriptor(legacy.AssetId, legacy.Bucket, legacy.ObjectKey,
            legacy.FileName, legacy.ContentType, legacy.Length, legacy.Hash, legacy.CreatedAtUtc, null, ownershipMode,
            ObjectAssetMetadataBagSerializer.Serialize(legacy.Metadata));
        var conflicts = existing is null ? new List<string>() : DetectConflicts(asset, MapDescriptor(asset), request).ToList();
        if (existing?.OriginInstanceId.HasValue == true && (existing.OriginInstanceId != descriptor.Origin.InstanceId || existing.OriginAssetId != descriptor.Origin.AssetId))
            conflicts.Add("Origin");
        if (existing?.BindingKind == ObjectAssetBindingKind.LocalOwner)
        {
            var localKey = existing.OwnerKeyKind switch
            {
                ObjectAssetOwnerKeyKind.Int64 => existing.OwnerKeyInt64!.Value.ToString(CultureInfo.InvariantCulture),
                ObjectAssetOwnerKeyKind.Guid => existing.OwnerKeyGuid!.Value.ToString("D"),
                _ => existing.OwnerKeyText!
            };
            if (owner.InstanceId != options.LocalInstanceId || owner.OwnerType != existing.OwnerType
                || owner.KeyKind != existing.OwnerKeyKind || owner.Key != localKey) conflicts.Add("Owner");
        }
        if (existing?.BindingKind == ObjectAssetBindingKind.ExternalOwner &&
            (existing.ExternalOwnerInstanceId != owner.InstanceId || existing.ExternalOwnerType != owner.OwnerType
            || existing.ExternalOwnerKeyKind != owner.KeyKind || existing.ExternalOwnerKey != owner.Key)) conflicts.Add("Owner");
        if (existing is not null && !storageResolver.IsSameAuthority(existing.StorageAuthorityId, descriptor.Storage.AuthorityId)) conflicts.Add("StorageAuthorityId");
        if (existing?.ProviderVersionId is not null && existing.ProviderVersionId != descriptor.Storage.ObjectVersion) conflicts.Add("ObjectVersion");
        if (existing?.NamingProfileId is not null && descriptor.Naming is { } supplied
            && (existing.NamingProfileId != supplied.ProfileId || existing.NamingProfileVersion != supplied.Version
                || existing.NamingDefinitionHash != supplied.DefinitionHash)) conflicts.Add("Naming");
        if (conflicts.Count > 0) return new()
        {
            AssetId = asset.Id,
            OwnershipMode = asset.OwnershipMode,
            Descriptor = MapDescriptor(asset),
            Outcome = ObjectAssetDescriptorRegistrationOutcome.RejectedConflict,
            ConflictFields = conflicts,
            ConflictMessage = "V3 descriptor conflicts with reserved asset identity."
        };
        if (existing?.BindingKind == ObjectAssetBindingKind.LocalOwner)
            return new() { AssetId = existing.Id, OwnershipMode = existing.OwnershipMode, Descriptor = MapDescriptor(existing), Outcome = ObjectAssetDescriptorRegistrationOutcome.IgnoredIdentical };
        asset.SetExternalOwnership(owner.InstanceId, owner.OwnerType, owner.KeyKind,
            owner.Key, descriptor.Origin.InstanceId, descriptor.Origin.AssetId, descriptor.Storage.AuthorityId);
        asset.RecordStorageVersion(null, descriptor.Storage.ObjectVersion);
        if (descriptor.Naming is { } naming) asset.SetNamingProvenance(naming.ProfileId, naming.Version, naming.DefinitionHash);
        if (existing is null) _objectStorageAssetDbContext.ObjectAssets.Add(asset);
        await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken);
        return new()
        {
            AssetId = asset.Id,
            OwnershipMode = asset.OwnershipMode,
            Descriptor = MapDescriptor(asset),
            Outcome = existing is null ? ObjectAssetDescriptorRegistrationOutcome.Added : ObjectAssetDescriptorRegistrationOutcome.IgnoredIdentical
        };
    }

    public async Task<ObjectAssetDescriptorRegistrationResult> RegisterLegacyDescriptorAsync(ObjectAssetDescriptorRegistrationRequest request,
        ObjectAssetLegacyPeerContext peer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peer);
        ValidateDescriptor(request.Descriptor);
        _ = storageResolver.Resolve(peer.StorageAuthorityId);
        if (peer.Owner is not null) ValidateOwner(peer.Owner);
        peer = peer with { Owner = peer.Owner?.Normalize() };
        var d = request.Descriptor;
        var existing = await _objectStorageAssetDbContext.FindAssetAsync(d.AssetId, true, cancellationToken);
        var asset = existing ?? ObjectAsset.CreateRegisteredDescriptor(d.AssetId, d.Bucket, d.ObjectKey, d.FileName, d.ContentType,
            d.Length, d.Hash, d.CreatedAtUtc, null, request.OwnershipMode, ObjectAssetMetadataBagSerializer.Serialize(d.Metadata));
        var conflicts = existing is null ? new List<string>() : DetectConflicts(existing, MapDescriptor(existing), request).ToList();
        if (existing is not null && !storageResolver.IsSameAuthority(existing.StorageAuthorityId, peer.StorageAuthorityId)) conflicts.Add("StorageAuthorityId");
        if (existing?.OriginInstanceId.HasValue == true && peer.Origin is { } origin
            && (existing.OriginInstanceId != origin.InstanceId || existing.OriginAssetId != origin.AssetId)) conflicts.Add("Origin");
        if (existing?.BindingKind == ObjectAssetBindingKind.ExternalOwner && peer.Owner is { } owner
            && (existing.ExternalOwnerInstanceId != owner.InstanceId || existing.ExternalOwnerType != owner.OwnerType
                || existing.ExternalOwnerKeyKind != owner.KeyKind || existing.ExternalOwnerKey != owner.Key)) conflicts.Add("Owner");
        if (conflicts.Count > 0) return new()
        {
            AssetId = asset.Id,
            OwnershipMode = asset.OwnershipMode,
            Descriptor = MapDescriptor(asset),
            Outcome = ObjectAssetDescriptorRegistrationOutcome.RejectedConflict,
            ConflictFields = conflicts,
            ConflictMessage = "Legacy peer context conflicts with known asset state."
        };
        if (existing?.BindingKind is ObjectAssetBindingKind.LocalOwner or ObjectAssetBindingKind.ExternalOwner)
        {
            if (!asset.OriginInstanceId.HasValue && peer.Origin is { } enrichment)
            {
                asset.SetImportedOrigin(enrichment.InstanceId, enrichment.AssetId);
                await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken);
            }
            return new() { AssetId = asset.Id, OwnershipMode = asset.OwnershipMode, Descriptor = MapDescriptor(asset), Outcome = ObjectAssetDescriptorRegistrationOutcome.IgnoredIdentical };
        }
        asset.SetLegacyStorageAuthority(peer.StorageAuthorityId);
        if (peer.Origin is { } knownOrigin)
        {
            asset.SetImportedOrigin(knownOrigin.InstanceId, knownOrigin.AssetId);
        }
        if (peer.Owner is { } knownOwner) asset.SetExternalOwnership(knownOwner.InstanceId, knownOwner.OwnerType,
            knownOwner.KeyKind, knownOwner.Key, peer.Origin?.InstanceId ?? Guid.Empty, peer.Origin?.AssetId ?? Guid.Empty, peer.StorageAuthorityId);
        if (existing is null) _objectStorageAssetDbContext.ObjectAssets.Add(asset);
        await _objectStorageAssetDbContext.SaveChangesAsync(cancellationToken);
        return new()
        {
            AssetId = asset.Id,
            OwnershipMode = asset.OwnershipMode,
            Descriptor = MapDescriptor(asset),
            Outcome = existing is null ? ObjectAssetDescriptorRegistrationOutcome.Added : ObjectAssetDescriptorRegistrationOutcome.IgnoredIdentical
        };
    }

    private static void ValidateOwner(ObjectAssetOwnerReference owner)
    {
        if (owner.InstanceId == Guid.Empty || string.IsNullOrWhiteSpace(owner.OwnerType) || owner.OwnerType.Length > 128
            || string.IsNullOrWhiteSpace(owner.Key) || owner.Key.Length > 256 || !Enum.IsDefined(owner.KeyKind)
            || owner.KeyKind == ObjectAssetOwnerKeyKind.Unassigned
            || owner.KeyKind == ObjectAssetOwnerKeyKind.Int64 && !long.TryParse(owner.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || owner.KeyKind == ObjectAssetOwnerKeyKind.Guid && (!Guid.TryParse(owner.Key, out var id) || id == Guid.Empty))
            throw new InvalidOperationException("Invalid external owner reference.");
    }
}
