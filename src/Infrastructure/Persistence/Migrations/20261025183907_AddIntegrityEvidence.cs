using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrityEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ObjectAssetIntegrityEvidence",
                schema: "osa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageAuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BucketName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ObjectKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ExpectedHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpectedSize = table.Column<long>(type: "bigint", nullable: false),
                    RequestedVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ActualHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ObjectVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    StatusLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [Status] WHEN 0 THEN N'NotChecked' WHEN 1 THEN N'Valid' WHEN 2 THEN N'Mismatch' WHEN 3 THEN N'Failed' ELSE NULL END)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssetIntegrityEvidence", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetIntegrityEvidence_AssetId_AttemptedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence",
                columns: new[] { "AssetId", "AttemptedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetIntegrityEvidence_ResolvedAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence",
                columns: new[] { "ResolvedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AssetLifecycleUpgradeSql.GuardDown);
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetIntegrityEvidence])
                    THROW 51000, 'OSA downgrade with validation evidence is blocked. Restore a verified backup.', 1;
                """);
            migrationBuilder.DropTable(
                name: "ObjectAssetIntegrityEvidence",
                schema: "osa");
        }
    }
}
