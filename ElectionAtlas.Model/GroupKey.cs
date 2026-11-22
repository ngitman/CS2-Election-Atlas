namespace ElectionAtlas.Model
{
    /// <summary>
    /// Packs a voter group's identity into one int, so the in-game reader can add into flat arrays.
    /// Layout: (((district * 2 + age) * 5 + education) * 4 + wealth) * 6 + job.
    /// </summary>
    public static class GroupKey
    {
        public const int AgeBands = 2;
        public const int EducationLevels = VoterGroup.EducationLevels;
        public const int WealthBands = VoterGroup.WealthBands;
        public const int JobSectors = 6;

        /// <summary>Slots per district: 2 x 5 x 4 x 6 = 240.</summary>
        public const int SlotsPerDistrict = AgeBands * EducationLevels * WealthBands * JobSectors;

        public static int Encode(int district, AgeBand age, int education, int wealthBand, JobSector job)
        {
            return (((district * AgeBands + (int)age) * EducationLevels + education) * WealthBands + wealthBand) * JobSectors + (int)job;
        }

        public static void Decode(int key, out int district, out AgeBand age, out int education, out int wealthBand, out JobSector job)
        {
            job = (JobSector)(key % JobSectors);
            key /= JobSectors;
            wealthBand = key % WealthBands;
            key /= WealthBands;
            education = key % EducationLevels;
            key /= EducationLevels;
            age = (AgeBand)(key % AgeBands);
            district = key / AgeBands;
        }
    }
}
