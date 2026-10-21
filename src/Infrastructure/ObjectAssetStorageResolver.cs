using Elf.ObjectStorageAsset.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetStorageResolver(IServiceProvider services, IObjectStorageProvider primary, ObjectStorageAssetRuntimeOptions options)
{
    public bool IsSameAuthority(Guid first, Guid second) => Canonical(first) == Canonical(second);
    private Guid Canonical(Guid value) => value == Guid.Empty ? options.StorageAuthorityId : value;
    public IObjectStorageProvider Resolve(Guid authorityId)
    {
        if (authorityId == Guid.Empty || authorityId == options.StorageAuthorityId) return primary;
        if (options.StorageAuthorityAliases.TryGetValue(authorityId, out var name))
            return services.GetRequiredKeyedService<IObjectStorageProvider>(name);
        throw new InvalidOperationException($"Storage authority '{authorityId}' has no configured provider mapping.");
    }
}
