using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal interface IObjectAssetMaintenanceSchedule
{
    Task<Dictionary<string, ObjectAssetMaintenanceState>> LoadAsync(string[] names, CancellationToken ct);
    Task SaveAsync(ObjectAssetMaintenanceState state, CancellationToken ct);
}

internal sealed class ObjectAssetMaintenanceSchedule(ObjectStorageAssetDbContext db) : IObjectAssetMaintenanceSchedule
{
    public Task<Dictionary<string, ObjectAssetMaintenanceState>> LoadAsync(string[] names, CancellationToken ct)
        => db.MaintenanceState.Where(x => names.Contains(x.Name)).ToDictionaryAsync(x => x.Name, ct);

    public async Task SaveAsync(ObjectAssetMaintenanceState state, CancellationToken ct)
    {
        if (db.Entry(state).State == EntityState.Detached) db.MaintenanceState.Add(state);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
