using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class PollTests
    {
        private static readonly ModelConfig Cfg = new ModelConfig();

        [Fact]
        public void WithNoGovernment_ApprovalIsTheGamesHappiness()
        {
            var table = TestCity.PoorAndRich(happiness: 62f);
            var poll = ElectionModel.RunPoll(table, TestCity.TwoParties, null, Cfg);

            Assert.Equal(62f, poll.Approval, 3);
            Assert.All(poll.Districts, d => Assert.Equal(62f, d.Approval, 3));
        }

        [Fact]
        public void VotersCloseToTheGovernment_ApproveMoreThanTheirHappiness()
        {
            var table = TestCity.PoorAndRich(happiness: 50f);
            var poll = ElectionModel.RunPoll(table, TestCity.TwoParties, "left", Cfg);

            Assert.True(poll.Districts[0].Approval > 50f, "the poor district sits near Left");
            Assert.True(poll.Districts[1].Approval < 50f, "the rich district sits far from Left");
        }

        [Fact]
        public void Approval_StaysBetween0And100()
        {
            var cfg = new ModelConfig { ApprovalAlignmentPoints = 500f };

            Assert.Equal(100f, ElectionModel.Approval(95f, true, 1f, 2, cfg));
            Assert.Equal(0f, ElectionModel.Approval(5f, true, 0f, 2, cfg));
        }

        [Fact]
        public void Approval_IsUnchanged_AtAnEvenSplit_OrWithOneParty()
        {
            Assert.Equal(60f, ElectionModel.Approval(60f, true, 0.5f, 2, Cfg), 3);
            Assert.Equal(60f, ElectionModel.Approval(60f, true, 1f / 3f, 3, Cfg), 3);
            Assert.Equal(60f, ElectionModel.Approval(60f, true, 1f, 1, Cfg), 3);
        }

        [Fact]
        public void Approval_DoesNotDriftUp_ForAVoterHalfwayBetweenTwoParties()
        {
            var table = new GroupTable(1, new[] { new VoterGroup(0, AgeBand.Adult, 2, 1, JobSector.Commercial, 100, 50f) });
            var lean = LeanModel.Lean(table.Groups[0], Cfg.Lean);
            var mirrorLeft = new Party("l", "L", 0, new Position(lean.Economic - 0.5f, lean.Social));
            var mirrorRight = new Party("r", "R", 0, new Position(lean.Economic + 0.5f, lean.Social));

            var poll = ElectionModel.RunPoll(table, new[] { mirrorLeft, mirrorRight }, "l", Cfg);

            Assert.Equal(50f, poll.Approval, 2);
        }

        [Fact]
        public void Intention_SumsToOne_InEveryDistrictWithResidents()
        {
            var table = TestCity.Random(districts: 8);
            var poll = ElectionModel.RunPoll(table, TestCity.TwoParties, "right", Cfg);

            Assert.Equal(1f, poll.Intention.Sum(), 3);
            Assert.All(poll.Districts, d => Assert.Equal(1f, d.Intention.Sum(), 3));
            Assert.Equal(table.TotalVoters, poll.Respondents);
        }

        [Fact]
        public void AnEmptyDistrict_PollsAsZeros()
        {
            var table = new GroupTable(2, new[] { TestCity.Group(district: 0) });
            var poll = ElectionModel.RunPoll(table, TestCity.TwoParties, null, Cfg);

            Assert.Equal(0, poll.Districts[1].Respondents);
            Assert.All(poll.Districts[1].Intention, s => Assert.Equal(0f, s));
        }
    }
}
