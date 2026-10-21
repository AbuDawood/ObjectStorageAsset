using System.Security.Cryptography;
using Elf.ObjectStorageAsset.Application;
using Elf.ObjectStorageAsset.Domain.Entities;
using Elf.ObjectStorageAsset.Domain.Enums;
using Elf.ObjectStorageAsset.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Transactions;

namespace Elf.ObjectStorageAsset.Infrastructure;

internal sealed class ObjectAssetIntegrityService(ObjectStorageAssetDbContext db, ObjectAssetStorageResolver storageResolver,
    ObjectAssetStager stager, ObjectStorageAssetRuntimeOptions options, ILogger<ObjectAssetIntegrityService> logger,
    IServiceScopeFactory scopes)
    : IObjectAssetIntegrityService
{
    public async Task<ObjectAssetIntegrityResult> VerifyAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        try
        {
            var opened = await OpenAssetAsync(assetId, true, ObjectAssetReadScope.Any, cancellationToken)
                ?? throw new InvalidOperationException("Asset not found.");
            await opened.Content.DisposeAsync();
            return opened.Integrity!;
        }
        catch (ObjectAssetIntegrityException ex) when (options.Integrity.FailureBehavior == Sha256FailureBehavior.Warning)
        { return ex.Result; }
    }

    internal async Task<OpenedAsset?> OpenAssetAsync(Guid assetId, bool verify, ObjectAssetReadScope readScope, CancellationToken ct)
    {
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = await context.FindAssetAsync(assetId, true, ct);
        if (asset is null || readScope == ObjectAssetReadScope.Active && asset.Status != ObjectAssetStatus.Active
            || readScope == ObjectAssetReadScope.Archive && (asset is not ArchivedObjectAsset || asset.PhysicalDeletedAtUtc.HasValue))
            return null;
        var core = scope.ServiceProvider.GetRequiredService<ObjectAssetIntegrityService>();
        OpenedAsset? result = null;
        try
        {
            var opened = await core.OpenAsync(asset, verify, ct);
            result = new(asset, opened.Content, opened.Integrity);
            var tracked = db.ChangeTracker.Entries<ObjectAsset>().FirstOrDefault(x => x.Entity.Id == assetId && x.State == EntityState.Unchanged);
            if (verify && tracked is not null && db.Database.CurrentTransaction is null)
                await tracked.ReloadAsync(ct);
            suppression.Complete();
            return result;
        }
        catch
        {
            if (result is not null) await result.Content.DisposeAsync();
            var tracked = db.ChangeTracker.Entries<ObjectAsset>().FirstOrDefault(x => x.Entity.Id == assetId && x.State == EntityState.Unchanged);
            if (verify && result is null && tracked is not null && db.Database.CurrentTransaction is null)
                await tracked.ReloadAsync(ct);
            throw;
        }
    }

    internal async Task<(Stream Content, ObjectAssetIntegrityResult? Integrity)> OpenAsync(ObjectAsset asset, bool verify, CancellationToken ct)
    {
        var attempted = DateTimeOffset.UtcNow;
        var requestedVersion = asset.ProviderVersionId;
        Stream? content = null;
        ObjectAssetIntegrityResult? result = null;
        try
        {
            var downloaded = await stager.DownloadAsync(storageResolver.Resolve(asset.StorageAuthorityId), new ObjectStorageGetRequest
            { BucketName = asset.BucketName, ObjectKey = asset.ObjectKey, VersionId = asset.ProviderVersionId }, ct);
            content = downloaded.Content;
            if (!verify) return (content, null);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
            content.Position = 0;
            var expectedValid = asset.Sha256.Length == 64 && asset.Sha256.All(Uri.IsHexDigit);
            var status = !expectedValid ? Sha256ValidationStatus.Failed
                : string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase) && content.Length == asset.SizeBytes
                    ? Sha256ValidationStatus.Valid : Sha256ValidationStatus.Mismatch;
            result = new(asset.Id, status, asset.Sha256, actual, attempted,
                expectedValid ? DateTimeOffset.UtcNow : null, downloaded.ObjectVersion,
                !expectedValid ? "ExpectedHashUnavailable" : status == Sha256ValidationStatus.Mismatch ? "HashOrLengthMismatch" : null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (content is not null) await content.DisposeAsync();
            throw;
        }
        catch (Exception ex)
        {
            if (content is not null) await content.DisposeAsync();
            content = null;
            if (!verify) throw;
            result = new(asset.Id, Sha256ValidationStatus.Failed, asset.Sha256, null, attempted, null,
                asset.ProviderVersionId, ex.GetType().Name);
        }

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                asset.RecordSha256Validation(result!.Status, result.AttemptedAtUtc, result.CompletedAtUtc,
                    result.ActualHash, result.ObjectVersion, result.Error);
                try { await db.SaveChangesAsync(ct); break; }
                catch (DbUpdateConcurrencyException) when (attempt == 0)
                {
                    db.ChangeTracker.Clear();
                    asset = await db.FindAssetAsync(asset.Id, true, ct)
                        ?? throw new InvalidOperationException("Asset disappeared during validation.");
                    if (asset.Sha256 != result.ExpectedHash || asset.ProviderVersionId != requestedVersion)
                        throw new InvalidOperationException("Asset content identity changed during validation.");
                }
            }
            if (result.Status != Sha256ValidationStatus.Valid)
            {
                logger.LogWarning("SHA-256 validation for asset {AssetId} returned {Status}: {Reason}", asset.Id, result.Status, result.Error);
                if (options.Integrity.FailureBehavior == Sha256FailureBehavior.Guard || content is null)
                    throw new ObjectAssetIntegrityException(result);
            }
            return (content!, result);
        }
        catch { if (content is not null) await content.DisposeAsync(); throw; }
    }
}

internal enum ObjectAssetReadScope { Active, Archive, Any }
internal sealed record OpenedAsset(ObjectAsset Asset, Stream Content, ObjectAssetIntegrityResult? Integrity);
