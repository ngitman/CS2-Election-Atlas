using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    public sealed class DistrictResult
    {
        public int District { get; internal set; }

        /// <summary>Voting-age residents.</summary>
        public long Eligible { get; internal set; }

        /// <summary>Ballots cast (the sum of <see cref="PartyVotes"/>).</summary>
        public long Votes { get; internal set; }

        public float Turnout { get; internal set; }

        /// <summary>Votes per party, in the election's party order.</summary>
        public long[] PartyVotes { get; internal set; }

        /// <summary>Index of the party with the most votes; -1 when nobody voted. Ties go to the lower index.</summary>
        public int WinnerIndex { get; internal set; }

        /// <summary>Winner's share minus the runner-up's, 0 to 1.</summary>
        public float Margin { get; internal set; }
    }

    public sealed class ElectionResult
    {
        /// <summary>Party ids in the order every per-party array uses.</summary>
        public string[] PartyIds { get; internal set; }

        public long Eligible { get; internal set; }
        public long Votes { get; internal set; }
        public float Turnout { get; internal set; }

        /// <summary>The citywide turnout the curve was calibrated to, after the random shift.</summary>
        public float TargetTurnout { get; internal set; }

        public long[] PartyVotes { get; internal set; }
        public float[] Shares { get; internal set; }

        /// <summary>Districts won per party (districts where nobody voted count for no one).</summary>
        public int[] DistrictsWon { get; internal set; }

        public GoverningRule Rule { get; internal set; }

        /// <summary>Index of the party that governs after this election; -1 when nobody voted.</summary>
        public int GoverningPartyIndex { get; internal set; }

        public string GoverningPartyId => GoverningPartyIndex >= 0 ? PartyIds[GoverningPartyIndex] : null;

        public IReadOnlyList<DistrictResult> Districts { get; internal set; }

        public ulong Seed { get; internal set; }
    }

    public sealed class DistrictPoll
    {
        public int District { get; internal set; }

        /// <summary>Voting-age residents polled (everyone, not just likely voters).</summary>
        public long Respondents { get; internal set; }

        /// <summary>Approval of the governing party, 0 to 100. Equals mean happiness when nobody governs.</summary>
        public float Approval { get; internal set; }

        /// <summary>Voting intention per party, summing to 1. All zero when the district has no residents.</summary>
        public float[] Intention { get; internal set; }
    }

    public sealed class PollResult
    {
        public string[] PartyIds { get; internal set; }
        public long Respondents { get; internal set; }
        public float Approval { get; internal set; }
        public float[] Intention { get; internal set; }
        public IReadOnlyList<DistrictPoll> Districts { get; internal set; }
    }
}
