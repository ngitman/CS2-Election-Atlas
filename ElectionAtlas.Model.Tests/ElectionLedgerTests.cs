using ElectionAtlas.Model;
using Xunit;
using S = ElectionAtlas.Model.GovernmentState;

namespace ElectionAtlas.Model.Tests
{
    public class ElectionLedgerTests
    {
        private static int M(int year, int month) => ElectionSchedule.MonthIndex(year, month);

        private static ElectionSnapshot Snapshot(int number, string winner = "left")
        {
            var table = TestCity.PoorAndRich();
            var result = ElectionModel.RunElection(table, TestCity.TwoParties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, new ModelConfig(), 1);
            var snap = ElectionSnapshot.Capture(result, TestCity.TwoParties, new[] { new DistrictInfo(1, "A"), new DistrictInfo(2, "B") }, number, 2027, 0, new MapTint());
            return snap;
        }

        [Fact]
        public void Unlocking_SchedulesTheFoundingElectionOneCadenceLater()
        {
            var ledger = new ElectionLedger();
            ledger.OnGovernmentChanged(S.NoCityHall, S.AwaitingFounding, M(2026, 2), ElectionCadence.EveryYear);

            Assert.Equal(M(2026, 2), ledger.UnlockedMonth);
            Assert.Equal(M(2027, 2), ledger.NextElectionMonth);
        }

        [Fact]
        public void MonthTicks_RunPolls_AndTheElectionWhenDue()
        {
            var ledger = new ElectionLedger();
            ledger.OnGovernmentChanged(S.NoCityHall, S.AwaitingFounding, M(2026, 0), ElectionCadence.EveryYear);

            var first = ledger.OnMonth(M(2026, 1), false);
            Assert.True(first.IsNewMonth && first.RunPoll && !first.RunElection);

            var sameMonth = ledger.OnMonth(M(2026, 1), false);
            Assert.False(sameMonth.IsNewMonth);

            var due = ledger.OnMonth(M(2027, 0), false);
            Assert.True(due.RunPoll && due.RunElection);
        }

        [Fact]
        public void NoPollsOrElections_WithoutACityHall()
        {
            var ledger = new ElectionLedger();
            var tick = ledger.OnMonth(M(2026, 5), false);
            Assert.True(tick.IsNewMonth);
            Assert.False(tick.RunPoll || tick.RunElection);
        }

        [Fact]
        public void RecordingAnElection_SetsTheGovernment_AndTheNextDate()
        {
            var ledger = new ElectionLedger();
            ledger.OnGovernmentChanged(S.NoCityHall, S.AwaitingFounding, M(2026, 0), ElectionCadence.EveryTwoYears);
            ledger.ShutdownMonths = 4;

            ledger.RecordElection(Snapshot(1), M(2028, 0), ElectionCadence.EveryTwoYears);

            Assert.Equal(S.Active, ledger.State);
            Assert.Equal(M(2030, 0), ledger.NextElectionMonth);
            Assert.NotNull(ledger.GoverningPartyId);
            Assert.Equal(0f, ledger.ShutdownMonths);
            Assert.Equal(1, ledger.Archive.Count);
        }

        [Fact]
        public void ShutdownMonths_CountOnlyWhileGoverning()
        {
            var ledger = new ElectionLedger();
            ledger.OnMonth(M(2026, 0), true);
            Assert.Equal(0f, ledger.ShutdownMonths);

            ledger.OnGovernmentChanged(S.NoCityHall, S.AwaitingFounding, M(2026, 0), ElectionCadence.EveryYear);
            ledger.OnMonth(M(2026, 1), true);
            ledger.OnMonth(M(2026, 2), true);
            ledger.OnMonth(M(2026, 3), false);
            Assert.Equal(2f, ledger.ShutdownMonths);

            var condition = ledger.Condition(hasCityHall: true, shutDown: false, efficiency: 0.75f);
            Assert.Equal(2f, condition.ShutdownMonths);
            Assert.Equal(0.25f, condition.EfficiencyDeficit, 4);
        }

        [Fact]
        public void Demolishing_BeforeTheFoundingVote_ClearsTheSchedule()
        {
            var ledger = new ElectionLedger();
            ledger.OnGovernmentChanged(S.NoCityHall, S.AwaitingFounding, M(2026, 0), ElectionCadence.EveryYear);
            ledger.OnGovernmentChanged(S.AwaitingFounding, S.NoCityHall, M(2026, 4), ElectionCadence.EveryYear);

            Assert.Equal(ElectionSchedule.NotScheduled, ledger.NextElectionMonth);
            Assert.Equal(ElectionSchedule.NotScheduled, ledger.UnlockedMonth);
        }

        [Fact]
        public void ResumingAfterSuspension_SkipsMissedElections()
        {
            var ledger = new ElectionLedger { State = S.Suspended, NextElectionMonth = M(2027, 0) };
            ledger.OnGovernmentChanged(S.Suspended, S.Active, M(2029, 3), ElectionCadence.EveryYear);

            Assert.Equal(M(2030, 0), ledger.NextElectionMonth);
        }

        [Fact]
        public void EnsureScheduled_FillsAMissingSchedule()
        {
            var awaiting = new ElectionLedger { State = S.AwaitingFounding };
            awaiting.EnsureScheduled(M(2026, 6), ElectionCadence.EveryYear);
            Assert.Equal(M(2027, 6), awaiting.NextElectionMonth);

            var active = new ElectionLedger { State = S.Active };
            active.EnsureScheduled(M(2026, 6), ElectionCadence.EveryFourYears);
            Assert.Equal(M(2030, 6), active.NextElectionMonth);

            var none = new ElectionLedger();
            none.EnsureScheduled(M(2026, 6), ElectionCadence.EveryYear);
            Assert.Equal(ElectionSchedule.NotScheduled, none.NextElectionMonth);
        }
    }
}
