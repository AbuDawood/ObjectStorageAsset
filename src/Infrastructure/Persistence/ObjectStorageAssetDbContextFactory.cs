using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

public sealed class ObjectStorageAssetDbContextFactory
    : IDesignTimeDbContextFactory<ObjectStorageAssetDbContext>
{
    public ObjectStorageAssetDbContext CreateDbContext(string[] args)
    {
        var schema = ResolveSchema(args);
        var connectionString = ResolveConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<ObjectStorageAssetDbContext>();
        optionsBuilder.UseSqlServer(
            connectionString,
            sql => sql.MigrationsHistoryTable(
                ObjectStorageAssetDbContext.MigrationsHistoryTableName,
                schema));

        return new ObjectStorageAssetDbContext(
            optionsBuilder.Options,
            new ObjectStorageAssetSchema(schema));
    }

    private static string ResolveSchema(string[] args)
    {
        var index = Array.FindIndex(args, x => string.Equals(x, "--schema", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index < args.Length - 1 && !string.IsNullOrWhiteSpace(args[index + 1]))
        {
            return args[index + 1].Trim();
        }

        return ObjectStorageAssetDbContext.DefaultSchemaName;
    }

    private static string ResolveConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetConnectionString("ObjectStorageAsset")
               ?? "Server=(localdb)\\mssqllocaldb;Database=ObjectStorageAsset;Trusted_Connection=True;TrustServerCertificate=True;";
    }
}
