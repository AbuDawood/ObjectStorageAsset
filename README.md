# Elf.ObjectStorageAsset

`Elf.ObjectStorageAsset` is a .NET 8 library for host-managed object storage metadata, lifecycle orchestration, and provider-backed file IO.

Current implementation state:
- Layered architecture (`Helpers`, `Domain`, `Application`, `Infrastructure`, `Bootstrap`, `Minio`)
- Standalone solution with direct-reference console host
- Binding contracts and runtime binding catalog are available
- Persistence core includes the `ObjectAsset` aggregate, SQL Server DbContext, schema-aware mappings, and the initial migration model
- Provider abstractions include `IObjectStorageProvider`, `IObjectKeyStrategy`, and reserved OSA metadata stamps
- Default MinIO support is available through `Elf.ObjectStorageAsset.Minio`
- Command-side orchestration is available through `IObjectAssetSessionFactory` and `IObjectAssetCoordinator`
- Descriptor registry APIs are available through `IObjectAssetRegistry`
- Temporary upload/finalization APIs are available through `IObjectAssetTemporarySessionFactory` and `IObjectAssetBindingFinalizer`
- Read-side services are available through `IObjectAssetReader`, `IObjectAssetContentReader`, and the SQL query bridge
- Lifecycle operations are available through `IObjectAssetMaintenanceService` with optional hosted maintenance and auto-migration services
- Automated workflow tests use a reusable in-memory MinIO-like provider together with SQL Server-backed metadata persistence
- A dedicated SQL-backed in-memory E2E runner is available under `test/InMemoryE2E`
- A Docker-backed MinIO E2E test project is available under `test/DockerMinioE2ETests`
- A dedicated smoke-test runner is available under `test/SmokeTest`
- A dedicated live E2E runner is available under `test/LiveE2E`

Contribution rules and engineering conventions are documented in `CONTRIBUTING.md`.
Version history is documented in `CHANGELOG.md`.

## Solution Layout

```text
src/
  Helpers/
  Domain/
  Application/
  Infrastructure/
  Bootstrap/
  Minio/
test/
  ConsoleTest/
  DockerMinioE2ETests/
  InMemoryE2E/
  LiveE2E/
  SmokeTest/
  TestSupport/
  UnitTests/
```

## NuGet Packaging

NuGet packages are produced only from the projects under `src/`:
- `Elf.ObjectStorageAsset`
- `Elf.ObjectStorageAsset.Minio`
- `Elf.ObjectStorageAsset.Application`
- `Elf.ObjectStorageAsset.Domain`
- `Elf.ObjectStorageAsset.Infrastructure`
- `Elf.ObjectStorageAsset.Helpers`

The GitHub Actions workflow at `.github/workflows/nuget.yml` runs unit and SQL integration tests,
actual v0.1.6 binary compatibility checks, and both the in-memory-provider and real MinIO E2E runners
against disposable local SQL Server databases. The opt-in million-row scale test remains a separate
manual validation. CI builds the MinIO test server from the pinned official source release
`RELEASE.2024-10-13T13-34-11Z`, rather than relying on retired binary/container download endpoints.
Only after these checks pass does the workflow build and pack
`ObjectStorageAsset.Pack.slnf`, using one version for assemblies, packages and internal dependencies.
It checks all six package/symbol pairs and embeds this README and the license in every package.

