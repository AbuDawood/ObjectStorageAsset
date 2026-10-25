# Contributing Guide

This document defines the contribution conventions for `Elf.ObjectStorageAsset`.

## 1) Scope and Principles

- Keep architecture boundaries strict.
- Keep the host-facing API compact and explicit.
- Prefer deterministic, production-safe behavior over shortcuts.
- Keep slices small and independently verifiable.

## 2) Repository Layout

- `src/Helpers`: shared helper utilities
- `src/Domain`: aggregates, enums, value objects, and abstractions
- `src/Application`: public contracts and orchestration-facing interfaces
- `src/Infrastructure`: EF Core, migrations, workers, and orchestration implementations
- `src/Bootstrap`: public registration entry points
- `src/Minio`: MinIO provider package
- `test/ConsoleTest`: runnable sample host
- `test/DockerMinioE2ETests`: Docker-backed MinIO E2E tests
- `test/InMemoryE2E`: SQL-backed E2E runner using the in-memory MinIO-like provider
- `test/LiveE2E`: SQL-backed E2E runner using a real MinIO endpoint
- `test/SmokeTest`: local MinIO + SQL Server smoke runner that preserves the database
- `test/TestSupport`: reusable test-only infrastructure and storage doubles
- `test/UnitTests`: automated tests

## 3) Dependency Rules

- `Domain` -> `Helpers`
- `Application` -> `Domain`, `Helpers`
- `Infrastructure` -> `Application`
- `Bootstrap` -> `Application`, `Infrastructure`
- `Minio` -> `Application`

Do not add references that break these boundaries.

## 4) Platform Rules

- Target framework: `net8.0`
- EF Core version: `8.0.11`
- SQL Server is the persistence provider
- Public namespaces must start with `Elf.ObjectStorageAsset`

## 5) Slice Workflow

For every slice:
1. Implement the slice.
2. Add tests for the slice.
3. Fix until tests pass.
4. Update `README.md` and `CONTRIBUTING.md`.
5. Move to the next slice.

## 6) Binding Contract Rules

- Every host entity that owns files must have one explicit `IObjectAssetBinding<T, TKey>` contract.
- Every contract must declare:
  - one stable `OwnerType`
  - one key selector
  - at least one slot
- Slot names must be unique within one owner contract.
- Prefer static slot fields over raw string duplication in host code.

## 7) Persistence Rules

Content availability is separate from lifecycle and SHA validation. Confirmed object/version absence
uses `ObjectStorageObjectNotFoundException`; transient, authorization and missing-bucket failures must
propagate. Persist only one current availability observation per asset and bind it to exact content
identity. Do not add an asset FK or write availability inside a caller transaction: missing detection
must survive rollback without waiting on that caller's asset locks. Normal reads short circuit known
missing content; restoration is an explicit recheck, and missing archival is explicit and preserves
stable references. Validate both provider download paths, serializable caller locks, concurrent/older
observations, schema-aware migrations and real-MinIO deletion/restoration in the existing E2E runners.

- The persistence root is `ObjectAsset`.
- Migrations live under `src/Infrastructure/Persistence/Migrations`.
- Do not hardcode `dbo`; always use the configured schema.
- Enum-backed persisted properties must expose computed `...Label` columns.
- Reads and deletes must use persisted `BucketName`, `StorageNamespace`, and `ObjectKey`; never rebuild old object paths from current settings.

## 8) Command and Read Rules

- Host writes must go through `IObjectAssetSessionFactory` plus `IObjectAssetCoordinator`.
- Do not call raw `SaveChangesAsync()` when staged file work is pending; use `SaveChangesWithAssetsAsync(...)`.
- `SetSingleAsync(...)` is only for single slots.
- `AddAsync(...)` is only for multi-file slots.
- Content reads must stay separate from metadata reads.
- Prefer `IObjectAssetReader` for business/application queries and `IObjectAssetContentReader` only when bytes are really needed.

## 9) Query Bridge Rules

