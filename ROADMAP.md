# Election Atlas release roadmap

Election Day is the next gameplay feature to try. Watch polls open, see turnout build, then follow district returns as the count comes in. The Lab prototype runs both phases from the same archived result. Geographic precincts follow as a separate milestone because they change how the census groups voters and how much election data the mod keeps.

The version labels describe an order of work. The 0.1 beta candidate has its own release checks and can ship independently of Election Day.

## 0.1 Election simulation release candidate

The staged candidate covers City Hall elections, monthly polls, editable parties, district results and maps, and saved election history. Keep it as the baseline for testing and comparing future changes.

The release pass covers switching between two named saves with the save-isolation fix installed, opening a mod-written save with the mod disabled, and running a smoke test on the Release build. Run the paused calendar shortcuts in Debug during the same round of testing. [Combined checklist](TESTING.md)

## 0.2 Election Day with district returns

Put a **Watch Election Day** action on the current result and on archived elections. During voting, show the clock, ballots cast, and turnout. Party standings first appear during counting. Let players jump to the count or the final result. During counting, reveal districts in small waves, show provisional standings and the number of districts reporting, and shade the map as districts come in.

Build the report sequence from the frozen `ElectionSnapshot`. It already carries the election seed, district IDs, party vote counts, and the election-day party names and colors. Use the seed with a stable district order for a repeatable reveal. As districts come in, add their integer vote counts. Include the outside-district vote in the citywide count and exclude it from the seat tally. Use the archived governing result when the reveal finishes, including when the most-districts rule gives a different answer from the popular vote.

Keep the reporting unit generic, a district now and a precinct later. Route map colors through one region color path so unreported districts, precinct results, and later map modes all recolor the same renderer.

The scheduler finalizes and saves the election on its existing path. Scheduled playback follows the city clock, and archive playback uses a separate timer so it can run while the city is paused. The view closes when the player changes cities or leaves the replay. The existing final-results view remains available alongside the replay. [Snapshot](ElectionAtlas.Model/ElectionSnapshot.cs) · [Election model](ElectionAtlas.Model/ElectionModel.cs)

