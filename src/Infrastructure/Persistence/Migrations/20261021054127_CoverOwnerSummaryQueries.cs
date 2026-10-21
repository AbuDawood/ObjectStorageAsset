using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CoverOwnerSummaryQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyGuid", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyInt64", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyText", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyGuid", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyInt64", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyText", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyGuid", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyInt64", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyText", "Status", "CreatedAtUtc", "Id" })
                .Annotation("SqlServer:Include", new[] { "BindingKind", "OwnerKeyKind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyGuid", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyInt64", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyText", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyGuid", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyInt64", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyText", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyGuid_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyGuid", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyInt64_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyInt64", "Status", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_OwnerType_SlotName_OwnerKeyText_Status_CreatedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "OwnerType", "SlotName", "OwnerKeyText", "Status", "CreatedAtUtc", "Id" });
        }
    }
}
