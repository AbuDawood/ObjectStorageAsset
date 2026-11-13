param(
    [Parameter(Mandatory)][ValidatePattern('^[1-9][0-9]*$')][string] $RunId,
    [Parameter(Mandatory)][string] $PackageDirectory
)
$ErrorActionPreference = 'Stop'
Import-Module "$PSScriptRoot/ReleaseArtifact.psm1" -Force -DisableNameChecking
if (-not $env:GH_TOKEN) { throw 'A read-only GitHub Actions token is required.' }
$repository = 'AbuDawood/ObjectStorageAsset'
if ($env:GITHUB_REPOSITORY -ine $repository -or
    -not (($env:GITHUB_EVENT_NAME -eq 'push' -and $env:GITHUB_REF -clike 'refs/tags/v*') -or
          ($env:GITHUB_EVENT_NAME -eq 'workflow_dispatch' -and $env:GITHUB_REF -ceq 'refs/heads/main'))) {
    throw 'Publishing is allowed only on release tag pushes or explicit recovery from main.'
}
$base = "https://api.github.com/repos/$repository"
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$run = Invoke-RestMethod "$base/actions/runs/$RunId" -Headers $headers
$jobs = Invoke-RestMethod "$base/actions/runs/$RunId/attempts/$($run.run_attempt)/jobs?per_page=100" -Headers $headers
$artifacts = Invoke-RestMethod "$base/actions/runs/$RunId/artifacts?per_page=100" -Headers $headers
if ($jobs.total_count -gt 100 -or $artifacts.total_count -gt 100) { throw 'Unexpected source run size.' }
$artifact = @($artifacts.artifacts | Where-Object name -CEQ "nuget-$($run.head_branch.Substring(1))")
if ($artifact.Count -ne 1) { throw 'Exactly one release artifact is required.' }
$version = Assert-ReleaseArtifact -Run $run -Jobs $jobs.jobs -Artifact $artifact[0] -CurrentRun:($RunId -eq $env:GITHUB_RUN_ID)

# Resolve annotated or lightweight tags locally; never publish from a moved tag or an unmerged commit.
$tagCommit = & git rev-parse --verify "refs/tags/$($run.head_branch)^{commit}"
if ($LASTEXITCODE -ne 0 -or $tagCommit -cne $run.head_sha) { throw 'Release tag no longer matches the tested source.' }
& git merge-base --is-ancestor $tagCommit origin/main
if ($LASTEXITCODE -ne 0) { throw 'Release source is not merged into main.' }
$props = & git show "${tagCommit}:Directory.Build.props"
if ($LASTEXITCODE -ne 0 -or ([xml]($props -join "`n")).SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText -cne $version) {
    throw 'Source VersionPrefix does not match the release tag.'
}
$archive = Join-Path $env:RUNNER_TEMP "osa-release-$RunId-$($artifact[0].id).zip"
Invoke-WebRequest "$base/actions/artifacts/$($artifact[0].id)/zip" -Headers $headers -OutFile $archive
Expand-ReleaseArtifact -Archive $archive -Digest $artifact[0].digest -Version $version -Destination $PackageDirectory
& "$PSScriptRoot/Test-Packages.ps1" -PackageDirectory $PackageDirectory -Version $version -ExpectedRepositoryCommit $run.head_sha
"Publishing original artifact $($artifact[0].id), SHA-256 $($artifact[0].digest), source $($run.head_sha), version $version." | Write-Host
if ($env:GITHUB_STEP_SUMMARY) {
    "Release **$version** from [$RunId](https://github.com/$repository/actions/runs/$RunId), artifact **$($artifact[0].id)**, commit ``$($run.head_sha)``. Archive digest: ``$($artifact[0].digest)``." | Add-Content $env:GITHUB_STEP_SUMMARY
}
