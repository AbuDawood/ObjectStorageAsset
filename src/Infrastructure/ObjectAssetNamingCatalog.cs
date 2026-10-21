using System.Collections.Concurrent;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Transactions;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetNamingCatalog(IServiceScopeFactory scopes)
{
    private readonly ConcurrentDictionary<string, string> _registered = new();
    private readonly SemaphoreSlim _gate = new(1);
    public async Task RegisterAsync(ObjectKeyNamingProfile profile, CancellationToken ct)
    {
        profile.Validate();
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var connection = db.Database.GetDbConnection();
        var key = $"{connection.DataSource}|{connection.Database}|{db.SchemaName}|{profile.ProfileId}|{profile.Version}";
        if (_registered.TryGetValue(key, out var known)) { Check(known, profile); return; }
        await _gate.WaitAsync(ct);
        try
        {
            if (_registered.TryGetValue(key, out known)) { Check(known, profile); return; }
            var row = await db.NamingProfiles.FindAsync([profile.ProfileId, profile.Version], ct);
            if (row is null)
            {
                row = new ObjectAssetNamingProfileRecord
                {
                    ProfileId = profile.ProfileId,
                    Version = profile.Version,
                    Template = profile.Template,
                    DefinitionHash = profile.DefinitionHash,
                    DefinitionFormatVersion = profile.DefinitionFormatVersion
                };
                db.NamingProfiles.Add(row);
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    db.Entry(row).State = EntityState.Detached;
                    row = await db.NamingProfiles.FindAsync([profile.ProfileId, profile.Version], ct)
                        ?? throw new InvalidOperationException("Naming profile registration failed.");
                }
            }
            Check(row.DefinitionHash, profile);
            suppression.Complete();
            _registered[key] = row.DefinitionHash;
        }
        finally { _gate.Release(); }
    }
    private static void Check(string known, ObjectKeyNamingProfile profile)
    {
        if (!string.Equals(known, profile.DefinitionHash, StringComparison.Ordinal))
            throw new InvalidOperationException("Naming profile/version is immutable; choose a new version.");
    }
}
