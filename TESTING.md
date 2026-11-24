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
6. **Check shutdown and scheduling while paused.** Turn City Hall off, wait for the shutdown warning, then click **+1 month**. Check that the Developer status says the poll completed and that the poll month, shutdown count, and approval update. Turn City Hall back on, wait for the warning to clear, then choose **Skip to election**. Check that the new election appears in History and the next date advances.
7. **Save and reload a copy.** Save under a new name and reload it within the same game session. Check the government, election archive, past results, and polls. Open the map on an election held before the reload and check that its districts are still colored. The log should contain `Election data saved`, `Election data read (format v3)`, and `District IDs remapped: N of N (v3 table)`. Saves written before format v3 log `name match` instead. On the next normal launch with the mod enabled, check that the edited party lineup is still present in settings. Avoid saving a city after using **+1 month** or **Skip to election**, because its polls then wait until the game calendar reaches the test date.
8. **Switch cities explicitly.** Use **Load Game** and choose a second city by name. Check that the panel shows that city and its loaded save, that the map overlay starts closed, and that History contains only elections saved in that file. Load the first city again and check that its archive returns. Try loading an older copy of either city to verify that it shows history as of that save. Start a **New Game** and check that the panel has no elections. Then use **Continue** from the main menu and check that the history matches the save it opens.

## Election Day Tier A

Use the same Debug build and save copies as above. If the build is already installed, launch with `.\tools\dev.ps1 -NoBuild`.

1. **Check both result settings.** Pause the game. In **Options → Election Atlas**, set **Election results** to **Show final results**. In **Developer tools**, choose **Run election**. The usual Elections panel should open. Set **Election results** to **Watch Election Day**, then choose **Run election** again. The replay should open at **07:00 · Polls open** with zero ballots cast. As it advances, check that turnout rises without party standings and that the paused city stays paused. Look for `[election day] started election #` with `(from snapshot)` in the mod log.
2. **Step through voting and the count.** Choose **Step replay** in **Developer tools**. The time should advance one hour and hold. Choose **Skip to count**. At 19:00 the ballots cast total stays fixed, and the count starts at zero. Choose **Play**. Districts and provisional party totals should arrive in waves. Check that the final count equals the ballots cast and the original result, then choose **Back to results**. The log should show `[election day] step:`, `[election day] polls closed:`, and `[election day] final:`.
3. **Check the map and skips.** Open **History**, then choose **Watch Election Day** for an older result. Choose **Show map** during voting. Districts should be grey. Choose **Skip to count** and confirm that winning colors appear with the reports while the panel and tally columns stay in place. Choose **Final result**, then **Back to results**. Check that the Districts tab shows the chosen archive entry and that **View on map** shows the usual final colors. Look for `[dev map]` color-refresh timings.
4. **Exercise the edge cases.** Under **Developer tools → Election Day replays**, choose **Four parties** and **Outside residents**. Confirm that four parties fit and that residents outside districts contribute ballots but no district seat. Try **Three per wave**, **One per wave**, **Fast pace**, and **Normal pace**. Each wave-size choice restarts the replay. Check the selected wave size and pace in the `[election day]` log.
5. **Switch saves mid-replay.** Start a replay from **History**, open the map, and load another named save. The overlay should close and only the new city's history should appear. Reload the first city and replay the same saved election. Its hourly turnout, reporting order, and final tally should match. Look for `[election day] replay closed` and the existing v3 district remap log.
6. **Check the large city.** Repeat the map and replay steps on New Roxbury. Record any hitch, along with the replay preparation time in `[election day] started` and the color refresh times in `[dev map]`. Watching an archived election should produce no new census or election run.
7. **Check the city clock and party order.** From **Developer tools → Election Day replays**, choose **Use city clock** with an election in History. Compare the replay hour with the game's clock. Before 07:00 the polls should show as closed. Pause and resume the city, and confirm that the count advances only as city time passes. Polls close at 19:00 and all ballots are counted by the next midnight. Use **Clock 06:55**, **Clock 18:55**, and **Clock 23:55** under **Replay controls** to reach opening, closing, and the end of the count without waiting a whole game day. The jumps move only the replay's clock, never the city. Each logs `[election day] dev clock: jumped to`. **Skip to count** switches to timed playback. In **Close race** (popular vote, two lead changes), check that the party list reorders by cumulative votes, that **Lead changes hands** shows on those reports, and that the log has `[election day] lead change: A -> B at report k`. Check that **Close race (most districts)** still uses its own governing rule.
8. **Keep a live Election Day running.** With **Election Day pace** set to **City clock**, start a live day with **Use city clock**, then close the Elections panel. Jump the clock with the panel closed, then reopen it. The replay should be at the later hour, not restarted. In **History**, choose **Watch Election Day** for that same election. It should return to the live day, not start a timed replay. Loading another save should still end it with `[election day] replay closed`.
9. **Check the defaults and the pace setting.** On a fresh settings file, **Election results** should be **Watch Election Day** and **Election Day pace** should be **City clock**. Set the pace to **Faster** and let a scheduled election happen, or use **Skip to election**. The day should play in seconds per hour, whatever the city's speed.
10. **Check the district panel and empty districts.** Select a district so its info panel shows the **Election** section, then start a replay. The section should hide. When the replay finishes or closes, it should come back without reselecting the district. In a city with an empty district, such as an industrial-only one, that district should be missing from the voting-hours list and from "n of k districts".

