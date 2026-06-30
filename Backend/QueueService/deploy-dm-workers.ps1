# Backward-compatible alias — use deploy.ps1 instead.
#   powershell -ExecutionPolicy Bypass -File Backend\QueueService\deploy.ps1

$deployScript = Join-Path $PSScriptRoot "deploy.ps1"
if (-not (Test-Path $deployScript)) {
    throw "Missing deploy.ps1 next to this script."
}

& $deployScript @args
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
