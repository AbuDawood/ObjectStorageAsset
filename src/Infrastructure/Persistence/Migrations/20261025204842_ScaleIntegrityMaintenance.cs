using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScaleIntegrityMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAtUtc",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SynchronizationError",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ObjectAssetMaintenanceState",
                schema: "osa",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NextRunAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    LastSucceededAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PendingEvidenceCount = table.Column<long>(type: "bigint", nullable: true),
                    OldestPendingEvidenceAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiredEvidenceCount = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssetMaintenanceState", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetIntegrityEvidence_NextAttemptAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence",
                columns: new[] { "NextAttemptAtUtc", "Id" },
                filter: "[ResolvedAtUtc] IS NULL")
                .Annotation("SqlServer:Include", new[] { "AssetId", "AttemptedAtUtc" });
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
                name: "ObjectAssetMaintenanceState",
                schema: "osa");

            migrationBuilder.DropIndex(
                name: "IX_ObjectAssetIntegrityEvidence_NextAttemptAtUtc_Id",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence");

            migrationBuilder.DropColumn(
                name: "SynchronizationError",
                schema: "osa",
                table: "ObjectAssetIntegrityEvidence");
        }
    }
}
