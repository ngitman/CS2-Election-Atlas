using System;
using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class SnapshotTests
    {
        private static readonly ModelConfig Cfg = new ModelConfig();

        private static DistrictInfo[] Districts(int n) =>
            Enumerable.Range(0, n).Select(i => new DistrictInfo(1000 + i, $"District {i}")).ToArray();

        private static (ElectionResult result, ElectionSnapshot snap) RunAndCapture(GroupTable table, int number = 1)
        {
            var result = ElectionModel.RunElection(table, TestCity.TwoParties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, Cfg, 3);
            var snap = ElectionSnapshot.Capture(result, TestCity.TwoParties, Districts(table.DistrictCount), number, 2031, 4, Cfg.Map);
            return (result, snap);
        }

        [Fact]
        public void Capture_KeepsTheSameNumbersAsTheResult()
        {
            var (result, snap) = RunAndCapture(TestCity.Random(districts: 10));

            Assert.Equal(result.PartyVotes, snap.PartyVotes);
            Assert.Equal(result.Votes, snap.Votes);
            Assert.Equal(result.Eligible, snap.Eligible);
            Assert.Equal(result.Turnout, snap.Turnout, 5);
            Assert.Equal(result.DistrictsWon, snap.DistrictsWon);
            Assert.Equal(result.GoverningPartyId, snap.GoverningParty.Id);
            Assert.Equal(result.Shares[0], snap.Share(0), 5);
            Assert.Equal(1003L, snap.Districts[3].DistrictId);
            Assert.Equal("District 3", snap.Districts[3].Name);
            Assert.Equal((2031, 4, 1), (snap.Year, snap.Month, snap.Number));
        }

        [Fact]
        public void Capture_DoesNotCountTheOutsideBucketAsADistrictWon()
        {
            var table = new GroupTable(2, new[]
            {
                new VoterGroup(0, AgeBand.Adult, 0, 0, JobSector.Unemployed, 300, 50f),
                new VoterGroup(1, AgeBand.Adult, 4, 3, JobSector.Office, 5000, 50f),
            });
            var districts = new[] { new DistrictInfo(1000, "Old Town"), DistrictInfo.Outside };
            var result = ElectionModel.RunElection(table, TestCity.TwoParties, null, GoverningRule.MostDistricts,
                TurnoutPreset.UsGeneral, Cfg, 3, default, DistrictInfo.IndexOfOutside(districts));

            var snap = ElectionSnapshot.Capture(result, TestCity.TwoParties, districts, 1, 2031, 4, Cfg.Map);

            Assert.Equal(new[] { 1, 0 }, snap.DistrictsWon);
            Assert.False(snap.Districts[1].IsSeat);
            Assert.True(snap.Districts[0].IsSeat);
            Assert.Equal(DistrictInfo.OutsideName, snap.Districts[1].Name);
        }

        [Fact]
        public void Capture_FreezesThePartyLineup()
        {
            var (_, snap) = RunAndCapture(TestCity.PoorAndRich());

            Assert.Equal(new[] { "left", "right" }, snap.Parties.Select(p => p.Id));
            Assert.Equal("Left", snap.Parties[0].Name);
            Assert.Equal(0xCC3333u, snap.Parties[0].Color);
            Assert.Equal(-0.6f, snap.Parties[0].Economic, 4);
        }

        [Fact]
        public void Capture_CopiesVoteArrays_SoTheResultCanBeReused()
        {
            var (result, snap) = RunAndCapture(TestCity.PoorAndRich());
            long before = snap.Districts[0].PartyVotes[0];

            result.Districts[0].PartyVotes[0] = 999_999;

            Assert.Equal(before, snap.Districts[0].PartyVotes[0]);
        }

        [Fact]
        public void Capture_RejectsALineupThatDoesNotMatchTheResult()
        {
            var table = TestCity.PoorAndRich();
            var result = ElectionModel.RunElection(table, TestCity.TwoParties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, Cfg, 3);
            var swapped = new[] { TestCity.Right, TestCity.Left };

            Assert.Throws<ArgumentException>(() => ElectionSnapshot.Capture(result, swapped, Districts(2), 1, 2031, 4, Cfg.Map));
            Assert.Throws<ArgumentException>(() => ElectionSnapshot.Capture(result, TestCity.TwoParties, Districts(3), 1, 2031, 4, Cfg.Map));
        }

        [Fact]
        public void MapColor_IsTheWinnersColorTintedByMargin()
        {
            var (_, snap) = RunAndCapture(TestCity.PoorAndRich());
            var d = snap.Districts[0];
            uint expected = Cfg.Map.DistrictColor(d.WinnerIndex, snap.Parties[d.WinnerIndex].Color, d.Margin);

            Assert.Equal(expected, d.MapColor);
        }

        [Fact]
        public void Tint_LandslideShowsThePartyColor_DeadHeatShowsAFaintTint()
        {
            var tint = new MapTint { Base = 0xFFFFFF, MinStrength = 0.25f, FullMargin = 0.3f };
            const uint red = 0xFF0000;

            Assert.Equal(red, tint.DistrictColor(0, red, 0.45f));
            Assert.Equal(0xFFBFBFu, tint.DistrictColor(0, red, 0f)); // 25% of the way from white to red
            Assert.Equal(tint.NoResult, tint.DistrictColor(-1, red, 0f));
        }

        [Fact]
        public void Tint_WiderMarginsAreCloserToThePartyColor()
        {
            var tint = new MapTint { Base = 0xFFFFFF };
            // Tinting white toward green: the red channel falls as the green gets stronger.
            uint RedChannel(float margin) => (tint.DistrictColor(0, 0x00AA00, margin) >> 16) & 0xFF;

            Assert.True(RedChannel(0.05f) > RedChannel(0.15f));
            Assert.True(RedChannel(0.15f) > RedChannel(0.25f));
        }

        [Fact]
        public void Archive_AcceptsElectionsInOrderOnly()
        {
            var archive = new ElectionArchive();
            var (_, first) = RunAndCapture(TestCity.PoorAndRich(), number: 1);
            var (_, third) = RunAndCapture(TestCity.PoorAndRich(), number: 3);
            var (_, second) = RunAndCapture(TestCity.PoorAndRich(), number: 2);

            archive.Add(first);
            Assert.Throws<ArgumentException>(() => archive.Add(third));
            Assert.Throws<ArgumentException>(() => archive.Add(first));
            archive.Add(second);

            Assert.Equal(2, archive.Count);
            Assert.Same(second, archive.Latest);
            Assert.Equal(3, archive.NextNumber);
        }
    }
}
