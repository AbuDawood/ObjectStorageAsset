param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('net8.0','net10.0')][string]$Framework,
    [string]$PackageDirectory,
    [string]$Dotnet = 'dotnet',
    [switch]$PublishedReproduction
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ($PublishedReproduction -and ($Version -ne '0.2.1' -or $Framework -ne 'net10.0')) {
    throw 'The negative reproduction must use published OSA 0.2.1 under net10.0/EF 10.0.15.'
}
if (-not $PublishedReproduction -and -not $PackageDirectory) { throw 'Candidate qualification requires the packed artifact directory.' }
$case = Join-Path $repository "artifacts/pq/$Framework-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
New-Item -ItemType Directory -Path $case -Force | Out-Null
foreach ($file in @('PackageCompatibility.csproj','Directory.Build.props','global.json','Program.cs')) {
    Copy-Item -LiteralPath (Join-Path $repository "test/PackageCompatibility/$file") -Destination $case
}
$config = '<configuration><packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'
if ($PublishedReproduction) { $config += '</packageSources></configuration>' }
else {
    $feed = [Security.SecurityElement]::Escape((Resolve-Path -LiteralPath $PackageDirectory).Path)
    $config += "<add key=`"candidate`" value=`"$feed`" /></packageSources><packageSourceMapping><packageSource key=`"candidate`"><package pattern=`"Elf.ObjectStorageAsset*`" /></packageSource><packageSource key=`"nuget.org`"><package pattern=`"*`" /></packageSource></packageSourceMapping></configuration>"
}
[IO.File]::WriteAllText((Join-Path $case 'NuGet.Config'), $config)
Push-Location -LiteralPath $case
try {
    $properties = @("-p:OsaPackageVersion=$Version", "-p:TargetFrameworks=$Framework")
    & $Dotnet restore PackageCompatibility.csproj @properties --configfile NuGet.Config --packages (Join-Path $case 'p') 2>&1 | Tee-Object -FilePath restore.log
    if ($LASTEXITCODE -ne 0) { throw 'Clean package consumer restore failed.' }
    $assets = Get-Content -LiteralPath obj/project.assets.json -Raw | ConvertFrom-Json -Depth 100
    $osa = @($assets.libraries.PSObject.Properties | Where-Object Name -Like 'Elf.ObjectStorageAsset*')
    if ($osa.Count -ne 6 -or @($osa | Where-Object { $_.Value.type -ne 'package' -or $_.Name.Split('/')[1] -ne $Version }).Count) {
        throw 'The clean consumer must resolve all six OSA libraries from the requested packages.'
    }
    $efVersion = if ($Framework -eq 'net10.0') { '10.0.15' } else { '8.0.11' }
    foreach ($ef in @($assets.libraries.PSObject.Properties | Where-Object Name -Like 'Microsoft.EntityFrameworkCore*')) {
        if ($ef.Name.Split('/')[1] -ne $efVersion) { throw "Mixed EF graph: $($ef.Name)" }
    }
    $target = ($assets.targets.PSObject.Properties | Where-Object Name -EQ $Framework).Value
    $assetFramework = if ($PublishedReproduction) { 'net8.0' } else { $Framework }
    foreach ($library in $osa) {
        $runtime = ($target.PSObject.Properties | Where-Object Name -EQ $library.Name).Value.runtime
        if (-not @($runtime.PSObject.Properties | Where-Object Name -Like "lib/$assetFramework/*.dll").Count) {
            throw "Incorrect framework asset selected for $($library.Name)"
        }
    }
    Write-Host "Verified six package references, $assetFramework assets and a consistent EF $efVersion graph."
    & $Dotnet build PackageCompatibility.csproj --configuration Release --framework $Framework --no-restore @properties 2>&1 | Tee-Object -FilePath build.log
    if ($LASTEXITCODE -ne 0) { throw 'Clean package consumer build failed.' }
    $runtimeArguments = @('--expected-version', $Version)
    if ($PublishedReproduction) { $runtimeArguments += '--expect-legacy-failure' }
    & $Dotnet (Join-Path $case "bin/Release/$Framework/PackageCompatibility.dll") @runtimeArguments 2>&1 | Tee-Object -FilePath runtime.log
    if ($LASTEXITCODE -ne 0) { throw 'Clean package consumer runtime qualification failed.' }
    Write-Host "Package qualification passed: $Version / $Framework. Evidence: $case"
}
finally { Pop-Location }
