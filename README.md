# Election Atlas (Cities: Skylines II mod)

Election results for every district of your city, as exact numbers and as maps. Once a City Hall is built, every district votes on a schedule you set. Elections and monthly polls count adult and senior residents, including adult students. Past elections are archived so you can flick back through them. Elections in v1 don't change anything in the city; policy actions come later.

> **First release in testing.** Elections, polls, and the district map have run in game. See the combined [release checklist](TESTING.md).

See the [release roadmap](ROADMAP.md) for Election Day, precincts, and later policy features.

## Status

- The monthly census reads households in worker-thread slices during the last quarter of each month. Elections and polls use the census at the month boundary. A City Hall shutdown reduces government approval and support.
- The **Parties** tab edits the lineup for future elections and polls. Party names, colors, and positions are saved in mod settings. Each archived election preserves its own party details.
- Election history is saved in the city by default. The release checklist includes verifying save/reload and that a test save opens with the mod disabled.

## Layout

```
ElectionAtlas.sln
ElectionAtlas/                  the mod (net48), references Game.dll
  ElectionAtlas.csproj          builds with the official toolchain, or compile-only without it
  Mod.cs                        entry point (IMod)
  Setting.cs, LocaleEN.cs       options menu + English strings
  Systems/CityHallSystem.cs     the City Hall gate (phase 2)
  Systems/ElectionsUISystem.cs  C# <-> UI bindings (group "electionAtlas")
  Dev/                          developer commands (DEV_TOOLS builds only)
  Diagnostics/                  spike logger and save probe (DEV_TOOLS builds only)
  Properties/PublishConfiguration.xml   Paradox Mods metadata
  UI/                           TypeScript/React UI module, built with webpack to ElectionAtlas.mjs
ElectionAtlas.Model/            netstandard2.0, no game references: the election model
ElectionAtlas.Model.Tests/      xUnit, net8.0; Cities/ holds exported cities for replay tests
ElectionAtlas.Lab/              console tool: run elections on exported cities without the game
tools/                          dev.ps1 (build, deploy, launch) and logs.ps1
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

## Developer tools

The game takes a long time to start, so most testing is set up to avoid restarts.

**Build flag.** Everything in `ElectionAtlas/Dev/` and `ElectionAtlas/Diagnostics/` is compiled only when `DEV_TOOLS` is defined: on in Debug builds, off in Release. `-p:DevTools=true` turns it on in Release, and `-p:DevTools=false` turns it off in Debug. Publishing refuses any build that has it on.

**One-command loop.** `tools\dev.ps1` builds and deploys (Debug), then starts the game through Steam. The supported Steam launch options for this workflow are `-noSplash -developerMode --uiDeveloperMode`. `-developerMode` turns on the game's built-in dev tools. `--uiDeveloperMode` opens the debugger on `http://localhost:9444` and hot-reloads the UI while `npm run dev` runs in `ElectionAtlas\UI`. The Paradox Launcher manages continuing the last save.

```powershell
.\tools\dev.ps1 -Restart
```

`-Restart` closes a running game first; `-NoLaunch`, `-NoBuild` and `-SkipUI` skip steps. `tools\logs.ps1 -Follow` prints both Election Atlas logs as they're written.

**In-game commands.** Dev builds add a Developer section to the Elections panel:

| Button | What it does |
| --- | --- |
| Run election | Reads eligible residents and holds an election with the current lineup. The first election moves Awaiting founding to Active. Later elections treat the last winner as incumbent. |
| Run poll | Voting intention among all adults and approval of the governing party |
| Clear elections | Forgets the dev elections; nobody governs |
| Spike report | Writes a full spike report to `ElectionAtlas.Spikes.log` now, even while paused |
| Export city CSV | Writes the city's voter groups to `ModsData\ElectionAtlas\dev\city-<time>.csv` in the game's user data folder |
| NoCityHall … Suspended | Forces the government state, to see each panel state without building or bulldozing |

**Offline runs.** Export a city once, then tune the model without the game:

```powershell
dotnet run --project ElectionAtlas.Lab -- "$env:USERPROFILE\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\ElectionAtlas\dev" --parties 4 --elections 3
```

A folder argument uses its newest CSV. `--preset`, `--rule`, `--incumbent` and `--seed` change the run, and `--sample <file>` writes a synthetic city if you don't have one yet. Copy exported CSVs into `ElectionAtlas.Model.Tests\Cities\` and `dotnet test` replays them too.

Run `dotnet run --project ElectionAtlas.Lab -- --day-samples` to see voting hours and district returns for the built-in elections. Add `--day` to a city CSV run to preview its turnout and count.

## Notes

- `UI/types/cs2.d.ts` is a minimal hand-written shim for `cs2/modding`, `cs2/api` and `cs2/ui`, with prop names checked against the game's UI bundle (game 1.6.4). Swap it for the toolchain's typings once installed.
- `PublishConfiguration.xml` uses the [publish thumbnail](ElectionAtlas/Properties/Thumbnail.png).
- Performance rule for every later phase: no per-frame work, no components added to citizens, and Burst jobs that only read game data. Targets: about 0 ms per frame between polls, under 100 ms per election (spread over frames), under 100 KB added to a save.

## License

[MIT](LICENSE). Cities: Skylines II and its assemblies belong to Colossal Order and Paradox Interactive; none of their files are in this repository.
