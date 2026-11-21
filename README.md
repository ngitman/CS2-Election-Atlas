# Election Atlas (Cities: Skylines II mod)

Election results for every district of your city, as exact numbers and as maps. Once a City Hall is built, every district votes on a schedule you set. Turnout and vote choice come from the citizens who actually live there, and a monthly poll tracks sentiment between elections. Past elections are archived so you can flick back through them. Elections in v1 don't change anything in the city; policy actions come later.

> **Work in progress.** Not yet released on Paradox Mods and not yet tested in game.

## Status

- **Phase 1 (scaffold):** done and tested in game (1.6.4): the mod and model load, Burst code loads, the settings show in Options, and the top-left button opens the Elections panel.
- **Phase 2 (City Hall gate):** built, not yet tested in game. Detects a placed City Hall (by `AdminBuilding` plus a "cityhall" prefab name, since the Central Bank carries `AdminBuilding` too), moves through No City Hall → Awaiting founding → Active ⇄ Suspended, and shows the state in the panel. Not saved yet; recomputed on load.
- **Phase 4a (model library):** done. `ElectionAtlas.Model` runs elections and polls over voter groups, with no game references: lean, turnout calibrated to real-world presets, vote shares, governing rules, approval from the game's happiness, and frozen election snapshots with tinted map colors. 44 xUnit tests.
- Next: phase 3 (voter-group reader), after spikes 4, 6 and 7 run on a populated city.

## Roadmap

1. Scaffold: mod loads, settings, empty panel
2. City Hall gate: elections unlock with a City Hall and pause without one
3. Voter-group reader: every adult resident grouped by district, age, education, wealth and job (Burst job, spread over 16 updates)
4. Vote model (done) and a debug button that runs one real election
5. Scheduler and save data: elections on a cadence, monthly polls, archive in the save file
6. Full UI: district cards, results table, and map modes for winner (tinted by margin), turnout, swing, party strength and approval
7. Tuning and release on Paradox Mods

## Layout

```
ElectionAtlas.sln
ElectionAtlas/                  the mod (net48), references Game.dll
  ElectionAtlas.csproj          builds with the official toolchain, or compile-only without it
  Mod.cs                        entry point (IMod)
  Setting.cs, LocaleEN.cs       options menu + English strings
  Systems/ElectionsUISystem.cs  C# <-> UI bindings (group "electionAtlas")
  Diagnostics/                  spike logger and save probe (Debug builds only)
  Properties/PublishConfiguration.xml   Paradox Mods metadata
  UI/                           TypeScript/React UI module, built with webpack to ElectionAtlas.mjs
ElectionAtlas.Model/            netstandard2.0, no game references: the election model
ElectionAtlas.Model.Tests/      xUnit, net8.0
```

## Prerequisites

1. **Modding toolchain**, installed from inside the game:
    1. Start Cities: Skylines II and, from the main menu, open **Options → Modding**.
    2. The toolchain section lists its dependencies, each with a status. Install every one that isn't installed yet and wait for the downloads to finish.
    3. Restart your terminal or IDE (or sign out and back in) so the new `CSII_*` user environment variables are picked up.
    4. Check it worked; this should print a folder path:

       ```powershell
       [Environment]::GetEnvironmentVariable('CSII_TOOLPATH', 'User')
       ```
2. .NET SDK 8+ and Node 20+. The toolchain's post-processor was built for .NET 6; the project lets it run on .NET 8 when .NET 6 isn't installed, so there's nothing extra to install.
3. Once, in `ElectionAtlas\UI`: `npm install`.

## Build

Everything (mod, model, tests):

```powershell
dotnet build ElectionAtlas.sln
```

Model tests (no game needed):

```powershell
dotnet test ElectionAtlas.Model.Tests
```

C# mod only:

```powershell
dotnet build ElectionAtlas\ElectionAtlas.csproj -c Release
```

- **With the toolchain:** this imports `Mod.props` / `Mod.targets`, which post-process the DLL (Burst) and deploy it to `Mods\ElectionAtlas` in the game's user data folder. The deploy wipes that folder, so the project then rebuilds the UI into it: one `dotnet build` deploys everything. Pass `-p:SkipUI=true` to skip the UI.
- **Without the toolchain:** this is a compile check against the game's own DLLs. Nothing is deployed. Copy `ElectionAtlas\Local.props.example` to `Local.props` and set `GameManagedPath` if the game isn't in the default Steam folder.

UI module:

```powershell
cd ElectionAtlas\UI; npm install; npm run build
```

With `CSII_USERDATAPATH` set (by the toolchain), the bundle goes to `<user data>\Mods\ElectionAtlas\`. Otherwise it goes to `UI\dist\`. Use `npm run dev` to rebuild on save.

## Phase 1 is done when

- [ ] The mod loads with no errors: `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\ElectionAtlas.Mod.log` shows "ElectionAtlas loaded".
- [ ] Options > Election Atlas shows the Schedule and Rules settings, and they persist after a restart.
- [ ] The top-left toolbar button opens and closes the Elections panel, and the panel's close button works.

## Running the spikes (Debug builds)

Debug builds include a diagnostics system that answers the open questions about the game in one play session. It writes everything to `ElectionAtlas.Spikes.log` and is compiled out of Release builds.

1. With the toolchain installed, build in Debug (the default) and build the UI:

   ```powershell
   dotnet build ElectionAtlas.sln
   ```

   ```powershell
   cd ElectionAtlas\UI; npm run build
   ```
2. Start the game and load a mid-size city, ideally one with a City Hall and a few districts.
3. Let it run at full speed for at least one in-game year. A full report is written at load and every 6 in-game months.
4. Send back `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\ElectionAtlas.Spikes.log` and `ElectionAtlas.Mod.log` from the same folder.

| Log tag | What it checks |
| --- | --- |
| `[spike 1]` | Which buildings carry `AdminBuilding`, and every admin building prefab in the game |
| `[spike 3]` | Whether a mod's own system is saved and loaded (only with the save probe, below) |
| `[spike 4]` | Which company type each worker's workplace has, and the buildings where it has none |
| `[spike 5]` | Year, month and calendar values at every month change |
| `[spike 6]` | Spread of household wealth, with suggested cut points for the 4 bands |
| `[spike 7]` | Residents by age group, and the age range in days of each group |
| `[spike 8]` | That `ElectionAtlas.Model.dll` loaded, and from where |
| `[phase 3]` | Voting-age residents and average happiness per district |
| `[preview]` | A real election on your city with two test parties: turnout, shares and each district's winner and margin |

**Save probe (spike 3), optional:** this checks whether the game saves data from a system a mod adds. A save made with the probe may not load once the probe is gone, so use a copy of a city.

1. Copy a save.
2. Turn on Options → Election Atlas → Debug → Save probe, then restart the game.
3. Load the copy, save it, then load it again.
4. Turn the probe off afterwards.

## Notes

- `UI/types/cs2.d.ts` is a minimal hand-written shim for `cs2/modding`, `cs2/api` and `cs2/ui`, with prop names checked against the game's UI bundle (game 1.6.4). Swap it for the toolchain's typings once installed.
- `PublishConfiguration.xml` points at `Properties/Thumbnail.png`, which doesn't exist yet. Add one before the first publish.
- Performance rule for every later phase: no per-frame work, no components added to citizens, and Burst jobs that only read game data. Targets: about 0 ms per frame between polls, under 100 ms per election (spread over frames), under 100 KB added to a save.

## License

[MIT](LICENSE). Cities: Skylines II and its assemblies belong to Colossal Order and Paradox Interactive; none of their files are in this repository.
