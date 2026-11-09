# iCUE-HA-Bridge [![MIT license](https://img.shields.io/badge/License-MIT-blue.svg)](https://lbesson.mit-license.org/)

Corsair iCUE devices in Home Assistant, over MQTT, with animated lighting effects.

Every iCUE device shows up as a light with its own colour, brightness and effect list, plus one
entity that drives them all at once. The effects are drawn by this program through the official
iCUE SDK, so there is nothing to export from iCUE and no profile to configure.

While every light is off, iCUE keeps control of your lighting as usual. Turn one on from Home
Assistant and this program draws in front of iCUE; turn them all off and iCUE comes straight back.

*Effects*

`None` (a plain colour), `Rainbow`, `Color Cycle`, `Breathing`, `Wave`, `Pulse`.

`Rainbow` and `Color Cycle` paint their own colours; the others use the colour you picked. All of
them follow the brightness.

*What you need*

| Item | Notes |
|---|---|
| **iCUE** (Corsair) | Running, with **iCUE SDK** enabled in Settings → SDK. Everything goes through it. |
| **An MQTT broker** | Usually the Mosquitto broker add-on, with the MQTT integration set up in Home Assistant. |

Nothing else: the release is self contained, so there is no .NET runtime to install.

*Installing*

1. Download the latest `iCUE-HA-Bridge-<version>-win-x64.zip` from the
   [releases](https://github.com/Relyld/iCUE-HA-Bridge/releases) and extract it anywhere.
2. Double-click **`install.cmd`**. It asks for administrator rights, then for your MQTT broker.
3. It installs into `C:\Program Files\iCUE-HA-Bridge`, registers the service and starts it.

The devices then appear in Home Assistant under *Settings → Devices & services → MQTT*, grouped
under a single **iCUE** device.

To update, extract the new archive and run `install.cmd` again; your settings are kept.
To remove everything, run `uninstall.ps1`.

*Settings*

`C:\Program Files\iCUE-HA-Bridge\settings.json`, restart the service after editing it.

| Property | Meaning |
|---|---|
| `mqttHost`, `mqttPort` | Your MQTT broker |
| `mqttUser`, `mqttPassword` | Broker credentials, empty for an anonymous broker |
| `discoveryPrefix` | Only if yours is not `homeassistant` |
| `framesPerSecond` | Animation rate, 30 by default; lower it to save CPU |

*Log*

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='iCUE-HA-Bridge'} -MaxEvents 30 |
  Sort-Object TimeCreated | Format-List TimeCreated, Message
```

*How it works*

The service talks to iCUE through the lighting SDK, which exposes every device, every LED and
where each LED sits. A render loop paints the LEDs up to 30 times a second, which is what makes
the effects move, and writes them in one batch per frame. When no light is on, the loop idles and
the program steps behind iCUE's own lighting layer instead of drawing.

*Limitations*

* Effects are computed by this program, so they stop if the service stops.
* The whole device is treated as one light; individual keys or zones are not exposed separately.
* Devices whose LEDs report no position, such as some headsets, have their LEDs spread evenly by
  index so that effects still travel across them.
* A Windows service runs in its own session. Lighting works fine there; anything that needs to see
  your desktop, such as detecting which game is in the foreground, does not. That is why game
  detection is not part of this version.

*Building it yourself*

Only needed to work on the code.
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* `dotnet publish src/IcueHaBridge/IcueHaBridge.csproj -c Release -o publish/app`
* Every push is built by GitHub Actions; pushing a `vX.Y.Z` tag publishes the release archive.

*Thanks*

The approach, and the first working version of an iCUE to MQTT bridge, come from
[icue2mqtt](https://github.com/tobinbrooke/icue2mqtt) by tobinbrooke.
The SDK is reached through [CUESDK.NET](https://github.com/AndrewBabbitt97/CUESDK.NET).
