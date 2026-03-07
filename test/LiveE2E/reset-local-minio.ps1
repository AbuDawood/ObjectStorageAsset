$ErrorActionPreference = "Stop"

$containerName = if ($env:OSA_LIVE_MINIO_CONTAINER_NAME) { $env:OSA_LIVE_MINIO_CONTAINER_NAME } else { "osa-live-minio" }
$volumeName = if ($env:OSA_LIVE_MINIO_VOLUME_NAME) { $env:OSA_LIVE_MINIO_VOLUME_NAME } else { "osa-live-minio-data" }

$existing = docker ps -a --filter "name=^/${containerName}$" --format "{{.ID}}"
if ($existing) {
    docker rm -f $containerName | Out-Null
    Write-Host "Removed $containerName."
}
else {
    Write-Host "$containerName is not present."
}

$volume = docker volume ls --filter "name=^${volumeName}$" --format "{{.Name}}"
if ($volume) {
    docker volume rm $volumeName | Out-Null
    Write-Host "Removed volume $volumeName."
}
else {
    Write-Host "Volume $volumeName is not present."
}
