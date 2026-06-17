$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$serverBuildDir = Join-Path $repoRoot "Build\Server"
$launcherDir = Join-Path $repoRoot "tools\client-launcher"
$builtExe = Join-Path $launcherDir "LaunchClient2.exe"
$serverExe = Join-Path $serverBuildDir "LaunchClient2.exe"

New-Item -ItemType Directory -Path $serverBuildDir -Force | Out-Null
New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null

$source = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

public static class LaunchClient2Program
{
    private const string ClientIndexArgument = "-clientIndex 2";

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
        File.WriteAllText(Path.Combine(launchDir, "LaunchClient2.error.txt"), message, Encoding.UTF8);
    }
}
'@

Write-Host "[launcher] Compiling LaunchClient2.exe..."
if (Test-Path $builtExe) {
    Remove-Item $builtExe -Force
}

Add-Type -TypeDefinition $source -OutputAssembly $builtExe -OutputType WindowsApplication -ReferencedAssemblies @(
    "System.dll",
    "System.Core.dll"
)

Copy-Item -Path $builtExe -Destination $serverExe -Force

Write-Host "[launcher] Ready:"
Write-Host "  $serverExe"
Write-Host "  $builtExe"
