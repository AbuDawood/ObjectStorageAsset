using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReserveAssetIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The following data operation commits batches before EF writes this migration's history row.
            // Keep this scaffolded index creation rerunnable if a later batch fails.
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(QUOTENAME(N'__OSA_SCHEMA_LITERAL__') + N'.[ObjectAssetArchive]')
                    AND [name]=N'IX_ObjectAssetArchive_DeletedAtUtc_Id')
                    CREATE INDEX [IX_ObjectAssetArchive_DeletedAtUtc_Id] ON [__OSA_SCHEMA__].[ObjectAssetArchive] ([DeletedAtUtc],[Id]);
                """);
            migrationBuilder.Sql(AssetLifecycleUpgradeSql.Reserve, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AssetLifecycleUpgradeSql.GuardDown);
            migrationBuilder.Sql("DELETE FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState];");
            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_DeletedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");
        }
    }
}
