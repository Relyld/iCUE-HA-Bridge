<#
.SYNOPSIS
    Removes the iCUE-HA-Bridge service.

.PARAMETER Destination
    Folder the service runs from. Default: C:\Program Files\iCUE-HA-Bridge

.PARAMETER KeepFiles
    Removes the service but leaves the installed folder and its settings.
#>
[CmdletBinding()]
param(
    [string]$Destination = "$env:ProgramFiles\iCUE-HA-Bridge",
    [switch]$KeepFiles
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'iCUE-HA-Bridge'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
                 '-Destination', "`"$Destination`"")
    if ($KeepFiles) { $argList += '-KeepFiles' }
    Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs
    exit
}

try {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.Status -ne 'Stopped') {
            Write-Host "Stopping $ServiceName"
            Stop-Service -Name $ServiceName -Force
            $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Service $ServiceName removed."
    } else {
        Write-Host "Service $ServiceName was not installed."
    }
    Get-Process -Name 'icue-ha-bridge' -ErrorAction SilentlyContinue | Stop-Process -Force

    if (-not $KeepFiles) {
        $destFull  = [IO.Path]::GetFullPath($Destination.TrimEnd('\')).TrimEnd('\')
        $driveRoot = [IO.Path]::GetPathRoot($destFull).TrimEnd('\')
        if ($destFull -eq $driveRoot) { throw "Refusing to delete the drive root '$destFull'." }
        if (Test-Path $destFull) {
            Start-Sleep -Seconds 1
            Remove-Item $destFull -Recurse -Force
            Write-Host "$destFull deleted."
        }
    }

    if ([System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
        Remove-EventLog -Source $ServiceName
        Write-Host 'Event log source removed.'
    }

    Write-Host ''
    Write-Host 'iCUE and its own settings were not touched.'
    Write-Host 'The entities stay in Home Assistant until you delete them there.'
} catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ''
Read-Host 'Press Enter to close'
