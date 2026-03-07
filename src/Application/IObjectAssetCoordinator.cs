using Microsoft.EntityFrameworkCore;

namespace Elf.ObjectStorageAsset.Application;

/// <summary>
/// Coordinates host database changes with staged object asset operations.
/// </summary>
public interface IObjectAssetCoordinator
{
    Task<int> SaveChangesWithAssetsAsync(
        DbContext hostDbContext,
        CancellationToken cancellationToken = default);
}