- **Lab slice.** Hourly turnout rises from zero at 07:00 to the archived ballot count at 19:00, including residents outside drawn districts. Use `--day-samples` to compare turnout and district returns across the built-in examples.
- **Game slice.** The replay opens from Districts or History. **Election results** chooses whether new elections start with the replay or show final results. The map recolors existing triangles on each reporting wave. The [in-game checklist](TESTING.md#election-day-tier-a) covers both settings, two and four parties, outside-district voters, and the large city.
- **Performance check.** Verify that the reveal starts without another census read and that UI and map changes happen when a report arrives. Check the large test city's map and results in the combined in-game pass.

## 0.3 Geographic precinct returns

Prototype automatic precincts inside districts. Start by building city blocks from the game's road graph and assigning each home to its block through the road it fronts, during the existing household read. Test how many useful precincts that produces in sparse, dense, and irregular districts, including cul-de-sac suburbs. Keep district-level reporting available for older elections and for cities where the precinct option is disabled.

The first experiment should keep the district model as the source of the result. Allocate each voter group's expected party votes across precincts in proportion to its population in each precinct. Reconcile those allocations to the final district and party totals with deterministic rounding. This gives neighborhoods different results where their voter mixes differ and preserves the certified citywide count and governing result. [Voter reader](ElectionAtlas/Systems/VoterReader.cs) · [Election model](ElectionAtlas.Model/ElectionModel.cs)

The proposed precinct design builds city blocks from the game's road and rail graph with a half-edge structure. Precinct lines favour arterials, rail and water. The prototype compares two ways to draw them.

- a polling-place mode, balanced by an exact transport solution
- a random mode based on the Recombination paper

The design also covers capped precinct sizes with local splits, plans that are repaired when roads change, exact two-way vote rounding, save format v4, and why the solver stays in C#. It was revised after an expert review.

Benchmark an opt-in reader and Lab export on Test City and the roughly 470,000-resident New Roxbury. Record census and model timing, temporary memory, populated voter-group slots, and bytes per election. Use those measurements and a sample replay to decide whether precinct returns add enough detail to justify their cost. If approved, add precinct rows to Election Night, save the compact returns needed to replay them, and measure map redraw time in game. Precinct outlines and manual boundary editing each need their own player use case.

## Election Day

Scheduled Election Day playback follows the city's clock by default. Polls open at 07:00, close at 19:00, and districts report between 19:00 and midnight. The scheduler records the result and updates the governing party at the month boundary. History reopens a live Election Day while it is running. Timed replays use three real seconds per hour or reporting wave. The city save stores the result for replay from History after a reload.

- **Tier A: turnout replay.** Show polls opening and closing and a turnout counter that rises per district through the day with morning and evening peaks. Start district reporting when polls close. The frozen district ballots, party lineup, and seed reconstruct the whole replay after a reload. The model uses voter groups and writes only aggregate results. Add polling-place markers and precinct counters once 0.3 has polling places.
- **Tier B: sampled real trips.** After 0.3, send a capped sample of real voters to their polling place, for example 1 per 100 voters and at most 2,000 per election day, so lines form at schools. The game has no voting trip purpose. First confirm from the game code that a borrowed purpose with a polling-place target arrives cleanly. Then benchmark in Test City with 500, 2,000 and 10,000 trips: frame time, simulation speed, pathfinding queue length and pedestrian count. The budget is no visible slowdown in the simulation and under 1 ms per frame of the mod's own work.
- **Tier C: every voter travels.** Not planned. At 65% turnout, New Roxbury would add about 213,000 round trips (about 426,000 path requests) in one in-game day, roughly the size of the city's whole commute. It would also have the mod write to citizens' schedules and make results depend on traffic. Reconsider only as an opt-in setting for small cities, if tier B shows that each trip costs little.

## 0.4 District candidates

Start with named candidates tied to a party and a district, with repeat candidacies across elections. Decide whether the first release of candidates is informational or includes a small local vote effect. Freeze candidate names and affiliations in each result, and decide what happens when a resident moves or dies, a district changes, or a party is removed. Reuse the party IDs and district IDs the mod already carries. Save candidate identity only as far as the continuity feature needs it.

## Later governance and policy

Build one player-visible policy action that the current governing party can take, using a reversible game action once an eligible one is identified. Give the action a clear effect and track the state needed to undo or reconcile it on reload and a change in government. Add a council when a selected policy feature needs seat composition, votes, or coalition approval. A citywide action can start with the existing governing rule.

## Independent tuning and deferred ideas

- **District profiles.** Review the existing Lab comparison before enabling any profile effect in the simulation. It can ship as a tuning update on its own schedule. The first Test City replay moved Left by about one point and flipped a close district while holding citywide turnout near the preset. [Lab prototype](ElectionAtlas.Lab/ProfileExperiment.cs)
- **Map views.** Turnout and historical swing can be considered as focused additions using the archive. Define the comparison rules for changed districts and party lineups before showing a swing. Party strength and approval views can follow when a player task calls for them.
- **Condition trends and turnout.** Add a district's monthly history when a specific rule needs changes in crime, health, or happiness. Record the measures and denominators that rule uses. Revisit citywide turnout calibration when a feature changes voting access or mobilization. Per-citizen registration and hidden polarization stay deferred.

## Proposed Codex and Claude split

1. **Claude built the count and playback model. Codex integrated it and added the Election Day Lab replay.** The fixtures cover the turnout timeline, a lead change, both governing rules, and outside-district residents.
2. **Codex connected the game bindings, panel, and map.** The same replay runs from History, Districts, or the new election notice. Claude can review the map, city switching, and playback behavior against the fixtures. Bring the integrated build to one in-game test session.
3. **Either agent can take the next milestone.** Start from the agreed interface and leave benchmark results, open questions, and the next test in the local handoff. Use the existing performance, save, and deployment rules for both lanes.
