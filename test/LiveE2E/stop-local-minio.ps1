$ErrorActionPreference = "Stop"

$containerName = if ($env:OSA_LIVE_MINIO_CONTAINER_NAME) { $env:OSA_LIVE_MINIO_CONTAINER_NAME } else { "osa-live-minio" }
$running = docker ps --filter "name=^/${containerName}$" --format "{{.ID}}"
if ($running) {
    docker stop $containerName | Out-Null
    Write-Host "Stopped $containerName."
}
else {
    $existing = docker ps -a --filter "name=^/${containerName}$" --format "{{.ID}}"
    if ($existing) {
        Write-Host "$containerName is already stopped."
    }
    else {
        Write-Host "$containerName is not present."
    }
}