If a step fails, send the step number, save name, election number, and matching log lines from `Logs\ElectionAtlas.Mod.log`.

## Election notifications

1. Start a Debug build and load a test city with an archived election. In **Options → Election Atlas**, check that **Election notifications** is on. Open **Developer tools → Election notifications** and choose **Show polls-closed notice**. Close the Elections panel while the popup is visible, then choose **Watch**. The latest election's Election Day view should open.
2. Choose **Show results notice** and check the governing party name. **View results** should open the **Districts** tab on that election. Preview it again and wait eight seconds to check that it disappears. Preview it once more and check **Dismiss**.
3. Set **Election results** to **Watch Election Day** and use **Run election** in the developer window. **Skip to count** should show the polls-closed notice once. Close the panel and choose **Watch**. The view should return to counting, then **Final result** should show the results notice. On a scheduled city-clock day, verify the popup at 19:00 and the results notice at midnight. Replaying the archived election afterward should produce zero notices.
4. Set **Election results** to **Show final results** and run another election. Only the results notice should appear. Turn **Election notifications** off while the popup is visible and check it closes. Both preview commands should respect the setting.
5. With notifications back on, show a preview and switch saves. The popup should clear. For each notice shown, look for `[election day] notice: polls closed for election #` or `[election day] notice: results for election #` in `Logs\ElectionAtlas.Mod.log`.

## Release checks

1. **Load the test copy with the mod disabled.** Close the game and move `Mods\ElectionAtlas` out of the game's user data folder, then load a save written by the mod. Check that the city opens and plays through a month. `Player.log` should contain one `Not serializable type: ElectionAtlas.Systems.ElectionStoreSystem` line and no exceptions. A Release build restores the mod folder.
2. **Smoke-test the Release build.** Launch through Steam and load a named test copy with election history, such as **EA-A-clean-v3**. In **History**, choose **Watch Election Day**, then **Show map** and **Skip to count**. Check that districts fill in as they report and that the final count matches the archived result. Use **Back to results** and open **Parties** to confirm the editor loads and saves. Let a normal month pass and check that **Polls** adds an entry. Save under a new name, reload it, and confirm the election still replays. Developer tools should be absent. Verify the publish metadata and thumbnail before uploading.

Send the step number and what you observed for any failed check. The game and mod logs are available under `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II`.
