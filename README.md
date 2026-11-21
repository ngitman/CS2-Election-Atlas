# Election Atlas (Cities: Skylines II mod)

Election results for every district of your city, as exact numbers and as maps. Once a City Hall is built, every district votes on a schedule you set. Turnout and vote choice come from the citizens who actually live there, and a monthly poll tracks sentiment between elections. Past elections are archived so you can flick back through them. Elections in v1 don't change anything in the city; policy actions come later.

> **Work in progress.** Not yet released on Paradox Mods and not yet tested in game.

## Status

- **Phase 1 (scaffold):** the mod loads, logs, registers its settings and the UI bindings; a top-left toolbar button opens an empty Elections panel. Not yet tested in game.
- **Phase 4a (model library):** done. `ElectionAtlas.Model` runs elections and polls over voter groups, with no game references: lean, turnout calibrated to real-world presets, vote shares, governing rules, approval from the game's happiness, and frozen election snapshots with tinted map colors. 44 xUnit tests.
- Next: phases 2 (City Hall gate) and 3 (voter-group reader).

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
  Properties/PublishConfiguration.xml   Paradox Mods metadata
  UI/                           TypeScript/React UI module, built with webpack to ElectionAtlas.mjs
ElectionAtlas.Model/            netstandard2.0, no game references: the election model
ElectionAtlas.Model.Tests/      xUnit, net8.0
```

## Prerequisites

1. **Modding toolchain:** in the game, open Options > Modding and install the toolchain. It sets the `CSII_*` user environment variables used by the build. Restart your terminal or IDE afterwards.
2. .NET SDK 8+ and Node 20+.

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

- **With the toolchain:** this imports `Mod.props` / `Mod.targets`, which post-process the DLL and deploy it to your local Mods folder.
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

## Notes

- `UI/types/cs2.d.ts` is a minimal hand-written shim for `cs2/modding`, `cs2/api` and `cs2/ui`, with prop names checked against the game's UI bundle (game 1.6.4). Swap it for the toolchain's typings once installed.
- `PublishConfiguration.xml` points at `Properties/Thumbnail.png`, which doesn't exist yet. Add one before the first publish.
- Performance rule for every later phase: no per-frame work, no components added to citizens, and Burst jobs that only read game data. Targets: about 0 ms per frame between polls, under 100 ms per election (spread over frames), under 100 KB added to a save.

## License

[MIT](LICENSE). Cities: Skylines II and its assemblies belong to Colossal Order and Paradox Interactive; none of their files are in this repository.
