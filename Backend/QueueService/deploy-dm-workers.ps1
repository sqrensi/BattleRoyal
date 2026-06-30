# Deploy DM respawn worker fixes to VPS.
# Usage (one command from repo root):
#   powershell -ExecutionPolicy Bypass -File Backend\QueueService\deploy-dm-workers.ps1
#
# Optional env:
#   $env:DEPLOY_USER = "deploy"   # or "root"
#   $env:DEPLOY_HOST = "api.game35.ru"

$ErrorActionPreference = "Stop"

$HostName = if ($env:DEPLOY_HOST) { $env:DEPLOY_HOST } else { "api.game35.ru" }
$User = if ($env:DEPLOY_USER) { $env:DEPLOY_USER } else { "deploy" }
$Remote = "${User}@${HostName}"

$WorkersDir = Join-Path $PSScriptRoot "workers"
$Files = @("movement.js", "deathmatch.js", "player.js", "clientProtocol.js")

foreach ($name in $Files) {
    $path = Join-Path $WorkersDir $name
    if (-not (Test-Path $path)) {
        throw "Missing file: $path"
    }
}

$localPaths = $Files | ForEach-Object { Join-Path $WorkersDir $_ }

Write-Host "Uploading to ${Remote}:/home/deploy/ ..."
& scp @localPaths "${Remote}:/home/deploy/"
if ($LASTEXITCODE -ne 0) {
    throw "scp failed (exit $LASTEXITCODE). Check SSH key: ssh ${Remote}"
}

if ($User -eq "root") {
    $remoteCmd = @"
cp /home/deploy/movement.js /home/deploy/deathmatch.js /home/deploy/player.js /home/deploy/clientProtocol.js /opt/shooter/QueueService/workers/ &&
systemctl restart shooter-queue &&
systemctl is-active shooter-queue && echo DEPLOY_OK
"@
    Write-Host "Installing as root ..."
    & ssh -t $Remote $remoteCmd
} else {
    $remoteCmd = @"
sudo cp /home/deploy/movement.js /home/deploy/deathmatch.js /home/deploy/player.js /home/deploy/clientProtocol.js /opt/shooter/QueueService/workers/ &&
sudo systemctl restart shooter-queue &&
sudo systemctl is-active shooter-queue && echo DEPLOY_OK
"@
    Write-Host "Installing (sudo password may be prompted) ..."
    & ssh -t $Remote $remoteCmd
}

if ($LASTEXITCODE -ne 0) {
    throw "Remote install failed (exit $LASTEXITCODE)"
}

Write-Host "Done."
