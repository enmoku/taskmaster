# taskmaster

General bloaty thing to deal with Windows system maintenance and automatically fix certain annoyances.

## Purpose

* Monitor application start-ups and apply changes to them as appropriate.
Changes that can be applied currently are CPU affinity, CPU priority, and power mode based on either executable name or path.
This was added originally due to browsers behaving like dorks but has found its uses in controlling OS services and such along with games.
* Microphone recording volume (not voice level) can also be monitored and reset,
this was added due to Skype having a habit of randomly adjusting the volume despite having been told not to.
* Network monitoring is also possible, though it mostly provides current IPv4 and IPv6 addresses and attempts to detect when internet connectivity is disrupted,
a thing that Windows itself does very poor job of informing users of.
* Ability to page applications and monitor temp folders is also included, but these features are infantile and thus mostly unusable.
* Foreground app detection, to allow some watchlist rules to be applied only when an app is at foreground.
Allowing apps that normally have higher priority to be pushed into the background when not needed.
* Automatic power mode adjustment based on system load.

## Planned features

* At run-time configuration.
* Game detection – unlikely to occur as I have found no clues how to accomplish this.
* Automatic load-balancing between cores.
* Improve recognition of system state and when things like disk cleanup are advisable, actual cleanup will be delegated to calling `cleanmgr`.
* Improved underlying code.
* Move to better UI system such as WPF.

## Command-line

* --bootdelay – adds about 30 seconds delay before TM starts processing things, allowing other programs that probably are higher priority to finish startup.
* --admin – requests privilege elevation if it's not already acquired.
* --once - scans for processes and applies watchlist rules before exiting.

## Installing, deployment, and usage

Place the executable and support libraries in a folder of your choice and run.
TM is intended to be fire-and-forget style, so once it's configured nicely, it should not require much attention from you unless your system configuration changes.

User configuration can be found in:
```
%APPDATA%\MKAh\Taskmaster
```

User configuration currently can not be changed during run-time.

## Dependencies

* [NAudio](https://github.com/naudio/NAudio) – for audio devices
* [Serilog](https://github.com/serilog/serilog) – for logging
* [SharpConfig](https://github.com/cemdervis/SharpConfig) – for INI user configuration

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

You can support the development and/or the developer by donating at [Itch.io](https://mkah.itch.io/taskmaster).
