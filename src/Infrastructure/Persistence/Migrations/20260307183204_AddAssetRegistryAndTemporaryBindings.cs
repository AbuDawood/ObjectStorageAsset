using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetRegistryAndTemporaryBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.AddColumn<int>(
                name: "OwnershipMode",
                schema: "osa",
                table: "ObjectAssets",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TemporaryBindingExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemporaryBindingId",
                schema: "osa",
                table: "ObjectAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OwnerKeyKindLabel",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                computedColumnSql: "(CASE [OwnerKeyKind] WHEN 0 THEN N'Unassigned' WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(6)",
                oldMaxLength: 6,
                oldNullable: true,
                oldComputedColumnSql: "(CASE [OwnerKeyKind] WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)",
                oldStored: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipModeLabel",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                computedColumnSql: "(CASE [OwnershipMode] WHEN 1 THEN N'Managed' WHEN 2 THEN N'Referenced' ELSE NULL END)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName_TemporaryBindingId",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "OwnerKeyKind", "OwnerKeyInt64", "OwnerKeyGuid", "OwnerKeyText", "SlotName", "TemporaryBindingId" },
                unique: true,
                filter: "[SlotMultiplicity] = 1 AND [Status] IN (1, 2, 3, 4, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_Status_OwnershipMode",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "Status", "OwnershipMode" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_Status_TemporaryBindingExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "Status", "TemporaryBindingExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_TemporaryBindingId_Status",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "TemporaryBindingId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName_TemporaryBindingId",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_Status_OwnershipMode",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_Status_TemporaryBindingExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_TemporaryBindingId_Status",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "OwnershipModeLabel",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "OwnershipMode",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "TemporaryBindingExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "TemporaryBindingId",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.AlterColumn<string>(
                name: "OwnerKeyKindLabel",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: true,
                computedColumnSql: "(CASE [OwnerKeyKind] WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)",
                stored: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10,
                oldNullable: true,
                oldComputedColumnSql: "(CASE [OwnerKeyKind] WHEN 0 THEN N'Unassigned' WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)",
                oldStored: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "OwnerKeyKind", "OwnerKeyInt64", "OwnerKeyGuid", "OwnerKeyText", "SlotName" },
                unique: true,
                filter: "[SlotMultiplicity] = 1 AND [Status] IN (1, 2, 3, 4, 6)");
        }
    }
}
