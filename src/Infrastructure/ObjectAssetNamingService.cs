using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetNamingService(ObjectAssetNamingCatalog catalog,
    ObjectStorageAssetRuntimeOptions options) : IObjectAssetNamingService
{
    public async Task ActivateAsync(ObjectKeyNamingProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await catalog.RegisterAsync(profile, cancellationToken);
        options.Naming.ActiveProfile = profile;
    }
}
