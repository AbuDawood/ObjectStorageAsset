using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Application;

public sealed record ObjectAssetOriginReference(Guid InstanceId, Guid AssetId);
public sealed record ObjectAssetOwnerReference(Guid InstanceId, string OwnerType, ObjectAssetOwnerKeyKind KeyKind, string Key)
{
    public ObjectAssetOwnerReference Normalize()
    {
        if (InstanceId == Guid.Empty || string.IsNullOrWhiteSpace(OwnerType) || OwnerType.Length > 128 || string.IsNullOrWhiteSpace(Key)
            || Key.Length > 256 || !Enum.IsDefined(KeyKind) || KeyKind == ObjectAssetOwnerKeyKind.Unassigned)
            throw new InvalidOperationException("Invalid owner reference.");
        var key = KeyKind switch
        {
            ObjectAssetOwnerKeyKind.Int64 => long.Parse(Key, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ObjectAssetOwnerKeyKind.Guid => Guid.Parse(Key).ToString("D"),
            _ => Key.Trim()
        };
        if (KeyKind == ObjectAssetOwnerKeyKind.Guid && Guid.Parse(key) == Guid.Empty) throw new InvalidOperationException("Owner GUID cannot be empty.");
        return this with { OwnerType = OwnerType.Trim(), Key = key };
    }
}

public interface IObjectAssetOwnerBinder
{
    Task<ObjectAssetReferenceDto> BindRegisteredAsync<T>(Guid assetId, T owner, ObjectAssetSlot slot, CancellationToken cancellationToken = default);
}
public sealed record ObjectAssetStorageReference(Guid AuthorityId, string Bucket, string ObjectKey, string? ObjectVersion = null);
public sealed record ObjectAssetNamingReference(string ProfileId, int Version, string DefinitionHash);

public sealed class ObjectAssetDescriptorV3
{
    [System.Text.Json.Serialization.JsonRequired]
    public int DescriptorVersion { get; init; } = 3;
    public required Guid AssetId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long Length { get; init; }
    public required string Hash { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required ObjectAssetOriginReference Origin { get; init; }
    public required ObjectAssetOwnerReference Owner { get; init; }
    public required ObjectAssetStorageReference Storage { get; init; }
    public ObjectAssetNamingReference? Naming { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ObjectAssetLegacyPeerContext(Guid StorageAuthorityId,
    ObjectAssetOriginReference? Origin = null, ObjectAssetOwnerReference? Owner = null);

public interface IObjectAssetExchangeRegistry
{
    Task<ObjectAssetDescriptorV3?> GetDescriptorV3Async(Guid assetId, CancellationToken cancellationToken = default);
    Task<ObjectAssetDescriptorRegistrationResult> RegisterDescriptorV3Async(ObjectAssetDescriptorV3 descriptor,
        ObjectAssetOwnershipMode ownershipMode = ObjectAssetOwnershipMode.Referenced, CancellationToken cancellationToken = default);
    Task<ObjectAssetDescriptorRegistrationResult> RegisterLegacyDescriptorAsync(ObjectAssetDescriptorRegistrationRequest request,
        ObjectAssetLegacyPeerContext peer, CancellationToken cancellationToken = default);
}