- The host must opt in explicitly with `ApplyObjectStorageAssetQueryModel(...)`.
- The query bridge is metadata-only; it does not expose provider SDK types or direct MinIO access.
- Prefer correlated subquery projections over left-joining custom bridge DTOs.
- Keep large list endpoints summary-oriented:
  - single-file refs where needed
  - attachment counts / existence flags
  - full multi-file lists only for detail endpoints

## 10) Lifecycle and Maintenance Rules

- Upload failures must be preserved as `UploadFailed`; do not silently drop them.
- Delete failures must be preserved as `DeleteFailed`; retry through maintenance/reconciliation.
- Expiration uses the same delete lifecycle as intentional delete.
- Physical deletion behavior must respect:
  - `PhysicallyDeleteRequestedAssets`
  - `PhysicallyDeleteExpiredAssets`
- If background maintenance is enabled, keep the worker idempotent and safe to rerun.

## 11) Test Rules

- Workflow tests should use the reusable in-memory MinIO-like provider instead of embedding ad hoc fake storage classes in individual test files.
- Reusable storage doubles must live in `test/TestSupport`, not under a specific test project.
- The in-memory test provider must preserve MinIO-style identity semantics:
  - uniqueness by `bucket + object key`
  - overwrite on same bucket/key
  - independence across buckets
- SQL-backed workflow tests currently target the shared local SQL Server defaults unless overridden:
  - `OSA_TEST_SQL_SERVER`
  - `OSA_TEST_SQL_USER`
  - `OSA_TEST_SQL_PASSWORD`
- SQL-backed in-memory end-to-end validation belongs in `test/InMemoryE2E`.
- Docker-backed MinIO end-to-end validation belongs in `test/DockerMinioE2ETests`.
- Docker-backed MinIO tests must allocate ports outside the reserved local container ports and should prefer the `19100+` host-port range.
- Live MinIO validation belongs in `test/LiveE2E`, not in the default unit-test run.
- Smoke validation that intentionally preserves the SQL database belongs in `test/SmokeTest`.
- The local `start-local-minio.ps1` helper is intended to create a persistent MinIO container with a named Docker volume; destructive cleanup must go through `reset-local-minio.ps1`.

## 12) Provider Rules

- All provider-neutral contracts must stay in `src/Application`.
- Provider packages must not leak SDK-specific types into host-facing APIs.
- `IObjectKeyStrategy` implementations must be deterministic.
- Default object keys must remain namespace-prefixed and date-partitioned unless the host explicitly replaces the strategy.
- Every upload path must preserve OSA metadata stamps:
  - `osa-asset-id`
  - `osa-storage-namespace`
  - `osa-sha256`
- MinIO registration must validate endpoint, bucket name, access key, and secret key before runtime resolution.
- MinIO-specific tests should mock `IMinioClient`; do not require a live MinIO server for unit coverage.

## 13) Lifecycle Split and Integrity

- Keep payloads in exactly one lifecycle table; reserve ids/physical locators in `ObjectAssetIdentity`.
- Use identity-first lookup/locking for single-asset lifecycle transitions. Retain rowversion guards,
  including the v0.1.6 prohibition on expired temporary finalization.
- Owner list queries target permanent local-owner rows. Use SQL counts/compact projections and
  bounded keyset pages; do not materialize every attachment to calculate a count.
- External ownership, immutable origin, storage authority and deletion rights are separate concepts.
  Missing v2 provenance must remain unresolved, never invented or treated as temporary.
- Keep v2 descriptors unchanged. New exchange fields belong in the explicit v3 contract, with old
  binary interoperability coverage. Known origin/naming information cannot be overwritten on return.
- Expected SHA-256 is immutable. Upload hashing does not mean stored bytes were verified.
  Validation defaults to `NotChecked`; Warning is the default policy. Guard must commit failed
  evidence independently before rejecting access and must expose no unverified bytes.
- Persist resolved naming at staging time. Published profile id/version definitions are immutable.
  Never recompute old or temporary asset keys after a configuration change.
- Stage large content in bounded chunks with spill-to-disk, aggregate quotas and deterministic cleanup.
  An optional provider copy operation returns metadata for the bytes actually copied.
