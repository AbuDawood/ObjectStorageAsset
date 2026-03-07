$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$startMinioScript = Join-Path $scriptDir "..\LiveE2E\start-local-minio.ps1"
$smokeProject = Join-Path $scriptDir "SmokeTest.csproj"

powershell -ExecutionPolicy Bypass -File $startMinioScript
dotnet run --project $smokeProject
