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
