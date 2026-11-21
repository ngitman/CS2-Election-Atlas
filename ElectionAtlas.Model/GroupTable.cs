using System;
using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    /// <summary>The non-empty voter groups of a whole city. Built once per read, then only read.</summary>
    public sealed class GroupTable
    {
        private readonly VoterGroup[] m_Groups;

        public GroupTable(int districtCount, IEnumerable<VoterGroup> groups)
        {
            if (districtCount < 1) throw new ArgumentOutOfRangeException(nameof(districtCount));
            if (groups == null) throw new ArgumentNullException(nameof(groups));

            var list = new List<VoterGroup>();
            long total = 0;
            foreach (var g in groups)
            {
                if (g.District >= districtCount)
                    throw new ArgumentException($"Group district {g.District} is outside 0..{districtCount - 1}.", nameof(groups));
                if (g.Count == 0)
                    continue;
                list.Add(g);
                total += g.Count;
            }

            DistrictCount = districtCount;
            m_Groups = list.ToArray();
            TotalVoters = total;
        }

        public int DistrictCount { get; }

        public IReadOnlyList<VoterGroup> Groups => m_Groups;

        /// <summary>Voting-age residents across all groups.</summary>
        public long TotalVoters { get; }

        /// <summary>
        /// Builds a table from the flat arrays the in-game reader fills, indexed by <see cref="GroupKey"/>.
        /// Both arrays must be districtCount x <see cref="GroupKey.SlotsPerDistrict"/> long.
        /// </summary>
        public static GroupTable FromSlots(int districtCount, int[] counts, long[] happinessSums)
        {
            if (counts == null) throw new ArgumentNullException(nameof(counts));
            if (happinessSums == null) throw new ArgumentNullException(nameof(happinessSums));

            int slots = districtCount * GroupKey.SlotsPerDistrict;
            if (counts.Length < slots || happinessSums.Length < slots)
                throw new ArgumentException($"Slot arrays must hold at least {slots} entries.");

            var groups = new List<VoterGroup>();
            for (int key = 0; key < slots; key++)
            {
                int count = counts[key];
                if (count <= 0)
                    continue;

                GroupKey.Decode(key, out int district, out AgeBand age, out int education, out int wealth, out JobSector job);
                groups.Add(new VoterGroup(district, age, education, wealth, job, count, (float)((double)happinessSums[key] / count)));
            }

            return new GroupTable(districtCount, groups);
        }
    }
}
