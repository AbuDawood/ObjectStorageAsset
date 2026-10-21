namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Migrations;

// Frozen against the EF-generated ExpandAssetLifecycle migration, not the current runtime model.
// Schema tokens are resolved and escaped by ObjectAssetMigrationsSqlGenerator.
internal static class AssetLifecycleUpgradeSql
{
    private const string Columns = """
        [Id],[OwnerType],[OwnerKeyKind],[OwnerKeyInt64],[OwnerKeyGuid],[OwnerKeyText],[SlotName],[SlotMultiplicity],
        [BucketName],[StorageNamespace],[ObjectKey],[OriginalFileName],[Extension],[ContentType],[SizeBytes],[Sha256],
        [ETag],[ProviderVersionId],[OwnershipMode],[CustomMetadataJson],[Status],[TemporaryBindingId],
        [TemporaryBindingExpiresAtUtc],[ExpiresAtUtc],[DeletedAtUtc],[PhysicalDeletedAtUtc],[ErrorMessage],
        [CreatedAtUtc],[UploadedAtUtc],[LastStatusChangedAtUtc],[BindingKind],[OriginKind],[OriginInstanceId],
        [OriginAssetId],[ExternalOwnerInstanceId],[ExternalOwnerType],[ExternalOwnerKeyKind],[ExternalOwnerKey],
        [StorageAuthorityId],[NamingProfileId],[NamingProfileVersion],[NamingDefinitionHash],[Sha256ValidationStatus],
        [Sha256ValidationAttemptedAtUtc],[Sha256ValidationCompletedAtUtc],[Sha256ValidationActualHash],
        [Sha256ValidationObjectVersion],[Sha256ValidationError],[DeleteAttemptCount],[NextDeleteAttemptAtUtc],[DeleteRetrySuspended]
        """;

    private static string Locked(string body) => """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        IF @@TRANCOUNT <> 0 THROW 51000, 'OSA data upgrade requires committed batches outside an enclosing transaction.', 1;
        DECLARE @osaLockResult int, @osaResource nvarchar(255) = N'OSA:Upgrade:' + N'__OSA_SCHEMA_LITERAL__';
        EXEC @osaLockResult = sys.sp_getapplock @Resource=@osaResource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0;
        IF @osaLockResult < 0 THROW 51000, 'Another OSA upgrade runner is active.', 1;
        BEGIN TRY
        """ + "\n" + body + "\n" + """
            EXEC sys.sp_releaseapplock @Resource=@osaResource, @LockOwner='Session';
        END TRY
        BEGIN CATCH
            IF XACT_STATE() <> 0 ROLLBACK;
            EXEC sys.sp_releaseapplock @Resource=@osaResource, @LockOwner='Session';
            THROW;
        END CATCH;
        """;

