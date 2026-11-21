using ElectionAtlas.Model;
using Xunit;
using S = ElectionAtlas.Model.GovernmentState;

namespace ElectionAtlas.Model.Tests
{
    public class GovernmentGateTests
    {
        [Theory]
        // current,            hasCityHall, pause, expected
        [InlineData(S.NoCityHall,       false, true,  S.NoCityHall)]
        [InlineData(S.NoCityHall,       true,  true,  S.AwaitingFounding)]
        [InlineData(S.AwaitingFounding, true,  true,  S.AwaitingFounding)]
        [InlineData(S.AwaitingFounding, false, true,  S.NoCityHall)]       // demolished before the first vote
        [InlineData(S.AwaitingFounding, false, false, S.NoCityHall)]
        [InlineData(S.Active,           true,  true,  S.Active)]
        [InlineData(S.Active,           false, true,  S.Suspended)]
        [InlineData(S.Active,           false, false, S.Active)]           // pausing turned off
        [InlineData(S.Suspended,        false, true,  S.Suspended)]
        [InlineData(S.Suspended,        true,  true,  S.Active)]           // rebuilt
        [InlineData(S.Suspended,        false, false, S.Active)]           // pausing turned off later
        public void Next_FollowsTheStateChart(S current, bool hasCityHall, bool pause, S expected)
        {
            Assert.Equal(expected, GovernmentGate.Next(current, hasCityHall, pause));
        }

        [Fact]
        public void FoundingElection_OnlyMovesAwaitingToActive()
        {
            Assert.Equal(S.Active, GovernmentGate.AfterFoundingElection(S.AwaitingFounding));
            Assert.Equal(S.NoCityHall, GovernmentGate.AfterFoundingElection(S.NoCityHall));
            Assert.Equal(S.Suspended, GovernmentGate.AfterFoundingElection(S.Suspended));
        }

        [Fact]
        public void Polls_RunOnlyWhileACityHallStands()
        {
            Assert.False(GovernmentGate.PollsRun(S.NoCityHall));
            Assert.True(GovernmentGate.PollsRun(S.AwaitingFounding));
            Assert.True(GovernmentGate.PollsRun(S.Active));
            Assert.False(GovernmentGate.PollsRun(S.Suspended));
        }

        [Theory]
        [InlineData("CityHall01", true)]
        [InlineData("EE_Cityhall", true)]
        [InlineData("CentralBank01", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsCityHallPrefabName_MatchesTheSpike1Prefabs(string name, bool expected)
        {
            Assert.Equal(expected, GovernmentGate.IsCityHallPrefabName(name));
        }
    }
}
