using System.Security.Cryptography;
using System.Diagnostics;
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
    IServiceScopeFactory scopes, ObjectAssetIntegrityJournal journal, ObjectAssetAvailabilityStore availability, TimeProvider clock)
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
        if (db.HasCallerTransaction)
        {
            var local = db.ChangeTracker.Entries<ObjectAsset>().FirstOrDefault(e => e.Entity.Id == assetId)?.Entity
                ?? await db.FindAssetAsync(assetId, true, ct).ConfigureAwait(false);
            if (!InScope(local, readScope)) return null;
            var content = await OpenAsync(local!, verify, ct, journalEvidence: verify).ConfigureAwait(false);
            return new(local!, content.Content, content.Integrity);
        }
        using var suppression = new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled);
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ObjectStorageAssetDbContext>();
        var asset = await context.FindAssetAsync(assetId, true, ct);
        if (!InScope(asset, readScope))
            return null;
        var core = scope.ServiceProvider.GetRequiredService<ObjectAssetIntegrityService>();
        OpenedAsset? result = null;
        try
        {
            var opened = await core.OpenAsync(asset!, verify, ct);
            result = new(asset!, opened.Content, opened.Integrity);
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

    private static bool InScope(ObjectAsset? asset, ObjectAssetReadScope scope) => asset is not null
        && (scope != ObjectAssetReadScope.Active || asset.Status == ObjectAssetStatus.Active)
        && (scope != ObjectAssetReadScope.Archive || asset is ArchivedObjectAsset && !asset.PhysicalDeletedAtUtc.HasValue);

    internal async Task<(Stream Content, ObjectAssetIntegrityResult? Integrity)> OpenAsync(ObjectAsset asset, bool verify, CancellationToken ct,
        bool journalEvidence = false)
    {
        var known = await availability.GetAsync(asset, ct).ConfigureAwait(false);
        if (known.Availability == ObjectAssetContentAvailability.Missing) throw new ObjectAssetContentMissingException(known);
        var attempted = clock.GetUtcNow();
        var started = Stopwatch.GetTimestamp();
        var requestedVersion = asset.ProviderVersionId;
        Stream? content = null;
        ObjectAssetIntegrityResult? result = null;
        ObjectAssetContentMissingException? missing = null;
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
            if (verify) ObjectAssetIntegrityMetrics.VerificationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("status", "Cancelled"));
            throw;
        }
        catch (ObjectStorageObjectNotFoundException ex)
        {
            if (content is not null) await content.DisposeAsync();
            content = null;
            var observation = await availability.RecordAsync(asset, ObjectAssetContentAvailability.Missing, attempted, ex.Reason, ct).ConfigureAwait(false);
            missing = new ObjectAssetContentMissingException(observation, ex);
            if (!verify) throw missing;
            result = new(asset.Id, Sha256ValidationStatus.Failed, asset.Sha256, null, attempted, null,
                asset.ProviderVersionId, ex.Reason.ToString());
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
            if (journalEvidence)
            {
                var evidence = await journal.AppendAsync(asset, result!, ct).ConfigureAwait(false);
                db.RegisterIntegrityEvidence(journal, evidence.Id);
            }
            else for (var attempt = 0; ; attempt++)
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
            if (missing is not null) throw missing;
            if (result.Status != Sha256ValidationStatus.Valid)
            {
                logger.LogWarning("SHA-256 validation for asset {AssetId} returned {Status}: {Reason}", asset.Id, result.Status, result.Error);
                if (options.Integrity.FailureBehavior == Sha256FailureBehavior.Guard || content is null)
                    throw new ObjectAssetIntegrityException(result);
            }
            return (content!, result);
        }
        catch { if (content is not null) await content.DisposeAsync(); throw; }
        finally
        {
            if (verify) ObjectAssetIntegrityMetrics.VerificationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("status", result?.Status.ToString() ?? "Failed"));
        }
    }
}

internal enum ObjectAssetReadScope { Active, Archive, Any }
internal sealed record OpenedAsset(ObjectAsset Asset, Stream Content, ObjectAssetIntegrityResult? Integrity);
