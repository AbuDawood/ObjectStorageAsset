using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

internal sealed class ObjectStorageAssetModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        if (context is not ObjectStorageAssetDbContext assetContext)
        {
            return (context.GetType(), designTime);
        }

        return (context.GetType(), assetContext.SchemaName, designTime);
    }
}
