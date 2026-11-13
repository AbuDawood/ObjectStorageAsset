$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/ReleaseArtifact.psm1" -Force -DisableNameChecking
$checks = 0
function Expect-Rejection([scriptblock] $Action, [string] $Message) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw "Safeguard did not reject: $Message" }
    $script:checks++
}
$source = @{
    run = @{
        id = 123; run_attempt = 1; path = '.github/workflows/nuget.yml'; event = 'push'
        head_branch = 'v0.2.2'; head_sha = ('a' * 40); status = 'completed'
        repository = @{ full_name = 'AbuDawood/ObjectStorageAsset' }
        head_repository = @{ full_name = 'AbuDawood/ObjectStorageAsset' }
    }
    jobs = @(@{
        name = 'pack'; status = 'completed'; conclusion = 'failure'; head_sha = ('a' * 40); run_attempt = 1; run_id = 123
        started_at = '2026-11-13T07:00:00Z'; completed_at = '2026-11-13T07:10:00Z'
        steps = @(@(
            'Build validation solution', 'Unit, SQL integration and legacy compatibility tests',
            'In-memory provider E2E with real SQL', 'Real MinIO E2E with real SQL', 'Pack',
            'Validate package set and versions', 'Reproduce published 0.2.1 binary failure under EF 10',
            'Qualify packed candidate through clean consumers', 'Upload packages'
        ) | ForEach-Object { @{ name = $_; conclusion = 'success' } }) + @(@{ name = 'Publish to NuGet.org'; conclusion = 'failure' })
    })
    artifact = @{
        name = 'nuget-0.2.2'; expired = $false; digest = ('sha256:' + ('b' * 64))
        created_at = '2026-11-13T07:09:00Z'; workflow_run = @{ id = 123; head_sha = ('a' * 40) }
    }
}
function Copy-Source { $source | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable }
function Check-Source($Fixture, [switch] $CurrentRun) {
    Assert-ReleaseArtifact -Run $Fixture.run -Jobs $Fixture.jobs -Artifact $Fixture.artifact -CurrentRun:$CurrentRun
}
if ((Check-Source (Copy-Source)) -cne '0.2.2') { throw 'Legacy authentication failure should be recoverable.' }
$checks++
$valid = Copy-Source
$valid.run.status = 'in_progress'; $valid.jobs[0].conclusion = 'success'
$valid.jobs[0].steps = @($valid.jobs[0].steps | Where-Object name -NE 'Publish to NuGet.org')
if ((Check-Source $valid -CurrentRun) -cne '0.2.2') { throw 'Current completed pack job should publish.' }
$checks++
foreach ($mutation in @(
    { param($f) $f.run.event = 'pull_request' },
    { param($f) $f.run.repository.full_name = 'other/repo' },
    { param($f) $f.run.head_repository.full_name = 'fork/ObjectStorageAsset' },
    { param($f) $f.run.path = '.github/workflows/other.yml' },
    { param($f) $f.run.head_branch = 'main' },
    { param($f) $f.run.head_branch = 'v0.2.2-ci.1' },
    { param($f) $f.run.head_sha = 'short-sha' },
    { param($f) $f.run.status = 'in_progress' },
    { param($f) $f.jobs[0].status = 'in_progress' },
    { param($f) $f.jobs[0].head_sha = ('c' * 40) },
    { param($f) $f.jobs[0].run_attempt = 2 },
    { param($f) $f.jobs[0].run_id = 124 },
    { param($f) $f.jobs[0].conclusion = 'success' },
    { param($f) $f.jobs[0].conclusion = 'cancelled' },
    { param($f) $f.jobs[0].steps += @{ name = 'Other failed step'; conclusion = 'failure' } },
    { param($f) $f.artifact.name = 'nuget-0.2.3' },
    { param($f) $f.artifact.expired = $true },
    { param($f) $f.artifact.workflow_run.id = 124 },
    { param($f) $f.artifact.workflow_run.head_sha = ('c' * 40) },
    { param($f) $f.artifact.digest = '' },
    { param($f) $f.artifact.created_at = '2026-11-13T06:00:00Z' },
    { param($f) $f.artifact.created_at = '2026-11-13T08:00:00Z' }
)) {
    $fixture = Copy-Source
    & $mutation $fixture
    Expect-Rejection { Check-Source $fixture } 'invalid source provenance'
}
foreach ($step in $source.jobs[0].steps | Where-Object name -NE 'Publish to NuGet.org') {
    foreach ($conclusion in @('failure','skipped')) {
        $fixture = Copy-Source
        ($fixture.jobs[0].steps | Where-Object name -EQ $step.name).conclusion = $conclusion
        Expect-Rejection { Check-Source $fixture } "$($step.name): $conclusion"
    }
}
$fixture = Copy-Source; $fixture.jobs = @()
Expect-Rejection { Check-Source $fixture } 'missing pack job'
$fixture = Copy-Source; $fixture.jobs += $fixture.jobs[0]
Expect-Rejection { Check-Source $fixture } 'ambiguous pack job'

# The ZIP is untrusted input: verify digest and exact flat file allowlist before extracting.
$scratch = Join-Path (Join-Path $PSScriptRoot '../../artifacts') "release-safeguards-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $scratch
$names = @(@('Elf.ObjectStorageAsset', 'Elf.ObjectStorageAsset.Application', 'Elf.ObjectStorageAsset.Domain',
    'Elf.ObjectStorageAsset.Helpers', 'Elf.ObjectStorageAsset.Infrastructure', 'Elf.ObjectStorageAsset.Minio') |
    ForEach-Object { "$_.0.2.2.nupkg"; "$_.0.2.2.snupkg" })
function New-TestArchive($FileNames, [string] $Name) {
    $path = Join-Path $scratch "$Name.zip"
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try { foreach ($name in $FileNames) { $null = $zip.CreateEntry($name) } } finally { $zip.Dispose() }
    return $path
}
$archive = New-TestArchive $names 'valid'
$digest = "sha256:$((Get-FileHash $archive).Hash.ToLowerInvariant())"
Expand-ReleaseArtifact $archive $digest '0.2.2' (Join-Path $scratch 'valid-output')
if (@(Get-ChildItem (Join-Path $scratch 'valid-output')).Count -ne 12) { throw 'Incorrect extraction.' }
$checks++
Expect-Rejection { Expand-ReleaseArtifact $archive ('sha256:' + ('0' * 64)) '0.2.2' (Join-Path $scratch 'bad-digest') } 'tampered bytes'
Expect-Rejection { Expand-ReleaseArtifact $archive $digest '0.2.2' (Join-Path $scratch 'valid-output') } 'preexisting output directory'
foreach ($badNames in @(
    ,($names + '../outside.nupkg')
    ,($names[0..10] + '../outside.nupkg')
    ,($names[0..10] + $names[0])
    ,($names[0..10] + 'nested/package.nupkg')
)) {
    $bad = New-TestArchive $badNames ([Guid]::NewGuid().ToString('N'))
    $badDigest = "sha256:$((Get-FileHash $bad).Hash.ToLowerInvariant())"
    Expect-Rejection { Expand-ReleaseArtifact $bad $badDigest '0.2.2' (Join-Path $scratch ([Guid]::NewGuid().ToString('N'))) } 'malicious ZIP entries'
}
Write-Host "Passed $checks release artifact safeguard checks."
