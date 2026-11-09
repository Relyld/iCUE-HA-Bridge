<#
.SYNOPSIS
    Installs or updates iCUE-HA-Bridge as a Windows service.

.DESCRIPTION
    Run install.cmd from the folder extracted from the release archive. The script copies the
    program, asks for your MQTT broker, registers the service and starts it. Settings from a
    previous install are kept.

.PARAMETER Destination
    Folder the service runs from. Default: C:\Program Files\iCUE-HA-Bridge

.PARAMETER MqttHost
    Host name or IP address of the MQTT broker. Asked for when not given.

.PARAMETER MqttPort
    Broker port. Default: 1883

.PARAMETER MqttUser, MqttPassword
    Broker credentials, when it needs them.

.PARAMETER Unattended
    Never ask anything: use the values given on the command line and keep the rest.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -MqttHost 192.168.1.20 -MqttUser hass -MqttPassword secret
#>
[CmdletBinding()]
param(
    [string]$Destination = "$env:ProgramFiles\iCUE-HA-Bridge",
    [string]$MqttHost = '',
    [int]$MqttPort = 0,
    [string]$MqttUser = '',
    [string]$MqttPassword = '',
    [switch]$Unattended
)

$ErrorActionPreference = 'Stop'

$ServiceName  = 'iCUE-HA-Bridge'
$DisplayName  = 'iCUE Home Assistant Bridge'
$ExecutableName = 'icue-ha-bridge.exe'

# ---- relaunch as administrator -------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
                 '-Destination', "`"$Destination`"")
    if ($MqttHost)     { $argList += @('-MqttHost', "`"$MqttHost`"") }
    if ($MqttPort -gt 0) { $argList += @('-MqttPort', $MqttPort) }
    if ($MqttUser)     { $argList += @('-MqttUser', "`"$MqttUser`"") }
    if ($MqttPassword) { $argList += @('-MqttPassword', "`"$MqttPassword`"") }
    if ($Unattended)   { $argList += '-Unattended' }
    Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs
    exit
}

function Write-Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }

$failed = $false
try {
    $sourceDir = Join-Path $PSScriptRoot 'app'
    if (-not (Test-Path (Join-Path $sourceDir $ExecutableName))) {
        throw "app\$ExecutableName not found next to this script. Extract the whole release archive and run install.cmd from there."
    }

    $destFull  = [IO.Path]::GetFullPath($Destination.TrimEnd('\')).TrimEnd('\')
    $driveRoot = [IO.Path]::GetPathRoot($destFull).TrimEnd('\')
    if ($destFull -eq $driveRoot) { throw "Refusing to install into the drive root '$destFull'." }
    if ($PSScriptRoot.TrimEnd('\') -eq $destFull) { throw "Install somewhere else than the extracted archive itself." }

    # ---- stop the service while the files are replaced ---------------------------------------
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        Write-Step "Stopping $ServiceName"
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    Get-Process -Name 'icue-ha-bridge' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1

    # ---- copy the program, keeping the settings ----------------------------------------------
    Write-Step "Installing into $destFull"
    if (-not (Test-Path $destFull)) { New-Item -ItemType Directory -Path $destFull -Force | Out-Null }

    $settingsPath = Join-Path $destFull 'settings.json'
    $existing = $null
    if (Test-Path $settingsPath) {
        try { $existing = Get-Content $settingsPath -Raw | ConvertFrom-Json }
        catch { Write-Warning 'The existing settings.json could not be read, it will be replaced.' }
    }

    Copy-Item -Path (Join-Path $sourceDir '*') -Destination $destFull -Recurse -Force

    # ---- settings -----------------------------------------------------------------------------
    $settings = [ordered]@{
        mqttHost        = ''
        mqttPort        = 1883
        mqttUser        = ''
        mqttPassword    = ''
        discoveryPrefix = 'homeassistant'
        framesPerSecond = 30
    }
    if ($existing) {
        foreach ($property in $existing.PSObject.Properties) { $settings[$property.Name] = $property.Value }
    }
    if ($MqttHost)       { $settings['mqttHost'] = $MqttHost }
    if ($MqttPort -gt 0) { $settings['mqttPort'] = $MqttPort }
    if ($MqttUser)       { $settings['mqttUser'] = $MqttUser }
    if ($MqttPassword)   { $settings['mqttPassword'] = $MqttPassword }

    if (-not $settings['mqttHost'] -and -not $Unattended) {
        Write-Host ''
        Write-Host 'Address of your MQTT broker, for instance 192.168.1.20 (usually your Home Assistant machine).'
        $settings['mqttHost'] = (Read-Host 'MQTT broker').Trim()
        if (-not $settings['mqttUser']) {
            $user = (Read-Host 'MQTT user (leave empty if your broker allows anonymous access)').Trim()
            if ($user) {
                $settings['mqttUser'] = $user
                $secret = Read-Host 'MQTT password' -AsSecureString
                $settings['mqttPassword'] = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
                    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret))
            }
        }
    }
    if (-not $settings['mqttHost']) {
        Write-Warning "No MQTT broker set. Edit $settingsPath before the service can do anything."
    }
    ($settings | ConvertTo-Json) | Set-Content -Path $settingsPath -Encoding UTF8
    Write-Host "Settings written to $settingsPath"

    # ---- event log source, created here so the service never needs to ------------------------
    if (-not [System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
        New-EventLog -LogName 'Application' -Source $ServiceName
        Write-Host "Event log source '$ServiceName' created."
    }

    # ---- service --------------------------------------------------------------------------------
    $exePath = Join-Path $destFull $ExecutableName
    if (-not $service) {
        Write-Step "Creating the service $ServiceName"
        $result = sc.exe create $ServiceName binPath= "`"$exePath`"" start= delayed-auto DisplayName= "$DisplayName"
        if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed: $result" }
        sc.exe description $ServiceName "Publishes Corsair iCUE devices to Home Assistant over MQTT, with lighting effects." | Out-Null
    } else {
        sc.exe config $ServiceName binPath= "`"$exePath`"" start= delayed-auto | Out-Null
    }

    $startedAt = Get-Date
    Write-Step "Starting $ServiceName"
    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    Write-Host "$ServiceName is running." -ForegroundColor Green

    Start-Sleep -Seconds 5
    try {
        $events = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = $ServiceName } -MaxEvents 20 -ErrorAction Stop |
            Where-Object { $_.TimeCreated -ge $startedAt } | Sort-Object TimeCreated
        if ($events) {
            Write-Step 'Service log'
            foreach ($entry in $events) {
                Write-Host ("{0:HH:mm:ss}  {1}" -f $entry.TimeCreated, ($entry.Message -split "`r?`n")[0])
            }
        }
    } catch { }

    Write-Host ''
    Write-Host 'Done.' -ForegroundColor Green
    Write-Host "  Program  : $destFull"
    Write-Host "  Settings : $settingsPath"
    Write-Host "  Log      : Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='$ServiceName'}"
    Write-Host ''
    Write-Host 'In Home Assistant the devices appear under Settings > Devices & services > MQTT.'
    Write-Host 'iCUE must be running, with the iCUE SDK enabled in its settings.'
} catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    $failed = $true
}

Write-Host ''
Read-Host 'Press Enter to close'
if ($failed) { exit 1 }
