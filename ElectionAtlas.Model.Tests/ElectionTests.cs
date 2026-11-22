using System;
using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class ElectionTests
    {
        private static readonly ModelConfig Cfg = new ModelConfig();

        private static ElectionResult Run(GroupTable table, ulong seed = 7, string incumbent = null,
            GoverningRule rule = GoverningRule.PopularVote, Party[] parties = null)
        {
            return ElectionModel.RunElection(table, parties ?? TestCity.TwoParties, incumbent, rule, TurnoutPreset.UsGeneral, Cfg, seed);
        }

        [Fact]
        public void PoorDistrictVotesLeft_RichDistrictVotesRight()
        {
            var result = Run(TestCity.PoorAndRich());

            Assert.Equal(0, result.Districts[0].WinnerIndex); // Left
            Assert.Equal(1, result.Districts[1].WinnerIndex); // Right
        }

        [Fact]
        public void SameSeed_GivesIdenticalResults()
        {
            var table = TestCity.Random();
            var a = Run(table, seed: 42);
            var b = Run(table, seed: 42);

            Assert.Equal(a.PartyVotes, b.PartyVotes);
            Assert.Equal(a.Turnout, b.Turnout);
            Assert.Equal(a.Districts.Select(d => d.Votes), b.Districts.Select(d => d.Votes));
        }

        [Fact]
        public void DifferentSeeds_ShiftTurnout_ByAtMostThreePoints()
        {
            var table = TestCity.Random();
            float target = Cfg.GetPreset(TurnoutPreset.UsGeneral).Target;
            var turnouts = Enumerable.Range(0, 20).Select(i => Run(table, seed: (ulong)i).Turnout).ToArray();

            Assert.All(turnouts, t => Assert.InRange(t, target - 0.031f, target + 0.031f));
            Assert.True(turnouts.Distinct().Count() > 1, "turnout should vary between elections");
        }

        [Fact]
        public void CityTotals_EqualTheSumOfDistricts()
        {
            var result = Run(TestCity.Random(districts: 12));

            for (int p = 0; p < result.PartyIds.Length; p++)
                Assert.Equal(result.Districts.Sum(d => d.PartyVotes[p]), result.PartyVotes[p]);

            Assert.Equal(result.Districts.Sum(d => d.Votes), result.Votes);
            Assert.Equal(result.Districts.Sum(d => d.Eligible), result.Eligible);
            Assert.Equal(1f, result.Shares.Sum(), 4);
            Assert.All(result.Districts, d => Assert.InRange(d.Turnout, 0f, 1f));
        }

        [Fact]
        public void AnEmptyDistrict_HasNoWinner()
        {
            var table = new GroupTable(2, new[] { TestCity.Group(district: 0, count: 500) });
            var result = Run(table);

            Assert.Equal(-1, result.Districts[1].WinnerIndex);
            Assert.Equal(0, result.Districts[1].Votes);
            Assert.Equal(0f, result.Districts[1].Turnout);
        }

        [Fact]
        public void GoverningRule_MostDistricts_CanDifferFromThePopularVote()
        {
            // Two small rich districts vote Right; one huge poor district votes Left.
            var table = new GroupTable(3, new[]
            {
                new VoterGroup(0, AgeBand.Adult, 4, 3, JobSector.Office, 200, 50f),
                new VoterGroup(1, AgeBand.Adult, 4, 3, JobSector.Office, 200, 50f),
                new VoterGroup(2, AgeBand.Adult, 0, 0, JobSector.Unemployed, 5000, 50f),
            });

            var popular = Run(table, rule: GoverningRule.PopularVote);
            var districts = Run(table, rule: GoverningRule.MostDistricts);

            Assert.Equal("left", popular.GoverningPartyId);
            Assert.Equal(new[] { 1, 2 }, districts.DistrictsWon);
            Assert.Equal("right", districts.GoverningPartyId);
        }

        [Fact]
        public void GoverningRule_MostDistricts_TieGoesToThePopularVote()
        {
            var table = new GroupTable(2, new[]
            {
                new VoterGroup(0, AgeBand.Adult, 4, 3, JobSector.Office, 200, 50f),
                new VoterGroup(1, AgeBand.Adult, 0, 0, JobSector.Unemployed, 5000, 50f),
            });

            var result = Run(table, rule: GoverningRule.MostDistricts);

            Assert.Equal(new[] { 1, 1 }, result.DistrictsWon);
            Assert.Equal("left", result.GoverningPartyId);
        }

        [Fact]
        public void GoverningRule_MostDistricts_IgnoresResidentsOutsideAnyDistrict()
        {
            // Left wins the one real district. Right wins the outside bucket by a lot, so it leads the popular vote
            // and would take a 1-1 seat tie on it, but the outside bucket is not a seat.
            var table = new GroupTable(2, new[]
            {
                new VoterGroup(0, AgeBand.Adult, 0, 0, JobSector.Unemployed, 300, 50f),
                new VoterGroup(1, AgeBand.Adult, 4, 3, JobSector.Office, 5000, 50f),
            });

            var result = ElectionModel.RunElection(table, TestCity.TwoParties, null, GoverningRule.MostDistricts,
                TurnoutPreset.UsGeneral, Cfg, 7, default, outsideDistrict: 1);

            Assert.Equal(1, result.Districts[1].WinnerIndex);       // Right carries the outside bucket...
            Assert.Equal(new[] { 1, 0 }, result.DistrictsWon);       // ...but it is not a seat
            Assert.Equal("left", result.GoverningPartyId);
            Assert.True(result.PartyVotes[1] > result.PartyVotes[0]); // its votes still count citywide
        }

        [Fact]
        public void ResidentsOutsideAnyDistrict_StillCountInThePopularVote()
        {
            var table = new GroupTable(2, new[]
            {
                new VoterGroup(0, AgeBand.Adult, 0, 0, JobSector.Unemployed, 300, 50f),
                new VoterGroup(1, AgeBand.Adult, 4, 3, JobSector.Office, 5000, 50f),
            });

            var with = ElectionModel.RunElection(table, TestCity.TwoParties, null, GoverningRule.PopularVote,
                TurnoutPreset.UsGeneral, Cfg, 7, default, outsideDistrict: 1);
            var without = ElectionModel.RunElection(table, TestCity.TwoParties, null, GoverningRule.PopularVote,
                TurnoutPreset.UsGeneral, Cfg, 7);

            Assert.Equal(without.PartyVotes, with.PartyVotes);
            Assert.Equal("right", with.GoverningPartyId);
        }

        [Fact]
        public void AHappyCity_FavorsTheIncumbent()
        {
            var unhappy = Run(TestCity.PoorAndRich(happiness: 15f), incumbent: "right");
            var happy = Run(TestCity.PoorAndRich(happiness: 85f), incumbent: "right");

            Assert.True(happy.Shares[1] > unhappy.Shares[1]);
        }

        [Fact]
        public void RejectsMissingOrDuplicateParties()
        {
            var table = TestCity.PoorAndRich();
            Assert.Throws<ArgumentException>(() => Run(table, parties: Array.Empty<Party>()));
            Assert.Throws<ArgumentException>(() => Run(table, parties: new[] { TestCity.Left, TestCity.Left }));
        }

        [Fact]
        public void UnknownIncumbentId_MeansNobodyGoverns()
        {
            var table = TestCity.PoorAndRich();
            var none = Run(table, incumbent: null);
            var unknown = Run(table, incumbent: "dissolved-party");

            Assert.Equal(none.PartyVotes, unknown.PartyVotes);
        }

        [Fact]
        public void MakeSeed_IsStableAndDistinct()
        {
            Assert.Equal(ElectionModel.MakeSeed(2026, 3), ElectionModel.MakeSeed(2026, 3));
            Assert.NotEqual(ElectionModel.MakeSeed(2026, 3), ElectionModel.MakeSeed(2026, 4));
            Assert.NotEqual(ElectionModel.MakeSeed(2026, 3), ElectionModel.MakeSeed(2027, 3));
        }
    }
}
