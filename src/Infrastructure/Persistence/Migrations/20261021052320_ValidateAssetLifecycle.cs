using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ValidateAssetLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AssetLifecycleUpgradeSql.Validate, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AssetLifecycleUpgradeSql.GuardDown);
        }
    }
}
