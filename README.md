# taskmaster

General background app to deal with Windows system maintenance and automatically mitigate certain annoyances. Primary use case is dealing with limitations of aging hardware by mitigating the effect of apps behaving poorly or acting like they're the only thing running on your computer.

## Purpose

* Monitor application start-ups and apply changes to them as appropriate.
Changes that can be applied currently are CPU affinity, CPU priority, power mode, and system mixer volume based on either executable name and/or path.
* Automatic power management depending on system load.
* Microphone recording volume (not voice level) can also be monitored and reset.
* Network monitoring is also possible, though it mostly provides current IPv4 and IPv6 addresses and attempts to detect when internet connectivity is disrupted, a thing that Windows (7) itself does very poor job of informing users of.
* Ability to page applications and monitor temp folders is also included, but these features are infantile and thus mostly unusable.
* Foreground app detection, to allow some watchlist rules to be applied only when an app is at foreground.
Allowing apps that normally have higher priority to be pushed into the background when not needed.
* Automatic power mode adjustment based on system load.
* Hung foreground app detection and mitigation (reduce process priority, minimize, and/or even kill) to allow user to regain control of their system.

All features are optional, nothing is forced on the user, except the tray icon.

## Planned features

* At run-time configuration.
* Game detection – unlikely to occur as I have found no clues how to accomplish this reliably.
* Automatic load-balancing between cores.
* Improve recognition of system state and when things like disk cleanup are advisable, actual cleanup will be delegated to calling `cleanmgr`.
* Improved underlying code.
* Move to better UI system such as WPF.

## Original purpose

* To mitigate the impact of browsers (notably Chrome) making it very difficult to play games or use actual production software without closing the browsers.
* To reset microphone recording volume, due to Skype consistently adjusting it despite being told not to.

## Command-line

* --admin – requests privilege elevation if it's not already acquired.

## Installing, deployment, and usage

Place the executable and support libraries in a folder of your choice and run.
TM is intended to be fire-and-forget style, so once it's configured nicely, it should not require much attention from you unless your system configuration changes.

User configuration can be found in:
```
%APPDATA%\MKAh\Taskmaster
```

User configuration currently can not be changed during run-time.

## DLL Dependencies

* [NAudio](https://github.com/naudio/NAudio) – for audio devices
* [Serilog](https://github.com/serilog/serilog) – for logging
* [OpenHardwareMonitorLib](https://github.com/Ashwinning/openhardwaremonitorlib) - for GPU monitoring (only Nvidia tested due to lack of access to hardware or testers), desiring better supported/updated alternatives

## License

This project is available under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

You can support the development and/or the developer by donating at [Itch.io](https://mkah.itch.io/taskmaster).
