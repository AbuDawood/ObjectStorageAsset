using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence;

/// <summary>Maps the library's historical logical schema to the configured schema.</summary>
public sealed class ObjectAssetMigrationsSqlGenerator(MigrationsSqlGeneratorDependencies dependencies, ICommandBatchPreparer preparer)
    : SqlServerMigrationsSqlGenerator(dependencies, preparer)
{
    public override IReadOnlyList<MigrationCommand> Generate(IReadOnlyList<MigrationOperation> operations, IModel? model = null,
        MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
    {
        var schema = ((ObjectStorageAssetDbContext)Dependencies.CurrentContext.Context).SchemaName;
        var changes = new List<(object Target, PropertyInfo Property, object? Original)>();
        try
        {
            foreach (var operation in operations) Rewrite(operation, schema, changes);
            return base.Generate(operations, model, options);
        }
        finally { foreach (var change in changes) change.Property.SetValue(change.Target, change.Original); }
    }

    private static void Rewrite(MigrationOperation operation, string schema, List<(object, PropertyInfo, object?)> changes)
    {
        if (operation is SqlOperation sql)
        {
            changes.Add((sql, typeof(SqlOperation).GetProperty(nameof(SqlOperation.Sql))!, sql.Sql));
            sql.Sql = sql.Sql.Replace("[__OSA_SCHEMA__]", "[" + schema.Replace("]", "]]") + "]", StringComparison.Ordinal)
                .Replace("__OSA_SCHEMA_LITERAL__", schema.Replace("'", "''"), StringComparison.Ordinal);
        }
        foreach (var property in operation.GetType().GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
            var value = property.GetValue(operation);
            if (property.CanWrite && (property.Name is "Schema" or "PrincipalSchema" or "NewSchema"
                || operation is EnsureSchemaOperation && property.Name == "Name")
                && value is string text && text == ObjectStorageAssetDbContext.DefaultSchemaName)
            {
                changes.Add((operation, property, value)); property.SetValue(operation, schema);
            }
            else if (value is MigrationOperation child) Rewrite(child, schema, changes);
            else if (value is IEnumerable<MigrationOperation> children)
                foreach (var nested in children) Rewrite(nested, schema, changes);
        }
    }
}
