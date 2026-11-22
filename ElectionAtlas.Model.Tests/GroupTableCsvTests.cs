using System;
using System.IO;
using System.Linq;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class GroupTableCsvTests
    {
        [Fact]
        public void RoundTrip_KeepsEveryGroupAndDistrictName()
        {
            var table = TestCity.Random(districts: 4, seed: 9);
            var names = new[] { "Downtown", "Harbor, East", "Old Town", "Hills" };

            var sw = new StringWriter();
            GroupTableCsv.Write(sw, table, names, "Test City, 2026-11-21");
            var back = GroupTableCsv.Read(new StringReader(sw.ToString()), out var names2, out var source);

            Assert.Equal(table.DistrictCount, back.DistrictCount);
            Assert.Equal(table.TotalVoters, back.TotalVoters);
            Assert.Equal(table.Groups.Count, back.Groups.Count);
            Assert.Equal(names, names2);
            Assert.Equal("Test City, 2026-11-21", source);

            for (int i = 0; i < table.Groups.Count; i++)
            {
                var a = table.Groups[i];
                var b = back.Groups[i];
                Assert.Equal((a.District, a.Age, a.Education, a.WealthBand, a.Job, a.Count), (b.District, b.Age, b.Education, b.WealthBand, b.Job, b.Count));
                Assert.InRange(Math.Abs(a.AvgHappiness - b.AvgHappiness), 0f, 0.001f); // written with 3 decimals
            }
        }

        [Fact]
        public void RoundTrip_GivesTheSameElection()
        {
            var table = TestCity.Random(districts: 6, seed: 4);
            var sw = new StringWriter();
            GroupTableCsv.Write(sw, table, null, null);
            var back = GroupTableCsv.Read(new StringReader(sw.ToString()), out _, out _);

            var cfg = new ModelConfig();
            var a = ElectionModel.RunElection(table, TestCity.TwoParties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, cfg, 1);
            var b = ElectionModel.RunElection(back, TestCity.TwoParties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, cfg, 1);

            // Happiness is written with 3 decimals, so allow a vote or two of rounding per district.
            Assert.All(a.Districts.Zip(b.Districts, (x, y) => Math.Abs(x.Votes - y.Votes)), diff => Assert.InRange(diff, 0, 2));
        }

        [Fact]
        public void Read_RejectsFilesWithoutTheHeader()
        {
            Assert.Throws<FormatException>(() => GroupTableCsv.Read(new StringReader("district,age\n0,Adult"), out _, out _));
        }

        [Fact]
        public void Read_ReportsTheBadLine()
        {
            string csv = GroupTableCsv.Magic + "\n0,Adult,2,1,Commercial,10,50\n0,Adult,9,1,Commercial,10,50\n";
            var ex = Assert.Throws<FormatException>(() => GroupTableCsv.Read(new StringReader(csv), out _, out _));
            Assert.Contains("Line 3", ex.Message);
        }
    }
}
