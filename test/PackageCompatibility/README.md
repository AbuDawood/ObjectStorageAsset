# Package-only SQL / MinIO qualification

This consumer has no project references and shields itself from repository build properties. The runner
copies its four input files to a fresh short directory under `artifacts/pq`, restores into a fresh cache,
checks all six OSA packages and the selected TFM/EF graph, then executes the real storage workflow.
Candidate OSA packages are source-mapped exclusively to the supplied artifact folder. Other dependencies
come from NuGet.org. The negative reproduction obtains OSA 0.2.1 exclusively from NuGet.org.

Use a disposable local SQL Server and MinIO with these environment variables:

- `OSA_TEST_SQL_SERVER`, `OSA_TEST_SQL_INTEGRATED_SECURITY=true` (LocalDB), or SQL user/password variables.
- `OSA_E2E_MINIO_ENDPOINT`, `OSA_E2E_MINIO_ACCESS_KEY`, `OSA_E2E_MINIO_SECRET_KEY`.

The program rejects non-loopback services, atomically creates a new `ObjectStorageAssetWorkflow_<guid>`
database and private `osa-compat-<guid>` bucket, and removes only the resources it created in `finally`.
Never point it at an application database or shared object store.

From the repository root after building and packing with the same version:

```powershell
.github/scripts/Test-PackageCompatibility.ps1 -Version 0.2.1 -Framework net10.0 -PublishedReproduction
.github/scripts/Test-PackageCompatibility.ps1 -Version 0.2.2-ef10.1 -Framework net10.0 -PackageDirectory artifacts/nuget
.github/scripts/Test-PackageCompatibility.ps1 -Version 0.2.2-ef10.1 -Framework net8.0 -PackageDirectory artifacts/nuget
```

`-Dotnet` can select a workspace-local SDK. The runtime and actual EF/OSA assembly versions are logged.
The workflow covers upload, temporary finalization, identical finalization/descriptor replay, verified
reads, same-length corruption, confirmed missing content, persisted/short-circuited missing observations,
availability rechecks that leave corrupt content blocked, and restoration of the exact original bytes.
It checks that IDs, owner binding, origin, locator and expected SHA-256 survive the whole sequence.
The negative mode succeeds only when both missing reads and rechecks throw the exact legacy
`SetPropertyCalls<T>` TypeLoadException after the earlier normal and corruption scenarios passed.
It is confined to the test consumer; candidate runtime errors are never caught as success.

The CI workflow runs this qualification before its existing tag-only publication step and uploads the
restore/build/runtime logs alongside the TRX evidence. A PR does not publish packages.
