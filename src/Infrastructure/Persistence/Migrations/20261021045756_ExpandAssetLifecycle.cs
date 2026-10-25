using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAssetLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ObjectAssets_ObjectKey",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.AddColumn<int>(
                name: "BindingKind",
                schema: "osa",
                table: "ObjectAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DeleteAttemptCount",
                schema: "osa",
                table: "ObjectAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "DeleteRetrySuspended",
                schema: "osa",
                table: "ObjectAssets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ExternalOwnerInstanceId",
                schema: "osa",
                table: "ObjectAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOwnerKey",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExternalOwnerKeyKind",
                schema: "osa",
                table: "ObjectAssets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOwnerType",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NamingDefinitionHash",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NamingProfileId",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NamingProfileVersion",
                schema: "osa",
                table: "ObjectAssets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextDeleteAttemptAtUtc",
                schema: "osa",
                table: "ObjectAssets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginAssetId",
                schema: "osa",
                table: "ObjectAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginInstanceId",
                schema: "osa",
                table: "ObjectAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginKind",
                schema: "osa",
                table: "ObjectAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Sha256ValidationActualHash",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Sha256ValidationAttemptedAtUtc",
                schema: "osa",
                table: "ObjectAssets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "Sha256ValidationCompletedAtUtc",
                schema: "osa",
                table: "ObjectAssets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256ValidationError",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256ValidationObjectVersion",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sha256ValidationStatus",
                schema: "osa",
                table: "ObjectAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageAuthorityId",
                schema: "osa",
                table: "ObjectAssets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "BindingKindLabel",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true,
                computedColumnSql: "(CASE [BindingKind] WHEN 0 THEN N'LegacyUnresolved' WHEN 1 THEN N'LocalOwner' WHEN 2 THEN N'ExternalOwner' WHEN 3 THEN N'Temporary' ELSE NULL END)",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOwnerKeyKindLabel",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                computedColumnSql: "(CASE [ExternalOwnerKeyKind] WHEN 0 THEN N'Unassigned' WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginKindLabel",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true,
                computedColumnSql: "(CASE [OriginKind] WHEN 0 THEN N'LegacyUnknown' WHEN 1 THEN N'Local' WHEN 2 THEN N'ImportedFromOsa' ELSE NULL END)",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "Sha256ValidationStatusLabel",
                schema: "osa",
                table: "ObjectAssets",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true,
                computedColumnSql: "(CASE [Sha256ValidationStatus] WHEN 0 THEN N'NotChecked' WHEN 1 THEN N'Valid' WHEN 2 THEN N'Mismatch' WHEN 3 THEN N'Failed' ELSE NULL END)",
                stored: true);

            migrationBuilder.CreateTable(
                name: "ObjectAssetArchive",
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
                    OwnershipMode = table.Column<int>(type: "int", nullable: false),
                    CustomMetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TemporaryBindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemporaryBindingExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PhysicalDeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStatusChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    BindingKind = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OriginKind = table.Column<int>(type: "int", nullable: false),
                    OriginInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalOwnerInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalOwnerType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExternalOwnerKeyKind = table.Column<int>(type: "int", nullable: true),
                    ExternalOwnerKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StorageAuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NamingProfileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    NamingProfileVersion = table.Column<int>(type: "int", nullable: true),
                    NamingDefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Sha256ValidationStatus = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Sha256ValidationAttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Sha256ValidationCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Sha256ValidationActualHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Sha256ValidationObjectVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Sha256ValidationError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DeleteAttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextDeleteAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeleteRetrySuspended = table.Column<bool>(type: "bit", nullable: false),
                    BindingKindLabel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true, computedColumnSql: "(CASE [BindingKind] WHEN 0 THEN N'LegacyUnresolved' WHEN 1 THEN N'LocalOwner' WHEN 2 THEN N'ExternalOwner' WHEN 3 THEN N'Temporary' ELSE NULL END)", stored: true),
                    ExternalOwnerKeyKindLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [ExternalOwnerKeyKind] WHEN 0 THEN N'Unassigned' WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)", stored: true),
                    OriginKindLabel = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true, computedColumnSql: "(CASE [OriginKind] WHEN 0 THEN N'LegacyUnknown' WHEN 1 THEN N'Local' WHEN 2 THEN N'ImportedFromOsa' ELSE NULL END)", stored: true),
                    OwnerKeyKindLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [OwnerKeyKind] WHEN 0 THEN N'Unassigned' WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)", stored: true),
                    OwnershipModeLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [OwnershipMode] WHEN 1 THEN N'Managed' WHEN 2 THEN N'Referenced' ELSE NULL END)", stored: true),
                    Sha256ValidationStatusLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [Sha256ValidationStatus] WHEN 0 THEN N'NotChecked' WHEN 1 THEN N'Valid' WHEN 2 THEN N'Mismatch' WHEN 3 THEN N'Failed' ELSE NULL END)", stored: true),
                    SlotMultiplicityLabel = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true, computedColumnSql: "(CASE [SlotMultiplicity] WHEN 1 THEN N'Single' WHEN 2 THEN N'Many' ELSE NULL END)", stored: true),
                    StatusLabel = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true, computedColumnSql: "(CASE [Status] WHEN 1 THEN N'PendingUpload' WHEN 2 THEN N'Active' WHEN 3 THEN N'UploadFailed' WHEN 4 THEN N'PendingDelete' WHEN 5 THEN N'Deleted' WHEN 6 THEN N'DeleteFailed' ELSE NULL END)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssetArchive", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ObjectAssetIdentity",
                schema: "osa",
                columns: table => new
                {
                    AssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Location = table.Column<int>(type: "int", nullable: false),
                    StorageAuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BucketName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ObjectKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LocationLabel = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true, computedColumnSql: "(CASE [Location] WHEN 0 THEN N'Main' WHEN 1 THEN N'Temporary' WHEN 2 THEN N'Archive' ELSE NULL END)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssetIdentity", x => x.AssetId);
                });

            migrationBuilder.CreateTable(
                name: "ObjectAssetNamingProfiles",
                schema: "osa",
                columns: table => new
                {
                    ProfileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DefinitionFormatVersion = table.Column<int>(type: "int", nullable: false),
                    Template = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    DefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssetNamingProfiles", x => new { x.ProfileId, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "ObjectAssetTemporary",
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
                    OwnershipMode = table.Column<int>(type: "int", nullable: false),
                    CustomMetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TemporaryBindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TemporaryBindingExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PhysicalDeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStatusChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    BindingKind = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OriginKind = table.Column<int>(type: "int", nullable: false),
                    OriginInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalOwnerInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExternalOwnerType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExternalOwnerKeyKind = table.Column<int>(type: "int", nullable: true),
                    ExternalOwnerKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    StorageAuthorityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NamingProfileId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    NamingProfileVersion = table.Column<int>(type: "int", nullable: true),
                    NamingDefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Sha256ValidationStatus = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Sha256ValidationAttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Sha256ValidationCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Sha256ValidationActualHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Sha256ValidationObjectVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Sha256ValidationError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DeleteAttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextDeleteAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeleteRetrySuspended = table.Column<bool>(type: "bit", nullable: false),
                    BindingKindLabel = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true, computedColumnSql: "(CASE [BindingKind] WHEN 0 THEN N'LegacyUnresolved' WHEN 1 THEN N'LocalOwner' WHEN 2 THEN N'ExternalOwner' WHEN 3 THEN N'Temporary' ELSE NULL END)", stored: true),
                    ExternalOwnerKeyKindLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [ExternalOwnerKeyKind] WHEN 0 THEN N'Unassigned' WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)", stored: true),
                    OriginKindLabel = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true, computedColumnSql: "(CASE [OriginKind] WHEN 0 THEN N'LegacyUnknown' WHEN 1 THEN N'Local' WHEN 2 THEN N'ImportedFromOsa' ELSE NULL END)", stored: true),
                    OwnerKeyKindLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [OwnerKeyKind] WHEN 0 THEN N'Unassigned' WHEN 1 THEN N'Int64' WHEN 2 THEN N'Guid' WHEN 3 THEN N'String' ELSE NULL END)", stored: true),
                    OwnershipModeLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [OwnershipMode] WHEN 1 THEN N'Managed' WHEN 2 THEN N'Referenced' ELSE NULL END)", stored: true),
                    Sha256ValidationStatusLabel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true, computedColumnSql: "(CASE [Sha256ValidationStatus] WHEN 0 THEN N'NotChecked' WHEN 1 THEN N'Valid' WHEN 2 THEN N'Mismatch' WHEN 3 THEN N'Failed' ELSE NULL END)", stored: true),
                    SlotMultiplicityLabel = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true, computedColumnSql: "(CASE [SlotMultiplicity] WHEN 1 THEN N'Single' WHEN 2 THEN N'Many' ELSE NULL END)", stored: true),
                    StatusLabel = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: true, computedColumnSql: "(CASE [Status] WHEN 1 THEN N'PendingUpload' WHEN 2 THEN N'Active' WHEN 3 THEN N'UploadFailed' WHEN 4 THEN N'PendingDelete' WHEN 5 THEN N'Deleted' WHEN 6 THEN N'DeleteFailed' ELSE NULL END)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssetTemporary", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ObjectAssetUpgradeState",
                schema: "osa",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProcessedCount = table.Column<long>(type: "bigint", nullable: false),
                    Completed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectAssetUpgradeState", x => x.Name);
                });

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
                name: "IX_ObjectAssets_Status_NextDeleteAttemptAtUtc_DeleteRetrySuspended",
                schema: "osa",
                table: "ObjectAssets",
                columns: new[] { "Status", "NextDeleteAttemptAtUtc", "DeleteRetrySuspended" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName_Status",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "OwnerType", "OwnerKeyKind", "OwnerKeyInt64", "OwnerKeyGuid", "OwnerKeyText", "SlotName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName_TemporaryBindingId",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "OwnerType", "OwnerKeyKind", "OwnerKeyInt64", "OwnerKeyGuid", "OwnerKeyText", "SlotName", "TemporaryBindingId" },
                unique: true,
                filter: "[SlotMultiplicity] = 1 AND [Status] IN (1, 2, 3, 4, 6)");

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

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_Status_ExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_Status_NextDeleteAttemptAtUtc_DeleteRetrySuspended",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "Status", "NextDeleteAttemptAtUtc", "DeleteRetrySuspended" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_Status_OwnershipMode",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "Status", "OwnershipMode" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_Status_TemporaryBindingExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "Status", "TemporaryBindingExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetArchive_TemporaryBindingId_Status",
                schema: "osa",
                table: "ObjectAssetArchive",
                columns: new[] { "TemporaryBindingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetIdentity_StorageAuthorityId_BucketName_ObjectKey",
                schema: "osa",
                table: "ObjectAssetIdentity",
                columns: new[] { "StorageAuthorityId", "BucketName", "ObjectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName_Status",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "OwnerType", "OwnerKeyKind", "OwnerKeyInt64", "OwnerKeyGuid", "OwnerKeyText", "SlotName", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_OwnerType_OwnerKeyKind_OwnerKeyInt64_OwnerKeyGuid_OwnerKeyText_SlotName_TemporaryBindingId",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "OwnerType", "OwnerKeyKind", "OwnerKeyInt64", "OwnerKeyGuid", "OwnerKeyText", "SlotName", "TemporaryBindingId" },
                unique: true,
                filter: "[SlotMultiplicity] = 1 AND [Status] IN (1, 2, 3, 4, 6)");

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
                name: "IX_ObjectAssetTemporary_Status_ExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_Status_NextDeleteAttemptAtUtc_DeleteRetrySuspended",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "Status", "NextDeleteAttemptAtUtc", "DeleteRetrySuspended" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_Status_OwnershipMode",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "Status", "OwnershipMode" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_Status_TemporaryBindingExpiresAtUtc",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "Status", "TemporaryBindingExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssetTemporary_TemporaryBindingId_Status",
                schema: "osa",
                table: "ObjectAssetTemporary",
                columns: new[] { "TemporaryBindingId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AssetLifecycleUpgradeSql.GuardDown);
            migrationBuilder.DropTable(
                name: "ObjectAssetArchive",
                schema: "osa");

            migrationBuilder.DropTable(
                name: "ObjectAssetIdentity",
                schema: "osa");

            migrationBuilder.DropTable(
                name: "ObjectAssetNamingProfiles",
                schema: "osa");

            migrationBuilder.DropTable(
                name: "ObjectAssetTemporary",
                schema: "osa");

            migrationBuilder.DropTable(
                name: "ObjectAssetUpgradeState",
                schema: "osa");

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
                name: "IX_ObjectAssets_Status_NextDeleteAttemptAtUtc_DeleteRetrySuspended",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "BindingKindLabel",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "ExternalOwnerKeyKindLabel",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "OriginKindLabel",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "Sha256ValidationStatusLabel",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "BindingKind",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "DeleteAttemptCount",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "DeleteRetrySuspended",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "ExternalOwnerInstanceId",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "ExternalOwnerKey",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "ExternalOwnerKeyKind",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "ExternalOwnerType",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "NamingDefinitionHash",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "NamingProfileId",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "NamingProfileVersion",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "NextDeleteAttemptAtUtc",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "OriginAssetId",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "OriginInstanceId",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "OriginKind",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "Sha256ValidationActualHash",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "Sha256ValidationAttemptedAtUtc",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "Sha256ValidationCompletedAtUtc",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "Sha256ValidationError",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "Sha256ValidationObjectVersion",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "Sha256ValidationStatus",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.DropColumn(
                name: "StorageAuthorityId",
                schema: "osa",
                table: "ObjectAssets");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectAssets_ObjectKey",
                schema: "osa",
                table: "ObjectAssets",
                column: "ObjectKey",
                unique: true);
        }
    }
}
