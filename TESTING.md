# Election Atlas first-release checklist

Use one combined Debug build for the party editor, voter reader, scrolling, and save checks. Start from a populated test city with districts, a City Hall, and adult students. Keep the original save and give the test save a new name.

## Build and load once

Exit Cities: Skylines II normally, then build and launch through Steam.

```powershell
.\tools\dev.ps1
```

If Windows blocks local PowerShell scripts, run the build and Steam launch directly.

```powershell
dotnet build ElectionAtlas.sln -c Debug
if ($LASTEXITCODE -eq 0) { Start-Process 'steam://rungameid/949230' }
```

Open the Elections panel from the top-left toolbar. In **Options → Election Atlas**, set an annual cadence, popular vote, and the US General turnout preset. Turn **Save election data in the city** on. The Developer drawer is available in this build.

## In the same game session

1. **Establish a baseline.** Open **Parties** and check the initial lineup. Use **Run election** in the Developer drawer. Record the winning party's name and color. Open the **Districts** tab, its detail popup, and the native Election section in a selected district. Open the map, choose **Back to results**, and check that the overlay clears. Open the **History** tab to see the archive.
2. **Edit the lineup.** Rename and recolor the winner, move both position sliders, and add a party. Switch tabs and return to check that the draft is still there. Try a duplicate name or an invalid hex color and check the validation message. Correct it, choose **Save lineup**, and check that the renamed party still shows as governing. Open the earlier election and check its original name, color, and vote totals.
3. **Exercise the editor controls.** Try **Remove party** and each preset on the draft, then choose **Discard edits**. Reopen **Parties** and check that the saved lineup is intact. Confirm the two-party minimum and scroll a long list if needed. For the next check, keep the customized lineup.
4. **Check the next poll and election.** Let a normal month boundary pass, then open **Polls** and run another election. Check that both show the edited lineup and that the older election still uses its own lineup. Look for `census from last month` and the worker slice count in `ElectionAtlas.Mod.log`.
5. **Check Student groups and reader performance.** In the Developer drawer choose **Export city CSV** and **Benchmark readers**. The export should contain `Adult` rows in the `Student` sector where the city has adult students without jobs. The log records reader and model timings. Note any stutter during either the last quarter of the month or the poll itself.
6. **Check shutdown and scheduling.** Turn City Hall off, advance one month, and check the shutdown warning and reduced approval. Turn it back on, use **Skip to election**, and check that the election runs and the next date advances.
7. **Save and reload a copy.** Save under a new name and reload it within the same game session. Check the government, election archive, past results, and polls. The log should contain both `Election data saved` and `Election data read`. On the next normal launch with the mod enabled, check that the edited party lineup is still present in settings.

## Release checks

1. **Load the test copy with the mod disabled.** Use a separate launch if needed. Check that the city opens, and look for the missing election serializer in `Player.log`. Keep the original test copy for the enabled build.
2. **Smoke-test the Release build.** The toolbar, editor, polls, elections, and map should work with the Developer drawer absent. Verify the publish metadata and the thumbnail referenced by `ElectionAtlas/Properties/PublishConfiguration.xml` before publishing.

Send the step number and what you observed for any failed check. The game and mod logs are available under `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II`.
