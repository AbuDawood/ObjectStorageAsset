using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal static class ObjectAssetOwnerKeyAdapter
{
    public static object NormalizeKey(object? key, Type ownerKeyType)
    {
        if (key is null)
        {
            throw new InvalidOperationException(
                $"Owner key for '{ownerKeyType.FullName}' resolved to null.");
        }

        var effectiveType = Nullable.GetUnderlyingType(ownerKeyType) ?? ownerKeyType;
        if (effectiveType == typeof(int))
        {
            var value = Convert.ToInt32(key);
            if (value == 0)
            {
                throw new InvalidOperationException("Owner key must be materialized before staging object assets.");
            }

            return (long)value;
        }

        if (effectiveType == typeof(long))
        {
            var value = Convert.ToInt64(key);
            if (value == 0)
            {
                throw new InvalidOperationException("Owner key must be materialized before staging object assets.");
            }

            return value;
        }

        if (effectiveType == typeof(Guid))
        {
            var value = key is Guid guid ? guid : Guid.Parse(key.ToString()!);
            if (value == Guid.Empty)
            {
                throw new InvalidOperationException("Owner key must be materialized before staging object assets.");
            }

            return value;
        }

        if (effectiveType == typeof(string))
        {
            var value = key.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Owner key must be materialized before staging object assets.");
            }

            return value;
        }

        throw new InvalidOperationException(
            $"Owner key type '{effectiveType.FullName}' is not supported by ObjectStorageAsset.");
    }

    public static void BindOwner(ObjectAsset asset, object normalizedKey)
    {
        switch (normalizedKey)
        {
            case long int64:
                asset.BindOwner(int64);
                return;
            case Guid guid:
                asset.BindOwner(guid);
                return;
            case string text:
                asset.BindOwner(text);
                return;
            default:
                throw new InvalidOperationException(
                    $"Normalized owner key type '{normalizedKey.GetType().FullName}' is not supported.");
        }
    }
}
