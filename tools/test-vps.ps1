$ErrorActionPreference = 'Continue'

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Url,
        [string]$Body = $null
    )

    try {
        $params = @{
            Uri             = $Url
            Method          = $Method
            UseBasicParsing = $true
            TimeoutSec      = 10
        }
        if ($Body) {
            $params.Body = $Body
            $params.ContentType = 'application/json'
        }

        $r = Invoke-WebRequest @params
        $preview = if ($r.Content.Length -gt 160) { $r.Content.Substring(0, 160) + '...' } else { $r.Content }
        Write-Host "OK $Name $($r.StatusCode)"
        Write-Host "  $preview"
    }
    catch {
        Write-Host "FAIL $Name"
        Write-Host "  $($_.Exception.Message)"
    }
}

Test-Endpoint -Name 'health' -Method 'GET' -Url 'http://83.220.165.44:5050/health'
Test-Endpoint -Name 'profile ensure 1' -Method 'POST' -Url 'http://83.220.165.44:5050/profile/ensure' -Body '{"playerId":"test-webgl-duplicate"}'
Test-Endpoint -Name 'profile ensure 2' -Method 'POST' -Url 'http://83.220.165.44:5050/profile/ensure' -Body '{"playerId":"test-webgl-duplicate"}'
Test-Endpoint -Name 'catalog bin' -Method 'GET' -Url 'http://83.220.165.44:5050/addressables/WebGL/catalog.bin'
Test-Endpoint -Name 'addressables dir' -Method 'GET' -Url 'http://83.220.165.44:5050/addressables/WebGL/'

$localWebGl = 'c:\me\unity\ShooterPrototype\ServerData\WebGL'
if (Test-Path $localWebGl) {
    Write-Host ''
    Write-Host "Local ServerData/WebGL:"
    Get-ChildItem $localWebGl | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length/1MB, 2)) MB)" }
}
