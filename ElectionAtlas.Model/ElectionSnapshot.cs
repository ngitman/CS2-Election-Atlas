using System;
using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    /// <summary>A party as it stood on election day. Later edits to the party never change past results.</summary>
    public sealed class PartySnapshot
    {
        public PartySnapshot(string id, string name, uint color, float economic, float social)
        {
            Id = id;
            Name = name;
            Color = color;
            Economic = economic;
            Social = social;
        }

        public string Id { get; }
        public string Name { get; }
        public uint Color { get; }
        public float Economic { get; }
        public float Social { get; }
    }

    /// <summary>
    /// The identity of a district, supplied by the mod. <see cref="Id"/> is opaque to the model (the mod packs
    /// the district entity into it); <see cref="Name"/> is kept so dissolved districts still have a label.
    /// </summary>
    public readonly struct DistrictInfo
    {
        public DistrictInfo(long id, string name)
        {
            Id = id;
            Name = name;
        }

        public readonly long Id;
        public readonly string Name;

        /// <summary>Id of the bucket for residents whose home is outside every drawn district.
        /// Its votes count toward the city total, but it is not a seat anyone can win.</summary>
        public const long OutsideId = -1;
        public const string OutsideName = "Outside any district";

        public bool IsSeat => Id != OutsideId;

        public static DistrictInfo Outside => new DistrictInfo(OutsideId, OutsideName);

        /// <summary>Index of the outside bucket in <paramref name="districts"/>, or -1 if there is none.</summary>
        public static int IndexOfOutside(DistrictInfo[] districts)
        {
            if (districts == null) return -1;
            for (int i = 0; i < districts.Length; i++)
                if (!districts[i].IsSeat) return i;
            return -1;
        }
    }

    public sealed class DistrictSnapshot
    {
        public DistrictSnapshot(long districtId, string name, long eligible, long[] partyVotes, int winnerIndex, float margin, uint mapColor)
        {
            DistrictId = districtId;
            Name = name;
            Eligible = eligible;
            PartyVotes = partyVotes ?? throw new ArgumentNullException(nameof(partyVotes));
            WinnerIndex = winnerIndex;
            Margin = margin;
            MapColor = mapColor;

            long votes = 0;
            foreach (long v in partyVotes) votes += v;
            Votes = votes;
        }

        public long DistrictId { get; }
        public string Name { get; }
        public long Eligible { get; }
        public long Votes { get; }
        public long[] PartyVotes { get; }
        public int WinnerIndex { get; }
        public float Margin { get; }

        /// <summary>Precomputed map color (0xRRGGBB): the winner's color tinted by margin.</summary>
        public uint MapColor { get; }

        public float Turnout => Eligible > 0 ? (float)((double)Votes / Eligible) : 0f;

        /// <summary>False for the outside-any-district bucket: its votes count citywide, but nobody wins it.</summary>
        public bool IsSeat => DistrictId != DistrictInfo.OutsideId;
    }

    /// <summary>
    /// The frozen numbers of one past election. Captured once from an <see cref="ElectionResult"/> and saved;
    /// browsing past elections only reads these, never re-runs the model.
    /// </summary>
    public sealed class ElectionSnapshot
    {
        public ElectionSnapshot(
            int number,
            int year,
            int month,
            GoverningRule rule,
            ulong seed,
            float targetTurnout,
            int governingPartyIndex,
            PartySnapshot[] parties,
            DistrictSnapshot[] districts)
        {
            if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
            Parties = parties ?? throw new ArgumentNullException(nameof(parties));
            Districts = districts ?? throw new ArgumentNullException(nameof(districts));

            Number = number;
            Year = year;
            Month = month;
            Rule = rule;
            Seed = seed;
            TargetTurnout = targetTurnout;
            GoverningPartyIndex = governingPartyIndex;

            PartyVotes = new long[parties.Length];
            DistrictsWon = new int[parties.Length];
            foreach (var d in districts)
            {
                if (d.PartyVotes.Length != parties.Length)
                    throw new ArgumentException("Every district needs one vote count per party.", nameof(districts));
                for (int p = 0; p < parties.Length; p++) PartyVotes[p] += d.PartyVotes[p];
                if (d.WinnerIndex >= 0 && d.IsSeat) DistrictsWon[d.WinnerIndex]++;
                Votes += d.Votes;
                Eligible += d.Eligible;
            }
        }

        /// <summary>1 for the founding election, then counting up.</summary>
        public int Number { get; }
        public int Year { get; }

        /// <summary>0 to 11.</summary>
        public int Month { get; }

        public GoverningRule Rule { get; }
        public ulong Seed { get; }
        public float TargetTurnout { get; }
        public int GoverningPartyIndex { get; }
        public PartySnapshot[] Parties { get; }
        public DistrictSnapshot[] Districts { get; }

        // Totals below are sums of the stored district counts, worked out once when the snapshot is built.
        public long Eligible { get; }
        public long Votes { get; }
        public long[] PartyVotes { get; }
        public int[] DistrictsWon { get; }

        public float Turnout => Eligible > 0 ? (float)((double)Votes / Eligible) : 0f;

        public float Share(int partyIndex) => Votes > 0 ? (float)((double)PartyVotes[partyIndex] / Votes) : 0f;

        public PartySnapshot GoverningParty => GoverningPartyIndex >= 0 ? Parties[GoverningPartyIndex] : null;

        /// <summary>
        /// Freezes an election result. <paramref name="parties"/> must be the lineup the election ran with, and
        /// <paramref name="districts"/> gives each district index its id and current name.
        /// </summary>
        public static ElectionSnapshot Capture(
            ElectionResult result,
            IReadOnlyList<Party> parties,
            IReadOnlyList<DistrictInfo> districts,
            int number,
            int year,
            int month,
            MapTint tint)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (parties == null) throw new ArgumentNullException(nameof(parties));
            if (districts == null) throw new ArgumentNullException(nameof(districts));
            if (tint == null) throw new ArgumentNullException(nameof(tint));
            if (parties.Count != result.PartyIds.Length)
                throw new ArgumentException("The party lineup doesn't match the result.", nameof(parties));
            for (int p = 0; p < parties.Count; p++)
                if (!string.Equals(parties[p].Id, result.PartyIds[p], StringComparison.Ordinal))
                    throw new ArgumentException("The party lineup doesn't match the result.", nameof(parties));
            if (districts.Count != result.Districts.Count)
                throw new ArgumentException("Need one DistrictInfo per district in the result.", nameof(districts));

            var partySnaps = new PartySnapshot[parties.Count];
            for (int p = 0; p < parties.Count; p++)
            {
                var party = parties[p];
                partySnaps[p] = new PartySnapshot(party.Id, party.Name, party.Color, party.Position.Economic, party.Position.Social);
            }

            var districtSnaps = new DistrictSnapshot[result.Districts.Count];
            for (int i = 0; i < districtSnaps.Length; i++)
            {
                var d = result.Districts[i];
                uint color = tint.DistrictColor(d.WinnerIndex, d.WinnerIndex >= 0 ? parties[d.WinnerIndex].Color : 0u, d.Margin);
                districtSnaps[i] = new DistrictSnapshot(
                    districts[i].Id,
                    districts[i].Name,
                    d.Eligible,
                    (long[])d.PartyVotes.Clone(),
                    d.WinnerIndex,
                    d.Margin,
                    color);
            }

            return new ElectionSnapshot(
                number, year, month, result.Rule, result.Seed, result.TargetTurnout,
                result.GoverningPartyIndex, partySnaps, districtSnaps);
        }
    }

    /// <summary>Every past election, oldest first. Append-only.</summary>
    public sealed class ElectionArchive
    {
        private readonly List<ElectionSnapshot> m_Elections = new List<ElectionSnapshot>();

        public int Count => m_Elections.Count;

        public ElectionSnapshot this[int index] => m_Elections[index];

        public ElectionSnapshot Latest => m_Elections.Count > 0 ? m_Elections[m_Elections.Count - 1] : null;

        public IReadOnlyList<ElectionSnapshot> All => m_Elections;

        /// <summary>The number the next election gets.</summary>
        public int NextNumber => m_Elections.Count + 1;

        /// <summary>Adds the next election. Numbers must run 1, 2, 3... with no gaps, so the archive can't be corrupted by a double run.</summary>
        public void Add(ElectionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Number != NextNumber)
                throw new ArgumentException($"Expected election {NextNumber}, got {snapshot.Number}.", nameof(snapshot));
            m_Elections.Add(snapshot);
        }
    }
}
