# Third-party notices

Mizu source is MIT licensed. Third-party software retains its own license.

- **.NET 10 / WPF / Windows Forms**, Microsoft and contributors. MIT, with additional third-party notices. https://github.com/dotnet/runtime and https://github.com/dotnet/wpf. Self-contained distributions include Microsoft runtime binaries. See `licenses/dotnet-*`.
- **LibreHardwareMonitorLib 0.9.6**, LibreHardwareMonitor contributors. MPL-2.0; source and license: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6. The unmodified library is distributed in binary form; its MPL terms apply to it. See `licenses/LibreHardwareMonitor-*` and its upstream third-party notices.
- PawnIO.Modules 0.1.6 (embedded in LibreHardwareMonitor) is LGPL-2.1. The original source archive and license are included in `licenses/sources` and `licenses/PawnIO.Modules-LGPL.txt`. The unmodified hardware library remains a replaceable external DLL.
- **PawnIO official installer 2.2.0**, Copyright 2026 namazso. The original, digitally signed installer is bundled unmodified in `drivers/PawnIO/`. Its embedded notice expressly permits redistribution of the unmodified installer. This is the official binary distribution, distinct from the source repository's license. See the accompanying `README.txt` for original notices, provenance and SHA256. https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0. Installation uses the default signed edition and the vendor's installer; the driver binary is not separately extracted or redistributed.

- Transitive NuGet dependencies retain their respective licenses. The release includes the package manifest and available license files under `licenses/`.
- **atkctl**, Copyright (c) 2026 Mert Cobanov, MIT. ATK packet layouts, hardware catalog facts and protocol reference consulted for an independent Windows transport implementation. Full notice: `licenses/atkctl-MIT.txt`. https://github.com/cobanov/atkctl.
- **ATK Hub** manufacturer-provided VID/PID, usage and model metadata was used to identify protocol families. Reference snapshot 3.2.29: https://bpcdn.atkgear.com/hub-v3/production/3.2.29/static/filter-BRWy5Cag.js. Vendor application and assets are not redistributed.
- **Logitech HID++** public protocol documentation: https://github.com/Logitech/cpg-docs. No Solaar implementation code is incorporated.
- **Open-Meteo** weather data, CC BY 4.0: https://open-meteo.com/ and https://creativecommons.org/licenses/by/4.0/. Attribution is available in weather settings and hover details. Public API use is subject to https://open-meteo.com/en/terms. Commercial deployments should select an appropriate service plan.
- **PushPlus** optional external service: https://www.pushplus.plus/doc/guide/api.html.

Mizu is not affiliated with Apple, MUJI, Logitech, ATK, VXE, Microsoft, Open-Meteo or PushPlus. Product names remain their owners' trademarks. UI drawings and the Mizu icon are original code-created assets; manufacturer product images are not bundled.
