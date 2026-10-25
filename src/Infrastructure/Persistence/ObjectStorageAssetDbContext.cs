using System.Reflection;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Helpers;
using Elf.ObjectStorageAsset.Infrastructure.Persistence.Configurations;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

public sealed partial class ObjectStorageAssetDbContext : DbContext
{
    public const string DefaultSchemaName = "osa";
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";
    private readonly string _schemaName;
    private readonly Guid _primaryAuthorityId;

    public ObjectStorageAssetDbContext(
        DbContextOptions<ObjectStorageAssetDbContext> options,
        ObjectStorageAssetSchema? schema = null)
        : base(options)
    {
        _schemaName = schema?.Name ?? DefaultSchemaName;
    }

    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public ObjectStorageAssetDbContext(DbContextOptions<ObjectStorageAssetDbContext> options,
        ObjectStorageAssetSchema schema, Elf.ObjectStorageAsset.Application.ObjectStorageAssetRuntimeOptions? runtimeOptions = null)
        : this(options, schema) => _primaryAuthorityId = runtimeOptions?.StorageAuthorityId ?? Guid.Empty;

    internal string SchemaName => _schemaName;

    public DbSet<ObjectAsset> ObjectAssets => Set<ObjectAsset>();
    public DbSet<PermanentObjectAsset> PermanentAssets => Set<PermanentObjectAsset>();
    public DbSet<TemporaryObjectAsset> TemporaryAssets => Set<TemporaryObjectAsset>();
    public DbSet<ArchivedObjectAsset> ArchivedAssets => Set<ArchivedObjectAsset>();
    public DbSet<ObjectAssetIdentity> AssetIdentities => Set<ObjectAssetIdentity>();
    public DbSet<ObjectAssetNamingProfileRecord> NamingProfiles => Set<ObjectAssetNamingProfileRecord>();
    public DbSet<ObjectAssetUpgradeState> UpgradeState => Set<ObjectAssetUpgradeState>();

    // Only bracket-escaped schema identifiers are interpolated; all data values are SqlParameters.
#pragma warning disable EF1002
    /// <summary>Tracked lookups reserve mutation intent; use tracking=false for a metadata-only lookup.</summary>
    public async Task<ObjectAsset?> FindAssetAsync(Guid assetId, bool tracking = true, CancellationToken cancellationToken = default)
        => await FindAssetCoreAsync(assetId, tracking, tracking, cancellationToken).ConfigureAwait(false);

    /// <summary>Loads a mutable asset while reserving its identity until the caller's transaction completes.</summary>
    public Task<ObjectAsset?> FindAssetForUpdateAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("A mutation lookup requires an explicit OSA transaction.");
        return FindAssetCoreAsync(assetId, true, true, cancellationToken);
    }

    private async Task<ObjectAsset?> FindAssetCoreAsync(Guid assetId, bool tracking, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var transaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken) : null;
        var table = "[" + _schemaName.Replace("]", "]]", StringComparison.Ordinal) + "].[ObjectAssetIdentity]";
        // Tracked lookups are mutation lookups. The public metadata readers use tracking=false.
        // Reserve U before loading the payload, avoiding competing S-to-X conversions in caller transactions.
        var hint = forUpdate ? "UPDLOCK,HOLDLOCK" : "HOLDLOCK";
        var identity = await AssetIdentities.FromSqlRaw($"SELECT * FROM {table} WITH ({hint}) WHERE [AssetId]=@id",
            new SqlParameter("@id", assetId)).AsNoTracking().SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<ObjectAsset> query = identity?.Location switch
        {
            ObjectAssetLocation.Main => PermanentAssets,
            ObjectAssetLocation.Temporary => TemporaryAssets,
            ObjectAssetLocation.Archive => ArchivedAssets,
            _ => ObjectAssets.Where(_ => false)
        };
        if (!tracking) query = query.AsNoTracking();
        var asset = identity is null ? null : await query.SingleOrDefaultAsync(x => x.Id == assetId, cancellationToken);
        if (identity is not null && asset is null) throw new InvalidOperationException("Asset identity and payload location disagree.");
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return asset;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) => SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        foreach (var added in ChangeTracker.Entries<ObjectAsset>().Where(e => e.State == EntityState.Added
            && e.Entity.GetType() != StorageType(e.Entity)).ToArray())
        {
            var replacement = added.Entity.CopyToLocation(Location(added.Entity));
            added.State = EntityState.Detached;
            ObjectAssets.Add(replacement);
        }
        var entries = ChangeTracker.Entries<ObjectAsset>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToArray();
        var moves = entries.Where(e => e.State == EntityState.Modified && e.Entity.GetType() != StorageType(e.Entity)).ToArray();
        if (moves.Length > 0 && !acceptAllChangesOnSuccess)
            throw new NotSupportedException("Lifecycle table moves require acceptAllChangesOnSuccess=true.");
        // Protect every payload write, including a pending-delete update preceding a move.
        // Otherwise a reader can hold S(identity), wait for X(payload), and block the writer's identity update.
        await using var transaction = entries.Length > 0
            ? await BeginAssetSaveAsync(entries.Select(e => e.Entity.Id), cancellationToken).ConfigureAwait(false) : null;
        foreach (var entry in entries.Where(e => e.State == EntityState.Added))
            AssetIdentities.Add(new ObjectAssetIdentity
            {
                AssetId = entry.Entity.Id,
                Location = Location(entry.Entity),
                StorageAuthorityId = CanonicalAuthority(entry.Entity.StorageAuthorityId),
                BucketName = entry.Entity.BucketName,
                ObjectKey = entry.Entity.ObjectKey
            });
        foreach (var entry in entries.Where(e => e.State == EntityState.Modified && e.Property(x => x.StorageAuthorityId).IsModified))
        {
            var identity = await AssetIdentities.FindAsync([entry.Entity.Id], cancellationToken)
                ?? throw new InvalidOperationException("Asset identity is missing.");
            identity.StorageAuthorityId = CanonicalAuthority(entry.Entity.StorageAuthorityId);
        }
        foreach (var entry in moves) entry.State = EntityState.Deleted;
        var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var entry in moves)
        {
            var location = Location(entry.Entity);
            var replacement = entry.Entity.CopyToLocation(location);
            entry.State = EntityState.Detached;
            ObjectAssets.Add(replacement);
            var identity = await AssetIdentities.FindAsync([replacement.Id], cancellationToken)
                ?? throw new InvalidOperationException("Asset identity is missing during movement.");
            identity.Location = location;
        }
        if (moves.Length > 0) count += await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return count;
    }
