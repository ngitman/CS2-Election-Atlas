using System;
using Colossal.UI.Binding;
using ElectionAtlas.Model;

namespace ElectionAtlas.Systems
{
    public partial class ElectionsUISystem
    {
        private const uint kCityTicksPerDay = 262144;

        private RawValueBinding m_ReplayBinding;
        private ElectionNightPlayback m_Replay;
        private int m_ReplayArchiveIndex = -1;
        private uint? m_LiveElectionDay;
        private bool m_SectionSawReplay;
#if DEV_TOOLS
        private uint m_DevClockTicks; // dev-only offset on the replay's view of the city clock (never the simulation)
#else
        private const uint m_DevClockTicks = 0;
#endif

        internal bool ReplayActive => m_Replay != null && !m_Replay.IsFinished;

        /// <summary>The playback the map follows (null when none): <see cref="ElectionNightPlayback.IsRegionShown"/> and
        /// <see cref="ElectionNightPlayback.MapFrame"/> say which districts show their result.</summary>
        internal ElectionNightPlayback MapReplay => m_Replay;

        private uint CityTicks => unchecked(m_Elections.CityClockTicks + m_DevClockTicks);

        private void BindReplay()
        {
            AddBinding(m_ReplayBinding = new RawValueBinding(kGroup, "replay", Guarded("replay", WriteReplay)));
            AddBinding(new TriggerBinding<int>(kGroup, "watchElectionDay", index => WatchElectionDay(index)));
            AddBinding(new TriggerBinding(kGroup, "toggleReplayPause", ToggleReplayPause));
            AddBinding(new TriggerBinding(kGroup, "skipToCount", SkipToCount));
            AddBinding(new TriggerBinding(kGroup, "skipToFinal", SkipToFinal));
            AddBinding(new TriggerBinding(kGroup, "closeReplay", () =>
            {
                m_MapView.Update(false);
                EndReplay();
            }));
        }

        private void WatchElectionDay(int index, bool useCityClock = false)
        {
            if (!m_Elections.HasLoadedCity) return;
            var archive = m_Elections.Ledger.Archive;
            if ((uint)index >= (uint)archive.Count) return;
            SelectElection(index, archive.Count);

            // Watching the election whose live day is running goes back into it instead of restarting it.
            if (m_LiveElectionDay.HasValue && m_Replay.Night.Snapshot == archive[index])
            {
                Invalidate();
                m_ReplayBinding.Update();
                return;
            }
            StartReplay(archive[index], index, useCityClock: useCityClock);
        }

        /// <param name="waveSize">Reporting units per wave; 0 sizes the waves from the districts that have voters.</param>
        private void StartReplay(ElectionSnapshot snapshot, int index, int waveSize = 0, bool useCityClock = false,
            double secondsPerFrame = ElectionNightPlayback.DefaultSecondsPerFrame)
        {
#if DEV_TOOLS
            var watch = System.Diagnostics.Stopwatch.StartNew();
#endif
            if (waveSize <= 0) waveSize = ElectionNightPlayback.WaveSizeFor(ElectionNight.DistrictsWithVoters(snapshot));
            m_Replay = new ElectionNightPlayback(ElectionNight.ByDistrict(snapshot, waveSize), secondsPerFrame);
            m_ReplayArchiveIndex = index;
            m_LiveElectionDay = null;
            if (useCityClock)
            {
                uint ticks = CityTicks;
                m_LiveElectionDay = ticks / kCityTicksPerDay;
                m_Replay.FollowGameClock(HourFor(m_LiveElectionDay.Value, ticks));
            }
            m_MapView.Update(false);
            Invalidate();
            PublishReplay();
#if DEV_TOOLS
            string mode = useCityClock ? "city clock" : index < 0 ? "sample" : "archive";
            Mod.Log.Info($"[election day] started election #{snapshot.Number}, {mode}, " +
                $"{snapshot.Districts.Length} regions, wave size {waveSize}, prepared in {watch.Elapsed.TotalMilliseconds:0.00} ms (from snapshot)");
#endif
        }

        private void EndReplay()
        {
            if (m_Replay == null) return;
            m_Replay = null;
            m_ReplayArchiveIndex = -1;
            m_LiveElectionDay = null;
            m_ReplayBinding.Update();
            SyncDistrictSection();
#if DEV_TOOLS
            Mod.Log.Info("[election day] replay closed");
#endif
        }

        /// <summary>Ends a timed replay; a live Election Day keeps following the city clock.</summary>
        private void EndTimedReplay()
        {
            if (!m_LiveElectionDay.HasValue) EndReplay();
        }

        private void UpdateReplay()
        {
            if (m_Replay == null) return;
            if (m_ReplayArchiveIndex >= 0)
            {
                var archive = m_Elections.Ledger.Archive;
                if (m_ReplayArchiveIndex >= archive.Count || archive[m_ReplayArchiveIndex] != m_Replay.Night.Snapshot)
                {
                    EndReplay();
                    return;
                }
            }
            if (!m_Replay.IsPlaying) return;
            int previousFrame = m_Replay.FrameIndex;
            bool wasVoting = m_Replay.IsVoting;
            bool changed = m_LiveElectionDay.HasValue
                ? m_Replay.FollowGameClock(HourFor(m_LiveElectionDay.Value, CityTicks))
                : m_Replay.Advance(UnityEngine.Time.unscaledDeltaTime);
            if (changed) PublishReplay(wasVoting || previousFrame != m_Replay.FrameIndex);
        }

