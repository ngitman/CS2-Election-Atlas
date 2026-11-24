using System;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class ElectionNightPlaybackTests
    {
        /// <summary>The close race with the polls already closed, so a test starts at the first count frame.</summary>
        private static ElectionNightPlayback CloseRace(double secondsPerFrame = 1.0)
        {
            var play = new ElectionNightPlayback(ElectionNight.ByDistrict(SampleNights.CloseRace()), secondsPerFrame);
            play.SkipToCount();
            return play;
        }

        /// <summary>One contested seat, one seat nobody lives in, and residents outside any district.</summary>
        private static ElectionSnapshot WithEmptySeat() =>
            SampleNights.Build(1, 2031, 4, GoverningRule.PopularVote, 7, 0.6f, SampleParties.Two(), new[]
            {
                (1L, "Harbor", 100L, new[] { 60L, 30L }),
                (2L, "Docks", 0L, new[] { 0L, 0L }),
                (DistrictInfo.OutsideId, DistrictInfo.OutsideName, 50L, new[] { 20L, 10L }),
            });

        [Fact]
        public void StartsWhenThePollsOpen_Playing()
        {
            var play = new ElectionNightPlayback(ElectionNight.ByDistrict(SampleNights.CloseRace()));

            Assert.True(play.IsVoting);
            Assert.True(play.IsPlaying);
            Assert.Equal(ElectionDay.OpeningHour, play.Hour);
            Assert.Equal(-1, play.MapFrame);
        }

        [Fact]
        public void IsRegionShown_HidesEveryRegionWhileVoting_ThenFollowsTheReports()
        {
            var snapshot = SampleNights.CloseRace();
            var play = new ElectionNightPlayback(ElectionNight.ByDistrict(snapshot));
            for (int r = 0; r < snapshot.Districts.Length; r++) Assert.False(play.IsRegionShown(r));

            play.SkipToCount();
            Assert.Equal(0, play.MapFrame);
            for (int frame = 0; ; frame++)
            {
                for (int r = 0; r < snapshot.Districts.Length; r++)
                    Assert.Equal(frame >= play.Night.RegionReportFrame(r), play.IsRegionShown(r));
                if (!play.Step()) break;
                Assert.Equal(frame + 1, play.MapFrame);
            }

            Assert.True(play.IsFinished);
            for (int r = 0; r < snapshot.Districts.Length; r++) Assert.True(play.IsRegionShown(r));
        }

        [Fact]
        public void EmptySeats_AreLeftOutOfTheDistrictsReportedCount()
        {
            var snapshot = WithEmptySeat();
            var night = ElectionNight.ByDistrict(snapshot);

            Assert.Equal(2, night.SeatCount);
            Assert.Equal(1, night.EmptySeats);
            Assert.Equal(2, ElectionNight.DistrictsWithVoters(snapshot));
            Assert.False(night.HasVoters(1));
            Assert.Equal(0, night.First.ContestedSeatsReported);
            Assert.Equal(1, night.First.ContestedSeatCount);
            Assert.Equal(1, night.Final.ContestedSeatsReported);
        }

        [Fact]
        public void StartsAtThePollsClose_Playing()
        {
            var play = CloseRace();

            Assert.Equal(0, play.FrameIndex);
            Assert.True(play.IsPlaying);
            Assert.False(play.IsFinished);
            Assert.Equal(0, play.Frame.Votes);
        }

        [Fact]
        public void Advance_ReportsOnePerInterval()
        {
            var play = CloseRace(1.0);

            Assert.False(play.Advance(0.4));
            Assert.False(play.Advance(0.4));
            Assert.True(play.Advance(0.4));   // 1.2 s: first report, 0.2 s carried
            Assert.Equal(1, play.FrameIndex);
            Assert.False(play.Advance(0.7));  // 0.9 s
            Assert.True(play.Advance(0.1));   // 1.0 s
            Assert.Equal(2, play.FrameIndex);
        }

        [Fact]
        public void Advance_AHitchGivesOneFrame_NotABacklog()
        {
            var play = CloseRace(1.0);

            Assert.True(play.Advance(10.0));
            Assert.Equal(1, play.FrameIndex);
            Assert.False(play.Advance(0.5));  // the next report still waits its full second
            Assert.True(play.Advance(0.5));
            Assert.Equal(2, play.FrameIndex);
        }

        [Fact]
        public void PlaysToTheArchivedResult_ThenStops()
        {
            var play = CloseRace(0.5);
            int frames = 0;
            for (int tick = 0; tick < 1000 && play.IsPlaying; tick++)
                if (play.Advance(1.0 / 60)) frames++;

            Assert.True(play.IsFinished);
            Assert.False(play.IsPlaying);
            Assert.Equal(play.Night.FrameCount - 1, frames);
            Assert.Equal(play.Night.Snapshot.GoverningPartyIndex, play.Frame.LeaderIndex);
            Assert.False(play.Advance(5.0));
            Assert.False(play.Step());
        }

        [Fact]
        public void Pause_HoldsTheFrame_StepStillWorks()
        {
            var play = CloseRace(1.0);
            play.Pause();

            Assert.False(play.Advance(5.0));
            Assert.Equal(0, play.FrameIndex);
            Assert.True(play.Step());
            Assert.Equal(1, play.FrameIndex);
            Assert.False(play.IsPlaying);

            play.Play();
            Assert.True(play.Advance(1.0));
            Assert.Equal(2, play.FrameIndex);
        }

        [Fact]
        public void Step_RestartsTheWait()
        {
            var play = CloseRace(1.0);

            Assert.False(play.Advance(0.9));
            Assert.True(play.Step());
            Assert.False(play.Advance(0.5)); // not 0.9 + 0.5
            Assert.Equal(1, play.FrameIndex);
        }

        [Fact]
        public void ElectionDay_OpensAtSevenAndRunsTheCountAfterClosing()
        {
            var snapshot = SampleNights.OutsideResidents();
            var play = new ElectionNightPlayback(ElectionNight.ByDistrict(snapshot), 1);

            for (int hour = ElectionDay.OpeningHour; hour < ElectionDay.ClosingHour; hour++)
            {
                Assert.True(play.IsVoting);
                Assert.Equal(hour, play.Hour);
                Assert.Equal(0, play.FrameIndex);
                Assert.True(play.Advance(1));
            }

            Assert.False(play.IsVoting);
            Assert.Equal(ElectionDay.ClosingHour, play.Hour);
            Assert.Equal(snapshot.Votes, play.Day.BallotsCastAt(play.Hour));
            Assert.Equal(0, play.Frame.Votes);
            Assert.True(play.Advance(1));
            Assert.Equal(1, play.FrameIndex);
        }

        [Fact]
        public void ElectionDay_SkipsWorkFromEitherPhase()
        {
            var snapshot = SampleNights.CloseRace(GoverningRule.MostDistricts);
            var play = new ElectionNightPlayback(ElectionNight.ByDistrict(snapshot));
            play.Pause();
            Assert.True(play.SkipToCount());
            Assert.False(play.IsVoting);
            Assert.False(play.IsPlaying);
            Assert.Equal(0, play.FrameIndex);
            Assert.False(play.SkipToCount());

            play = new ElectionNightPlayback(ElectionNight.ByDistrict(snapshot));
            Assert.True(play.IsVoting);
            Assert.True(play.SkipToFinal());
            Assert.True(play.IsFinished);
            Assert.Equal(snapshot.Votes, play.Frame.Votes);
            Assert.Equal(snapshot.GoverningPartyIndex, play.Frame.LeaderIndex);
        }

        [Fact]
        public void CityClock_OpensAtSevenAndReportsUntilMidnight()
        {
            var snapshot = SampleNights.CloseRace();
            var play = new ElectionNightPlayback(ElectionNight.ByDistrict(snapshot));

            Assert.True(play.FollowGameClock(5.75));
            Assert.True(play.BeforePolls);
            Assert.Equal(5, play.Hour);
            Assert.Equal(0, play.Day.BallotsCastAt(play.Hour));
            Assert.False(play.Advance(100));
            Assert.False(play.FollowGameClock(5.95));

            Assert.True(play.FollowGameClock(7));
            Assert.False(play.BeforePolls);
            Assert.True(play.IsVoting);
            Assert.True(play.FollowGameClock(13.25));
            Assert.Equal(13, play.Hour);
            Assert.True(play.Day.BallotsCastAt(play.Hour) > 0);

            Assert.True(play.FollowGameClock(ElectionDay.ClosingHour));
            Assert.False(play.IsVoting);
            Assert.Equal(0, play.FrameIndex);
            // The count is spread from the close of polls to midnight: 22:00 is 3 of those 5 hours.
            Assert.True(play.FollowGameClock(22));
            Assert.Equal(3 * (play.Night.FrameCount - 1) / 5, play.FrameIndex);
            Assert.False(play.FollowGameClock(21));
            Assert.True(play.FollowGameClock(24));
            Assert.True(play.IsFinished);
            Assert.Equal(snapshot.Votes, play.Frame.Votes);
        }

        [Fact]
        public void CityClock_ViewPauseHoldsAndSkipRestoresTimedPlayback()
        {
            var snapshot = SampleNights.CloseRace();
            var play = new ElectionNightPlayback(ElectionNight.ByDistrict(snapshot));
            Assert.True(play.FollowGameClock(8));

            play.Pause();
            Assert.False(play.FollowGameClock(12));
            Assert.Equal(8, play.Hour);
            play.Play();
            Assert.True(play.FollowGameClock(12));
            Assert.Equal(12, play.Hour);

            Assert.True(play.SkipToCount());
            Assert.False(play.FollowsGameClock);
            Assert.False(play.Advance(2.9));
            Assert.True(play.Advance(0.2));
            Assert.Equal(1, play.FrameIndex);
        }

        [Fact]
        public void SkipToFinal_ShowsTheArchive()
        {
            var play = CloseRace();

            Assert.True(play.SkipToFinal());
            Assert.True(play.IsFinished);
            Assert.False(play.IsPlaying);
            Assert.Equal(play.Night.Snapshot.Votes, play.Frame.Votes);
            Assert.False(play.SkipToFinal());
        }

        [Fact]
        public void AnElectionWithNothingToReveal_EndsWhenThePollsClose()
        {
            var empty = SampleNights.Build(1, 2031, 4, GoverningRule.PopularVote, 1, 0.6f, SampleParties.Two(),
                Array.Empty<(long, string, long, long[])>());
            var play = new ElectionNightPlayback(ElectionNight.ByDistrict(empty));

            Assert.True(play.IsVoting);
            Assert.True(play.SkipToCount());
            Assert.True(play.IsFinished);
            Assert.False(play.IsPlaying);
            Assert.False(play.Advance(1.0));
        }

        [Fact]
        public void Pace_IsClamped()
        {
            Assert.Equal(ElectionNightPlayback.MinSecondsPerFrame, CloseRace(0).SecondsPerFrame);
            Assert.Equal(ElectionNightPlayback.MaxSecondsPerFrame, CloseRace(1e9).SecondsPerFrame);
            Assert.Equal(ElectionNightPlayback.DefaultSecondsPerFrame, CloseRace(double.NaN).SecondsPerFrame);
            Assert.False(CloseRace().Advance(double.NaN));
            Assert.False(CloseRace().Advance(-1));
        }

        [Fact]
        public void WaveSizeFor_KeepsBigNightsShort()
        {
            Assert.Equal(1, ElectionNightPlayback.WaveSizeFor(0));
            Assert.Equal(1, ElectionNightPlayback.WaveSizeFor(11));
            Assert.Equal(1, ElectionNightPlayback.WaveSizeFor(20));
            Assert.Equal(2, ElectionNightPlayback.WaveSizeFor(21));
            Assert.Equal(3, ElectionNightPlayback.WaveSizeFor(60));
            Assert.Equal(105, ElectionNightPlayback.WaveSizeFor(2100));
            Assert.Throws<ArgumentOutOfRangeException>(() => ElectionNightPlayback.WaveSizeFor(10, 0));

            var big = SampleNights.CloseRace();
            int wave = ElectionNightPlayback.WaveSizeFor(big.Districts.Length, maxFrames: 4);
            Assert.True(ElectionNight.ByDistrict(big, wave).FrameCount - 1 <= 4);
        }
    }
}
