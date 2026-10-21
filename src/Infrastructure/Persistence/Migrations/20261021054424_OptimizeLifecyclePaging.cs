using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeLifecyclePaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_ExternalOwnerInstanceId_ExternalOwnerType_ExternalOwnerKeyKind_ExternalOwnerKey_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "ExternalOwnerInstanceId", "ExternalOwnerType", "ExternalOwnerKeyKind", "ExternalOwnerKey", "Status", "CreatedAtUtc", "Id" },
                filter: "[BindingKind]=2 AND [Status]=2");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_Status_LastStatusChangedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "Status", "LastStatusChangedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_TemporaryBindingId_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "TemporaryBindingId", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_ExternalOwnerInstanceId_ExternalOwnerType_ExternalOwnerKeyKind_ExternalOwnerKey_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "ExternalOwnerInstanceId", "ExternalOwnerType", "ExternalOwnerKeyKind", "ExternalOwnerKey", "Status", "CreatedAtUtc", "Id" },
                filter: "[BindingKind]=2 AND [Status]=2");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_Status_LastStatusChangedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "Status", "LastStatusChangedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_TemporaryBindingId_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "TemporaryBindingId", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_ExternalOwnerInstanceId_ExternalOwnerType_ExternalOwnerKeyKind_ExternalOwnerKey_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "ExternalOwnerInstanceId", "ExternalOwnerType", "ExternalOwnerKeyKind", "ExternalOwnerKey", "Status", "CreatedAtUtc", "Id" },
                filter: "[BindingKind]=2 AND [Status]=2");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_Status_LastStatusChangedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "Status", "LastStatusChangedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_TemporaryBindingId_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "TemporaryBindingId", "Status", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_ExternalOwnerInstanceId_ExternalOwnerType_ExternalOwnerKeyKind_ExternalOwnerKey_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_Status_LastStatusChangedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_TemporaryBindingId_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_ExternalOwnerInstanceId_ExternalOwnerType_ExternalOwnerKeyKind_ExternalOwnerKey_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_Status_LastStatusChangedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_TemporaryBindingId_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_ExternalOwnerInstanceId_ExternalOwnerType_ExternalOwnerKeyKind_ExternalOwnerKey_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_Status_LastStatusChangedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_TemporaryBindingId_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");
        }
    }
}
