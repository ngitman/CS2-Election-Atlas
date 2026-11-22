using System;
using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// Runs elections and polls over a <see cref="GroupTable"/>. Uses expected values, not dice:
    /// votes = count x turnout x share, rounded once per district and party. The only randomness is
    /// the citywide turnout shift, drawn from the seed, so the same seed always gives the same result.
    /// </summary>
    public static class ElectionModel
    {
        /// <summary>A seed that is stable for a given save and election.</summary>
        public static ulong MakeSeed(int startingYear, int electionNumber)
        {
            return ((ulong)(uint)startingYear << 32) | (uint)electionNumber;
        }

        public static ElectionResult RunElection(
            GroupTable table,
            IReadOnlyList<Party> parties,
            string incumbentId,
            GoverningRule rule,
            TurnoutPreset preset,
            ModelConfig cfg,
            ulong seed,
            GovernmentCondition condition = default)
        {
            ValidateInputs(table, parties, cfg);
            int partyCount = parties.Count;
            int districtCount = table.DistrictCount;
            int incumbent = IndexOf(parties, incumbentId);
            float incumbentPenalty = incumbent >= 0 ? condition.IncumbentPenalty(cfg) : 0f;

            var rng = new SplitMix64(seed);
            var presetData = cfg.GetPreset(preset);
            float shift = (float)((rng.NextDouble() * 2.0 - 1.0) * cfg.TurnoutVariation);
            float target = Clamp(presetData.Target + shift, 0.01f, 0.99f);
            var curve = TurnoutModel.Calibrate(table, cfg.Turnout, presetData.ElderlyBoost, target);

            var votes = new double[districtCount * partyCount];
            var eligible = new long[districtCount];
            var shares = new float[partyCount];

            var groups = table.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                eligible[g.District] += g.Count;

                double voters = g.Count * (double)curve.Turnout(g);
                VoteModel.Shares(LeanModel.Lean(g, cfg.Lean), g.AvgHappiness, parties, incumbent, cfg, shares, incumbentPenalty);

                int row = g.District * partyCount;
                for (int p = 0; p < partyCount; p++)
                    votes[row + p] += voters * shares[p];
            }

            var districts = new DistrictResult[districtCount];
            var cityVotes = new long[partyCount];
            var districtsWon = new int[partyCount];
            long cityTotal = 0, cityEligible = 0;

            for (int d = 0; d < districtCount; d++)
            {
                var partyVotes = new long[partyCount];
                long total = 0;
                for (int p = 0; p < partyCount; p++)
                {
                    long v = (long)Math.Round(votes[d * partyCount + p], MidpointRounding.AwayFromZero);
                    partyVotes[p] = v;
                    total += v;
                    cityVotes[p] += v;
                }

                int winner = TopIndex(partyVotes, out int runnerUp);
                if (winner >= 0) districtsWon[winner]++;

                districts[d] = new DistrictResult
                {
                    District = d,
                    Eligible = eligible[d],
                    Votes = total,
                    Turnout = eligible[d] > 0 ? (float)((double)total / eligible[d]) : 0f,
                    PartyVotes = partyVotes,
                    WinnerIndex = winner,
                    Margin = winner >= 0 && total > 0
                        ? (float)((double)(partyVotes[winner] - (runnerUp >= 0 ? partyVotes[runnerUp] : 0)) / total)
                        : 0f,
                };

                cityTotal += total;
                cityEligible += eligible[d];
            }

            var cityShares = new float[partyCount];
            for (int p = 0; p < partyCount; p++)
                cityShares[p] = cityTotal > 0 ? (float)((double)cityVotes[p] / cityTotal) : 0f;

            return new ElectionResult
            {
                PartyIds = CopyIds(parties),
                Eligible = cityEligible,
                Votes = cityTotal,
                Turnout = cityEligible > 0 ? (float)((double)cityTotal / cityEligible) : 0f,
                TargetTurnout = target,
                PartyVotes = cityVotes,
                Shares = cityShares,
                DistrictsWon = districtsWon,
                Rule = rule,
                GoverningPartyIndex = PickGoverningParty(rule, cityVotes, districtsWon),
                Districts = districts,
                Seed = seed,
            };
        }

        /// <summary>
        /// Monthly poll: voting intention among all voting-age residents (no turnout step) and approval of the
        /// governing party, which starts from the game's happiness and shifts with how much the group prefers
        /// that party over the others.
        /// </summary>
        public static PollResult RunPoll(GroupTable table, IReadOnlyList<Party> parties, string incumbentId, ModelConfig cfg, GovernmentCondition condition = default)
        {
            ValidateInputs(table, parties, cfg);
            int partyCount = parties.Count;
            int districtCount = table.DistrictCount;
            int incumbent = IndexOf(parties, incumbentId);
            float incumbentPenalty = incumbent >= 0 ? condition.IncumbentPenalty(cfg) : 0f;
            float approvalPenalty = incumbent >= 0 ? condition.ApprovalPenalty(cfg) : 0f;

            var intention = new double[districtCount * partyCount];
            var approvalSum = new double[districtCount];
            var respondents = new long[districtCount];
            var shares = new float[partyCount];
            var preference = new float[partyCount];

            var groups = table.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                var lean = LeanModel.Lean(g, cfg.Lean);
                respondents[g.District] += g.Count;

                float governingPreference = 0f;
                if (incumbent >= 0)
                {
                    // Preference on policy alone: no incumbent bonus, so happiness isn't counted twice.
                    VoteModel.Shares(lean, g.AvgHappiness, parties, -1, cfg, preference);
                    governingPreference = preference[incumbent];
                }

                float approval = Approval(g.AvgHappiness, incumbent >= 0, governingPreference, partyCount, cfg);
                approval = Clamp(approval - approvalPenalty, 0f, 100f);
                approvalSum[g.District] += g.Count * (double)approval;

                VoteModel.Shares(lean, g.AvgHappiness, parties, incumbent, cfg, shares, incumbentPenalty);
                int row = g.District * partyCount;
                for (int p = 0; p < partyCount; p++)
                    intention[row + p] += g.Count * (double)shares[p];
            }

            var districts = new DistrictPoll[districtCount];
            var cityIntention = new double[partyCount];
            double cityApproval = 0;
            long cityRespondents = 0;

            for (int d = 0; d < districtCount; d++)
            {
                long n = respondents[d];
                var districtIntention = new float[partyCount];
                for (int p = 0; p < partyCount; p++)
                {
                    double v = intention[d * partyCount + p];
                    cityIntention[p] += v;
                    districtIntention[p] = n > 0 ? (float)(v / n) : 0f;
                }

                districts[d] = new DistrictPoll
                {
                    District = d,
                    Respondents = n,
                    Approval = n > 0 ? (float)(approvalSum[d] / n) : 0f,
                    Intention = districtIntention,
                };

                cityApproval += approvalSum[d];
                cityRespondents += n;
            }

            var cityShares = new float[partyCount];
            for (int p = 0; p < partyCount; p++)
                cityShares[p] = cityRespondents > 0 ? (float)(cityIntention[p] / cityRespondents) : 0f;

            return new PollResult
            {
                PartyIds = CopyIds(parties),
                Respondents = cityRespondents,
                Approval = cityRespondents > 0 ? (float)(cityApproval / cityRespondents) : 0f,
                Intention = cityShares,
                Districts = districts,
            };
        }

        /// <summary>
        /// A group's approval of the governing party, 0 to 100. Starts at the game's happiness. Alignment is
        /// partyCount x the group's policy share for the governing party - 1, clamped to -1..+1: 0 at an even
        /// split, +1 when the group clearly prefers the government, -1 when it never would vote for it.
        /// </summary>
        public static float Approval(float avgHappiness, bool hasGovernment, float governingPreference, int partyCount, ModelConfig cfg)
        {
            if (!hasGovernment || partyCount < 2)
                return avgHappiness;

            float alignment = Clamp(partyCount * governingPreference - 1f, -1f, 1f);
            return Clamp(avgHappiness + cfg.ApprovalAlignmentPoints * alignment, 0f, 100f);
        }

        private static int PickGoverningParty(GoverningRule rule, long[] cityVotes, int[] districtsWon)
        {
            int popular = TopIndex(cityVotes, out _);
            if (rule == GoverningRule.PopularVote || popular < 0)
                return popular;

            int best = -1;
            for (int p = 0; p < districtsWon.Length; p++)
            {
                if (districtsWon[p] == 0) continue;
                if (best < 0 || districtsWon[p] > districtsWon[best]
                    || (districtsWon[p] == districtsWon[best] && cityVotes[p] > cityVotes[best]))
                    best = p;
            }

            return best >= 0 ? best : popular;
        }

        /// <summary>Index of the largest value (lowest index wins ties); -1 when every value is 0.</summary>
        private static int TopIndex(long[] values, out int runnerUp)
        {
            int top = -1;
            runnerUp = -1;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] <= 0) continue;
                if (top < 0 || values[i] > values[top])
                {
                    runnerUp = top;
                    top = i;
                }
                else if (runnerUp < 0 || values[i] > values[runnerUp])
                {
                    runnerUp = i;
                }
            }

            return top;
        }

        private static void ValidateInputs(GroupTable table, IReadOnlyList<Party> parties, ModelConfig cfg)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (parties == null) throw new ArgumentNullException(nameof(parties));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (parties.Count == 0) throw new ArgumentException("At least one party is needed.", nameof(parties));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < parties.Count; i++)
            {
                if (parties[i] == null) throw new ArgumentException("Parties can't be null.", nameof(parties));
                if (!seen.Add(parties[i].Id)) throw new ArgumentException($"Duplicate party id '{parties[i].Id}'.", nameof(parties));
            }
        }

        private static int IndexOf(IReadOnlyList<Party> parties, string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < parties.Count; i++)
                if (string.Equals(parties[i].Id, id, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private static string[] CopyIds(IReadOnlyList<Party> parties)
        {
            var ids = new string[parties.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = parties[i].Id;
            return ids;
        }

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
