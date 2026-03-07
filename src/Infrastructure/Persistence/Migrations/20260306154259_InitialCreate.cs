using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "osa");

            migrationBuilder.CreateTable(
                name: "ObjectAssets",
                schema: "osa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OwnerKeyKind = table.Column<int>(type: "int", nullable: false),
                    OwnerKeyInt64 = table.Column<long>(type: "bigint", nullable: true),
                    OwnerKeyGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OwnerKeyText = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SlotName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SlotMultiplicity = table.Column<int>(type: "int", nullable: false),
                    BucketName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StorageNamespace = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ObjectKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ETag = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProviderVersionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PhysicalDeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStatusChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OwnerKeyKindLabel = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true, computedColumnSql: "(CASE [OwnerKeyKind] WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)", stored: true),
                    SlotMultiplicityLabel = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true, computedColumnSql: "(CASE [SlotMultiplicity] WHEN 1 THEN N'Single' WHEN 2 THEN N'Many' ELSE NULL END)", stored: true),
                    StatusLabel = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true, computedColumnSql: "(CASE [Status] WHEN 1 THEN N'PendingUpload' WHEN 2 THEN N'Active' WHEN 3 THEN N'UploadFailed' WHEN 4 THEN N'PendingDelete' WHEN 5 THEN N'Deleted' WHEN 6 THEN N'DeleteFailed' ELSE NULL END)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_ObjectKey",
                schema: "osa",
                table: "ObjectAssets",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "OwnerKeyKind", "OwnerKeyInt64", "OwnerKeyGuid", "OwnerKeyText", "SlotName" },
                unique: true,
                filter: "[SlotMultiplicity] = 1 AND [Status] IN (1, 2, 3, 4, 6)");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName_Status",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "OwnerType", "OwnerKeyKind", "OwnerKeyInt64", "OwnerKeyGuid", "OwnerKeyText", "SlotName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_Status_ExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ObjectAssets",
                schema: "osa");
        }
    }
}
