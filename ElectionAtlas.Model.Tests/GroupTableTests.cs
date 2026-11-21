using System;
using ElectionAtlas.Model;
using Xunit;

namespace ElectionAtlas.Model.Tests
{
    public class GroupTableTests
    {
        [Fact]
        public void GroupKey_RoundTripsEverySlot()
        {
            const int districts = 3;
            var seen = new bool[districts * GroupKey.SlotsPerDistrict];

            for (int d = 0; d < districts; d++)
            for (int a = 0; a < GroupKey.AgeBands; a++)
            for (int e = 0; e < GroupKey.EducationLevels; e++)
            for (int w = 0; w < GroupKey.WealthBands; w++)
            for (int j = 0; j < GroupKey.JobSectors; j++)
            {
                int key = GroupKey.Encode(d, (AgeBand)a, e, w, (JobSector)j);
                Assert.InRange(key, 0, seen.Length - 1);
                Assert.False(seen[key], $"key {key} produced twice");
                seen[key] = true;

                GroupKey.Decode(key, out int d2, out AgeBand a2, out int e2, out int w2, out JobSector j2);
                Assert.Equal((d, a, e, w, j), (d2, (int)a2, e2, w2, (int)j2));
            }

            Assert.Equal(200, GroupKey.SlotsPerDistrict);
        }

        [Fact]
        public void FromSlots_KeepsOnlyFilledSlots_AndAveragesHappiness()
        {
            var counts = new int[2 * GroupKey.SlotsPerDistrict];
            var sums = new long[counts.Length];

            int a = GroupKey.Encode(0, AgeBand.Adult, 2, 1, JobSector.Office);
            int b = GroupKey.Encode(1, AgeBand.Elderly, 0, 3, JobSector.Retired);
            counts[a] = 4; sums[a] = 4 * 70 + 2;   // average 70.5
            counts[b] = 10; sums[b] = 10 * 40;

            var table = GroupTable.FromSlots(2, counts, sums);

            Assert.Equal(2, table.Groups.Count);
            Assert.Equal(14, table.TotalVoters);

            var ga = table.Groups[0];
            Assert.Equal((0, AgeBand.Adult, 2, 1, JobSector.Office, 4), (ga.District, ga.Age, ga.Education, ga.WealthBand, ga.Job, ga.Count));
            Assert.Equal(70.5f, ga.AvgHappiness, 3);

            var gb = table.Groups[1];
            Assert.Equal((1, AgeBand.Elderly, JobSector.Retired), (gb.District, gb.Age, gb.Job));
            Assert.Equal(40f, gb.AvgHappiness, 3);
        }

        [Fact]
        public void Constructor_RejectsGroupsOutsideTheDistrictRange()
        {
            Assert.Throws<ArgumentException>(() => new GroupTable(1, new[] { TestCity.Group(district: 1) }));
        }

        [Fact]
        public void VoterGroup_ClampsHappiness()
        {
            Assert.Equal(100f, TestCity.Group(happiness: 140f).AvgHappiness);
            Assert.Equal(0f, TestCity.Group(happiness: -5f).AvgHappiness);
        }
    }
}
