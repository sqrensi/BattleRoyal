using System.Diagnostics;
using System.Reflection;
using System.Text;

const string ClientIndexArgument = "-clientIndex 2";
var launchDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
var selfName = Path.GetFileName(Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location);

var preferredNames = new[]
{
    "My project (12).exe",
    "ShooterPrototype.exe",
    "BattleRoyal.exe",
    "Game.exe",
};

string? gameExe = null;
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
    WriteError(launchDir, $"Game executable not found in:\r\n{launchDir}");
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
    WriteError(launchDir, $"Failed to start:\r\n{gameExe}\r\n\r\n{ex.Message}");
    return 1;
}

static bool IsLaunchableGameExe(string fileName, string selfName)
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

static void WriteError(string launchDir, string message)
{
    var logPath = Path.Combine(launchDir, "LaunchClient2.error.txt");
    File.WriteAllText(logPath, message, Encoding.UTF8);
}