#pragma warning restore EF1002

    private static ObjectAssetLocation Location(ObjectAsset asset) => asset.Status == ObjectAssetStatus.Deleted
        ? ObjectAssetLocation.Archive : asset.TemporaryBindingId.HasValue ? ObjectAssetLocation.Temporary : ObjectAssetLocation.Main;
    private Guid CanonicalAuthority(Guid authorityId) => authorityId == _primaryAuthorityId ? Guid.Empty : authorityId;
    private static Type StorageType(ObjectAsset asset) => Location(asset) switch
    {
        ObjectAssetLocation.Archive => typeof(ArchivedObjectAsset),
        ObjectAssetLocation.Temporary => typeof(TemporaryObjectAsset),
        _ => typeof(PermanentObjectAsset)
    };

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ObjectStorageAssetModelCacheKeyFactory>();
        optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsSqlGenerator, ObjectAssetMigrationsSqlGenerator>();
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(_schemaName);
        modelBuilder.ApplyConfiguration(new ObjectAssetConfiguration());
        modelBuilder.Entity<PermanentObjectAsset>().ToTable("ObjectAssets");
        modelBuilder.Entity<TemporaryObjectAsset>().ToTable("ObjectAssetTemporary");
        modelBuilder.Entity<ArchivedObjectAsset>().ToTable("ObjectAssetArchive");
        modelBuilder.Entity<ArchivedObjectAsset>().HasIndex(x => new { x.DeletedAtUtc, x.Id });
        modelBuilder.Entity<ObjectAssetIdentity>(b =>
        {
            b.ToTable("ObjectAssetIdentity");
            b.HasKey(x => x.AssetId);
            b.Property(x => x.AssetId).ValueGeneratedNever();
            b.Property(x => x.BucketName).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            b.Property(x => x.ObjectKey).HasMaxLength(512).UseCollation("Latin1_General_100_BIN2");
            b.HasIndex(x => new { x.StorageAuthorityId, x.BucketName, x.ObjectKey }).IsUnique();
        });
        modelBuilder.Entity<ObjectAssetNamingProfileRecord>(b =>
        {
            b.ToTable("ObjectAssetNamingProfiles"); b.HasKey(x => new { x.ProfileId, x.Version });
            b.Property(x => x.ProfileId).HasMaxLength(128); b.Property(x => x.DefinitionHash).HasMaxLength(64);
            b.Property(x => x.Template).HasMaxLength(1024);
        });
        modelBuilder.Entity<ObjectAssetUpgradeState>(b =>
        {
            b.ToTable("ObjectAssetUpgradeState"); b.HasKey(x => x.Name); b.Property(x => x.Name).HasMaxLength(128);
        });
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