    public static string Reserve => Locked("""
        IF OBJECT_ID(QUOTENAME(N'__OSA_SCHEMA_LITERAL__') + N'.[ObjectAssetIdentity]', N'U') IS NULL
            OR COL_LENGTH(QUOTENAME(N'__OSA_SCHEMA_LITERAL__') + N'.[ObjectAssets]', N'BindingKind') IS NULL
            THROW 51000, 'OSA expansion is incomplete; do not repair schema or migration history by guessing.', 1;
        IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'LifecycleSplitV1')
            THROW 51000, 'OSA identity reservation cannot be repeated after splitting has started.', 1;
        IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetTemporary]) OR EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetArchive])
            THROW 51000, 'Unexpected destination payloads before OSA identity reservation.', 1;
        IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssets] WHERE
             [Id]='00000000-0000-0000-0000-000000000000' OR [OwnerKeyKind] NOT IN (0,1,2,3) OR [Status] NOT IN (1,2,3,4,5,6)
          OR ([OwnerKeyKind]=0 AND ([OwnerKeyInt64] IS NOT NULL OR [OwnerKeyGuid] IS NOT NULL OR [OwnerKeyText] IS NOT NULL))
          OR ([OwnerKeyKind]=1 AND ([OwnerKeyInt64] IS NULL OR [OwnerKeyGuid] IS NOT NULL OR [OwnerKeyText] IS NOT NULL))
          OR ([OwnerKeyKind]=2 AND ([OwnerKeyGuid] IS NULL OR [OwnerKeyGuid]='00000000-0000-0000-0000-000000000000' OR [OwnerKeyInt64] IS NOT NULL OR [OwnerKeyText] IS NOT NULL))
          OR ([OwnerKeyKind]=3 AND (NULLIF(LTRIM(RTRIM([OwnerKeyText])),N'') IS NULL OR [OwnerKeyInt64] IS NOT NULL OR [OwnerKeyGuid] IS NOT NULL))
          OR ([OwnerKeyKind]<>0 AND (NULLIF(LTRIM(RTRIM([OwnerType])),N'') IS NULL OR NULLIF(LTRIM(RTRIM([SlotName])),N'') IS NULL))
          OR ([TemporaryBindingId] IS NOT NULL AND [OwnerKeyKind]<>0)
          OR ([Status]=5 AND [DeletedAtUtc] IS NULL))
            THROW 51000, 'Contradictory legacy owner or temporary binding; resolve it explicitly before upgrading.', 1;
        IF NOT EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'V016ExpectedTotal')
        BEGIN
            IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetIdentity])
                THROW 51000, 'Unexpected identities without an OSA reservation checkpoint.', 1;
            BEGIN TRANSACTION;
            INSERT [__OSA_SCHEMA__].[ObjectAssetUpgradeState] ([Name],[ProcessedCount],[Completed])
            SELECT N'V016ExpectedTotal', COUNT_BIG(*), 1 FROM [__OSA_SCHEMA__].[ObjectAssets]
            UNION ALL SELECT N'V016ExpectedMain', COUNT_BIG(*), 1 FROM [__OSA_SCHEMA__].[ObjectAssets] WHERE [Status]<>5 AND [TemporaryBindingId] IS NULL
            UNION ALL SELECT N'V016ExpectedTemporary', COUNT_BIG(*), 1 FROM [__OSA_SCHEMA__].[ObjectAssets] WHERE [Status]<>5 AND [TemporaryBindingId] IS NOT NULL
            UNION ALL SELECT N'V016ExpectedArchive', COUNT_BIG(*), 1 FROM [__OSA_SCHEMA__].[ObjectAssets] WHERE [Status]=5;
            INSERT [__OSA_SCHEMA__].[ObjectAssetUpgradeState] ([Name],[ProcessedCount],[Completed]) VALUES (N'LifecycleReserveV1',0,0);
            COMMIT;
        END;
        IF (SELECT COUNT_BIG(*) FROM [__OSA_SCHEMA__].[ObjectAssets]) <>
            (SELECT [ProcessedCount] FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'V016ExpectedTotal')
            THROW 51000, 'Legacy payload count changed during OSA upgrade. Stop all writers.', 1;
        DECLARE @reserveIds TABLE ([Id] uniqueidentifier PRIMARY KEY);
        DECLARE @reserveLast uniqueidentifier = '00000000-0000-0000-0000-000000000000', @reserveCount int;
        WHILE 1=1
        BEGIN
            DELETE FROM @reserveIds;
            INSERT @reserveIds SELECT TOP (1000) a.[Id] FROM [__OSA_SCHEMA__].[ObjectAssets] a WITH (INDEX([PK_ObjectAssets]))
                WHERE a.[Id]>@reserveLast
                AND NOT EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetIdentity] i WHERE i.[AssetId]=a.[Id]) ORDER BY a.[Id];
            SET @reserveCount=@@ROWCOUNT;
            IF @reserveCount=0 BREAK;
            SELECT TOP (1) @reserveLast=[Id] FROM @reserveIds ORDER BY [Id] DESC;
            BEGIN TRANSACTION;
            UPDATE a SET [BindingKind]=CASE WHEN [TemporaryBindingId] IS NOT NULL THEN 3 WHEN [OwnerKeyKind]<>0 THEN 1 ELSE 0 END
                FROM [__OSA_SCHEMA__].[ObjectAssets] a INNER JOIN @reserveIds b ON b.[Id]=a.[Id];
            INSERT [__OSA_SCHEMA__].[ObjectAssetIdentity] ([AssetId],[Location],[StorageAuthorityId],[BucketName],[ObjectKey])
                SELECT a.[Id],0,a.[StorageAuthorityId],a.[BucketName],a.[ObjectKey]
                FROM [__OSA_SCHEMA__].[ObjectAssets] a INNER JOIN @reserveIds b ON b.[Id]=a.[Id];
            UPDATE [__OSA_SCHEMA__].[ObjectAssetUpgradeState] SET [ProcessedCount]=[ProcessedCount]+@reserveCount WHERE [Name]=N'LifecycleReserveV1';
            COMMIT;
        END;
        IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetIdentity] i LEFT JOIN [__OSA_SCHEMA__].[ObjectAssets] a ON a.[Id]=i.[AssetId]
            WHERE a.[Id] IS NULL OR i.[Location]<>0 OR i.[BucketName]<>a.[BucketName] COLLATE Latin1_General_100_BIN2
                OR i.[ObjectKey]<>a.[ObjectKey] COLLATE Latin1_General_100_BIN2 OR i.[StorageAuthorityId]<>a.[StorageAuthorityId])
            THROW 51000, 'OSA reserved identity does not match its payload.', 1;
        IF (SELECT COUNT_BIG(*) FROM [__OSA_SCHEMA__].[ObjectAssetIdentity]) <>
            (SELECT [ProcessedCount] FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'V016ExpectedTotal')
            THROW 51000, 'OSA identity conservation check failed.', 1;
        UPDATE [__OSA_SCHEMA__].[ObjectAssetUpgradeState] SET [Completed]=1 WHERE [Name]=N'LifecycleReserveV1';
        """);

