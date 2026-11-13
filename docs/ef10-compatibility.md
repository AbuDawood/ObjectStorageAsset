# .NET 10 / EF Core 10 compatibility qualification

## Failure and affected boundaries

Latest published baseline: **OSA 0.2.1**, source commit
`218b8917fb1baef22985007347558e9f7cf58424`. Upstream main and the `v0.2.1` tag identified that commit
when this work started. There was no newer published OSA version.

A fresh package-reference-only consumer reproduced the reported failure with .NET **10.0.15** and EF
Core / Relational / SQL Server **10.0.15**. Upload, temporary finalization, identical replay, verified
reads and same-length corruption rejection passed first. Both the missing-content read and explicit
availability recheck then threw:

```text
System.TypeLoadException: Could not load type
'Microsoft.EntityFrameworkCore.Query.SetPropertyCalls`1'
from Microsoft.EntityFrameworkCore.Relational, Version=10.0.15.0.
```

EF 10 changed the bulk-update setter API. The published EF 8-compiled binary can load for other paths,
then fail when the CLR resolves this bulk-update signature. Its old minimum-only package requirements
allowed EF 10 to be selected even though the binary was compiled against EF 8. The affected production
call sites are:

| Call site | Operation |
| --- | --- |
| `ObjectAssetAvailabilityStore.RecordAsync` | `ExecuteUpdateAsync` of current availability |
| `ObjectAssetIntegrityJournal.Apply` | `ExecuteUpdate` of retry eligibility/count/error |
| `ObjectAssetIntegrityJournal.ApplyBatchAsync` | `ExecuteUpdateAsync` of retry eligibility/count/error |

The sweep also covered EF-bearing Application/Infrastructure/Bootstrap contracts, provider/Extensions
dependencies, `ExecuteDeleteAsync` evidence pruning, query/model-cache services, the SQL migrations
generator and migration snapshot/history handling. Only the three setter calls have the removed-type
signature; pruning and the remaining EF paths are exercised by the regression suite.

References: [EF 10 bulk-update breaking change](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes#execute-update-expression),
[EF 10 connection-string application name](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes#application-name-is-now-injected-into-the-connection-string).

## Supported graph and impact

The 0.2.2 candidate provides all six packages with `lib/net8.0` and `lib/net10.0` binaries:

| Consumer/package target | EF dependency range | Qualified exact EF version |
| --- | --- | --- |
| .NET 8 / net8.0 | `[8.0.11, 9.0.0)` | 8.0.11 |
| .NET 10 / net10.0 | `[10.0.15, 11.0.0)` | 10.0.15 |

The same source compiles its setters against the appropriate API in each target. NuGet selects the
matching asset; no exception-catching workaround or integrity bypass is used. EF dependencies have
upper bounds to make cross-major substitution unsupported. Keep EF Core/Relational/SqlServer/tools
aligned and update the OSA package set together. EF 9, EF 11 and .NET 10 with EF 8 are not qualified
combinations. A .NET 10 consumer of this candidate selects its EF 10 asset and needs EF >= 10.0.15.

The .NET 8 asset preserves MinIO 7's existing Extensions 9.0.4 minimum; it does not downgrade that vendor
dependency. OSA's .NET 10 references use Extensions 10.0.15. Building the multi-target repository now
requires the .NET 10 SDK (10.0.404 locally); .NET 8 hosts can continue consuming the net8.0 package asset.

EF 10 also validates snapshot drift before migrations. The existing generated snapshot uses the
logical default `osa` schema, whereas a configured custom-schema model uses another schema. The new
`IMigrationsAssembly` implementation resolves that context-scoped snapshot instance to the configured
schema, just as the existing SQL generator resolves schema tokens. It keeps the original generated
snapshot type and published migration IDs. Tests prove that an actual added model property still
produces pending model changes. No warning/exception is suppressed and no generated migration or
snapshot file is edited. There is no schema/data migration for this compatibility change.

The pooling test now supplies an explicit application name so its ADO.NET probes and EF operations
measure the same connection pool under EF 10. Production evidence/availability transaction suppression,
non-enlisted pooling, lock ordering and bounded retry/retention algorithms remain in place.

## Local runtime evidence

Candidate artifacts: **0.2.2-ef10.1**, packed with the same `/p:Version` used for the build. The package
validator passed all six package/symbol pairs, both target folders, internal versions and EF bounds.
Local services were SQL Server LocalDB **17.0.4025.3** and a real MinIO Windows server built with
**Go 1.24.9** (its version stamp is `DEVELOPMENT.GOGET`). CI separately uses its existing SQL Server
2022 container and pinned official MinIO source; the local stamp is not presented as an upstream release tag.

| Validation | Result |
| --- | --- |
| Published 0.2.1 / .NET 10 / EF 10.0.15 | Exact TypeLoadException reproduced in missing read and availability recheck |
| .NET 8.0.33 / EF 8.0.11 non-scale SQL/unit/legacy suite | 134 passed, 0 failed, 0 skipped |
| .NET 10.0.15 / EF 10.0.15 non-scale SQL/unit/legacy suite | 134 passed, 0 failed, 0 skipped |
| Existing SQL-backed in-memory E2E | Passed on net8.0 and net10.0 |
| Existing real MinIO + SQL E2E | Passed on net8.0 and net10.0 |
| Clean net8.0 package consumer | Passed all recovery/identity scenarios |
| Clean net10.0 package consumer | Passed all recovery/identity scenarios |

Each clean consumer is copied to a fresh directory, restores its own package cache, rejects project
references, verifies all six OSA libraries are packages, checks the selected TFM assets, and checks the
entire EF package family is the exact qualified version. Candidate OSA packages are mapped exclusively
to the local artifact feed; the baseline comes exclusively from NuGet.org. Runtime logs report the actual
loaded versions and execute the following against **real SQL Server and MinIO**:

1. Upload and temporary finalization; replay both finalization and the identical descriptor.
2. Read and SHA-verify the exact original bytes.
3. Overwrite storage with different bytes of the same length; Guard rejects them as `Mismatch`.
4. Delete the object; missing reads produce the typed result and persist a single availability row.
5. Restore corrupt bytes and recheck availability. Presence becomes `Available`, but Guard still rejects
   the content; the expected SHA-256 remains unchanged.
6. Restore the exact original bytes, recheck, verify and read successfully. Identity, owner binding,
   origin, locator and expected hash remain unchanged.

Additional SQL tests explicitly force both synchronous and asynchronous evidence retry bulk updates
by holding an asset lock. Custom-schema and migration-conservation tests run on both targets, and offline
model tests check schema isolation, migration identity and genuine drift detection.

Only new `ObjectStorageAssetWorkflow_<guid>` LocalDB databases and private local MinIO objects were used;
test databases/objects were removed afterward. No Request Platform SDK, shared/application database,
release tag or published package was modified. Scale/throughput benchmarks were not run; the change
does not claim a performance improvement. Existing nullable compiler warnings and the existing test-only
SQLite native-package audit warning remain visible; none was suppressed for this qualification.

## Reproduce and review

See [the package consumer instructions](../test/PackageCompatibility/README.md). Main CI now runs both
targets' regression/E2E suites, the published negative reproduction and both packed-candidate consumers
before the existing tag-only publication step. TRX and package restore/build/runtime logs are uploaded
as CI evidence. A PR publishes test/package artifacts only. Merge and a future `v0.2.2` publication remain
subject to explicit approval.
