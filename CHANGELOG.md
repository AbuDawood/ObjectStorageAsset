# Changelog

All notable changes to this project are documented in this file.

## Unreleased

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
