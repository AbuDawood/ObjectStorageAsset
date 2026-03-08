using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;

namespace Elf.ObjectStorageAsset.Infrastructure.Persistence.Configurations;

internal sealed class ObjectAssetConfiguration : IEntityTypeConfiguration<ObjectAsset>
{
    public void Configure(EntityTypeBuilder<ObjectAsset> builder)
    {
        builder.ToTable("ObjectAssets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OwnerType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.OwnerKeyKind)
            .IsRequired();

        builder.Property(x => x.OwnerKeyText)
            .HasMaxLength(256);

        builder.Property(x => x.SlotName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.SlotMultiplicity)
            .IsRequired();

        builder.Property(x => x.BucketName)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.StorageNamespace)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ObjectKey)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.OriginalFileName)
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(x => x.Extension)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ContentType)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.SizeBytes)
            .HasDefaultValue(0L);

        builder.Property(x => x.Sha256)
            .HasMaxLength(64);

        builder.Property(x => x.ETag)
            .HasMaxLength(128);

        builder.Property(x => x.ProviderVersionId)
            .HasMaxLength(128);

        builder.Property(x => x.OwnershipMode)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired();

        builder.Property(x => x.TemporaryBindingId);

        builder.Property(x => x.TemporaryBindingExpiresAtUtc);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(1024);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.LastStatusChangedAtUtc)
            .IsRequired();

        builder.Property(x => x.RowVersion)
            .IsRowVersion();

        builder.HasIndex(x => x.ObjectKey)
            .IsUnique();

        builder.HasIndex(x => new
        {
            x.OwnerType,
            x.OwnerKeyKind,
            x.OwnerKeyInt64,
            x.OwnerKeyGuid,
            x.OwnerKeyText,
            x.SlotName,
            x.Status
        });

        builder.HasIndex(x => new
        {
            x.Status,
            x.ExpiresAtUtc
        });

        builder.HasIndex(x => new
        {
            x.Status,
            x.TemporaryBindingExpiresAtUtc
        });

        builder.HasIndex(x => new
        {
            x.Status,
            x.OwnershipMode
        });

        builder.HasIndex(x => new
        {
            x.TemporaryBindingId,
            x.Status
        });

        builder.HasIndex(x => new
        {
            x.OwnerType,
            x.OwnerKeyKind,
            x.OwnerKeyInt64,
            x.OwnerKeyGuid,
            x.OwnerKeyText,
            x.SlotName,
            x.TemporaryBindingId
        })
            .IsUnique()
            .HasFilter(BuildSingleSlotFilter());
    }

    private static string BuildSingleSlotFilter()
    {
        var single = (int)ObjectAssetSlotMultiplicity.Single;
        var pendingUpload = (int)ObjectAssetStatus.PendingUpload;
        var active = (int)ObjectAssetStatus.Active;
        var uploadFailed = (int)ObjectAssetStatus.UploadFailed;
        var pendingDelete = (int)ObjectAssetStatus.PendingDelete;
        var deleteFailed = (int)ObjectAssetStatus.DeleteFailed;

        return
            $"[SlotMultiplicity] = {single} AND [Status] IN ({pendingUpload}, {active}, {uploadFailed}, {pendingDelete}, {deleteFailed})";
    }
}
