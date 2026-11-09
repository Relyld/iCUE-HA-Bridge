iCUE-HA-Bridge - Corsair iCUE in Home Assistant, over MQTT
==========================================================

WHAT THIS DOES
  Publishes every Corsair iCUE device to Home Assistant as a light with its own
  colour, brightness and animated effects (rainbow, breathing, wave, pulse...),
  plus one entity that drives them all at once.

  While every light is off, iCUE keeps control of your lighting as usual. As
  soon as you turn one on from Home Assistant, this program draws in front of
  iCUE. Turn them all off and iCUE comes straight back.

BEFORE YOU START
  1. iCUE is installed and running, with "iCUE SDK" enabled in its settings.
  2. You have an MQTT broker, usually the Mosquitto broker add-on of Home
     Assistant, with the MQTT integration set up there. Note its IP address,
     and the user and password if it needs them.

INSTALLING
  Double-click install.cmd and answer the questions. It asks for administrator
  rights, installs into C:\Program Files\iCUE-HA-Bridge and starts the service.

UPDATING
  Extract the new archive and run install.cmd again. Your settings are kept.

REMOVING
  Run uninstall.ps1 from an administrator PowerShell.

IF SOMETHING DOES NOT WORK
  In PowerShell:
    Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='iCUE-HA-Bridge'} -MaxEvents 30 |
      Sort-Object TimeCreated | Format-List TimeCreated, Message

  You should see the service starting, "Connected to the MQTT broker", then
  "Connected to iCUE" with the list of your devices.

  Nothing works while iCUE is closed; the service waits and connects by itself
  once iCUE is up.

SETTINGS
  C:\Program Files\iCUE-HA-Bridge\settings.json
    mqttHost, mqttPort, mqttUser, mqttPassword  your broker
    discoveryPrefix   only if yours is not "homeassistant"
    framesPerSecond   animation rate, 30 by default, lower it to save CPU
  Restart the service after editing it.

MORE
  https://github.com/Relyld/iCUE-HA-Bridge
