# City Elections (Cities: Skylines II mod)

Simulated elections for your city. Once a City Hall is built, every district votes on a schedule you set. Turnout and vote choice come from the citizens who actually live there, and a monthly poll tracks sentiment between elections. Elections in v1 don't change anything in the city; policy actions come later.

Design and implementation plan: https://claude.ai/artifact/3bhM5xbruNAYxxo1a5ihW7

## Status: phase 1 (scaffold)

- C# mod: loads, logs, registers settings (cadence, who governs, turnout preset, pause without City Hall) and the UI bindings.
- UI module: a toolbar button (top left) that opens an empty, draggable Elections panel.
- Nothing simulates yet. City Hall detection is phase 2.

## Layout

```
CityElections/
  CityElections.csproj      builds with the official toolchain, or compile-only without it
  Mod.cs                    entry point (IMod)
  Setting.cs, LocaleEN.cs   options menu + English strings
  Systems/ElectionsUISystem.cs   C# <-> UI bindings (group "cityElections")
  Properties/PublishConfiguration.xml   Paradox Mods metadata
  UI/                       TypeScript/React UI module, built with webpack to CityElections.mjs
```

## Prerequisites

1. **Modding toolchain:** in the game, open Options > Modding and install the toolchain. It sets the `CSII_*` user environment variables used by the build. Restart your terminal or IDE afterwards.
2. .NET SDK 8+ and Node 20+ (both already present on this machine).

## Build

C# mod:

```powershell
dotnet build CityElections\CityElections.csproj -c Release
```

- **With the toolchain:** this imports `Mod.props` / `Mod.targets`, which post-process the DLL and deploy it to your local Mods folder.
- **Without the toolchain:** this is a compile check against the game's own DLLs. Nothing is deployed. Copy `CityElections\Local.props.example` to `Local.props` and set `GameManagedPath` if the game isn't in the default Steam folder.

UI module:

```powershell
cd CityElections\UI; npm install; npm run build
```

With `CSII_USERDATAPATH` set (by the toolchain), the bundle goes to `<user data>\Mods\CityElections\`. Otherwise it goes to `UI\dist\`. Use `npm run dev` to rebuild on save.

## Phase 1 is done when

- [ ] The mod loads with no errors: `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\CityElections.Mod.log` shows "CityElections loaded".
- [ ] Options > City Elections shows the Schedule and Rules settings, and they persist after a restart.
- [ ] The top-left toolbar button opens and closes the Elections panel, and the panel's close button works.

## Notes

- `UI/types/cs2.d.ts` is a minimal hand-written shim for `cs2/modding`, `cs2/api` and `cs2/ui`, with prop names checked against the game's UI bundle (game 1.6.4). Swap it for the toolchain's typings once installed.
- `PublishConfiguration.xml` points at `Properties/Thumbnail.png`, which doesn't exist yet. Add one before the first publish.
- Performance rule for every later phase: no per-frame work, no components added to citizens, and Burst jobs that only read game data. See "Keeping it lightweight" in the design doc.
