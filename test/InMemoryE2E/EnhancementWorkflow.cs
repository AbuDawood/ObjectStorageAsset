using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Elf.ObjectStorageAsset.TestSupport.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elf.ObjectStorageAsset.InMemoryE2E;

public static partial class Program
{
    private static async Task VerifyEnhancedWorkflowAsync(IServiceProvider services, ServiceProvider root, InMemoryE2EHostDbContext host)
    {
        var options = root.GetRequiredService<ObjectStorageAssetOptions>();
        var naming = services.GetRequiredService<IObjectAssetNamingService>();
        await naming.ActivateAsync(new("e2e", 1, "original/{Year}/{Month}/{Day}/{AssetId}{Extension}"));
        var temporaryId = Guid.NewGuid();
        var temporary = await services.GetRequiredService<IObjectAssetTemporarySessionFactory>().For<Order>(temporaryId)
            .AddAsync(OrderAssets.Attachments, CreateStream("enhanced-e2e"), "enhanced.txt", "text/plain");
        var reader = services.GetRequiredService<IObjectAssetPagedReader>();
        var record = await reader.GetRecordAsync(temporary.AssetId) ?? throw new InvalidOperationException("Temporary asset missing.");
        Require(record.Asset.BindingKind == ObjectAssetBindingKind.Temporary, "Temporary discriminator");
        Require(record.Asset.Sha256ValidationStatus == Sha256ValidationStatus.NotChecked, "NotChecked default");
        var key = record.File.ObjectKey;
        await naming.ActivateAsync(new("e2e", 2, "replacement/{AssetId}{Extension}"));
        var order = new Order { Number = "enhanced-e2e" }; host.Orders.Add(order); await host.SaveChangesAsync();
        await services.GetRequiredService<IObjectAssetBindingFinalizer>().FinalizeTemporaryBindingAsync(temporaryId, order);
        var db = services.GetRequiredService<ObjectStorageAssetDbContext>();
        Require(!await db.TemporaryAssets.AnyAsync(x => x.Id == temporary.AssetId), "Temporary row moved");
        var bound = await db.PermanentAssets.AsNoTracking().SingleAsync(x => x.Id == temporary.AssetId);
        Require(bound.ObjectKey == key && bound.NamingProfileVersion == 1, "Naming snapshot across binding");
        var valid = await services.GetRequiredService<IObjectAssetIntegrityService>().VerifyAsync(temporary.AssetId);
        Require(valid.Status == Sha256ValidationStatus.Valid, "Explicit SHA validation");

        var storage = services.GetRequiredService<InMemoryMinioLikeObjectStorageProvider>();
        storage.CorruptOnGet = true;
        options.Integrity.Triggers = Sha256VerificationTriggers.OnRead;
        var contentReader = services.GetRequiredService<IObjectAssetContentReader>();
        var warning = await contentReader.OpenReadAsync(temporary.AssetId) ?? throw new InvalidOperationException("Warning read failed.");
        await warning.Content.DisposeAsync();
        Require(warning.Integrity?.Status == Sha256ValidationStatus.Mismatch, "Warning continues and reports mismatch");
        options.Integrity.FailureBehavior = Sha256FailureBehavior.Guard;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var locked = (await db.FindAssetForUpdateAsync(temporary.AssetId))!;
        locked.MarkUploadSucceeded(DateTimeOffset.UtcNow, locked.ETag, locked.ProviderVersionId);
        await db.SaveChangesAsync();
        try
        {
            var rejected = await contentReader.OpenReadAsync(temporary.AssetId);
            if (rejected is not null) await rejected.Content.DisposeAsync();
            throw new InvalidOperationException("Guard exposed corrupted content.");
        }
        catch (ObjectAssetIntegrityException ex) { Require(ex.Result.Status == Sha256ValidationStatus.Mismatch, "Guard mismatch"); }
        finally
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear(); // Begin the next EF unit with fresh state after rolling back a saved write.
            storage.CorruptOnGet = false;
            options.Integrity.FailureBehavior = Sha256FailureBehavior.Warning;
            options.Integrity.Triggers = Sha256VerificationTriggers.ExplicitOnly;
        }
        Require((await reader.GetRecordAsync(temporary.AssetId))!.Integrity?.Status == Sha256ValidationStatus.Mismatch, "Persisted mismatch");
        storage.RemoveObject(key);
        try { await contentReader.OpenReadAsync(temporary.AssetId); throw new InvalidOperationException("Missing content was exposed."); }
        catch (ObjectAssetContentMissingException) { }
        var reads = storage.GetCount;
        try { await contentReader.OpenReadAsync(temporary.AssetId); throw new InvalidOperationException("Missing content was exposed."); }
        catch (ObjectAssetContentMissingException) { }
        Require(storage.GetCount == reads, "Known missing content short circuits storage");
        Require((await reader.GetRecordAsync(temporary.AssetId))!.Availability?.Availability == ObjectAssetContentAvailability.Missing, "Persisted availability");
        await using (var restored = CreateStream("enhanced-e2e"))
            await storage.PutObjectAsync(new() { BucketName = bound.BucketName, ObjectKey = key, Content = restored,
                ContentLength = restored.Length, ContentType = "text/plain" });
        Require((await services.GetRequiredService<IObjectAssetAvailabilityService>().RecheckAsync(temporary.AssetId))!.Availability == ObjectAssetContentAvailability.Available,
            "Explicit restoration recheck");
        var restoredRead = await contentReader.OpenReadAsync(temporary.AssetId);
        await restoredRead!.Content.DisposeAsync();
        var sessions = services.GetRequiredService<IObjectAssetSessionFactory>();
        await sessions.For(order).RemoveAsync(temporary.AssetId);
        await services.GetRequiredService<IObjectAssetCoordinator>().SaveChangesWithAssetsAsync(host);
        Require(!await db.PermanentAssets.AnyAsync(x => x.Id == temporary.AssetId), "Main row archived");
        Require(await db.ArchivedAssets.AnyAsync(x => x.Id == temporary.AssetId), "Archive retrieval");
        Require((await reader.GetArchivePageAsync()).Items.Any(x => x.AssetId == temporary.AssetId), "Archive page");
        Require((await contentReader.OpenReadAsync(temporary.AssetId)) is null, "Normal reader excludes deleted asset");
        Console.WriteLine("Enhanced E2E passed: naming versions, temporary binding, SHA warning/guard with caller locks, durable evidence, missing-content short circuit/restoration, table moves and archive queries.");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"Enhanced E2E failed: {description}.");
    }
}
