# In-game test plan (build of 21 November 2026)

Everything below runs from one game session. The Elections panel opens from the top-left icon. The Developer section at its bottom exists in dev builds only.

Start with `.\tools\dev.ps1 -NoBuild` (launches into your last save) or start the game normally. Use a **populated city**; a new empty map only covers the first checks.

## Before you start

Options → Election Atlas:

- Schedule: cadence **Every year**, pause without City Hall **on**
- Rules: popular vote, US general, **Two parties**
- Saving: leave **off** for the first pass (section G covers it)

## A. City Hall gate

1. On a map without a City Hall the Overview says **No City Hall**.
2. Place a City Hall: the panel opens by itself on **Elections unlocked**, and the next election shows as 12 months away.
3. Demolish it: back to **No City Hall**, nothing scheduled.

## B. Monthly polls (populated city with a City Hall)

1. Unpause. Within an in-game month the **Polls** tab shows a first record, and Overview shows approval and voting intention.
2. Press **+1 month** in Developer, unpause until the next month tick, and repeat 2–3 times: one poll per month appears.

## C. Elections

1. **Skip to election**, then unpause: the election is held at the next tick, and the panel jumps to it.
2. **Results** tab: citywide shares with votes and districts won, then every district with its winner, margin and turnout, closest races first; the colored square is its map color (faded when close).
3. Overview: a governing party, state **Elections running**, next election 12 months later.
4. **Run election** holds one immediately, even while paused. After two or three, **‹ Older / Newer ›** steps through them.

## D. Government shutdown

1. Switch the City Hall off from its info panel. Within a few seconds Overview shows the shutdown warning and 0% efficiency.
2. Press **+1 month** and unpause, three times: "3 months shut down this term", and approval drops by about 4 points a month.
3. Switch it back on, then **Skip to election**: the governing party should lose ground. `ElectionAtlas.Mod.log` lists the shutdown months on that election.

## E. Parties

**Switch parties** (or Options → Parties): the four-party lineup appears from the next poll or election, in all tabs.

## F. Performance

At each month change `ElectionAtlas.Mod.log` logs how many households the census read and in how many worker slices ("census from last month", or "on the spot" right after loading), and the model time. Note any stutter you see, at month changes or in the last quarter of a month. The dev **Benchmark readers** button logs the full-read time, the per-slice worker time and the model time on your city.

## G. Saving (experimental — use a copy of a city)

1. Copy a save. Turn on Options → Election Atlas → Saving → **Save election data**, and restart the game.
2. Load the copy, run a couple of elections and polls, then save.
3. Reload it: Overview, Results and Polls show the same history, and the log says "Election data loaded".
4. Optional, risky: disable the mod and load that copy. Tell me whether it loads.

## H. Spikes still open

With a populated city, press **Spike report** once (spikes 4, 6 and 7 need residents).

## What to send back

Nothing to attach — I read these directly: `ElectionAtlas.Mod.log`, `ElectionAtlas.Spikes.log`, `Modding.log`, `UI.log` and `Player.log`. Tell me what looked wrong or confusing, and which section it was in.
