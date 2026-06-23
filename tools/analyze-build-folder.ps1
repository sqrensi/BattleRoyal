$ErrorActionPreference = "Stop"
$buildPath = "C:\me\unity\ShooterPrototype\Build\WEB_YandexGames_Build(4)"
if (-not (Test-Path $buildPath)) {
    Write-Host "Build path not found: $buildPath"
    exit 1
}

$files = Get-ChildItem $buildPath -Recurse -File
$total = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host ("TOTAL: {0:N2} MB ({1} files)" -f ($total / 1MB), $files.Count)
Write-Host ""

Write-Host "By extension:"
$files | Group-Object Extension | ForEach-Object {
    $sum = ($_.Group | Measure-Object -Property Length -Sum).Sum
    [pscustomobject]@{
        Ext = if ($_.Name) { $_.Name } else { "(none)" }
        Count = $_.Count
        MB = [math]::Round($sum / 1MB, 2)
    }
} | Sort-Object MB -Descending | Format-Table -AutoSize

Write-Host "Largest files:"
$files | Sort-Object Length -Descending | Select-Object -First 20 | ForEach-Object {
    $rel = $_.FullName.Substring($buildPath.Length + 1)
    Write-Host ("{0,8:N2} MB  {1}" -f ($_.Length / 1MB), $rel)
}
