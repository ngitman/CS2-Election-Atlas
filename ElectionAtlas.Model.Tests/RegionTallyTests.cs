using System;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class RegionTallyTests
    {
        [Fact]
        public void ClearWinner_HasRunnerUpLeadAndMargin()
        {
            var t = RegionTally.From(new long[] { 7, 3, 9 }, 40);

            Assert.Equal(19, t.Votes);
            Assert.Equal(40, t.Eligible);
            Assert.Equal(2, t.WinnerIndex);
            Assert.Equal(0, t.RunnerUpIndex);
            Assert.Equal(2, t.LeadVotes);
            Assert.False(t.IsTie);
            Assert.Equal((float)(2.0 / 19), t.Margin);
            Assert.Equal((float)(19.0 / 40), t.Turnout);
        }

        [Fact]
        public void Tie_GoesToTheLowerIndex_WithNoLead()
        {
            var t = RegionTally.From(new long[] { 2, 7, 7 }, 20);

            Assert.Equal(1, t.WinnerIndex);
            Assert.Equal(2, t.RunnerUpIndex);
            Assert.Equal(0, t.LeadVotes);
            Assert.True(t.IsTie);
            Assert.Equal(0f, t.Margin);
        }

        [Fact]
        public void PartiesWithoutVotes_NeverPlace()
        {
            var alone = RegionTally.From(new long[] { 0, 7, 0 }, 10);
            Assert.Equal(1, alone.WinnerIndex);
            Assert.Equal(-1, alone.RunnerUpIndex);
            Assert.Equal(7, alone.LeadVotes);
            Assert.Equal(1f, alone.Margin);

            var nobody = RegionTally.From(new long[] { 0, 0 }, 10);
            Assert.False(nobody.HasWinner);
            Assert.Equal(-1, nobody.RunnerUpIndex);
            Assert.Equal(0, nobody.LeadVotes);
            Assert.Equal(0f, nobody.Margin);
            Assert.Equal(0f, nobody.Turnout);

            Assert.Equal(0f, RegionTally.From(new long[] { 0 }, 0).Turnout);
        }

        [Fact]
        public void MatchesAnIndependentCountOnEveryDistrictOfAModelRun()
        {
            // The model now uses RegionTally itself, so compare against the rules written out from scratch:
            // the most votes wins (ties to the earlier party, nobody without a vote), margin over the next best.
            var parties = SampleParties.Four();
            var result = ElectionModel.RunElection(TestCity.Random(districts: 30, seed: 5), parties, null,
                GoverningRule.PopularVote, TurnoutPreset.UsGeneral, new ModelConfig(), 11);

            foreach (var d in result.Districts)
            {
                var t = RegionTally.From(d.PartyVotes, d.Eligible);
                long total = 0;
                int winner = -1;
                for (int p = 0; p < d.PartyVotes.Length; p++)
                {
                    total += d.PartyVotes[p];
                    if (d.PartyVotes[p] > 0 && (winner < 0 || d.PartyVotes[p] > d.PartyVotes[winner])) winner = p;
                }
                long second = 0;
                for (int p = 0; p < d.PartyVotes.Length; p++)
                    if (p != winner && d.PartyVotes[p] > second) second = d.PartyVotes[p];

                Assert.Equal(winner, t.WinnerIndex);
                Assert.Equal(total, t.Votes);
                Assert.Equal(winner >= 0 && total > 0 ? (float)((double)(d.PartyVotes[winner] - second) / total) : 0f, t.Margin);
                Assert.Equal(d.Eligible > 0 ? (float)((double)total / d.Eligible) : 0f, t.Turnout);
            }
        }

        [Fact]
        public void PickGoverningParty_MostDistricts_BreaksSeatTiesByVotesThenIndex()
        {
            Assert.Equal(1, ElectionModel.PickGoverningParty(GoverningRule.MostDistricts, new long[] { 10, 20, 30 }, new[] { 2, 2, 1 }));
            Assert.Equal(0, ElectionModel.PickGoverningParty(GoverningRule.MostDistricts, new long[] { 20, 20, 30 }, new[] { 2, 2, 1 }));
            Assert.Equal(2, ElectionModel.PickGoverningParty(GoverningRule.MostDistricts, new long[] { 10, 20, 30 }, new[] { 0, 0, 0 }));
            Assert.Equal(2, ElectionModel.PickGoverningParty(GoverningRule.PopularVote, new long[] { 10, 20, 30 }, new[] { 5, 0, 0 }));
            Assert.Equal(-1, ElectionModel.PickGoverningParty(GoverningRule.MostDistricts, new long[] { 0, 0 }, new[] { 0, 0 }));
            Assert.Throws<ArgumentException>(() => ElectionModel.PickGoverningParty(GoverningRule.PopularVote, new long[2], new int[3]));
        }

        [Fact]
        public void RejectsNull_AcceptsNoParties()
        {
            Assert.Throws<ArgumentNullException>(() => RegionTally.From(null, 0));
            Assert.False(RegionTally.From(Array.Empty<long>(), 0).HasWinner);
        }
    }
}
