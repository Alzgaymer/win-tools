param(
    [Parameter(Mandatory)] [string] $InstallerPath,
    [Parameter(Mandatory)] [string] $InstallDirectory
)

$ErrorActionPreference = 'Stop'
$startedAt = Get-Date
$app = $null
$secondary = $null
$probe = $null

# The flyout is excluded from app switchers, so Process.MainWindowHandle is unreliable.
Add-Type -Path (Join-Path $PSScriptRoot 'FlyoutProbe.cs')

function Wait-Flyout($Process, [bool] $Visible, [string] $Phase) {
    Write-Output "Checking $Phase (pid=$($Process.Id), expected visible=$Visible)."
    $deadline = (Get-Date).AddSeconds(30)
    do {
        if ($Process.HasExited) {
            throw "$Phase failed: WinTools exited with code $($Process.ExitCode)."
        }
        if ([FlyoutProbe]::Find($Process.Id, $Visible) -or ($Visible -and $probe.WasShown($Process.Id))) {
            Write-Output "$Phase passed. $($probe.Describe($Process.Id))"
            return
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)
    throw "$Phase failed: no flyout with Visible=$Visible. $($probe.Describe($Process.Id))"
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
    $probe = [FlyoutProbe]::new()
    $app = Start-Process $exe -WorkingDirectory $env:TEMP -PassThru
    Wait-Flyout $app $true 'Manual launch'
    Start-Sleep -Seconds 2
    if ($app.HasExited) { throw "WinTools crashed after showing the flyout (exit code $($app.ExitCode))." }
    Stop-Process -Id $app.Id -Force
    $app.WaitForExit()

    $app = Start-Process $exe -ArgumentList '--startup' -WorkingDirectory $env:TEMP -PassThru
    Wait-Flyout $app $false 'Windows startup'
    Start-Sleep -Seconds 2
    if ($app.HasExited) { throw "WinTools crashed during background startup (exit code $($app.ExitCode))." }
    if ([FlyoutProbe]::Find($app.Id, $true) -or $probe.WasShown($app.Id)) {
        throw "Windows startup unexpectedly showed the flyout. $($probe.Describe($app.Id))"
    }

    $secondary = Start-Process $exe -WorkingDirectory $env:TEMP -PassThru
    Wait-Flyout $app $true 'Second launch'
    if (-not $secondary.WaitForExit(15000)) { throw 'The second launch did not redirect to the running instance.' }
    if ($secondary.ExitCode -ne 0) { throw "The second launch failed with exit code $($secondary.ExitCode)." }
    Write-Output 'Installed app passed: manual launch, background startup, and second-launch activation.'
}
catch {
    if ($secondary) {
        Write-Output "Second launcher: pid=$($secondary.Id), exited=$($secondary.HasExited)."
        if ($secondary.HasExited) { Write-Output "Second launcher exit code: $($secondary.ExitCode)." }
    }
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $startedAt } -ErrorAction SilentlyContinue |
        Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error', 'Windows Error Reporting' } |
        Select-Object TimeCreated, ProviderName, Message | Format-List | Out-Host
    throw
}
finally {
    if ($probe) { $probe.Dispose() }
    foreach ($process in @($secondary, $app)) {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
    $uninstaller = Join-Path $InstallDirectory 'unins000.exe'
    if (Test-Path $uninstaller) {
        Start-Process $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait
    }
}
