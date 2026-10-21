using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RememberPhysicalDeleteIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PhysicalDeletionRequested",
                schema: "osa",
                table: "ObjectAssetTemporary",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhysicalDeletionRequested",
                schema: "osa",
                table: "ObjectAssets",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhysicalDeletionRequested",
                schema: "osa",
                table: "ObjectAssetArchive",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AssetLifecycleUpgradeSql.GuardDown);
            migrationBuilder.DropColumn(
                name: "PhysicalDeletionRequested",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropColumn(
                name: "PhysicalDeletionRequested",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "PhysicalDeletionRequested",
                schema: "osa",
                table: "ObjectAssetArchive");
        }
    }
}
