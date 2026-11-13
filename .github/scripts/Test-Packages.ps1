param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $Version,
    [string] $ExpectedRepositoryCommit
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$expected = @(
    'Elf.ObjectStorageAsset',
    'Elf.ObjectStorageAsset.Application',
    'Elf.ObjectStorageAsset.Domain',
    'Elf.ObjectStorageAsset.Helpers',
    'Elf.ObjectStorageAsset.Infrastructure',
    'Elf.ObjectStorageAsset.Minio'
)
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg')
$symbols = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.snupkg')
if ($packages.Count -ne $expected.Count -or $symbols.Count -ne $expected.Count) {
    throw "Expected six packages and six symbol packages; found $($packages.Count) and $($symbols.Count)."
}

foreach ($id in $expected) {
    foreach ($extension in @('nupkg', 'snupkg')) {
        $path = Join-Path $PackageDirectory "$id.$Version.$extension"
        $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path)
        try {
            $nuspec = @($zip.Entries | Where-Object FullName -Like '*.nuspec')
            if ($nuspec.Count -ne 1) { throw "Invalid manifest count in $path." }
            $reader = [System.IO.StreamReader]::new($nuspec[0].Open())
            try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $metadata = $manifest.package.metadata
            if ($metadata.id -ne $id -or $metadata.version -ne $Version) {
                throw "Incorrect package identity/version in $path."
            }
            if ($ExpectedRepositoryCommit -and
                ($metadata.repository.commit -cne $ExpectedRepositoryCommit -or
                 $metadata.repository.url -ine 'https://github.com/AbuDawood/ObjectStorageAsset')) {
                throw "Incorrect repository provenance in $path."
            }
            if ($extension -eq 'snupkg') {
                foreach ($framework in @('net8.0','net10.0')) {
                    if (-not @($zip.Entries | Where-Object FullName -Like "lib/$framework/*.pdb").Count) {
                        throw "Missing $framework symbols in $path."
                    }
                }
                continue
            }
            foreach ($entry in @('README.md', 'LICENSE', "lib/net8.0/$id.dll", "lib/net10.0/$id.dll")) {
                if (-not $zip.GetEntry($entry)) { throw "Missing $entry in $path." }
            }
            foreach ($group in $metadata.SelectNodes(".//*[local-name()='group']")) {
                if ($group.targetFramework -notin @('net8.0','net10.0')) { throw "Unexpected dependency target $($group.targetFramework)." }
                $expectedEfRange = if ($group.targetFramework -eq 'net8.0') { '[8.0.11, 9.0.0)' } else { '[10.0.15, 11.0.0)' }
                foreach ($dependency in $group.dependency) {
                    if ($dependency.id -like 'Microsoft.EntityFrameworkCore*' -and $dependency.version -ne $expectedEfRange) {
                        throw "Unbounded or mismatched EF dependency $($dependency.id): $($dependency.version)."
                    }
                }
            }
            foreach ($dependency in $metadata.SelectNodes(".//*[local-name()='dependency']")) {
                if ($dependency.id -in $expected -and $dependency.version -ne $Version) {
                    throw "Internal dependency $($dependency.id) has version $($dependency.version), expected $Version."
                }
            }
        }
        finally { $zip.Dispose() }
    }
}

Write-Host "Validated six dual-target package/symbol pairs at version $Version, including EF major bounds, dependencies, README and license."
