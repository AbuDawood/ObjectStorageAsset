using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

/// <summary>Resolves library-owned migrations and their logical schema in the current context's schema.</summary>
public sealed class ObjectAssetMigrationsAssembly(ICurrentDbContext currentContext, IMigrationsIdGenerator ids) : IMigrationsAssembly
{
    private IReadOnlyDictionary<string, TypeInfo>? _migrations;
    private ModelSnapshot? _snapshot;
    public Assembly Assembly => typeof(ObjectStorageAssetDbContext).Assembly;

    public IReadOnlyDictionary<string, TypeInfo> Migrations => _migrations ??= Assembly.DefinedTypes
        .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type)
            && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == typeof(ObjectStorageAssetDbContext))
        .Select(type => (Type: type, Id: type.GetCustomAttribute<MigrationAttribute>()?.Id))
        .Where(item => item.Id is not null).OrderBy(item => item.Id, StringComparer.Ordinal)
        .ToDictionary(item => item.Id!, item => item.Type, StringComparer.OrdinalIgnoreCase);

    public ModelSnapshot ModelSnapshot => _snapshot ??= ResolveSnapshot();

    private ModelSnapshot ResolveSnapshot()
    {
        // Keep the generated snapshot's type/name and all published migration files intact. This
        // context-scoped instance uses the same schema substitution as migration SQL generation.
        var snapshot = new Migrations.ObjectStorageAssetDbContextModelSnapshot();
        var model = (IMutableModel)snapshot.Model;
        var schema = ((ObjectStorageAssetDbContext)currentContext.Context).SchemaName;
        model.SetDefaultSchema(schema);
        foreach (var entity in model.GetEntityTypes())
            if (entity.GetSchema() == ObjectStorageAssetDbContext.DefaultSchemaName) entity.SetSchema(schema);
        return snapshot;
    }

    public string? FindMigrationId(string nameOrId) => Migrations.Keys.FirstOrDefault(id =>
        string.Equals(ids.IsValidId(nameOrId) ? id : ids.GetName(id), nameOrId, StringComparison.OrdinalIgnoreCase));

    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        var migration = (Migration)Activator.CreateInstance(migrationClass.AsType())!;
        migration.ActiveProvider = activeProvider;
        return migration;
    }
}
