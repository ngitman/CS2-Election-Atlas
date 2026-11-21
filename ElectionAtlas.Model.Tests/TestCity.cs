using System;
using System.Collections.Generic;
using ElectionAtlas.Model;

namespace ElectionAtlas.Model.Tests
{
    /// <summary>Builds synthetic cities and party lineups for tests.</summary>
    internal static class TestCity
    {
        public static readonly Party Left = new Party("left", "Left", 0xCC3333, new Position(-0.6f, -0.4f));
        public static readonly Party Right = new Party("right", "Right", 0x3355CC, new Position(0.6f, 0.4f));
        public static readonly Party[] TwoParties = { Left, Right };

        /// <summary>
        /// Every slot of every district filled with a random count and happiness.
        /// 50 districts x 200 slots = 10,000 groups.
        /// </summary>
        public static GroupTable Random(int districts = 50, int seed = 1)
        {
            var rng = new Random(seed);
            var groups = new List<VoterGroup>();
            for (int d = 0; d < districts; d++)
            for (int a = 0; a < GroupKey.AgeBands; a++)
            for (int e = 0; e < GroupKey.EducationLevels; e++)
            for (int w = 0; w < GroupKey.WealthBands; w++)
            for (int j = 0; j < GroupKey.JobSectors; j++)
            {
                var age = (AgeBand)a;
                var job = (JobSector)j;
                if (age == AgeBand.Elderly && job != JobSector.Retired && rng.NextDouble() < 0.8) continue;
                groups.Add(new VoterGroup(d, age, e, w, job, rng.Next(1, 400), (float)(rng.NextDouble() * 100)));
            }

            return new GroupTable(districts, groups);
        }

        /// <summary>District 0 is poor, industrial and less educated; district 1 is rich, office and university-educated.</summary>
        public static GroupTable PoorAndRich(float happiness = 50f)
        {
            return new GroupTable(2, new[]
            {
                new VoterGroup(0, AgeBand.Adult, 1, 0, JobSector.Industrial, 1000, happiness),
                new VoterGroup(0, AgeBand.Adult, 0, 0, JobSector.Unemployed, 300, happiness),
                new VoterGroup(1, AgeBand.Adult, 4, 3, JobSector.Office, 1000, happiness),
                new VoterGroup(1, AgeBand.Elderly, 3, 3, JobSector.Retired, 300, happiness),
            });
        }

        public static VoterGroup Group(
            AgeBand age = AgeBand.Adult,
            int education = 2,
            int wealth = 1,
            JobSector job = JobSector.Commercial,
            int count = 100,
            float happiness = 50f,
            int district = 0)
        {
            return new VoterGroup(district, age, education, wealth, job, count, happiness);
        }
    }
}
