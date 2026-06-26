param(
    [int]$FromIndex = 1,
    [int]$ToIndex = 10
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$serverBuildDir = Join-Path $repoRoot "Build\Server"
$launcherDir = Join-Path $repoRoot "tools\client-launcher"

New-Item -ItemType Directory -Path $serverBuildDir -Force | Out-Null
New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null

function New-LauncherSource {
    param([int]$ClientIndex)

    @"
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

public static class LaunchClient$ClientIndex`Program
{
    private const string ClientIndexArgument = "-clientIndex $ClientIndex";

    public static int Main()
    {
        var launchDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var selfName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);

        var preferredNames = new[]
        {
            "My project (12).exe",
            "ShooterPrototype.exe",
            "BattleRoyal.exe",
            "Game.exe",
        };

        string gameExe = null;
        foreach (var name in preferredNames)
        {
            var candidate = Path.Combine(launchDir, name);
            if (File.Exists(candidate))
            {
                gameExe = candidate;
                break;
            }
        }

        if (gameExe == null)
        {
            gameExe = Directory
                .EnumerateFiles(launchDir, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => IsLaunchableGameExe(Path.GetFileName(path), selfName));
        }

        if (gameExe == null)
        {
            WriteError(launchDir, "Game executable not found in:\r\n" + launchDir);
            return 1;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = gameExe,
                Arguments = ClientIndexArgument,
                WorkingDirectory = launchDir,
                UseShellExecute = true,
            });
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(launchDir, "Failed to start:\r\n" + gameExe + "\r\n\r\n" + ex.Message);
            return 1;
        }
    }

    private static bool IsLaunchableGameExe(string fileName, string selfName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (string.Equals(fileName, selfName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.StartsWith("LaunchClient", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.StartsWith("UnityPlayer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static void WriteError(string launchDir, string message)
    {
        File.WriteAllText(Path.Combine(launchDir, "LaunchClient$ClientIndex.error.txt"), message, Encoding.UTF8);
    }
}
"@
}

$built = @()
for ($index = $FromIndex; $index -le $ToIndex; $index++) {
    $launcherName = "LaunchClient$index"
    $builtExe = Join-Path $launcherDir "$launcherName.exe"
    $serverExe = Join-Path $serverBuildDir "$launcherName.exe"
    $source = New-LauncherSource -ClientIndex $index

    Write-Host "[launcher] Compiling $launcherName.exe (player-local-$index)..."
    if (Test-Path $builtExe) {
        Remove-Item $builtExe -Force
    }

    Add-Type -TypeDefinition $source -OutputAssembly $builtExe -OutputType WindowsApplication -ReferencedAssemblies @(
        "System.dll",
        "System.Core.dll"
    )

    Copy-Item -Path $builtExe -Destination $serverExe -Force
    $built += $serverExe

    $cmdSource = Join-Path $launcherDir "$launcherName.cmd"
    if (Test-Path $cmdSource) {
        Copy-Item -Path $cmdSource -Destination (Join-Path $serverBuildDir "$launcherName.cmd") -Force
    }
}

Write-Host "[launcher] Ready ($($built.Count) launchers):"
$built | ForEach-Object { Write-Host "  $_" }
