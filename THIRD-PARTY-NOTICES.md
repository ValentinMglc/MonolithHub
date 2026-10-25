# Third-party notices

Monolith Hub uses the following open-source components. Their licenses apply to those components only.

| Component | Use in Monolith Hub | License |
|---|---|---|
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | CPU, GPU, memory sensors | [MPL-2.0](https://www.mozilla.org/MPL/2.0/) |
| [HidSharp](https://www.zer7.com/software/hidsharp) | USB HID access to the lighting controller | [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0) |
| [System.Management](https://github.com/dotnet/runtime) | WMI queries (system information) | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |

## Acknowledgements

- **[OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB)**: the USB report format of the HP lighting controller
  (VID `0x103C`, PID `0x84FD`) was documented by the OpenRGB project (`HPOmen30LController`, GPL-2.0-or-later).
  Monolith Hub does not include any OpenRGB source code; it implements the documented protocol independently.
- **[PawnIO](https://pawnio.eu)**: optional kernel driver used by LibreHardwareMonitor to read CPU temperature,
  clock and power. It is not bundled with Monolith Hub; it is installed separately (from its website or `winget`).

## Trademarks

HP and OMEN are trademarks of HP Inc. Monolith Hub is an independent community project. It is not affiliated with,
endorsed by or sponsored by HP Inc. Product names are used only to describe hardware compatibility.