Publishing flow:
- Configure the existing `NUGET_API_KEY` repository secret with permission to publish all six packages.
- Review the [v0.1.6 upgrade procedure](#upgrade-from-v016) before adopting v0.2.0. Publishing a package does not migrate any host database.
- Merge the tested release changes into `main`, with `VersionPrefix` set to `0.2.0`.
- Push the new `v0.2.0` tag at that merged commit. Tags must exactly match `VersionPrefix` and belong to `main`.
- The workflow repeats validation, uploads the packages as a workflow artifact, then publishes the `.nupkg` and `.snupkg` files to NuGet.org.

Pull requests, untagged `main` pushes and manual runs produce downloadable CI packages without
publishing. Their version includes the run number and attempt. Never move an existing release tag
or reuse a published package version; correct a published release with a new version.

## License

This repository is licensed under the MIT License. See `LICENSE`.

## Development Loop

Each slice follows the same loop:
1. Implement the slice.
2. Add or update tests.
3. Fix defects until tests pass.
4. Update `README.md` and `CONTRIBUTING.md`.
5. Continue to the next slice.

## Binding Contracts

Host applications declare file ownership through explicit contract classes:

```csharp
public sealed class OrderAssets : IObjectAssetBinding<Order, int>
{
    public static readonly ObjectAssetSlot Invoice = ObjectAssetSlot.Single("invoice");
    public static readonly ObjectAssetSlot Attachments = ObjectAssetSlot.Many("attachments");

    public void Configure(IObjectAssetOwnerBuilder<Order, int> builder)
    {
        builder.OwnerType("order");
        builder.Key(x => x.Id);
        builder.Slot(Invoice);
        builder.Slot(Attachments);
    }
}
```

Registration:

```csharp
services.AddObjectStorageAsset(options =>
{
    options.ConnectionString = "...";
    options.Schema = "osa";
    options.DefaultStorageNamespace = "up-notification";
    options.AddBinding<OrderAssets>();
});
```

The runtime catalog can be resolved through `IObjectAssetBindingCatalog`.

## Persistence Core

The library stores one payload row per asset, with a stable identity across lifecycle changes.

Current persistence shape:
- SQL Server + EF Core `8.0.11`
- configurable schema
- schema-aware `__EFMigrationsHistory`
- owner binding columns on `ObjectAssets`
- stable `AssetId` as the immutable primary identifier for every asset row
- `ObjectAssets` holds permanent local, external-owner and unresolved legacy registrations
- `ObjectAssetTemporary` holds pending temporary bindings
- `ObjectAssetArchive` holds logically deleted records, including retained-content records
- `ObjectAssetIdentity` reserves each asset id and its current payload location
- immutable custom metadata, ownership and validation evidence move with the payload
- SQL Server `rowversion` for optimistic concurrency on asset updates
- computed enum label columns such as `StatusLabel`

The first migration is generated from `ObjectStorageAssetDbContext`.

## Provider Core

The current provider-facing contracts are:
- `IObjectKeyStrategy`
- `IObjectStorageProvider`
- `ObjectStoragePutRequest` / `ObjectStoragePutResult`
- `ObjectStorageGetRequest` / `ObjectStorageGetResult`
- `ObjectStorageStatRequest` / `ObjectStorageStatResult`
- `ObjectStorageDeleteRequest`
- `ObjectStorageSystemMetadata`

Default object keys are generated as:

```text
{storageNamespace}/{yyyy}/{MM}/{assetId:N}{extension}
```

Default metadata stamps written with every object:
- `osa-asset-id`
- `osa-storage-namespace`
- `osa-sha256`

## Configurable Naming

With no naming configuration, the existing default remains
`{storageNamespace}/{yyyy}/{MM}/{assetId:N}{extension}`. A host can still register its own
`IObjectKeyStrategy` before `AddObjectStorageAsset`; the library uses `TryAdd` and preserves it.

Built-in templates use the strongly typed `ObjectKeyToken` enum and the read-only
`ObjectKeyTokens.Definitions` catalog. Supported tokens are `StorageNamespace`, `AssetId`,
`OriginalFileNameStem`, `Extension`, `Year`, `Month`, `Day`, `Hour`, `Minute`, `Second` and
`Millisecond`. Dates use UTC and invariant formatting (`HH` is 24-hour time). Include the full
`AssetId` token to preserve uniqueness. Paths reject traversal, control characters and keys over
512 characters.

```csharp
options.Naming.ActiveProfile = new ObjectKeyNamingProfile("documents", 1,
    new ObjectKeyTemplate()
        .Token(ObjectKeyToken.StorageNamespace).Literal("/")
        .Token(ObjectKeyToken.Year).Literal("/")
        .Token(ObjectKeyToken.Month).Literal("/")
        .Token(ObjectKeyToken.Day).Literal("/")
        .Token(ObjectKeyToken.AssetId).Token(ObjectKeyToken.Extension).ToString());
```

Profile id/version pairs are immutable in `ObjectAssetNamingProfiles`. A changed definition requires
a new version. For runtime changes, call `IObjectAssetNamingService.ActivateAsync(newProfile)` to
register the definition before activation. The resolved key and profile version are captured when
the input is staged. Binding a temporary file later never renames it. Imported assets and existing
keys remain unchanged. A custom key strategy owns its configuration/versioning; OSA still persists
its resolved key once.

## SHA-256 Validation

Uploads compute an expected SHA-256 digest incrementally. Computing or importing a digest does not
verify the stored object. New and upgraded records default to `NotChecked`. Provider ETags are
not treated as SHA-256 evidence.

```csharp
options.Integrity.FailureBehavior = Sha256FailureBehavior.Warning; // default
options.Integrity.Triggers = Sha256VerificationTriggers.ExplicitOnly; // default
// Optional: OnRead | AfterUpload. Guard rejects mismatches and failed verification.
var result = await integrityService.VerifyAsync(assetId, cancellationToken);
```

Persisted states are `NotChecked`, `Valid`, `Mismatch` and `Failed`. OSA records attempt/completion
times, actual digest, the observed object version when available, and a bounded failure reason.
It never replaces the expected digest with the observed digest. Missing/invalid expected digests
and provider failures are `Failed`; differing bytes or length are `Mismatch`. Archive and binding
moves retain this evidence.

Warning mode logs and returns the result while allowing available content through. A missing object
still cannot be returned. Guard mode persists the result in an independent transaction before
rejecting access, including when the host transaction later rolls back. Guarded reads verify the
complete staged stream before exposing any bytes. On-read and after-upload verification add storage
I/O; enable them deliberately. Explicit verification in Warning mode returns a `Failed` result for
unavailable content; Guard throws `ObjectAssetIntegrityException` with its result.

Verification inside an explicit OSA transaction or ambient `TransactionScope` uses the caller's
metadata view, including its uncommitted uploads. Evidence is committed independently to
`ObjectAssetIntegrityEvidence`, which intentionally has no asset foreign key. The evidence write
does not read or update the caller-locked asset row. Verification never saves unrelated pending
caller changes. Guard still rejects invalid content before returning bytes or accepting the upload;
Warning retains its existing behavior. Normal API calls and defaults remain unchanged.

Transaction-completion and connection-close handling synchronize applicable evidence into the
asset's validation fields after commit or rollback. Results are matched by asset, expected hash,
size, storage identity and requested version; older results cannot replace newer evidence. Evidence
for a rolled-back new asset remains available for investigation even though its asset row is absent.
The record and paged APIs expose pending evidence automatically. Direct SQL consumers of the cached
asset columns can observe a delay whenever synchronization is deferred. Large transactions and
completion time/batch limits can also leave pending work; enable maintenance to finish it.

The existing maintenance worker also processes pending evidence and prunes resolved evidence in
bounded batches, skipping locked assets immediately. It does not download or rehash objects.
`options.Integrity.EvidenceRetention` defaults to 30 days after resolution, and
`options.Maintenance.IntegrityEvidenceInterval` defaults to the shared maintenance interval.
Completion synchronization works with maintenance disabled; enable maintenance for periodic crash
recovery and retention. Identical load-balanced pods can all enable it; the database lock and shared
schedule select the runner. This feature requires `20261025183907_AddIntegrityEvidence` and
`20261025204842_ScaleIntegrityMaintenance` in addition to the lifecycle upgrade below.

Evidence uses pooled connections with ambient enlistment disabled. A batch coalesces matching results
for each asset into its newest applicable validation update and retains every individual audit row.
Pending rows are ordered by persisted retry due time and id. Locked/failed rows receive bounded
exponential retry delays (one second through five minutes by default), allowing other assets to progress.
New failures are excluded from the remainder of that maintenance cycle. Pending evidence never expires.

Evidence recovery and pruning have independent batch sizes and separate time budgets. Defaults permit
up to ten batches of 200 pending rows and ten batches of 1,000 expired resolved rows per run. Each phase
has a five-second budget, checked between asset transactions or delete commands; in-flight work can
finish after the deadline. Retention makes resolved rows eligible for deletion; actual retention can
be longer when cleanup capacity falls behind. Size intervals/capacity above the expected arrival rate.

Both synchronous `SaveChanges()` and asynchronous `SaveChangesAsync()` use their corresponding
database APIs and share lifecycle planning. Synchronous saving does not block on an asynchronous
implementation, so it also works under a single-thread synchronization context.

Staging defaults to a 4 MiB per-stream memory threshold, 16 MiB aggregate retained memory, four
concurrent staging operations, 1,024 retained streams, 5 GiB per file and 20 GiB aggregate retained
bytes. Configure these through `options.Buffering`, including a writable temporary directory. Large
streams spill to disk and files are removed on disposal. Dispose returned content streams promptly.
Providers can implement optional `IObjectStorageCopyProvider` to stream directly into bounded staging;
legacy providers remain supported but control their own initial download buffering.

## Binding and External Ownership

| Binding kind | Payload table | Meaning |
| --- | --- | --- |
| `LocalOwner` | `ObjectAssets` | Bound to an owner in this OSA instance |
| `ExternalOwner` | `ObjectAssets` | Owner is in another OSA instance/environment |
| `LegacyUnresolved` | `ObjectAssets` | Legacy registration lacks sufficient ownership provenance |
| `Temporary` | `ObjectAssetTemporary` | Awaiting local binding, subject to temporary expiry |

Deleted payloads move to the archive regardless of their binding kind. `Managed`/`Referenced` governs
physical deletion authority independently of owner location. External owner keys never populate local
owner columns or match local-owner reads. Imported records are not treated as expiring temporary files.

Configure persistent `LocalInstanceId` and `StorageAuthorityId` GUIDs; do not generate them on startup.
An OSA instance identifies an owner environment; a storage authority identifies a physical storage
configuration. Multiple instances may use one authority. Map other trusted authority ids with
`StorageAuthorityAliases[id] = "provider-name"` and register that name as a keyed
`IObjectStorageProvider`. Unknown authorities are rejected; OSA never guesses an endpoint from an id.
Legacy authority `Guid.Empty` resolves to the configured default provider.

Existing `ObjectAssetDescriptor` and `IObjectAssetRegistry` contracts remain version 2. Use the explicit
`IObjectAssetExchangeRegistry` v3 methods for origin, current owner, storage version and naming
provenance. Origin is preserved through forwarding and rebinding. Validation status is local evidence
and is not trusted from a peer.

For old peers, continue sending v2 payloads. They cannot consume v3. On receipt, use
`RegisterLegacyDescriptorAsync(request, peerContext)` when the host knows the peer owner, origin or
storage authority. A bare v2 registration becomes `LegacyUnresolved`; it stays permanent. A v2 round
trip preserves richer provenance already known locally and rejects conflicting content/ownership.
Unknown historical origin cannot be inferred by the upgrade; resolve it explicitly before v3 export.
Use `IObjectAssetOwnerBinder.BindRegisteredAsync(...)` to attach a registered asset locally without
copying its bytes.

## Paged and Archive Reads

Existing reader APIs remain available. For list endpoints, prefer SQL-computed summaries and
`IObjectAssetPagedReader` owner, temporary, external-owner and archive methods. Pages use keyset
cursors with a maximum size of 500. Pass the returned `Next` cursor to continue; concurrent writes
do not provide a snapshot across pages. Owner and temporary pages sort by creation time/id; archive
pages sort by deletion time/id.

`GetRecordAsync(assetId)` follows the stable identity transparently. Normal content reads exclude
deleted assets. `OpenArchivedReadAsync(assetId)` explicitly opens retained archive content; it returns
null after physical deletion. Hosts do not need to union lifecycle tables.

The SQL query bridge maps only permanent local-owner metadata and remains excluded from host
migrations. Hosts with asset foreign keys should reference `ObjectAssetIdentityQueryModel.AssetId`,
mapped to `ObjectAssetIdentity`, so references survive temporary binding and archival.

## Upgrade from v0.1.6

The upgrade includes schema expansion, identity reservation, resumable data movement, validation,
and supporting indexes. Apply the entire migration sequence before starting the new runtime.

The library owns its migrations and `[configuredSchema].[__EFMigrationsHistory]`; keep the host's
history separate. Use a verified backup and stop **all old and new writers**, including maintenance,
throughout the upgrade. Mixed binaries may exchange supported descriptors after deployment but
must not write the same database during the split.

1. Use one migration runner with the same connection and schema configuration as the host. Generate
   an EF migration bundle or reviewed idempotent script from `ObjectStorageAssetDbContext`.
2. Apply through `20261021052301_ReserveAssetIdentities`. This expands the schema and backfills stable
   identities while leaving legacy payloads in place.
3. If the host has foreign keys to `ObjectAssets.Id`, generate/apply a **host EF migration** that
   drops those relationships and recreates them against `ObjectAssetIdentity.AssetId`. Keep OSA tables
   excluded from host migrations. No host FK change is needed when the host has no such relationships.
4. Apply `20261021052310_SplitAssetLifecycle`, `20261021052320_ValidateAssetLifecycle` and all subsequent
   index/deletion-intent migrations, `20261025183907_AddIntegrityEvidence` and
   `20261025204842_ScaleIntegrityMaintenance`. The split will
   reject remaining payload-table FKs. Temporary rows move to their table and deleted rows to archive;
   externally registered or ambiguous unbound records stay permanent. Counts and locations must pass
   validation before startup is allowed.
5. Configure stable instance/authority ids, peer mappings and desired naming/integrity options. Start
   new writers. Identical pods can all enable maintenance using the shared SQL lock and task schedule.
   Preserve the v0.1.6 expired-binding guard.

The repository design-time factory reads `ConnectionStrings__ObjectStorageAsset` from environment
configuration. Its migration command timeout defaults to 600 seconds. Supply the actual configured
schema with `--schema`; increase `--command-timeout` for the measured workload. No connection or
credentials belong in source control.

```powershell
dotnet ef database update 20261021052301_ReserveAssetIdentities --project src/Infrastructure --startup-project src/Infrastructure --context ObjectStorageAssetDbContext -- --schema osa --command-timeout 1800
# Apply the host FK migration here, if needed.
dotnet ef database update --project src/Infrastructure --startup-project src/Infrastructure --context ObjectStorageAssetDbContext -- --schema osa --command-timeout 1800
```

The split uses committed batches of 1,000 and checkpoints so interrupted work can resume without
duplicating ids. Existing hashes remain expected digests; validation defaults to `NotChecked`. Existing
object keys and custom metadata are preserved. No upgrade operation should contact or rename storage
objects. Downgrade after moving data is blocked; restore a verified backup to return to the old layout.
Do not edit previously published/applied migration IDs.

Temporary bindings without an expiry remain non-expiring, as in v0.1.6. Legacy pending/failed deletes
lack an original physical-delete decision. Their first maintenance attempt chooses physical deletion
only when both physical-delete options allow it; otherwise content is retained. New deletion requests
persist their decision and retries honor it, independently of later configuration changes.

## MinIO Provider

Register the MinIO provider separately from the core package:

```csharp
services.AddObjectStorageAssetMinio(options =>
{
    options.Endpoint = "localhost:9000";
    options.AccessKey = "minioadmin";
    options.SecretKey = "minioadmin";
    options.BucketName = "osa-dev";
    options.AutoCreateBucket = true;
    options.UseSsl = false;
});
```

Current MinIO notes:
- package version: `Minio 7.0.0`
- access key and secret key are required
- bucket auto-creation is opt-in
- upload metadata is written as MinIO headers with `x-amz-meta-...` keys

## Write Workflow

Host applications stage file changes against runtime owner instances and then call one coordinated save:

```csharp
var order = new Order { Number = request.Number };
dbContext.Orders.Add(order);

var assets = assetSessionFactory.For(order);
await assets.SetSingleAsync(
    OrderAssets.Invoice,
    fileStream,
    "invoice.pdf",
    "application/pdf",
    metadata: new Dictionary<string, string>
    {
        ["lang"] = "ar",
        ["usage"] = "attachment"
    },
    ct: cancellationToken);
await assets.AddAsync(
    OrderAssets.Attachments,
    attachmentStream,
    "proof.txt",
    "text/plain",
    metadata: new Dictionary<string, string>
    {
        ["variant"] = "secondary"
    },
    ct: cancellationToken);

await assetCoordinator.SaveChangesWithAssetsAsync(dbContext, cancellationToken);
```

Current command-side behavior:
- host entity changes are saved first so DB-generated keys are materialized before asset binding
- new assets are persisted as `PendingUpload`, uploaded to the provider, then finalized as `Active`
- failed uploads are retained as `UploadFailed`
- single-slot replacement deletes the previous asset before inserting the new one
- optional custom metadata is captured at upload time and becomes immutable descriptor state
- intentional delete follows the configured logical/physical delete mode

Temporary uploads can be stored before the real owner key exists:

```csharp
var temporaryBindingId = Guid.NewGuid();
var temporaryAssets = tempSessionFactory.For<Order>(temporaryBindingId, DateTimeOffset.UtcNow.AddMinutes(30));
var invoice = await temporaryAssets.SetSingleAsync(
    OrderAssets.Invoice,
    fileStream,
    "draft-invoice.pdf",
    "application/pdf",
    metadata: new Dictionary<string, string>
    {
        ["lang"] = "ar",
        ["groupid"] = Guid.NewGuid().ToString("D")
    },
    cancellationToken: cancellationToken);

await hostDbContext.SaveChangesAsync(cancellationToken);
await bindingFinalizer.FinalizeTemporaryBindingAsync(temporaryBindingId, order, cancellationToken);
```

Current temporary-binding behavior:
- uploads are persisted immediately and receive a stable `AssetId` up front
- finalization rejects expired temporary bindings before applying an owner binding
- expired temporary bindings are cleaned by maintenance when they are not finalized in time
- custom metadata survives from temporary upload through finalization without rebinding changes
- finalization preserves the same `AssetId`, moves the binding onto the concrete owner, and clears delete-timer fields so the asset becomes permanent

## Read Workflow

Metadata reads stay separate from file content reads:

```csharp
var invoice = await assetReader.GetSingleAsync(order, OrderAssets.Invoice, cancellationToken);
var attachments = await assetReader.GetManyAsync(order, OrderAssets.Attachments, cancellationToken);
var content = await assetContentReader.OpenReadAsync(invoice!.AssetId, cancellationToken);
```

Batch list endpoints can use the reader directly:

```csharp
var orderIds = orders.Select(x => x.Id).ToArray();
var invoices = await assetReader.GetSinglesAsync<Order, int>(orderIds, OrderAssets.Invoice, cancellationToken);
var summaries = await assetReader.GetSummariesAsync<Order, int>(orderIds, OrderAssets.Attachments, cancellationToken);
```

Recommended frontend payload shape:
- entity fields from the host query
- file reference metadata from OSA
- host-generated download URLs

Descriptor reads and portable exports use `IObjectAssetRegistry`:

```csharp
var descriptor = await assetRegistry.GetDescriptorAsync(assetId, cancellationToken);
var descriptors = await assetRegistry.GetDescriptorsAsync(assetIds, cancellationToken);
```

Only finalized, non-temporary active assets can be exported as descriptors.

Descriptor registration supports:
- add if missing
- ignore if identical
- reject when the same `AssetId` conflicts on descriptor metadata, custom metadata, or ownership mode

Descriptor payload shape:
- `AssetId`
- `FileName`
- `ContentType`
- `Length`
- `Hash`
- `Bucket`
- `ObjectKey`
- `CreatedAtUtc`
- `ExpiresAtUtc` (`null` for current finalized descriptors; retained for compatibility)
- `Metadata`
- `DescriptorVersion`

Custom metadata bag rules:
- optional string key/value dictionary
- host-agnostic and not interpreted by OSA
- exported, imported, and returned on descriptor reads
- normalized for conflict-safe comparison on shared `AssetId`

## Automated Test Infrastructure

Workflow tests use:
- real SQL Server metadata persistence
- a reusable in-memory MinIO-like provider in `test/TestSupport/TestDoubles/InMemoryMinioLikeObjectStorageProvider.cs`

The in-memory provider follows the same object identity model as MinIO:
- object uniqueness is `bucket + object key`
- same object key in the same bucket overwrites
- same object key in different buckets is independent

Default SQL test settings:
- server: `10.0.2.2,1433`
- user: `sa`
- password: `Password@123`

Environment overrides:
- `OSA_TEST_SQL_SERVER`
- `OSA_TEST_SQL_USER`
- `OSA_TEST_SQL_PASSWORD`

## In-Memory E2E

A SQL-backed end-to-end runner that uses the in-memory MinIO-like provider is available at:
- `test/InMemoryE2E/Program.cs`

Default in-memory E2E targets:
- SQL Server: `10.0.2.2,1433`
- bucket: `osa-inmemory-e2e`

In-memory E2E environment overrides:
- `OSA_INMEMORY_E2E_SQL_SERVER`
- `OSA_INMEMORY_E2E_SQL_USER`
- `OSA_INMEMORY_E2E_SQL_PASSWORD`
- `OSA_INMEMORY_E2E_SQL_DATABASE`
- `OSA_INMEMORY_E2E_BUCKET`

Typical local flow:

```powershell
dotnet run --project .\test\InMemoryE2E\InMemoryE2E.csproj
```

This path gives you a full host-style OSA workflow against real SQL Server without requiring a live MinIO container.

## SQL Query Bridge

Hosts can opt into a compact SQL-level projection model:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyObjectStorageAssetQueryModel("osa");
    base.OnModelCreating(modelBuilder);
}
```

Recommended usage is correlated subqueries, which remain compact and avoid null-materialization issues from left-joining custom projection rows:

```csharp
var invoiceRefs = dbContext.ObjectAssetSingleRefsFor<Order, int>(bindingCatalog, OrderAssets.Invoice);
var attachmentSummaries = dbContext.ObjectAssetSlotSummariesFor<Order, int>(bindingCatalog, OrderAssets.Attachments);

