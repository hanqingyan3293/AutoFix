# Autodesk Fix Toolbox

<img src="docs/images/icon/Autodesk-Fix_128.png" alt="Autodesk Fix Toolbox" width="96" height="96" align="right">

A Windows toolbox for **repairing Autodesk installation failures and cleaning up remnants**. Graphical interface, fully offline, no login required.

Chinese: [README.md](./README.md)

## Highlights

- **Fully offline** — Makes no network requests and downloads nothing
- **No login, no license checks** — Just run it
- **Dry-run mode** — Every fix can first print its execution plan; run for real only after you confirm
- **Read-only by default** — Environment check, remnant scan, system audit and post-cleanup verification are zero-write and never touch your system
- **Per-action confirmation** — The default button is **Cancel**; deleting user project files requires typing `YES`
- **Shared-component protection** — Components shared across vendors (such as FlexNet Publisher) are listed individually for you to keep or remove; the default is keep

## Feature Overview

| Category | Count | Contents |
| --- | --- | --- |
| Environment Check | 1 | Read-only health check of system environment, Autodesk component status and installed products (22 checks) |
| Install Error Fixes | 19 | 1603, 1625, 1606, 1632, 1622, 270, 1, 4000, -9, 1327, 1308, 2, 2755, 1618, 103, 3, 1644, ARX errors, uninstall AdskLicensing |
| Permissions & Registry | 7 | 1402, 2503, 1935, 1722, 5, folder permissions, hosts |
| Components & Cleanup | 6 | Restart loop, install crash, Genuine Service, legacy licenses, A360 drive |
| Extended Features | 16 | File associations, Maya / Revit / 3ds Max language switching, file version queries, CAD file slimming and version downgrade, configuration reset |
| Product Uninstall | 12 | Uninstall workbench: scan, uninstall, deep clean, 17-point verification, system audit, Desktop Connector, pending reboot, template backup |
| License Management | 5 | Network / standalone / user license switching, license reset, product key lookup |
| Disk Cleanup | 1 | Volume cache cleanup via the built-in Windows Disk Cleanup engine |

69 features in total. Full details: [src/README.md](src/README.md) (Chinese).

## Requirements

- Windows 10 / 11 (the compatibility build also supports Windows 7 SP1 and later)
- .NET Framework 4.7.2 or later (the compatibility build needs only 4.0)
- Fix operations require administrator privileges (declared in the application manifest, UAC is requested on launch)

## Build

```
git clone https://github.com/hanqingyan3293/Autodesk-Fix.git
cd <repository directory>

# Standard build (net472)
dotnet build src/Autodesk-Fix.csproj -c Release

# Compatibility build (net40, runs on Win7 SP1+)
dotnet build src/Autodesk-Fix.net40.csproj -c Release
```

Output:

```
src/bin/Release/net472/Autodesk-Fix.exe
src/bin/Release/net40/Autodesk-Fix.exe
```

No third-party DLL dependencies — a single file is enough to run.

## Usage

1. Run `Autodesk-Fix.exe` as administrator (the manifest declares `requireAdministrator`)
2. Turn on **Dry-run mode** in the top bar first, run the feature you need, and check that the "will execute" list in the log matches your expectations
3. Turn dry-run off and execute for real; every destructive action shows a confirmation dialog listing the **specific** operations
4. On first launch you will see a SmartScreen warning (unsigned binary) — choose "More info → Run anyway"

## Safety Design

| Mechanism | Description |
| --- | --- |
| Dry-run mode | 33 interception points covering all destructive operations: stopping services, killing processes, deleting folders/files, deleting registry keys/values, changing ACLs, writing files, clearing caches, language switching, CAD slimming and downgrade |
| Confirm dialogs default to Cancel | Pressing Enter by accident cancels instead of executing |
| Type YES | Irreversible operations — full clean, deleting the Desktop Connector workspace — require manual input |
| Read-only first | Detection and audit modules perform zero writes |
| Back up before acting | Registry branches are exported as `.reg` before modification; hosts is backed up as `.autodesk-fix.bak` |

## Known Limitations

- v1.0.0 has not yet been fully validated on real machines; behaviour descriptions come from the code. **Try it on a test machine first.**
- Features that require network access are intentionally excluded, in order to stay fully offline.
- CAD version downgrade requires ODA File Converter installed locally (the program never downloads it).
- CAD file slimming requires AutoCAD installed locally.
- Some security software may flag behaviour such as stopping services or changing registry permissions.

## Project Structure

```
src/                 Main application (WinForms, .NET Framework 4.0 / 4.7.2)
docs/                Documentation, release notes, maintainer checklists
docs/images/icon/    Icon assets (multi-size PNG + ICO + SVG source)
```

## Documentation

- [src/README.md](src/README.md) — Feature details, dry-run mode, results page
- [CHANGELOG.md](CHANGELOG.md) — Version history
- [CONTRIBUTING.md](CONTRIBUTING.md) — How to contribute
- [SECURITY.md](SECURITY.md) — How to report security issues
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — Third-party sources and licenses

> Note: apart from this file, the project documentation is currently written in Chinese.
> Contributions translating the remaining documents are welcome.

## License

Released under the [GNU AGPL-3.0](LICENSE) license.

The project icon (Autodesk-Fix.ico / Autodesk-Fix.svg and the PNG set) is an original asset of this project
and is likewise released under AGPL-3.0.

```
Copyright (C) 2026 Autodesk Fix Toolbox contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published
by the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.
```

## Disclaimer

This tool stops services, kills processes, deletes files and registry entries, and modifies access permissions.
**You are responsible for reviewing what each action does and for the consequences**, especially in production
environments — back up first, or validate on a test machine. The authors accept no liability for any data loss
or system damage caused by using this tool.
