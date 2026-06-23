$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# Build candidate set: MainMenu + Game + 1x1 scenes + everything under Resources
$guidToPath = @{}
Get-ChildItem (Join-Path $root "Assets") -Recurse -Filter "*.meta" | ForEach-Object {
    if ((Get-Content $_.FullName -Raw) -match 'guid: ([a-f0-9]{32})') {
        $guidToPath[$Matches[1]] = $_.FullName.Substring(0, $_.FullName.Length - 5)
    }
}

function Get-GuidsFromText([string]$text) {
    $set = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($m in [regex]::Matches($text, 'guid: ([a-f0-9]{32})')) {
        $g = $m.Groups[1].Value
        if ($g -notmatch '^0+$') { [void]$set.Add($g) }
    }
    return $set
}

function Expand-Dependencies([System.Collections.Generic.HashSet[string]]$seedGuids) {
    $all = New-Object 'System.Collections.Generic.HashSet[string]'
    $queue = New-Object System.Collections.Queue
    foreach ($g in $seedGuids) { [void]$all.Add($g); $queue.Enqueue($g) }
    while ($queue.Count -gt 0) {
        $g = $queue.Dequeue()
        if (-not $guidToPath.ContainsKey($g)) { continue }
        $p = $guidToPath[$g]
        if (-not (Test-Path $p) -or (Get-Item $p).PSIsContainer) { continue }
        $text = Get-Content $p -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        foreach ($dep in Get-GuidsFromText $text) {
            if ($all.Add($dep)) { $queue.Enqueue($dep) }
        }
    }
    return $all
}

$seed = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($scene in @("Assets/Scenes/MainMenu.unity", "Assets/Scenes/Game.unity", "Assets/Scenes/1x1.unity")) {
    $text = Get-Content (Join-Path $root $scene) -Raw
    foreach ($g in Get-GuidsFromText $text) { [void]$seed.Add($g) }
}

$resRoot = Join-Path $root "Assets/Resources"
if (Test-Path $resRoot) {
    Get-ChildItem $resRoot -Recurse -Filter "*.meta" | ForEach-Object {
        if ((Get-Content $_.FullName -Raw) -match 'guid: ([a-f0-9]{32})') {
            [void]$seed.Add($Matches[1])
        }
    }
}

$localGroup = Join-Path $root "Assets/AddressableAssetsData/AssetGroups/Skins_Local.asset"
if (Test-Path $localGroup) {
    foreach ($m in [regex]::Matches((Get-Content $localGroup -Raw), 'm_GUID: ([a-f0-9]{32})')) {
        [void]$seed.Add($m.Groups[1].Value)
    }
}

$all = Expand-Dependencies $seed
$total = [long]0
$folders = @{}
$rows = @()
foreach ($g in $all) {
    if (-not $guidToPath.ContainsKey($g)) { continue }
    $p = $guidToPath[$g]
    if (-not (Test-Path $p) -or (Get-Item $p).PSIsContainer) { continue }
    $item = Get-Item $p
    $total += $item.Length
    $rel = $p.Substring((Join-Path $root 'Assets').Length + 1).Replace('\','/')
    $top = ($rel.Split('/'))[0..1] -join '/'
    if (-not $folders.ContainsKey($top)) { $folders[$top] = [long]0 }
    $folders[$top] += $item.Length
    if ($item.Length -gt 500KB) {
        $rows += [pscustomobject]@{ MB = [math]::Round($item.Length/1MB,2); Path = $rel }
    }
}

Write-Host ("Estimated LOCAL build asset closure: {0:N1} MB ({1} files)" -f ($total/1MB), $all.Count)
Write-Host "`nBy folder:"
$folders.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Host ("  {0,-35} {1,6:N1} MB" -f $_.Key, ($_.Value/1MB))
}

Write-Host "`nHeavy assets (>500KB):"
$rows | Sort-Object MB -Descending | Select-Object -First 25 | Format-Table -AutoSize

# Remote bundle sizes
$serverData = Join-Path $root "ServerData/WebGL"
if (Test-Path $serverData) {
    Write-Host "`nRemote bundles (NOT in player build if uploaded to VPS):"
    Get-ChildItem $serverData -File | ForEach-Object {
        Write-Host ("  {0,-60} {1,6:N1} MB" -f $_.Name, ($_.Length/1MB))
    }
}

$resSize = (Get-ChildItem $resRoot -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
Write-Host ("`nResources folder raw size: {0:N1} MB" -f ($resSize/1MB))
