Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ReleaseArtifact {
    param($Run, $Jobs, $Artifact, [switch] $CurrentRun)
    $repository = 'AbuDawood/ObjectStorageAsset'
    if ($Run.repository.full_name -ine $repository -or $Run.head_repository.full_name -ine $repository -or
        $Run.path -cne '.github/workflows/nuget.yml' -or $Run.event -cne 'push') {
        throw 'Source must be a push run of this repository nuget.yml workflow.'
    }
    if ($Run.head_branch -cnotmatch '^v((0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*))$' -or
        $Run.head_sha -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Source must identify a stable release tag and full commit SHA.'
    }
    $version = $Run.head_branch.Substring(1)
    if (-not $CurrentRun -and $Run.status -cne 'completed') { throw 'Recovery source run is not completed.' }
    $pack = @($Jobs | Where-Object name -CEQ 'pack')
    if ($pack.Count -ne 1 -or $pack[0].status -cne 'completed' -or
        $pack[0].run_id -ne $Run.id -or $pack[0].head_sha -cne $Run.head_sha -or $pack[0].run_attempt -ne $Run.run_attempt) {
        throw 'A completed pack job from the source run attempt is required.'
    }
    $required = @(
        'Build validation solution',
        'Unit, SQL integration and legacy compatibility tests',
        'In-memory provider E2E with real SQL',
        'Real MinIO E2E with real SQL',
        'Pack',
        'Validate package set and versions',
        'Reproduce published 0.2.1 binary failure under EF 10',
        'Qualify packed candidate through clean consumers',
        'Upload packages'
    )
    foreach ($name in $required) {
        $step = @($pack[0].steps | Where-Object name -CEQ $name)
        if ($step.Count -ne 1 -or $step[0].conclusion -cne 'success') {
            throw "Source did not pass required release check: $name"
        }
    }
    # The old workflow published inside pack; an authentication failure there is recoverable.
    $failed = @($pack[0].steps | Where-Object { $_.conclusion -in @('failure', 'cancelled', 'timed_out') })
    if (@($failed | Where-Object name -CNE 'Publish to NuGet.org').Count -gt 0 -or
        $pack[0].conclusion -notin @('success', 'failure') -or
        ($pack[0].conclusion -eq 'failure' -and $failed.Count -ne 1) -or
        ($pack[0].conclusion -eq 'success' -and $failed.Count -ne 0)) {
        throw 'Source pack job has a failure outside the legacy publishing step.'
    }
    if ($Artifact.name -cne "nuget-$version" -or $Artifact.expired -or
        $Artifact.workflow_run.id -ne $Run.id -or $Artifact.workflow_run.head_sha -cne $Run.head_sha -or
        $Artifact.digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
        [DateTimeOffset]$Artifact.created_at -lt [DateTimeOffset]$pack[0].started_at -or
        [DateTimeOffset]$Artifact.created_at -gt [DateTimeOffset]$pack[0].completed_at) {
        throw 'Artifact identity, digest, expiry or source attempt is invalid.'
    }
    return $version
}

function Expand-ReleaseArtifact {
    param([string] $Archive, [string] $Digest, [string] $Version, [string] $Destination)
    if ($Digest -cnotmatch '^sha256:[0-9a-f]{64}$' -or
        "sha256:$((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant())" -cne $Digest) {
        throw 'Release artifact SHA-256 does not match GitHub metadata.'
    }
    $ids = @('Elf.ObjectStorageAsset', 'Elf.ObjectStorageAsset.Application', 'Elf.ObjectStorageAsset.Domain',
        'Elf.ObjectStorageAsset.Helpers', 'Elf.ObjectStorageAsset.Infrastructure', 'Elf.ObjectStorageAsset.Minio')
    $expected = @($ids | ForEach-Object { "$($_).$Version.nupkg"; "$($_).$Version.snupkg" })
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Archive).Path)
    try {
        if ($zip.Entries.Count -ne 12 -or @($zip.Entries.FullName | Sort-Object -Unique).Count -ne 12) {
            throw 'Release archive must contain exactly twelve unique package files.'
        }
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -cnotin $expected -or $entry.Length -gt 100MB -or
                (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                throw 'Release archive contains an unexpected, oversized or symbolic-link entry.'
            }
        }
        if (Test-Path -LiteralPath $Destination) { throw 'Release extraction requires a fresh destination.' }
        $null = New-Item -ItemType Directory -Path $Destination
        foreach ($entry in $zip.Entries) {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $Destination $entry.FullName))
        }
    }
    finally { $zip.Dispose() }
}

Export-ModuleMember -Function Assert-ReleaseArtifact, Expand-ReleaseArtifact
