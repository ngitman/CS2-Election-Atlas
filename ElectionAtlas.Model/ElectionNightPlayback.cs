using System;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// The clock for hourly voting, then the district count. Archive playback uses unscaled seconds. Scheduled
    /// election playback samples the city clock and publishes only when the hour or count changes.
    /// </summary>
    public sealed class ElectionNightPlayback
    {
        public const double DefaultSecondsPerFrame = 3.0;
        public const double MinSecondsPerFrame = 0.05;
        public const double MaxSecondsPerFrame = 30.0;

        /// <summary>Maximum number of reporting waves at the default wave size.</summary>
        public const int DefaultMaxFrames = 20;

        private double m_SecondsPerFrame;
        private double m_Waited;
        private int m_Step;
        private double? m_GameHour;

        private const int kVotingSteps = ElectionDay.ClosingHour - ElectionDay.OpeningHour;

        public ElectionNightPlayback(ElectionNight night, double secondsPerFrame = DefaultSecondsPerFrame)
        {
            Night = night ?? throw new ArgumentNullException(nameof(night));
            Day = new ElectionDay(night.Snapshot);
            SecondsPerFrame = secondsPerFrame;
            IsPlaying = true;
        }

        public ElectionNight Night { get; }
        public ElectionDay Day { get; }
        public bool IsVoting => m_Step < kVotingSteps;
        public bool FollowsGameClock => m_GameHour.HasValue;
        public bool BeforePolls => m_GameHour < ElectionDay.OpeningHour;
        public int Hour => m_GameHour.HasValue ? Math.Min(23, (int)m_GameHour.Value)
            : IsVoting ? ElectionDay.OpeningHour + m_Step : ElectionDay.ClosingHour;

        /// <summary>The frame on screen, 0 (polls close) to the final result.</summary>
        public int FrameIndex => Math.Max(0, m_Step - kVotingSteps);

        public ReportFrame Frame => Night[FrameIndex];

        /// <summary>Changes exactly when <see cref="IsRegionShown"/> can change: -1 while voting, then the count frame.</summary>
        public int MapFrame => IsVoting ? -1 : FrameIndex;

        /// <summary>
        /// Whether the map shows region <paramref name="region"/>'s result now: never while the polls are open, then
        /// once every unit of the region has reported. On the final frame every region shows.
        /// </summary>
        public bool IsRegionShown(int region) => !IsVoting && Frame.IsRegionReported(region);

        public bool IsFinished => m_Step == LastStep;
        private int LastStep => kVotingSteps + Night.FrameCount - 1;

        /// <summary>False when paused or finished.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>Real seconds between reports, clamped to <see cref="MinSecondsPerFrame"/>..<see cref="MaxSecondsPerFrame"/>.</summary>
        public double SecondsPerFrame
        {
            get => m_SecondsPerFrame;
            set => m_SecondsPerFrame = double.IsNaN(value) ? DefaultSecondsPerFrame
                : value < MinSecondsPerFrame ? MinSecondsPerFrame
                : value > MaxSecondsPerFrame ? MaxSecondsPerFrame
                : value;
        }

        /// <summary>
        /// Moves the clock on by <paramref name="realSeconds"/>. True when the next report arrived (the UI and map
        /// should update). A wait longer than a whole frame (a hitch, a loading screen) still gives one frame, and
        /// the next one waits its full time.
        /// </summary>
        public bool Advance(double realSeconds)
        {
            if (!IsPlaying || FollowsGameClock || !(realSeconds > 0)) return false;
            m_Waited += realSeconds;
            if (m_Waited < m_SecondsPerFrame) return false;

            m_Waited -= m_SecondsPerFrame;
            if (m_Waited >= m_SecondsPerFrame) m_Waited = 0;
            return MoveTo(m_Step + 1, keepWait: true);
        }

        /// <summary>Set playback from the election day's city clock. Hour 24 represents the next midnight.</summary>
        public bool FollowGameClock(double hour)
        {
            if (!IsPlaying || double.IsNaN(hour)) return false;
            bool starting = !FollowsGameClock;
            int previousHour = Hour;
            hour = Math.Max(m_GameHour.GetValueOrDefault(), Math.Min(hour, 24));
            m_GameHour = hour;

            int step = Math.Max(0, (int)hour - ElectionDay.OpeningHour);
            if (hour >= ElectionDay.ClosingHour)
            {
                // Spread all reporting waves between the close of polls and the next midnight.
                var progress = (hour - ElectionDay.ClosingHour) / (24 - ElectionDay.ClosingHour);
                step = kVotingSteps + (int)(progress * (Night.FrameCount - 1));
            }
            return MoveTo(Math.Max(step, m_Step), keepWait: false) || starting || previousHour != Hour;
        }

        /// <summary>Shows the next report now. Works while paused.</summary>
        public bool Step()
        {
            m_GameHour = null;
            return MoveTo(m_Step + 1, keepWait: false);
        }

        /// <summary>Closes the polls and starts at the first count frame.</summary>
        public bool SkipToCount()
        {
            if (!IsVoting) return false;
            m_GameHour = null;
            return MoveTo(kVotingSteps, keepWait: false);
        }

        /// <summary>Jumps to the final, archived result.</summary>
        public bool SkipToFinal()
        {
            if (IsFinished) return false;
            m_GameHour = null;
            return MoveTo(LastStep, keepWait: false);
        }

        public void Pause() => IsPlaying = false;

        public void Play() => IsPlaying = !IsFinished;

        /// <summary>
        /// Units per wave so a night of <paramref name="units"/> reports takes at most <paramref name="maxFrames"/>
        /// frames after the polls close: one at a time for a small city, small waves for a large one (or precincts).
        /// </summary>
        public static int WaveSizeFor(int units, int maxFrames = DefaultMaxFrames)
        {
            if (units < 0) throw new ArgumentOutOfRangeException(nameof(units));
            if (maxFrames < 1) throw new ArgumentOutOfRangeException(nameof(maxFrames));
            return units <= maxFrames ? 1 : (units + maxFrames - 1) / maxFrames;
        }

        private bool MoveTo(int step, bool keepWait)
        {
            if (step > LastStep) step = LastStep;
            if (!keepWait) m_Waited = 0;
            if (step == m_Step) return false;

            m_Step = step;
            if (IsFinished) IsPlaying = false;
            return true;
        }
    }
}
