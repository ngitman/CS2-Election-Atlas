using System;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// Replays turnout from a frozen election. While polls are open the public data is ballots cast,
    /// with party totals supplied separately by Election Night.
    /// </summary>
    public sealed class ElectionDay
    {
        public const int OpeningHour = 7;
        /// <summary>Polls close at 7 PM; the count starts then.</summary>
        public const int ClosingHour = 19;
        private const int HourCount = ClosingHour - OpeningHour + 1;
        private const ulong kTurnoutSalt = 0x45412D444159UL;

        // Cumulative share of final ballots, 07:00 to 19:00, with morning and evening rushes. Each hour adds at least
        // 6 points, so the ±2 × ramp district bias (it changes by at most 2 points an hour) never makes a count fall.
        private static readonly int[] kCumulativePercent = { 0, 6, 18, 29, 37, 44, 51, 58, 64, 71, 81, 92, 100 };
        private readonly long[] m_CityBallots = new long[HourCount];
        private readonly long[] m_DistrictBallots;

        public ElectionDay(ElectionSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            var districts = snapshot.Districts;
            m_DistrictBallots = new long[HourCount * districts.Length];

            // Walk the frozen order because a district's game ID can change when a save is loaded.
            var rng = new SplitMix64(snapshot.Seed ^ kTurnoutSalt);
            for (int d = 0; d < districts.Length; d++)
            {
                long total = districts[d].Votes;
                int morningBias = (int)(rng.Next() % 5) - 2;
                for (int offset = 0; offset < HourCount; offset++)
                {
                    // Shift up to six percentage points between morning and evening, fixed at both endpoints.
                    int ramp = Math.Min(3, Math.Min(offset, HourCount - 1 - offset));
                    int percent = kCumulativePercent[offset] + morningBias * ramp;
                    long ballots = total / 100 * percent + total % 100 * percent / 100;
                    m_DistrictBallots[d * HourCount + offset] = ballots;
                    m_CityBallots[offset] += ballots;
                }
            }
        }

        public ElectionSnapshot Snapshot { get; }

        /// <summary>Citywide ballots cast, including residents outside drawn districts.</summary>
        public long BallotsCastAt(int hour) => m_CityBallots[HourOffset(hour)];

        /// <summary>Ballots cast by the given district in the snapshot's order.</summary>
        public long DistrictBallotsCastAt(int districtIndex, int hour)
        {
            if ((uint)districtIndex >= (uint)Snapshot.Districts.Length)
                throw new ArgumentOutOfRangeException(nameof(districtIndex));

            return m_DistrictBallots[districtIndex * HourCount + HourOffset(hour)];
        }

        private static int HourOffset(int hour) => Math.Min(ClosingHour, Math.Max(OpeningHour, hour)) - OpeningHour;
    }
}
