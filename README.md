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

The GitHub Actions workflow at `.github/workflows/nuget.yml` restores, builds, and packs `ObjectStorageAsset.Pack.slnf`, which includes only those source projects. The repository `README.md` is embedded into every generated package.

Publishing flow:
- create a `NUGET_API_KEY` repository secret
- push a version tag such as `v0.1.1`
- the workflow publishes the generated `.nupkg` and `.snupkg` files to NuGet.org

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

The persistence slice stores one `ObjectAsset` row per owned file.

Current persistence shape:
- SQL Server + EF Core `8.0.11`
- configurable schema
- schema-aware `__EFMigrationsHistory`
- owner binding columns on `ObjectAssets`
- stable `AssetId` as the immutable primary identifier for every asset row
- optional temporary binding fields and ownership mode on `ObjectAssets`
- immutable custom metadata bag stored as normalized JSON on `ObjectAssets`
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
- temporary bindings expire independently from asset content expiry
- custom metadata survives from temporary upload through finalization without rebinding changes
- finalization preserves the same `AssetId` and only moves the binding onto the concrete owner

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
- `ExpiresAtUtc`
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

Lifecycle maintenance is exposed through `IObjectAssetMaintenanceService`:
- `ExpireAssetsAsync`
- `ProcessPendingDeletesAsync`
- `RetryDeleteFailuresAsync`
- `ReconcileAsync`

Reconciliation now also surfaces:
- expired temporary assets
- unbound/orphan assets
- referenced-only assets

When `EnableMaintenanceWorker` is enabled:
- expired assets are processed on the configured interval
- pending deletes are retried
- delete failures are retried

When `AutoApplyMigrations` is enabled:
- the bootstrap package runs `MigrateAsync()` on startup

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

Current verification set:
- `dotnet build ObjectStorageAsset.sln -m:1`
- `dotnet test test/UnitTests/UnitTests.csproj -m:1`
- `dotnet test test/DockerMinioE2ETests/DockerMinioE2ETests.csproj -m:1`
- `dotnet run --project test/ConsoleTest/ConsoleTest.csproj`
- `dotnet run --project test/InMemoryE2E/InMemoryE2E.csproj`
- `powershell -ExecutionPolicy Bypass -File test/SmokeTest/run-smoke-test.ps1`
- `powershell -ExecutionPolicy Bypass -File test/LiveE2E/start-local-minio.ps1`
- `dotnet run --project test/LiveE2E/LiveE2E.csproj`
