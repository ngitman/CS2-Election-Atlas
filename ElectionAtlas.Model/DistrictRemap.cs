using System;
using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// Re-points saved district ids at the districts of the city as it was loaded. The mod's ids come from district
    /// entities, and the game renumbers entities on every load, so saved ids mean nothing until they are mapped.
    ///
    /// <see cref="Map(long)"/> keeps the outside bucket's id, turns a known saved id into its current id, and gives
    /// every other saved id (a district that no longer exists, or one an old save can't identify) a
    /// <see cref="DistrictInfo.DissolvedId"/>: one per saved id, the same wherever that id appears, so a dissolved
    /// district still lines up across past elections and polls.
    /// </summary>
    public sealed class DistrictRemap
    {
        private readonly IReadOnlyDictionary<long, long> m_Known;
        private readonly Dictionary<long, long> m_Mapped = new Dictionary<long, long>();

        /// <param name="known">Saved id to current id, for the saved districts that still exist.</param>
        public DistrictRemap(IReadOnlyDictionary<long, long> known)
        {
            m_Known = known ?? throw new ArgumentNullException(nameof(known));
        }

        /// <summary>Distinct saved districts mapped to a current district so far.</summary>
        public int Matched { get; private set; }

        /// <summary>Distinct saved districts with no current district, given dissolved ids so far.</summary>
        public int Unmatched { get; private set; }

        public int Total => Matched + Unmatched;

        public long Map(long savedId)
        {
            if (savedId == DistrictInfo.OutsideId) return savedId;
            if (m_Mapped.TryGetValue(savedId, out long id)) return id;

            if (m_Known.TryGetValue(savedId, out id)) Matched++;
            else id = DistrictInfo.DissolvedId(Unmatched++);
            m_Mapped[savedId] = id;
            return id;
        }

        internal long[] Map(long[] savedIds)
        {
            if (savedIds == null) return null;
            var ids = new long[savedIds.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = Map(savedIds[i]);
            return ids;
        }

        /// <summary>
        /// For saves that hold only district ids and names: matches each saved district to the current district with
        /// the same name, but only where the name belongs to exactly one saved district and exactly one current
        /// district. Shared names, and a saved district whose names (renamed between elections) point at two current
        /// districts, stay unmatched. Polls carry no names, so a district only polled is matched through its
        /// elections or not at all.
        /// </summary>
        public static DistrictRemap ByUniqueName(ElectionLedger ledger, IEnumerable<DistrictInfo> current)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (current == null) throw new ArgumentNullException(nameof(current));

            var saved = new Dictionary<string, long>(StringComparer.Ordinal);
            var savedShared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var election in ledger.Archive.All)
                foreach (var d in election.Districts)
                    AddName(saved, savedShared, d.IsSeat, d.Name, d.DistrictId);

            var now = new Dictionary<string, long>(StringComparer.Ordinal);
            var nowShared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in current)
                AddName(now, nowShared, d.IsSeat, d.Name, d.Id);

            var known = new Dictionary<long, long>();
            var conflicted = new HashSet<long>();
            foreach (var pair in saved)
            {
                if (savedShared.Contains(pair.Key) || nowShared.Contains(pair.Key) || !now.TryGetValue(pair.Key, out long id))
                    continue;
                if (known.TryGetValue(pair.Value, out long other) && other != id) conflicted.Add(pair.Value);
                else known[pair.Value] = id;
            }

            foreach (long id in conflicted) known.Remove(id);
            return new DistrictRemap(known);
        }

        private static void AddName(Dictionary<string, long> byName, HashSet<string> shared, bool seat, string name, long id)
        {
            if (!seat || string.IsNullOrEmpty(name)) return;
            if (!byName.TryGetValue(name, out long other)) byName[name] = id;
            else if (other != id) shared.Add(name);
        }
    }
}
