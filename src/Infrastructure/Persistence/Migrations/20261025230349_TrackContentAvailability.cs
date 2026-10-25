using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackContentAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ObjectAssetContentState",
                schema: "osa",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageAuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BucketName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ObjectKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestedVersion = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ExpectedHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpectedSize = table.Column<long>(type: "bigint", nullable: false),
                    Availability = table.Column<int>(type: "int", nullable: false),
                    CheckedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MissingSinceUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MissingReason = table.Column<int>(type: "int", nullable: true),
                    AvailabilityLabel = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true, computedColumnSql: "(CASE [Availability] WHEN 0 THEN N'Unknown' WHEN 1 THEN N'Available' WHEN 2 THEN N'Missing' ELSE NULL END)", stored: true),
                    MissingReasonLabel = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true, computedColumnSql: "(CASE [MissingReason] WHEN 1 THEN N'ObjectNotFound' WHEN 2 THEN N'VersionNotFound' ELSE NULL END)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssetContentState", x => x.AssetId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetContentState])
                    THROW 51000, 'OSA downgrade with content availability observations is blocked. Restore a verified backup.', 1;
                """);
            migrationBuilder.DropTable(
                name: "ObjectAssetContentState",
                schema: "osa");
        }
    }
}
