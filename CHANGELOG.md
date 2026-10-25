# Changelog

All notable changes to this project are documented in this file.

## 0.2.0

### Added
- Separate permanent, temporary and archive payload tables with stable asset identities and explicit binding kinds.
- Paged owner/temporary/external/archive retrieval and resumable reconciliation of registered assets.
- V3 ownership exchange with immutable origin, storage authority mappings and explicit v2 peer adaptation.
- Configurable versioned naming templates with typed placeholders and preserved staging-time keys.
- Persisted SHA-256 verification evidence (`NotChecked` by default), Warning/Guard policies and opt-in read/upload triggers.
- Bounded memory/disk staging and optional provider copy-to-stream support.
- Multi-pod maintenance with one SQL session lock, shared persisted task schedules/backoff and isolated sequential tasks.
- Pooled evidence connections, coalesced validation writes, persisted fair retries and independently bounded multi-batch recovery/pruning.
- Shared-schedule evidence backlog sampling, integrity throughput/latency metrics and a sustained SQL evidence benchmark.
- Real SQL concurrency/integrity/lifecycle coverage, v0.1.6 binary compatibility checks and a configurable SQL scale scenario.
- Release pipeline gates packaging on unit/SQL, old-binary compatibility and real MinIO E2E validation.

### Changed
- Owner summaries use SQL counts and compact projections; registry import and maintenance use bounded batches.
- Existing naming and descriptor v2 defaults remain available; registered external assets remain permanent.
- Existing databases require an explicit staged EF upgrade and host FK retargeting when applicable.
- Release tags must match the package version and identify a commit already merged into `main`; assemblies and packages use the same version.

### Fixed
- Use genuine synchronous saving with shared lifecycle planning, preserving synchronous EF behavior under single-thread synchronization contexts.
- Verify within caller-owned transactions without waiting on their asset locks; retain independently committed SHA evidence across rollback and expose pending results through existing read APIs.
- Synchronize evidence automatically after transaction completion and recover/prune it through bounded maintenance work, preserving content-version checks and unsaved caller changes.
- Reserve write intent for tracked identity lookups and acquire identity locks in SQL key order before payload writes, avoiding the reproduced shared-to-exclusive lock conversion deadlock.
- Cover pending-delete writes as well as archive moves; reserve known multi-asset mutation sets before caller-transaction work.
- Bound pre-write lock acquisition retries without replaying provider operations or hiding caller-owned transaction deadlocks.

### Upgrade
- EF-generated schema migrations include guarded, resumable v0.1.6 data splitting and conservation validation.
- Preserve temporary bindings without expiry; require host FK retargeting before moving referenced payloads.
- Persist physical-delete intent for retries; retain legacy content when the historical decision is ambiguous and policies differ.
- Block destructive downgrade with reserved/moved data; use a verified pre-upgrade backup for recovery.

## 0.1.6

### Fixed
- Reject finalization of expired temporary bindings. This guard is retained by the lifecycle enhancements.

## 0.1.5 - 2026-03-11

### Changed
- Finalized assets now become permanent by clearing delete-timer fields during finalization.
- Expiry maintenance now targets only temporary bindings that were never finalized.
- Descriptor export and "push as reference" are now limited to finalized, non-temporary active assets.
- Finalized descriptor and reference projections normalize `ExpiresAtUtc` to `null` for compatibility.

### Tests
- Added workflow coverage for finalized permanence and finalized-only descriptor export.

## 0.1.4 - 2026-03-08

### Added
- Immutable custom metadata bag support on object assets.
- Descriptor import/export support for custom metadata.
- Conflict-safe metadata normalization for descriptor registration.
- Upload-time metadata capture on regular and temporary asset sessions.

### Infrastructure
- SQL migration for custom metadata persistence.
- Workflow test coverage for metadata round-tripping and conflict detection.

## 0.1.3 - 2026-03-08

### Added
- SQL Server `rowversion` concurrency support on object assets.
- Optimistic concurrency protection for stale object asset updates.

### Infrastructure
- SQL migration for the rowversion column.
- Persistence and workflow test coverage for concurrency handling.