- Identical pods can all enable maintenance; use the shared SQL session application lock and persisted
  per-task due times/backoff. Tasks are
  sequential and isolated; database records are processed in bounded batches with provider concurrency.
  Keep backoff and maximum attempts; do not retry a newly failed record again in the same cycle.
- Generate EF schema migrations/snapshots with EF CLI. Data moves need verified source columns,
  resumable committed batches, conservation checks, custom-schema escaping and explicit rollback guards.
  Keep a pause point after identity reservation so host FKs can move before source-row deletion.
- Keep upgrade, interruption/recovery and real-provider E2E tests passing before shipping lifecycle
  changes. Never alter published migration ids or auto-run a large split on application startup.

Additional local validation:

```powershell
$env:OSA_TEST_SQL_SERVER = '(localdb)\MSSQLLocalDB'
$env:OSA_TEST_SQL_INTEGRATED_SECURITY = 'true'
dotnet test test/UnitTests --filter 'FullyQualifiedName~WorkflowTests'
$env:OSA_INMEMORY_E2E_SQL_SERVER = '(localdb)\MSSQLLocalDB'
$env:OSA_INMEMORY_E2E_SQL_INTEGRATED_SECURITY = 'true'
dotnet run --project test/InMemoryE2E
$env:OSA_SCALE_MAIN_ROWS = '1000000'
dotnet test test/UnitTests --filter 'FullyQualifiedName~LargeDataset'
```

These scenarios create and delete uniquely named disposable databases. Do not point them at an
existing host database. `OSA_V016_ASSEMBLY_DIRECTORY` enables actual old-binary v2/v3 compatibility
testing; point it at a build of tag `v0.1.6` (`2d323d419c1d3e380a36efb0d88685eb46c09b5b`).

## Release validation

Synchronous saves must use synchronous provider APIs, including commit/disposal and evidence-completion
handling. Share pure lifecycle planning with async saving; never wrap async saving with GetResult.
Exercise a non-pumping single-thread synchronization context in regression tests.

Transactional SHA verification must use the caller's metadata view and independently persist its
evidence without an asset FK or a lookup against caller-locked rows. Preserve unsaved caller changes,
rollback-resistant evidence, Warning/Guard semantics, ambient transactions and post-commit rowversion
usability. Keep synchronization and retention bounded and test pending-evidence visibility without a worker.
Use pooled, non-enlisted journal connections and reset session settings before returning them. Coalesce
asset cache writes while preserving every evidence row. Keep recovery/purge budgets independent, persist
retry eligibility and never expire pending evidence. Sample indexed backlog aggregates on the shared
schedule. Test different pod service providers against the same SQL database, including restart/backoff.
Run the opt-in sustained evidence benchmark alongside cleanup and report measured workload/latencies;
do not treat a passing deadlock test or the separate payload scale run as evidence throughput proof.

Mutation paths must reserve identity write intent before payload writes. Keep multi-asset acquisition
in SQL key order, including a caller transaction's known replacement/finalization set. Pure reads use
untracked lookups. Retest competing same-asset moves, reversed batches, pending-write/read races and
independent-asset concurrency when changing locking. Never place provider IO inside a new long-lived
database transaction or retry provider side effects as part of database deadlock recovery.

The NuGet workflow gates all six packages on the non-scale unit/SQL suite, pinned v0.1.6 binary
interop, and both E2E runners, using a disposable SQL Server container and a MinIO process built
from pinned official source on the runner.
Run the opt-in scale scenario separately before a lifecycle/performance release. Never substitute
a shared SQL Server or object-storage environment for the workflow's local containers.

Set `VersionPrefix` and the changelog together. Build and pack with the same `/p:Version` value;
validate the output using `.github/scripts/Test-Packages.ps1 -PackageDirectory artifacts/nuget
-Version <version>`. Merge through a PR, then create a matching new `v<VersionPrefix>` tag on `main`.
Only a tag push publishes; PRs and manual workflow runs only upload artifacts. NuGet publication
can partially succeed across packages; the workflow supports retrying the same immutable artifacts
with `--skip-duplicate`, while corrections require a new release version.
