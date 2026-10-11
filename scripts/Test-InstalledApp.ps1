param(
    [Parameter(Mandatory)] [string] $InstallerPath,
    [Parameter(Mandatory)] [string] $InstallDirectory
)

$ErrorActionPreference = 'Stop'
$startedAt = Get-Date
$app = $null
$secondary = $null

# The flyout is excluded from app switchers, so Process.MainWindowHandle is unreliable.
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class FlyoutProbe
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    public static bool Find(int processId, bool visible)
    {
        bool found = false;
        EnumWindows((hwnd, state) =>
        {
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != processId || IsWindowVisible(hwnd) != visible) return true;
            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            found = title.ToString() == "WinTools";
            return !found;
        }, IntPtr.Zero);
        return found;
    }
}
'@

function Wait-Flyout($Process, [bool] $Visible) {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        if ($Process.HasExited) {
            throw "WinTools exited before creating its flyout (exit code $($Process.ExitCode))."
        }
        if ([FlyoutProbe]::Find($Process.Id, $Visible)) { return }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "WinTools did not create a flyout with Visible=$Visible."
}

try {
    $installer = Start-Process -FilePath (Resolve-Path $InstallerPath) -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=startup',
        "/DIR=`"$InstallDirectory`""
    ) -PassThru -Wait
    if ($installer.ExitCode -ne 0) { throw "Installer failed with exit code $($installer.ExitCode)." }

    $exe = Join-Path $InstallDirectory 'WinTools.exe'
    if (-not (Test-Path (Join-Path $InstallDirectory 'resources.pri'))) {
        throw 'The installer is missing the application resource index (resources.pri).'
    }
    $startup = Get-ItemPropertyValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'WinTools'
    if ($startup -ne "`"$exe`" --startup") { throw "Incorrect startup registration: $startup" }

    # Start outside the install directory, as Start menu and login launches can do.
    $app = Start-Process $exe -WorkingDirectory $env:TEMP -PassThru
    Wait-Flyout $app $true
    Start-Sleep -Seconds 2
    if ($app.HasExited) { throw "WinTools crashed after showing the flyout (exit code $($app.ExitCode))." }
    Stop-Process -Id $app.Id -Force
    $app.WaitForExit()

    $app = Start-Process $exe -ArgumentList '--startup' -WorkingDirectory $env:TEMP -PassThru
    Wait-Flyout $app $false
    Start-Sleep -Seconds 2
    if ($app.HasExited) { throw "WinTools crashed during background startup (exit code $($app.ExitCode))." }
    if ([FlyoutProbe]::Find($app.Id, $true)) { throw 'Windows startup unexpectedly showed the flyout.' }

    $secondary = Start-Process $exe -WorkingDirectory $env:TEMP -PassThru
    Wait-Flyout $app $true
    if (-not $secondary.WaitForExit(15000)) { throw 'The second launch did not redirect to the running instance.' }
    if ($secondary.ExitCode -ne 0) { throw "The second launch failed with exit code $($secondary.ExitCode)." }
    Write-Output 'Installed app passed: manual launch, background startup, and second-launch activation.'
}
catch {
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $startedAt } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error', 'Windows Error Reporting' } |
        Select-Object TimeCreated, ProviderName, Message | Format-List | Out-Host
    throw
}
finally {
    foreach ($process in @($secondary, $app)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
    $uninstaller = Join-Path $InstallDirectory 'unins000.exe'
    if (Test-Path $uninstaller) {
        Start-Process $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
    }
}
