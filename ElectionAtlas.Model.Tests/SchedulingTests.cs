using System;
using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class SchedulingTests
    {
        private static readonly ModelConfig Cfg = new ModelConfig();

        [Fact]
        public void MonthIndex_RoundTrips()
        {
            int idx = ElectionSchedule.MonthIndex(2031, 7);
            Assert.Equal(2031, ElectionSchedule.YearOf(idx));
            Assert.Equal(7, ElectionSchedule.MonthOf(idx));
            Assert.Equal("2031-08", ElectionSchedule.Format(idx));
        }

        [Theory]
        [InlineData(ElectionCadence.EveryYear, 12)]
        [InlineData(ElectionCadence.EveryTwoYears, 24)]
        [InlineData(ElectionCadence.EveryFourYears, 48)]
        public void FoundingAndNext_FollowTheCadence(ElectionCadence cadence, int months)
        {
            int unlocked = ElectionSchedule.MonthIndex(2026, 3);
            int founding = ElectionSchedule.FoundingElection(unlocked, cadence);
            Assert.Equal(unlocked + months, founding);
            Assert.Equal(founding + months, ElectionSchedule.NextAfter(founding, cadence));
        }

        [Fact]
        public void IsDue_AndMonthsUntil()
        {
            int due = ElectionSchedule.MonthIndex(2027, 0);
            Assert.False(ElectionSchedule.IsDue(due - 1, due));
            Assert.True(ElectionSchedule.IsDue(due, due));
            Assert.True(ElectionSchedule.IsDue(due + 5, due));
            Assert.False(ElectionSchedule.IsDue(due, ElectionSchedule.NotScheduled));
            Assert.Equal(3, ElectionSchedule.MonthsUntil(due - 3, due));
            Assert.Equal(0, ElectionSchedule.MonthsUntil(due + 2, due));
            Assert.Equal(-1, ElectionSchedule.MonthsUntil(due, ElectionSchedule.NotScheduled));
        }

        [Fact]
        public void PollHistory_KeepsCapacity_AndTrimsOldDistrictData()
        {
            var history = new PollHistory(capacity: 5, districtWindow: 2);
            for (int m = 0; m < 8; m++)
                history.Add(new PollRecord(m, null, new[] { "a" }, 50f, new[] { 1f }, new long[] { 1 }, new[] { 50f }));

            Assert.Equal(5, history.Count);
            Assert.Equal(3, history.Records[0].MonthIndex);
            Assert.Equal(7, history.Latest.MonthIndex);
            Assert.All(history.Records.Take(3), r => Assert.Null(r.DistrictApproval));
            Assert.All(history.Records.Skip(3), r => Assert.NotNull(r.DistrictApproval));
        }

        [Fact]
        public void PollHistory_RejectsOutOfOrderMonths()
        {
            var history = new PollHistory();
            history.Add(new PollRecord(10, null, new[] { "a" }, 50f, new[] { 1f }, null, null));
            Assert.Throws<ArgumentException>(() => history.Add(new PollRecord(10, null, new[] { "a" }, 50f, new[] { 1f }, null, null)));
        }

        [Fact]
        public void Shutdown_LowersApprovalOfTheGovernment()
        {
            var table = TestCity.PoorAndRich(happiness: 60f);
            var normal = ElectionModel.RunPoll(table, TestCity.TwoParties, "left", Cfg);
            var shut = ElectionModel.RunPoll(table, TestCity.TwoParties, "left", Cfg, new GovernmentCondition(3, 0));

            Assert.Equal(normal.Approval - 3 * Cfg.ShutdownApprovalPerMonth, shut.Approval, 2);
        }

        [Fact]
        public void Shutdown_PenaltyIsCapped()
        {
            var long_ = new GovernmentCondition(100, 0);
            Assert.Equal(Cfg.ShutdownApprovalCap, long_.ApprovalPenalty(Cfg), 3);
            Assert.Equal(Cfg.ShutdownIncumbentCap, long_.IncumbentPenalty(Cfg), 3);
        }

        [Fact]
        public void Shutdown_CostsTheIncumbentVotes()
        {
            var table = TestCity.PoorAndRich();
            var normal = ElectionModel.RunElection(table, TestCity.TwoParties, "left", GoverningRule.PopularVote, TurnoutPreset.UsGeneral, Cfg, 5);
            var shut = ElectionModel.RunElection(table, TestCity.TwoParties, "left", GoverningRule.PopularVote, TurnoutPreset.UsGeneral, Cfg, 5, new GovernmentCondition(6, 0));

            Assert.True(shut.Shares[0] < normal.Shares[0]);
        }

        [Fact]
        public void LowEfficiency_IsASmallerPenalty()
        {
            var half = new GovernmentCondition(0, 0.5f);
            var shutYear = new GovernmentCondition(12, 0);
            Assert.True(half.ApprovalPenalty(Cfg) > 0);
            Assert.True(half.ApprovalPenalty(Cfg) < shutYear.ApprovalPenalty(Cfg));
        }

        [Fact]
        public void Conditions_DoNothingWhenNobodyGoverns()
        {
            var table = TestCity.PoorAndRich(happiness: 60f);
            var a = ElectionModel.RunPoll(table, TestCity.TwoParties, null, Cfg);
            var b = ElectionModel.RunPoll(table, TestCity.TwoParties, null, Cfg, new GovernmentCondition(6, 0.5f));
            Assert.Equal(a.Approval, b.Approval, 4);
        }

        [Fact]
        public void DefaultCondition_IsNormal()
        {
            Assert.True(default(GovernmentCondition).IsNormal);
            Assert.Equal(0f, default(GovernmentCondition).ApprovalPenalty(Cfg));
        }
    }
}