var query = dbContext.Orders.Select(order => new
{
    order.Id,
    order.Number,
    InvoiceFile = invoiceRefs
        .Where(invoice => invoice.OwnerKey == order.Id)
        .Select(invoice => invoice.FileName)
        .FirstOrDefault(),
    AttachmentCount = attachmentSummaries
        .Where(summary => summary.OwnerKey == order.Id)
        .Select(summary => (int?)summary.Count)
        .FirstOrDefault() ?? 0
});
```

## Maintenance and Reconciliation

Asset mutations reserve identity locks before writing payload rows. Tracked point lookups reserve an
update lock; metadata-only point lookups use `tracking: false`. Saves lock every affected identity
exclusively before payload writes, using SQL key order in one parameterized batch. This also protects
pending-delete updates before their eventual archive move. Unrelated existing asset identities can
still be processed concurrently.

Normal session/coordinator APIs handle these reservations internally. Custom code that owns an OSA
transaction can use `FindAssetForUpdateAsync(id)` for a single asset. For several assets, call
`ReserveAssetsForUpdateAsync(allIds)` before the first payload read or write, then load and change them.
Reserve the complete set up front when one outer transaction spans several operations; separately
reserving different subsets in opposing orders can still deadlock. Do not promote a metadata-only
lookup to a mutation later in the same transaction. Keep caller-owned transactions short and arrange
host-table locks consistently with OSA locks; locks remain held until the outer transaction completes.

An OSA-owned save retries deadlocks during identity-lock acquisition at most three times with short
jittered delays, before any payload write. It does not replay provider operations or retry a partially
executed save. A deadlock in a caller-owned transaction is propagated: roll back and retry the complete
caller unit with fresh contexts, rather than continuing the aborted transaction. Default OSA saves use
short metadata transactions; an outer caller transaction determines its own lock lifetime.

Lifecycle maintenance is exposed through `IObjectAssetMaintenanceService`:
- `ExpireAssetsAsync`
- `ProcessPendingDeletesAsync`
- `RetryDeleteFailuresAsync`
- `ReconcileAsync`

Reconciliation now also surfaces:
- expired temporary assets
- unbound/orphan assets
- referenced-only assets

The worker is disabled by default. It can be enabled on every identical pod. A SQL session application lock,
scoped to the database and configured schema, prevents overlapping manual or scheduled maintenance
across pods. A contender skips the cycle. The lock uses a dedicated non-pooled connection and is
released when that connection closes. This is a maintenance lock; normal asset writes use transactions,
unique constraints and rowversion checks.

`ObjectAssetMaintenanceState` stores task due times, failures/backoff and last success. After acquiring
the lock, each pod checks that same schedule, so pod restarts or another pod acquiring the released lock
do not multiply task frequency. Use matching configuration on all pods. Task execution is idempotent:
a crash after processing but before checkpointing can repeat work on the next attempt.

Expiry, pending-delete, retry-delete, integrity-evidence and evidence-observation tasks execute sequentially, each in a fresh DI scope. A task
failure is logged and does not block subsequent tasks; cancellation stops the cycle. Each task handles
at most one bounded batch per cycle for deletion tasks; evidence has the separate limits above. Defaults
for deletion are 200 records, four concurrent provider operations,
ten delete attempts, and exponential backoff with jitter from one minute up to twelve hours. Exhausted
retries remain visible with `DeleteRetrySuspended = true`; an operator must investigate before resetting
the retry state. Failures created in a cycle are excluded from that cycle's retry task.

```csharp
options.EnableMaintenanceWorker = true; // same setting on every pod
options.MaintenanceInterval = TimeSpan.FromMinutes(15); // shared default
options.Maintenance.ExpiryInterval = TimeSpan.FromMinutes(5);
options.Maintenance.PendingDeleteInterval = TimeSpan.FromMinutes(2);
options.Maintenance.RetryInterval = TimeSpan.FromMinutes(1);
options.Maintenance.BatchSize = 200;
options.Maintenance.ProviderConcurrency = 4;
options.Maintenance.IntegrityEvidenceInterval = TimeSpan.FromMinutes(1);
options.Maintenance.Evidence.BatchSize = 200;
options.Maintenance.Evidence.PurgeBatchSize = 1000;
options.Maintenance.Evidence.MaximumBatchesPerRun = 10;
options.Maintenance.Evidence.TimeBudget = TimeSpan.FromSeconds(5); // each phase
options.Maintenance.Evidence.MetricsInterval = TimeSpan.FromMinutes(5);
```

`IObjectAssetMaintenanceRunner.RunOnceAsync()` uses the same lock, due-time checks and failure isolation.
The `Elf.ObjectStorageAsset.Maintenance` meter exposes processed counts, failures, skipped cycles,
duration and time since success. Configure your logging/metrics exporter in the host.

The `Elf.ObjectStorageAsset.Integrity` meter exposes verification and batch duration, appended/resolved/
deferred/purged evidence and actual asset cache updates. Gauges expose pending count, oldest pending age,
expired resolved count and observation age (`osa.integrity.evidence.pending`, `.oldest_pending_age`,
`.expired`, `.observation_age`). Backlog aggregates run on the shared observation schedule, using the
narrow evidence indexes and a five-second command timeout; they never scan asset payloads. The sample
is persisted in the shared task row and refreshed in each pod when it reads that schedule. Gauge values
describe the shared database: use the latest observation across pods rather than summing duplicate
counts, and alert on stale observations as well as backlog growth. No asset ids are used as metric tags.

`ReconcileAsync()` is explicit; the hosted worker never calls it. It reports registered assets only,
including missing objects, expired temporary bindings, external ownership, legacy unresolved records
and referenced assets. It never enumerates a bucket or deletes storage-only objects. For large datasets,
use `IObjectAssetReconciliationReader.ReconcilePageAsync()` and persist the returned cursor. The legacy
full-report API collects all report ids in memory for compatibility.

`AutoApplyMigrations` can initialize a new database. Existing databases needing the lifecycle split must
use the explicit upgrade procedure above. Startup readiness rejects pending migrations or an incomplete
split, even when automatic migration is disabled.

Current lifecycle states:
- `PendingUpload`
- `Active`
- `UploadFailed`
- `PendingDelete`
- `Deleted`
- `DeleteFailed`

## Live E2E

A Docker-backed MinIO E2E test project is available at:
- `test/DockerMinioE2ETests/DockerMinioE2ETests.cs`

Current Docker-backed MinIO E2E behavior:
- starts a fresh MinIO container per test
- avoids reserved ports such as `9000`, `1433`, `5672`, `15672`, and `6379`
- allocates MinIO host ports from the `19100-19999` range
- tears down the container after the run
- reuses the existing live OSA workflow against real SQL Server and real MinIO

Optional Docker-backed MinIO test host override:
- `OSA_DOCKER_MINIO_HOST_ADDRESS`

Typical local flow:

```powershell
dotnet test .\test\DockerMinioE2ETests\DockerMinioE2ETests.csproj
```

This path is intended for real MinIO validation without relying on your long-lived local containers or conflicting with Portainer on port `9000`.

## Smoke Test

A smoke-test runner that uses the persistent local MinIO container and keeps the SQL database is available at:
- `test/SmokeTest/Program.cs`
- `test/SmokeTest/run-smoke-test.ps1`

Default smoke-test behavior:
- starts or reuses the persistent local MinIO container through `start-local-minio.ps1`
- uses SQL Server `10.0.2.2,1433`
- uses MinIO endpoint `10.0.2.2:19000`
- creates a database named `ObjectStorageAsset_SmokeTest_yyyyMMddHHmmss`
- preserves the SQL database after completion
- preserves provider objects by default
- skips the end-of-run delete workflow so the created records remain inspectable

Smoke-test environment overrides:
- `OSA_SMOKE_SQL_SERVER`
- `OSA_SMOKE_SQL_USER`
- `OSA_SMOKE_SQL_PASSWORD`
- `OSA_SMOKE_SQL_DATABASE`
- `OSA_SMOKE_STORAGE_NAMESPACE`
- `OSA_SMOKE_MINIO_ENDPOINT`
- `OSA_SMOKE_MINIO_ACCESS_KEY`
- `OSA_SMOKE_MINIO_SECRET_KEY`
- `OSA_SMOKE_BUCKET`
- `OSA_SMOKE_MINIO_AUTO_CREATE_BUCKET`
- `OSA_SMOKE_MINIO_USE_SSL`
- `OSA_SMOKE_PRESERVE_OBJECTS`
- `OSA_SMOKE_SKIP_DELETE_WORKFLOW`

Typical local flow:

```powershell
powershell -ExecutionPolicy Bypass -File .\test\SmokeTest\run-smoke-test.ps1
```

The smoke-test database is printed to the console and is not deleted automatically.

## Live E2E

A real end-to-end runner is available at:
- `test/LiveE2E/Program.cs`

Local helper scripts:
- `test/LiveE2E/start-local-minio.ps1`
- `test/LiveE2E/stop-local-minio.ps1`
- `test/LiveE2E/reset-local-minio.ps1`

Default live E2E targets:
- SQL Server: `10.0.2.2,1433`
- MinIO API: `10.0.2.2:19000`
- MinIO console: `10.0.2.2:19001`
- bucket: `osa-live-e2e`
- MinIO credentials: `minioadmin / minioadmin`
- container: `osa-live-minio`
- volume: `osa-live-minio-data`

The local helper now creates a long-lived MinIO container:
- Docker restart policy: `unless-stopped`
- data persisted in the named Docker volume `osa-live-minio-data`
- `start-local-minio.ps1` reuses the existing container instead of recreating it
- `stop-local-minio.ps1` stops the container without deleting data
- `reset-local-minio.ps1` removes both the container and its volume when you want a clean state

Live E2E environment overrides:
- `OSA_E2E_SQL_SERVER`
- `OSA_E2E_SQL_USER`
- `OSA_E2E_SQL_PASSWORD`
- `OSA_E2E_SQL_DATABASE`
- `OSA_E2E_STORAGE_NAMESPACE`
- `OSA_E2E_MINIO_ENDPOINT`
- `OSA_E2E_MINIO_ACCESS_KEY`
- `OSA_E2E_MINIO_SECRET_KEY`
- `OSA_E2E_BUCKET`
- `OSA_E2E_MINIO_AUTO_CREATE_BUCKET`
- `OSA_E2E_MINIO_USE_SSL`
- `OSA_E2E_PRESERVE_DATABASE`
- `OSA_E2E_PRESERVE_OBJECTS`
- `OSA_E2E_SKIP_DELETE_WORKFLOW`

Persistent local MinIO helper overrides:
- `OSA_LIVE_MINIO_CONTAINER_NAME`
- `OSA_LIVE_MINIO_VOLUME_NAME`
- `OSA_LIVE_MINIO_IMAGE`
- `OSA_LIVE_MINIO_API_PORT`
- `OSA_LIVE_MINIO_CONSOLE_PORT`
- `OSA_LIVE_MINIO_HOST_ADDRESS`
- `OSA_LIVE_MINIO_ROOT_USER`
- `OSA_LIVE_MINIO_ROOT_PASSWORD`

Typical local flow:

```powershell
powershell -ExecutionPolicy Bypass -File .\test\LiveE2E\start-local-minio.ps1
dotnet run --project .\test\LiveE2E\LiveE2E.csproj
```

To stop the persistent local MinIO without deleting data:

```powershell
powershell -ExecutionPolicy Bypass -File .\test\LiveE2E\stop-local-minio.ps1
```

To remove the persistent local MinIO container and its stored data:

```powershell
powershell -ExecutionPolicy Bypass -File .\test\LiveE2E\reset-local-minio.ps1
```

## Verification

The lifecycle enhancement validation includes SQL concurrency and upgrade/recovery tests, actual
v0.1.6 binary interop, 64 MiB generated-stream spill/hash/cleanup tests, the enhanced in-memory E2E,
and real SQL + MinIO E2E with stored-content tampering and Guard rejection.

The optional scale scenario uses `OSA_SCALE_MAIN_ROWS=1000000`, 1,000 owners and a 100,000-record
expired temporary backlog. One LocalDB run returned 50 owner summaries in about 1.9 seconds and a
25-item owner page in 104 ms; expiry handled 200 records in about 3.1 seconds and tracked only those
200 payloads. The synthetic bulk seed took about 12m50s. These local cold-query observations include
index/storage costs and are not production latency targets; size indexes and transaction-log capacity
against the host's actual owner distribution and workload.

The separate evidence benchmark starts with 100,000 expired resolved rows and 5,000 pending rows
across 1,000 recovery assets, while four producers perform 2,000 real transactional SHA verifications
of 1 KiB files through the in-memory storage provider. A LocalDB run on 2026-10-25 completed producers
in 9.95 seconds (201.1 verifications/second), with transaction p50 14.62 ms and p95 45.82 ms. Recovery
and retention drained both backlogs in 36.92 seconds total. The journal reused five physical SQL
connections across 4,717 opens. That run used 250 ms evidence intervals, 200-row apply batches,
1,000-row purge batches, ten batches maximum and a 500 ms budget per phase. These are synthetic local
observations with small files and no production baseline; measure your own file sizes, asset distribution,
SQL latency and sustained arrival rate before choosing settings. Existing pending evidence is preserved
even when it is too old for resolved-row retention.

```powershell
$env:OSA_EVIDENCE_BENCHMARK_OPERATIONS = '2000'
$env:OSA_EVIDENCE_BENCHMARK_BACKLOG = '100000'
dotnet test test/UnitTests/UnitTests.csproj --configuration Release --filter 'FullyQualifiedName~SustainedEvidence_Should'
```

The benchmark uses the same disposable SQL fixture as the workflow suite; it is excluded from regular
CI by the `Scale` category. It must use isolated test SQL, never an existing application database.

Current verification set:
- `dotnet build ObjectStorageAsset.sln -m:1`
- `dotnet test test/UnitTests/UnitTests.csproj -m:1`
- `dotnet test test/DockerMinioE2ETests/DockerMinioE2ETests.csproj -m:1`
- `dotnet run --project test/ConsoleTest/ConsoleTest.csproj`
- `dotnet run --project test/InMemoryE2E/InMemoryE2E.csproj`
- `powershell -ExecutionPolicy Bypass -File test/SmokeTest/run-smoke-test.ps1`
- `powershell -ExecutionPolicy Bypass -File test/LiveE2E/start-local-minio.ps1`
- `dotnet run --project test/LiveE2E/LiveE2E.csproj`
