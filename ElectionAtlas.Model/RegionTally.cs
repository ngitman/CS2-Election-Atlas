using System;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// The result of one region from its integer vote counts: ballots, winner, runner-up and margin. The one place
    /// results are decided, shared by districts (<see cref="ElectionModel"/>), the citywide total, Election Night's
    /// partial counts and, later, precincts.
    ///
    /// Tie rules: a party with no votes never wins or places; among equal counts the lower party index ranks first.
    /// Everything that decides a result is an integer; <see cref="Margin"/> and <see cref="Turnout"/> are for display.
    /// </summary>
    public readonly struct RegionTally
    {
        private RegionTally(long eligible, long votes, int winnerIndex, int runnerUpIndex, long leadVotes)
        {
            Eligible = eligible;
            Votes = votes;
            WinnerIndex = winnerIndex;
            RunnerUpIndex = runnerUpIndex;
            LeadVotes = leadVotes;
        }

        /// <summary>Voting-age residents of the region.</summary>
        public long Eligible { get; }

        /// <summary>Ballots cast: the sum of the party votes.</summary>
        public long Votes { get; }

        /// <summary>The party with the most votes; -1 when nobody voted.</summary>
        public int WinnerIndex { get; }

        /// <summary>The party in second place; -1 when fewer than two parties got votes.</summary>
        public int RunnerUpIndex { get; }

        /// <summary>The winner's votes minus the runner-up's (all of the winner's votes when nobody else got any); 0 on a tie.</summary>
        public long LeadVotes { get; }

        public bool HasWinner => WinnerIndex >= 0;

        /// <summary>Two or more parties share the top count. The lower index is still the <see cref="WinnerIndex"/>.</summary>
        public bool IsTie => WinnerIndex >= 0 && RunnerUpIndex >= 0 && LeadVotes == 0;

        /// <summary>Winner's share minus the runner-up's, 0 to 1.</summary>
        public float Margin => WinnerIndex >= 0 && Votes > 0 ? (float)((double)LeadVotes / Votes) : 0f;

        public float Turnout => Eligible > 0 ? (float)((double)Votes / Eligible) : 0f;

        public static RegionTally From(long[] partyVotes, long eligible)
        {
            if (partyVotes == null) throw new ArgumentNullException(nameof(partyVotes));
            return From(partyVotes, 0, partyVotes.Length, eligible);
        }

        /// <summary>The tally of <paramref name="count"/> party counts starting at <paramref name="start"/> (a row of a flat table).</summary>
        internal static RegionTally From(long[] values, int start, int count, long eligible)
        {
            long votes = 0;
            int top = -1, runnerUp = -1;
            for (int i = 0; i < count; i++)
            {
                long v = values[start + i];
                votes += v;
                if (v <= 0) continue;
                if (top < 0 || v > values[start + top])
                {
                    runnerUp = top;
                    top = i;
                }
                else if (runnerUp < 0 || v > values[start + runnerUp])
                {
                    runnerUp = i;
                }
            }

            long lead = top < 0 ? 0 : values[start + top] - (runnerUp >= 0 ? values[start + runnerUp] : 0);
            return new RegionTally(eligible, votes, top, runnerUp, lead);
        }
    }
}
