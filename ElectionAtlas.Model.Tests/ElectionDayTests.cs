using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public sealed class ElectionDayTests
    {
        [Fact]
        public void BallotsRiseByDistrictAndAddUpToTheFullCount()
        {
            foreach (var sample in SampleNights.All())
            {
                var snapshot = sample.Value;
                var day = new ElectionDay(snapshot);
                var night = ElectionNight.ByDistrict(snapshot);
                Assert.Equal(0L, day.BallotsCastAt(ElectionDay.OpeningHour - 1));
                Assert.Equal(0L, day.BallotsCastAt(ElectionDay.OpeningHour));

                for (int hour = ElectionDay.OpeningHour; hour <= ElectionDay.ClosingHour; hour++)
                {
                    long sum = 0;
                    for (int d = 0; d < snapshot.Districts.Length; d++)
                    {
                        long ballots = day.DistrictBallotsCastAt(d, hour);
                        Assert.InRange(ballots, day.DistrictBallotsCastAt(d, hour - 1), snapshot.Districts[d].Votes);
                        sum += ballots;
                    }
                    Assert.Equal(sum, day.BallotsCastAt(hour));
                }

                for (int d = 0; d < snapshot.Districts.Length; d++)
                    Assert.Equal(snapshot.Districts[d].Votes, day.DistrictBallotsCastAt(d, ElectionDay.ClosingHour));

                Assert.Equal(night.Final.Votes, day.BallotsCastAt(ElectionDay.ClosingHour));
                Assert.Equal(night.Final.Votes, day.BallotsCastAt(ElectionDay.ClosingHour + 1));
            }
        }

        [Fact]
        public void OutsideDistrictBallotsAreAlreadyIncludedDuringVoting()
        {
            var snapshot = SampleNights.OutsideResidents();
            var day = new ElectionDay(snapshot);
            int outside = System.Array.FindIndex(snapshot.Districts, d => !d.IsSeat);
            long outsideBallots = day.DistrictBallotsCastAt(outside, 12);
            long seatBallots = Enumerable.Range(0, snapshot.Districts.Length)
                .Where(d => d != outside).Sum(d => day.DistrictBallotsCastAt(d, 12));

            Assert.True(outsideBallots > 0);
            Assert.Equal(seatBallots + outsideBallots, day.BallotsCastAt(12));
        }

        [Fact]
        public void TurnoutCurveSurvivesDistrictIdRemapping()
        {
            var s = SampleNights.CloseRace();
            var remapped = s.Districts.Select(d =>
                new DistrictSnapshot(-d.DistrictId, d.Name, d.Eligible, d.PartyVotes, d.WinnerIndex, d.Margin, d.MapColor))
                .ToArray();
            var reloaded = new ElectionSnapshot(s.Number, s.Year, s.Month, s.Rule, s.Seed, s.TargetTurnout,
                s.GoverningPartyIndex, s.Parties, remapped);
            var before = new ElectionDay(s);
            var after = new ElectionDay(reloaded);

            for (int hour = ElectionDay.OpeningHour; hour <= ElectionDay.ClosingHour; hour++)
                for (int d = 0; d < remapped.Length; d++)
                    Assert.Equal(before.DistrictBallotsCastAt(d, hour), after.DistrictBallotsCastAt(d, hour));
        }

        [Fact]
        public void EmptyAndVeryLargeCountsStayWithinTheirTotals()
        {
            var snapshot = SampleNights.Build(1, 2031, 3, GoverningRule.PopularVote, 1UL, 0.5f,
                SampleParties.Two(), new[]
                {
                    SampleNights.District(1, "Empty", 0, 0, 0),
                    SampleNights.District(2, "Large", long.MaxValue, long.MaxValue, 0),
                });
            var day = new ElectionDay(snapshot);

            Assert.Equal(0L, day.DistrictBallotsCastAt(0, 12));
            Assert.InRange(day.BallotsCastAt(12), 1L, long.MaxValue - 1);
            Assert.Equal(long.MaxValue, day.BallotsCastAt(ElectionDay.ClosingHour));
        }
    }
}
