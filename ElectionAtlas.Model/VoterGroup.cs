using System;

namespace ElectionAtlas.Model
{
    /// <summary>All the voters in one district who share the same age band, education, wealth band and job.</summary>
    public readonly struct VoterGroup
    {
        public const int EducationLevels = 5;
        public const int WealthBands = 4;

        public readonly int District;
        public readonly AgeBand Age;

        /// <summary>0 (none) to 4 (university), as the game reports it.</summary>
        public readonly int Education;

        /// <summary>0 (poorest) to 3 (richest).</summary>
        public readonly int WealthBand;

        public readonly JobSector Job;

        /// <summary>Number of voting-age residents in the group.</summary>
        public readonly int Count;

        /// <summary>Mean of the game's own citizen happiness, 0 to 100.</summary>
        public readonly float AvgHappiness;

        public VoterGroup(int district, AgeBand age, int education, int wealthBand, JobSector job, int count, float avgHappiness)
        {
            if (district < 0) throw new ArgumentOutOfRangeException(nameof(district));
            if (age != AgeBand.Adult && age != AgeBand.Elderly) throw new ArgumentOutOfRangeException(nameof(age));
            if (education < 0 || education >= EducationLevels) throw new ArgumentOutOfRangeException(nameof(education));
            if (wealthBand < 0 || wealthBand >= WealthBands) throw new ArgumentOutOfRangeException(nameof(wealthBand));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            District = district;
            Age = age;
            Education = education;
            WealthBand = wealthBand;
            Job = job;
            Count = count;
            AvgHappiness = avgHappiness < 0f ? 0f : avgHappiness > 100f ? 100f : avgHappiness;
        }
    }
}
