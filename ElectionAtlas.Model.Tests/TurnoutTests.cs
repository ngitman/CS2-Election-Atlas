using System;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class TurnoutTests
    {
        private static readonly ModelConfig Cfg = new ModelConfig();

        [Theory]
        [InlineData(TurnoutPreset.UsMunicipal)]
        [InlineData(TurnoutPreset.UsGeneral)]
        [InlineData(TurnoutPreset.UkGeneral)]
        [InlineData(TurnoutPreset.Nordic)]
        public void Calibrate_HitsEachPresetsTarget_WithinOnePoint(TurnoutPreset preset)
        {
            var table = TestCity.Random();
            Assert.True(table.Groups.Count > 5000, "synthetic city should be large");
            var p = Cfg.GetPreset(preset);

            var curve = TurnoutModel.Calibrate(table, Cfg.Turnout, p.ElderlyBoost, p.Target);

            Assert.InRange(WeightedTurnout(table, curve), p.Target - 0.01, p.Target + 0.01);
        }

        [Fact]
        public void Elderly_VoteMoreThanAdults_AllElseEqual()
        {
            var table = TestCity.Random();
            var p = Cfg.GetPreset(TurnoutPreset.UsGeneral);
            var curve = TurnoutModel.Calibrate(table, Cfg.Turnout, p.ElderlyBoost, p.Target);

            float adult = curve.Turnout(TestCity.Group(age: AgeBand.Adult, job: JobSector.Retired));
            float elderly = curve.Turnout(TestCity.Group(age: AgeBand.Elderly, job: JobSector.Retired));

            Assert.True(elderly > adult);
        }

        [Fact]
        public void EducatedRichHappyGroups_VoteMore()
        {
            var table = TestCity.Random();
            var curve = TurnoutModel.Calibrate(table, Cfg.Turnout, 1f, 0.6f);

            float low = curve.Turnout(TestCity.Group(education: 0, wealth: 0, happiness: 20f));
            float high = curve.Turnout(TestCity.Group(education: 4, wealth: 3, happiness: 80f));

            Assert.True(high > low);
        }

        [Fact]
        public void Calibrate_OnAnEmptyCity_DoesNotThrow()
        {
            var empty = new GroupTable(1, Array.Empty<VoterGroup>());
            var curve = TurnoutModel.Calibrate(empty, Cfg.Turnout, 1f, 0.5f);
            Assert.InRange(curve.Turnout(TestCity.Group()), 0f, 1f);
        }

        private static double WeightedTurnout(GroupTable table, TurnoutCurve curve)
        {
            double voters = 0, total = 0;
            foreach (var g in table.Groups)
            {
                voters += g.Count * (double)curve.Turnout(g);
                total += g.Count;
            }

            return voters / total;
        }
    }
}