    public static string Split => Locked("""
        IF NOT EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'LifecycleReserveV1' AND [Completed]=1)
            THROW 51000, 'Reserve OSA identities before splitting payloads.', 1;
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [referenced_object_id]=OBJECT_ID(QUOTENAME(N'__OSA_SCHEMA_LITERAL__') + N'.[ObjectAssets]'))
            THROW 51000, 'Retarget host foreign keys from ObjectAssets.Id to ObjectAssetIdentity.AssetId before splitting.', 1;
        IF NOT EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'LifecycleSplitV1')
            INSERT [__OSA_SCHEMA__].[ObjectAssetUpgradeState] ([Name],[ProcessedCount],[Completed]) VALUES (N'LifecycleSplitV1',0,0);
        """ + "\n" + Move("ObjectAssetArchive", "[Status]=5", 2) + Move("ObjectAssetTemporary", "[Status]<>5 AND [TemporaryBindingId] IS NOT NULL", 1));

    private static string Move(string target, string predicate, int location) => $$"""
        DECLARE @moveIds{{location}} TABLE ([Id] uniqueidentifier PRIMARY KEY);
        DECLARE @moveCount{{location}} int, @moveLast{{location}} uniqueidentifier = '00000000-0000-0000-0000-000000000000';
        WHILE 1=1
        BEGIN
            DELETE FROM @moveIds{{location}};
            INSERT @moveIds{{location}} SELECT TOP (1000) [Id] FROM [__OSA_SCHEMA__].[ObjectAssets] WITH (INDEX([PK_ObjectAssets]))
                WHERE [Id]>@moveLast{{location}} AND {{predicate}} ORDER BY [Id];
            SET @moveCount{{location}}=@@ROWCOUNT;
            IF @moveCount{{location}}=0 BREAK;
            SELECT TOP (1) @moveLast{{location}}=[Id] FROM @moveIds{{location}} ORDER BY [Id] DESC;
            BEGIN TRANSACTION;
            IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[{{target}}] a INNER JOIN @moveIds{{location}} b ON a.[Id]=b.[Id])
                THROW 51000, 'An asset exists in both source and destination. Stop and investigate.', 1;
            INSERT [__OSA_SCHEMA__].[{{target}}] ({{Columns}})
                SELECT {{Columns}} FROM [__OSA_SCHEMA__].[ObjectAssets] WHERE [Id] IN (SELECT [Id] FROM @moveIds{{location}});
            UPDATE i SET [Location]={{location}} FROM [__OSA_SCHEMA__].[ObjectAssetIdentity] i
                INNER JOIN @moveIds{{location}} b ON b.[Id]=i.[AssetId] WHERE i.[Location]=0;
            IF @@ROWCOUNT<>@moveCount{{location}} THROW 51000, 'OSA identity location changed during splitting.', 1;
            DELETE a FROM [__OSA_SCHEMA__].[ObjectAssets] a INNER JOIN @moveIds{{location}} b ON b.[Id]=a.[Id];
            IF @@ROWCOUNT<>@moveCount{{location}} THROW 51000, 'OSA source count changed during splitting.', 1;
            UPDATE [__OSA_SCHEMA__].[ObjectAssetUpgradeState] SET [ProcessedCount]=[ProcessedCount]+@moveCount{{location}} WHERE [Name]=N'LifecycleSplitV1';
            COMMIT;
        END;

        """;

