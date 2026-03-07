$ErrorActionPreference = "Stop"

$containerName = if ($env:OSA_LIVE_MINIO_CONTAINER_NAME) { $env:OSA_LIVE_MINIO_CONTAINER_NAME } else { "osa-live-minio" }
$volumeName = if ($env:OSA_LIVE_MINIO_VOLUME_NAME) { $env:OSA_LIVE_MINIO_VOLUME_NAME } else { "osa-live-minio-data" }
$image = if ($env:OSA_LIVE_MINIO_IMAGE) { $env:OSA_LIVE_MINIO_IMAGE } else { "quay.io/minio/minio:latest" }
$apiPort = if ($env:OSA_LIVE_MINIO_API_PORT) { [int]$env:OSA_LIVE_MINIO_API_PORT } else { 19000 }
$consolePort = if ($env:OSA_LIVE_MINIO_CONSOLE_PORT) { [int]$env:OSA_LIVE_MINIO_CONSOLE_PORT } else { 19001 }
$hostAddress = if ($env:OSA_LIVE_MINIO_HOST_ADDRESS) { $env:OSA_LIVE_MINIO_HOST_ADDRESS } else { "10.0.2.2" }
$rootUser = if ($env:OSA_LIVE_MINIO_ROOT_USER) { $env:OSA_LIVE_MINIO_ROOT_USER } else { "minioadmin" }
$rootPassword = if ($env:OSA_LIVE_MINIO_ROOT_PASSWORD) { $env:OSA_LIVE_MINIO_ROOT_PASSWORD } else { "minioadmin" }

$isRunning = docker ps --filter "name=^/${containerName}$" --format "{{.ID}}"
if ($isRunning) {
    Write-Host "MinIO container '$containerName' is already running."
}
else {
    $existing = docker ps -a --filter "name=^/${containerName}$" --format "{{.ID}}"
    if ($existing) {
        docker start $containerName | Out-Null
        Write-Host "Started existing MinIO container '$containerName'."
    }
    else {
        $volume = docker volume ls --filter "name=^${volumeName}$" --format "{{.Name}}"
        if (-not $volume) {
            docker volume create $volumeName | Out-Null
        }

        docker run -d `
            --name $containerName `
            --restart unless-stopped `
            -p "${apiPort}:9000" `
            -p "${consolePort}:9001" `
            -v "${volumeName}:/data" `
            -e "MINIO_ROOT_USER=$rootUser" `
            -e "MINIO_ROOT_PASSWORD=$rootPassword" `
            $image server /data --console-address ":9001" | Out-Null

        Write-Host "Created persistent MinIO container '$containerName' with volume '$volumeName'."
    }
}

$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Seconds 1
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "http://${hostAddress}:${apiPort}/minio/health/live" -TimeoutSec 5
        if ($response.StatusCode -eq 200) {
            Write-Host "MinIO is ready on http://${hostAddress}:${apiPort} (console http://${hostAddress}:${consolePort})."
            Write-Host "Container: $containerName"
            Write-Host "Volume: $volumeName"
            exit 0
        }
    }
    catch {
    }
} while ((Get-Date) -lt $deadline)

Write-Error "MinIO container '$containerName' did not become ready in time."