        private static double HourFor(uint day, uint ticks) =>
            ticks / kCityTicksPerDay > day ? 24 : (ticks % kCityTicksPerDay) * (24.0 / kCityTicksPerDay);

        private void ToggleReplayPause()
        {
            if (m_Replay == null || m_Replay.IsFinished) return;
            if (m_Replay.IsPlaying) m_Replay.Pause();
            else m_Replay.Play();
            m_ReplayBinding.Update();
        }

        private void SkipToCount()
        {
            if (m_Replay?.SkipToCount() != true) return;
            m_LiveElectionDay = null;
            PublishReplay();
        }

        private void SkipToFinal()
        {
            if (m_Replay?.SkipToFinal() != true) return;
            m_LiveElectionDay = null;
            PublishReplay();
        }

        private void PublishReplay(bool frameChanged = true)
        {
            m_ReplayBinding.Update();
            SyncDistrictSection();
#if DEV_TOOLS
            if (!frameChanged) return;
            var frame = m_Replay.Frame;
            if (m_Replay.IsFinished)
                Mod.Log.Info($"[election day] final: {frame.Votes} ballots counted");
            else if (!m_Replay.IsVoting && m_Replay.FrameIndex == 0)
                Mod.Log.Info($"[election day] polls closed: {m_Replay.Day.BallotsCastAt(m_Replay.Hour)} ballots cast");
            if (!m_Replay.IsVoting && frame.LeadChanged)
            {
                var parties = m_Replay.Night.Snapshot.Parties;
                int before = m_Replay.Night[frame.Index - 1].LeaderIndex;
                Mod.Log.Info($"[election day] lead change: {parties[before].Name} -> {parties[frame.LeaderIndex].Name} at report {frame.Index}");
            }
#endif
        }

        /// <summary>The district info panel hides its Election section during a replay; tell it when that changes.</summary>
        private void SyncDistrictSection()
        {
            bool active = ReplayActive;
            if (active == m_SectionSawReplay) return;
            m_SectionSawReplay = active;
            World.GetExistingSystemManaged<DistrictElectionSection>()?.RefreshForReplay();
        }

        private void WriteReplay(IJsonWriter writer)
        {
            if (m_Replay == null) writer.WriteNull();
            else writer.ElectionDayReplay(m_Replay, m_ReplayArchiveIndex < 0);
        }

#if DEV_TOOLS
        internal string DevWatchLatest()
        {
            int index = m_Elections.Ledger.Archive.Count - 1;
            if (index < 0) return "Run an election first.";
            WatchElectionDay(index);
            return $"Watching Election Day for election #{m_Replay.Night.Snapshot.Number}.";
        }

        internal string DevWatchCityClock()
        {
            int index = m_Elections.Ledger.Archive.Count - 1;
            if (index < 0) return "Run an election first.";
            EndReplay();
            WatchElectionDay(index, useCityClock: true);
            return "Watching the latest election on the city clock.";
        }

        internal string DevWatchSample(string name)
        {
            foreach (var sample in SampleNights.All())
            {
                if (sample.Key != name) continue;
                StartReplay(sample.Value, -1);
                return $"Watching the {name} sample.";
            }
            return "Unknown Election Day sample.";
        }

        internal string DevStepReplay()
        {
            if (m_Replay == null) return "Start an Election Day replay first.";
            if (m_Replay.IsFinished) return "The count is already complete.";
            m_Replay.Pause();
            m_LiveElectionDay = null;
            if (m_Replay.Step()) PublishReplay();
            else m_ReplayBinding.Update();
            Mod.Log.Info($"[election day] step: hour {m_Replay.Hour}, count frame {m_Replay.FrameIndex}");
            return "Advanced one step and paused.";
        }

        internal string DevReplayPace(string pace)
        {
            if (m_Replay == null) return "Start an Election Day replay first.";
            m_Replay.SecondsPerFrame = pace == "fast" ? 0.25 : ElectionNightPlayback.DefaultSecondsPerFrame;
            Mod.Log.Info($"[election day] pace: {m_Replay.SecondsPerFrame:0.00} seconds per step");
            return pace == "fast" ? "Fast replay pace." : "Normal replay pace.";
        }

        /// <param name="units">Reporting units per wave; 0 picks the size automatically, as a real Election Day does.</param>
        internal string DevReplayWave(int units)
        {
            if (m_Replay == null) return "Start an Election Day replay first.";
            StartReplay(m_Replay.Night.Snapshot, m_ReplayArchiveIndex, units, m_LiveElectionDay.HasValue, m_Replay.SecondsPerFrame);
            return units <= 0
                ? $"Restarted with automatic waves ({m_Replay.Night.WaveSize} per wave)."
                : $"Restarted with up to {units} reporting units per wave.";
        }

        /// <summary>Moves the replay's view of the city clock forward to <paramref name="hour"/> today (never back).</summary>
        internal string DevJumpClock(double hour)
        {
            uint now = CityTicks;
            uint target = now / kCityTicksPerDay * kCityTicksPerDay + (uint)(hour / 24.0 * kCityTicksPerDay);
            string label = $"{(int)hour:00}:{(int)Math.Round(hour % 1 * 60):00}";
            if (target <= now) return $"The city clock is already past {label}.";
            m_DevClockTicks = unchecked(m_DevClockTicks + (target - now));
            Mod.Log.Info($"[election day] dev clock: jumped to {label}");
            return $"City clock (for Election Day only) is now {label}.";
        }

        private void ResetDevClock() => m_DevClockTicks = 0;
#endif
    }
}