    public static string Validate => Locked("""
        IF NOT EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'LifecycleReserveV1' AND [Completed]=1)
            OR NOT EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'LifecycleSplitV1')
            THROW 51000, 'OSA lifecycle checkpoints are incomplete.', 1;
        IF (SELECT COUNT_BIG(*) FROM [__OSA_SCHEMA__].[ObjectAssets]) <>
            (SELECT [ProcessedCount] FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'V016ExpectedMain')
          OR (SELECT COUNT_BIG(*) FROM [__OSA_SCHEMA__].[ObjectAssetTemporary]) <>
            (SELECT [ProcessedCount] FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'V016ExpectedTemporary')
          OR (SELECT COUNT_BIG(*) FROM [__OSA_SCHEMA__].[ObjectAssetArchive]) <>
            (SELECT [ProcessedCount] FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'V016ExpectedArchive')
          OR (SELECT COUNT_BIG(*) FROM [__OSA_SCHEMA__].[ObjectAssetIdentity]) <>
            (SELECT [ProcessedCount] FROM [__OSA_SCHEMA__].[ObjectAssetUpgradeState] WHERE [Name]=N'V016ExpectedTotal')
            THROW 51000, 'OSA lifecycle row conservation failed. Stop all writers and investigate.', 1;
        IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssets] WHERE [Status]=5 OR [TemporaryBindingId] IS NOT NULL)
          OR EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetTemporary] WHERE [Status]=5 OR [TemporaryBindingId] IS NULL OR [BindingKind]<>3)
          OR EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetArchive] WHERE [Status]<>5 OR [DeletedAtUtc] IS NULL)
            THROW 51000, 'OSA payload is in the wrong lifecycle table.', 1;
        IF EXISTS (SELECT 1 FROM (
            SELECT [Id],0 AS [Location],[BucketName],[ObjectKey],[StorageAuthorityId],[BindingKind],[OwnerKeyKind],[Sha256ValidationStatus] FROM [__OSA_SCHEMA__].[ObjectAssets]
            UNION ALL SELECT [Id],1,[BucketName],[ObjectKey],[StorageAuthorityId],[BindingKind],[OwnerKeyKind],[Sha256ValidationStatus] FROM [__OSA_SCHEMA__].[ObjectAssetTemporary]
            UNION ALL SELECT [Id],2,[BucketName],[ObjectKey],[StorageAuthorityId],[BindingKind],[OwnerKeyKind],[Sha256ValidationStatus] FROM [__OSA_SCHEMA__].[ObjectAssetArchive]) a
            LEFT JOIN [__OSA_SCHEMA__].[ObjectAssetIdentity] i ON i.[AssetId]=a.[Id]
            WHERE i.[AssetId] IS NULL OR i.[Location]<>a.[Location] OR i.[StorageAuthorityId]<>a.[StorageAuthorityId]
                OR i.[BucketName]<>a.[BucketName] COLLATE Latin1_General_100_BIN2 OR i.[ObjectKey]<>a.[ObjectKey] COLLATE Latin1_General_100_BIN2
                OR a.[Sha256ValidationStatus]<>0 OR (a.[Location]=0 AND a.[BindingKind]<>CASE WHEN a.[OwnerKeyKind]=0 THEN 0 ELSE 1 END))
            THROW 51000, 'OSA identity, binding or validation state failed verification.', 1;
        UPDATE [__OSA_SCHEMA__].[ObjectAssetUpgradeState] SET [Completed]=1 WHERE [Name]=N'LifecycleSplitV1';
        """);

    public const string GuardDown = """
        IF EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetIdentity])
          OR EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetTemporary])
          OR EXISTS (SELECT 1 FROM [__OSA_SCHEMA__].[ObjectAssetArchive])
            THROW 51000, 'OSA downgrade with reserved or moved data is blocked. Restore a verified pre-upgrade backup.', 1;
        """;
}
