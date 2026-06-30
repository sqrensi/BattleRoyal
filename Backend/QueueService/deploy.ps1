# Deploy QueueService to VPS (one command).
#
# From repo root:
#   powershell -ExecutionPolicy Bypass -File Backend\QueueService\deploy.ps1
#
# Optional:
#   $env:DEPLOY_HOST = "api.game35.ru"
#   $env:DEPLOY_USER = "deploy"
#   $env:DEPLOY_HEALTH_URL = "http://127.0.0.1:5050/health"
#   powershell -ExecutionPolicy Bypass -File Backend\QueueService\deploy.ps1 -Install

param(
    [switch]$Install,
    [switch]$SkipHealth
)

$ErrorActionPreference = "Stop"

$DeployHost = if ($env:DEPLOY_HOST) { $env:DEPLOY_HOST } else { "api.game35.ru" }
$DeployUser = if ($env:DEPLOY_USER) { $env:DEPLOY_USER } else { "deploy" }
$RemotePath = if ($env:DEPLOY_REMOTE_PATH) { $env:DEPLOY_REMOTE_PATH } else { "/opt/shooter/QueueService" }
$HealthUrl = if ($env:DEPLOY_HEALTH_URL) { $env:DEPLOY_HEALTH_URL } else { "http://127.0.0.1:5050/health" }
$Remote = "${DeployUser}@${DeployHost}"

$ServiceRoot = $PSScriptRoot
$StagingDir = Join-Path $env:TEMP ("queue-deploy-" + [Guid]::NewGuid().ToString("N"))
$ArchivePath = Join-Path $env:TEMP "queue-deploy.tgz"

$RootFiles = @(
    "server.js",
    "config.js",
    "package.json",
    "package-lock.json",
    "duelMatch.js"
)

$RootDirs = @(
    "workers",
    "db",
    "network",
    "lib",
    "data",
    "bots"
)

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Command not found: $Name"
    }
}

Require-Command scp
Require-Command ssh
Require-Command tar

Write-Host "Building deploy bundle from $ServiceRoot ..."
New-Item -ItemType Directory -Path $StagingDir | Out-Null

foreach ($file in $RootFiles) {
    $source = Join-Path $ServiceRoot $file
    if (-not (Test-Path $source)) {
        throw "Missing file: $source"
    }
    Copy-Item $source (Join-Path $StagingDir $file)
}

foreach ($dir in $RootDirs) {
    $source = Join-Path $ServiceRoot $dir
    if (-not (Test-Path $source)) {
        throw "Missing directory: $source"
    }
    Copy-Item $source (Join-Path $StagingDir $dir) -Recurse
}

if (Test-Path $ArchivePath) {
    Remove-Item $ArchivePath -Force
}

Push-Location $StagingDir
try {
    & tar -czf $ArchivePath .
    if ($LASTEXITCODE -ne 0) {
        throw "tar pack failed (exit $LASTEXITCODE)"
    }
}
finally {
    Pop-Location
    Remove-Item $StagingDir -Recurse -Force
}

Write-Host "Uploading to ${Remote}:/home/deploy/queue-deploy.tgz ..."
& scp $ArchivePath "${Remote}:/home/deploy/queue-deploy.tgz"
if ($LASTEXITCODE -ne 0) {
    throw "scp failed (exit $LASTEXITCODE). Test SSH: ssh $Remote"
}

$installStep = if ($Install.IsPresent) {
    "cd '$RemotePath' && npm install --omit=dev"
} else {
    "true"
}

$healthStep = if ($SkipHealth.IsPresent) {
    "echo 'Health check skipped (-SkipHealth)'"
} else {
    @"
HEALTH_URL='$HealthUrl'
for attempt in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do
  if curl -fsS "`$HEALTH_URL" >/dev/null; then
    curl -fsS "`$HEALTH_URL"
    echo
    echo DEPLOY_OK
    exit 0
  fi
  sleep 2
done
echo "Health check failed: `$HEALTH_URL"
curl -v "`$HEALTH_URL" || true
exit 1
"@
}

$prefix = if ($DeployUser -eq "root") { "" } else { "sudo " }

$remoteCmd = @"
set -e
${prefix}mkdir -p '$RemotePath'
${prefix}tar -xzf /home/deploy/queue-deploy.tgz -C '$RemotePath'
$installStep
${prefix}systemctl restart shooter-queue
sleep 2
if ! ${prefix}systemctl is-active --quiet shooter-queue; then
  echo "Service shooter-queue is not active"
  ${prefix}systemctl status shooter-queue --no-pager -l || true
  ${prefix}journalctl -u shooter-queue -n 50 --no-pager || true
  exit 1
fi
$healthStep
"@

Write-Host "Installing on $DeployHost ..."
if ($DeployUser -eq "root") {
    & ssh -t $Remote $remoteCmd
}
else {
    Write-Host "(sudo password may be prompted)"
    & ssh -t $Remote $remoteCmd
}

$sshExit = $LASTEXITCODE
if ($sshExit -ne 0) {
    Write-Host ""
    Write-Host "Remote deploy failed (exit $sshExit)." -ForegroundColor Red
    Write-Host "Fetch logs:" -ForegroundColor Yellow
    Write-Host "  ssh $Remote `"${prefix}journalctl -u shooter-queue -n 80 --no-pager`"" -ForegroundColor Yellow
    throw "Remote deploy failed (exit $sshExit)"
}

Remove-Item $ArchivePath -Force -ErrorAction SilentlyContinue
Write-Host "Done."
