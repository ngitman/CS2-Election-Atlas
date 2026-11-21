using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class LeanAndVoteTests
    {
        private static readonly ModelConfig Cfg = new ModelConfig();

        [Fact]
        public void Lean_RicherGroupsSitFurtherRight()
        {
            var poor = LeanModel.Lean(TestCity.Group(wealth: 0), Cfg.Lean);
            var rich = LeanModel.Lean(TestCity.Group(wealth: 3), Cfg.Lean);
            Assert.True(rich.Economic > poor.Economic);
        }

        [Fact]
        public void Lean_MoreEducatedGroupsAreMoreProgressive()
        {
            var none = LeanModel.Lean(TestCity.Group(education: 0), Cfg.Lean);
            var uni = LeanModel.Lean(TestCity.Group(education: 4), Cfg.Lean);
            Assert.True(uni.Social < none.Social);
        }

        [Fact]
        public void Lean_ElderlyGroupsAreMoreTraditional()
        {
            var adult = LeanModel.Lean(TestCity.Group(age: AgeBand.Adult, job: JobSector.Retired), Cfg.Lean);
            var elderly = LeanModel.Lean(TestCity.Group(age: AgeBand.Elderly, job: JobSector.Retired), Cfg.Lean);
            Assert.True(elderly.Social > adult.Social);
        }

        [Fact]
        public void Lean_StaysInsideTheAxes_EvenWithExtremeWeights()
        {
            var w = new LeanWeights { WealthEconomicRichest = 5f, EducationSocialNone = 5f };
            var p = LeanModel.Lean(TestCity.Group(wealth: 3, education: 0), w);
            Assert.InRange(p.Economic, -1f, 1f);
            Assert.InRange(p.Social, -1f, 1f);
        }

        [Fact]
        public void Shares_SumToOne_AndFavorTheNearestParty()
        {
            var parties = new[]
            {
                new Party("a", "A", 0, new Position(-0.8f, 0f)),
                new Party("b", "B", 0, new Position(0f, 0f)),
                new Party("c", "C", 0, new Position(0.8f, 0.8f)),
            };
            var shares = new float[3];

            VoteModel.Shares(new Position(-0.7f, 0.1f), 50f, parties, -1, Cfg, shares);

            Assert.Equal(1f, shares.Sum(), 4);
            Assert.True(shares[0] > shares[1] && shares[1] > shares[2]);
        }

        [Fact]
        public void Shares_HappyVotersRewardTheIncumbent_UnhappyVotersPunishIt()
        {
            var midpoint = new Position(0f, 0f); // equally far from both parties
            var happy = new float[2];
            var neutral = new float[2];
            var unhappy = new float[2];

            VoteModel.Shares(midpoint, 90f, TestCity.TwoParties, 0, Cfg, happy);
            VoteModel.Shares(midpoint, 50f, TestCity.TwoParties, 0, Cfg, neutral);
            VoteModel.Shares(midpoint, 10f, TestCity.TwoParties, 0, Cfg, unhappy);

            Assert.Equal(0.5f, neutral[0], 4);
            Assert.True(happy[0] > 0.5f);
            Assert.True(unhappy[0] < 0.5f);
        }

        [Fact]
        public void Shares_SurviveLargeDistanceWeights()
        {
            var cfg = new ModelConfig { DistanceWeight = 1000f };
            var shares = new float[2];
            VoteModel.Shares(new Position(-1f, -1f), 50f, TestCity.TwoParties, -1, cfg, shares);
            Assert.All(shares, s => Assert.False(float.IsNaN(s)));
            Assert.Equal(1f, shares[0], 4);
        }
    }
}
