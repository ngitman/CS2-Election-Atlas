using System;
using System.IO;
using System.Linq;
using ElectionAtlas.Model;
using Xunit;
using Xunit.Abstractions;

namespace ElectionAtlas.Model.Tests
{
    /// <summary>
    /// Runs the model on real cities exported from the game (Developer > Export city CSV). Drop exported files
    /// into ElectionAtlas.Model.Tests/Cities/; with none there, these tests pass without doing anything.
    /// </summary>
    public class RealCityReplayTests
    {
        private readonly ITestOutputHelper m_Output;

        public RealCityReplayTests(ITestOutputHelper output)
        {
            m_Output = output;
        }

        private static string[] CityFiles()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Cities");
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.csv") : Array.Empty<string>();
        }

        [Fact]
        public void EveryExportedCity_RunsCleanly()
        {
            var cfg = new ModelConfig();
            foreach (string file in CityFiles())
            {
                GroupTable table;
                string[] names;
                using (var reader = File.OpenText(file))
                    table = GroupTableCsv.Read(reader, out names, out _);

                foreach (int partyCount in new[] { 2, 4 })
                {
                    var parties = SampleParties.Get(partyCount);
                    var target = cfg.GetPreset(TurnoutPreset.UsGeneral).Target;
                    var result = ElectionModel.RunElection(table, parties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, cfg, 1);
                    var poll = ElectionModel.RunPoll(table, parties, result.GoverningPartyId, cfg);

                    Assert.Equal(table.TotalVoters, result.Eligible);
                    Assert.InRange(result.Turnout, target - 0.035f, target + 0.035f);
                    Assert.Equal(1f, result.Shares.Sum(), 3);
                    Assert.InRange(poll.Approval, 0f, 100f);

                    m_Output.WriteLine($"{Path.GetFileName(file)} [{partyCount} parties]: {table.TotalVoters:N0} voters, " +
                        $"turnout {result.Turnout:P1}, {string.Join(" ", parties.Select((p, i) => $"{p.Name}={result.Shares[i]:P1}"))}, " +
                        $"governing {result.GoverningPartyId}, approval {poll.Approval:0.0}");
                }
            }
        }
    }
}
