using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Helpers;
using Elf.ObjectStorageAsset.Infrastructure.Persistence.Configurations;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

public sealed class ObjectStorageAssetDbContext : DbContext
{
    public const string DefaultSchemaName = "osa";
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";
    private readonly string _schemaName;

    public ObjectStorageAssetDbContext(
        DbContextOptions<ObjectStorageAssetDbContext> options,
        ObjectStorageAssetSchema? schema = null)
        : base(options)
    {
        _schemaName = schema?.Name ?? DefaultSchemaName;
    }

    internal string SchemaName => _schemaName;

    public DbSet<ObjectAsset> ObjectAssets => Set<ObjectAsset>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ObjectStorageAssetModelCacheKeyFactory>();
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(_schemaName);
        modelBuilder.ApplyConfiguration(new ObjectAssetConfiguration());
        ConfigureEnumLabelComputedColumns(modelBuilder, _schemaName);

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureEnumLabelComputedColumns(ModelBuilder modelBuilder, string fallbackSchema)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null || entityType.ClrType == typeof(Dictionary<string, object>))
            {
                continue;
            }

            var schema = entityType.GetSchema() ?? fallbackSchema;
            var storeObject = StoreObjectIdentifier.Table(tableName, schema);
            var entityBuilder = modelBuilder.Entity(entityType.ClrType);

            foreach (var property in entityType.GetProperties().ToList())
            {
                if (!TryGetEnumType(property.ClrType, out var enumType))
                {
                    continue;
                }

                var enumColumnName = property.GetColumnName(storeObject);
                if (string.IsNullOrWhiteSpace(enumColumnName))
                {
                    continue;
                }

                var labelPropertyName = $"{property.Name}Label";
                if (entityType.FindProperty(labelPropertyName) is not null)
                {
                    continue;
                }

                var enumLabelSql = BuildEnumCaseSql(enumType, enumColumnName);
                var maxLength = Enum.GetNames(enumType).Max(x => x.Length);

                entityBuilder.Property<string>(labelPropertyName)
                    .HasMaxLength(maxLength)
                    .HasComputedColumnSql(enumLabelSql, stored: true);
            }
        }
    }

    private static bool TryGetEnumType(Type type, out Type enumType)
    {
        var candidate = Nullable.GetUnderlyingType(type) ?? type;
        if (candidate.IsEnum)
        {
            enumType = candidate;
            return true;
        }

        enumType = typeof(void);
        return false;
    }

    private static string BuildEnumCaseSql(Type enumType, string columnName)
    {
        var buildMethod = typeof(EnumTextSqlCaseBuilder).GetMethod(nameof(EnumTextSqlCaseBuilder.BuildCaseSql));
        if (buildMethod is null)
        {
            throw new InvalidOperationException("EnumTextSqlCaseBuilder.BuildCaseSql<TEnum> was not found.");
        }

        var genericMethod = buildMethod.MakeGenericMethod(enumType);
        var sql = genericMethod.Invoke(null, [columnName]) as string;
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException(
                $"Failed to build computed SQL for enum '{enumType.Name}' and column '{columnName}'.");
        }

        return sql;
    }
}
