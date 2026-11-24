using System;
using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// One place that reports its count on election night: a district today, a precinct later. Its votes belong to
    /// one region (a district of the snapshot); the region's seat is decided once all of its units have reported.
    /// </summary>
    public sealed class ReportingUnit
    {
        public ReportingUnit(long id, string name, int regionIndex, long eligible, long[] partyVotes)
        {
            PartyVotes = partyVotes ?? throw new ArgumentNullException(nameof(partyVotes));
            if (regionIndex < 0) throw new ArgumentOutOfRangeException(nameof(regionIndex));
            if (eligible < 0) throw new ArgumentOutOfRangeException(nameof(eligible));
            foreach (long v in partyVotes)
                if (v < 0) throw new ArgumentException("Vote counts can't be negative.", nameof(partyVotes));

            Id = id;
            Name = name;
            RegionIndex = regionIndex;
            Eligible = eligible;
            Tally = RegionTally.From(partyVotes, eligible);
        }

        /// <summary>Opaque id from the snapshot: today the district's <see cref="DistrictSnapshot.DistrictId"/>.</summary>
        public long Id { get; }

        public string Name { get; }

        /// <summary>Index into <see cref="ElectionSnapshot.Districts"/> of the district these votes count toward.</summary>
        public int RegionIndex { get; }

        public long Eligible { get; }

        /// <summary>Votes per party, in the snapshot's party order. Read-only by convention, like the snapshot's.</summary>
        public long[] PartyVotes { get; }

        /// <summary>This unit's own result (winner and margin among its votes).</summary>
        public RegionTally Tally { get; }

        public long Votes => Tally.Votes;
    }

    /// <summary>
    /// Election Night: a replay that reveals an archived election one reporting unit at a time, or in waves. A pure
    /// function of a frozen <see cref="ElectionSnapshot"/>: the same snapshot (also after a save and reload, which
    /// renumbers district ids) always gives the same frames.
    ///
    /// <para><b>Order.</b> Units start in the snapshot's frozen order (the order of
    /// <see cref="ElectionSnapshot.Districts"/>, which saving and loading keep), and a Fisher-Yates shuffle seeded
    /// from <see cref="ElectionSnapshot.Seed"/> with <see cref="SplitMix64"/> picks the reveal order. District ids are
    /// deliberately not the sort key: they are entity ids the mod remaps on every load, so an order sorted by them
    /// would change after a reload. Units with no voters (an empty district or outside bucket, later empty
    /// precincts) have nothing to count and are in from frame 0, so no frame waits on them.</para>
    ///
    /// <para><b>Frames.</b> Frame 0 is the close of polls: no votes counted, only the empty units in. Each later
    /// frame adds one wave of <see cref="WaveSize"/> units; the last frame is <see cref="Final"/>. Running citywide votes include the
    /// outside-any-district bucket; seats count a district once all its units are in, using its archived winner, and
    /// never count the outside bucket. The final frame's totals equal the snapshot's exactly and its leader is the
    /// archived <see cref="ElectionSnapshot.GoverningPartyIndex"/>. Only integers decide order, totals and leaders.</para>
    ///
    /// <para><b>Cost.</b> Built once in O(units x parties); every frame is precomputed into flat arrays
    /// (frames x parties longs and ints), so reading or jumping to any frame allocates nothing. Measured (.NET 8,
    /// Release, 8 parties, one unit per frame): 10 units 7 µs and 3 KB; 60 units 35 µs and 15 KB; 2,100 units
    /// (precinct scale) 0.33 ms and 326 KB; reading all 2,101 frames afterwards 13 µs.</para>
    /// </summary>
    public sealed class ElectionNight
    {
        /// <summary>Mixed into the seed so the reveal order is independent of the model's turnout draw from the same seed.</summary>
        private const ulong kOrderSalt = 0x45412D4E49474854UL; // "EA-NIGHT"

        private readonly ReportingUnit[] m_Units;
        private readonly int[] m_Order;         // reveal position -> unit index
        private readonly int[] m_Position;      // unit index -> reveal position
        private readonly int[] m_RegionDone;    // region -> units reported once the region is complete
        private readonly int[] m_FrameEnd;      // frame -> units reported
        private readonly long[] m_Votes;        // frame x party: running citywide votes
        private readonly int[] m_Seats;         // frame x party: districts decided so far
        private readonly int[] m_SeatsReported; // frame -> seat districts decided
        private readonly RegionTally[] m_City;  // frame -> citywide tally so far
        private readonly int[] m_Leader;        // frame -> provisional governing party

        private ElectionNight(ElectionSnapshot snapshot, ReportingUnit[] units, int waveSize)
        {
            Snapshot = snapshot;
            m_Units = units;
            WaveSize = waveSize;
            PartyCount = snapshot.Parties.Length;

            var districts = snapshot.Districts;
            int parties = PartyCount, regions = districts.Length, n = units.Length;
            foreach (var d in districts)
            {
                if (!d.IsSeat) continue;
                SeatCount++;
                if (IsEmpty(d.Eligible, d.Votes)) EmptySeats++;
            }

            m_Order = RevealOrder(units, snapshot.Seed, out int empty);
            EmptyUnits = empty;
            m_Position = new int[n];
            for (int pos = 0; pos < n; pos++) m_Position[m_Order[pos]] = pos;

            var remaining = new int[regions];
            m_RegionDone = new int[regions];
            for (int u = 0; u < n; u++)
            {
                int r = units[u].RegionIndex;
                remaining[r]++;
                if (m_Position[u] + 1 > m_RegionDone[r]) m_RegionDone[r] = m_Position[u] + 1;
            }

            int frames = 1 + (n - empty + waveSize - 1) / waveSize;
            m_FrameEnd = new int[frames];
            m_Votes = new long[frames * parties];
            m_Seats = new int[frames * parties];
            m_SeatsReported = new int[frames];
            m_City = new RegionTally[frames];
            m_Leader = new int[frames];

            var votes = new long[parties];
            var seats = new int[parties];
            long eligible = 0;
            int seatsReported = 0, pos0 = 0;
            for (int f = 0; f < frames; f++)
            {
                int end = f == 0 ? empty : Math.Min(n, pos0 + waveSize);
                for (int pos = pos0; pos < end; pos++)
                {
                    var unit = units[m_Order[pos]];
                    for (int p = 0; p < parties; p++) votes[p] += unit.PartyVotes[p];
                    eligible += unit.Eligible;

                    int r = unit.RegionIndex;
                    if (--remaining[r] > 0 || !districts[r].IsSeat) continue;
                    seatsReported++;
                    if (districts[r].WinnerIndex >= 0) seats[districts[r].WinnerIndex]++;
                }

                pos0 = end;
                m_FrameEnd[f] = end;
                m_SeatsReported[f] = seatsReported;
                Array.Copy(votes, 0, m_Votes, f * parties, parties);
                Array.Copy(seats, 0, m_Seats, f * parties, parties);
                m_City[f] = RegionTally.From(votes, eligible);
                m_Leader[f] = ElectionModel.PickGoverningParty(snapshot.Rule, votes, seats);
            }

            // The archive decides who governs; on a snapshot made by the model this agrees with the full count.
            m_Leader[frames - 1] = snapshot.GoverningPartyIndex;

            for (int f = 1; f < frames; f++)
                if (LeadChangedAt(f)) LeadChanges++;
        }

        /// <summary>Districts report as one unit each, the outside bucket included.</summary>
        public static ElectionNight ByDistrict(ElectionSnapshot snapshot, int waveSize = 1)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var districts = snapshot.Districts;
            var units = new ReportingUnit[districts.Length];
            for (int i = 0; i < units.Length; i++)
            {
                var d = districts[i];
                units[i] = new ReportingUnit(d.DistrictId, d.Name, i, d.Eligible, d.PartyVotes);
            }

            return Create(snapshot, units, waveSize);
        }

        /// <summary>
        /// Any reporting units (precincts later). Every district of the snapshot needs at least one unit, and each
        /// district's units must add up to its archived eligible voters and party votes exactly, so the final frame
        /// equals the archive.
        /// </summary>
        public static ElectionNight FromUnits(ElectionSnapshot snapshot, IReadOnlyList<ReportingUnit> units, int waveSize = 1)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (units == null) throw new ArgumentNullException(nameof(units));
            var copy = new ReportingUnit[units.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = units[i] ?? throw new ArgumentException("Units can't be null.", nameof(units));
            return Create(snapshot, copy, waveSize);
        }

        private static ElectionNight Create(ElectionSnapshot snapshot, ReportingUnit[] units, int waveSize)
        {
            if (waveSize < 1) throw new ArgumentOutOfRangeException(nameof(waveSize), "A wave reports at least one unit.");
            CheckUnitsAddUp(snapshot, units);
            return new ElectionNight(snapshot, units, waveSize);
        }

        public ElectionSnapshot Snapshot { get; }

        /// <summary>Units reported per frame (the last wave may be smaller).</summary>
        public int WaveSize { get; }

        public int PartyCount { get; }

        /// <summary>The units in the snapshot's frozen order; <see cref="RevealedUnit"/> gives the reveal order.</summary>
        public IReadOnlyList<ReportingUnit> Units => m_Units;

        public int UnitCount => m_Units.Length;

        /// <summary>Districts that are seats: every district but the outside bucket.</summary>
        public int SeatCount { get; }

        /// <summary>Seats with no voters. They are complete at frame 0, so "districts reported" counts leave them out.</summary>
        public int EmptySeats { get; }

        /// <summary>Frames including frame 0, the close of polls.</summary>
        public int FrameCount => m_FrameEnd.Length;

        /// <summary>Frames whose provisional leader differs from the frame before's (a first leader is not a change).</summary>
        public int LeadChanges { get; }

        public ReportFrame this[int frame] => Frame(frame);

        public ReportFrame Frame(int index)
        {
            if ((uint)index >= (uint)m_FrameEnd.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return new ReportFrame(this, index);
        }

        public ReportFrame First => new ReportFrame(this, 0);

        public ReportFrame Final => new ReportFrame(this, m_FrameEnd.Length - 1);

        /// <summary>The unit revealed at <paramref name="position"/> (0 = first to report).</summary>
        public int RevealedUnit(int position) => m_Order[position];

        /// <summary>Where <paramref name="unit"/> falls in the reveal order.</summary>
        public int RevealPosition(int unit) => m_Position[unit];

        /// <summary>Units with no voters. They are in from frame 0 (nothing to count), so no frame waits on them.</summary>
        public int EmptyUnits { get; }

        /// <summary>The frame in which <paramref name="unit"/> reports.</summary>
        public int UnitReportFrame(int unit) => FrameOfReported(m_Position[unit] + 1);

        /// <summary>The frame in which district <paramref name="region"/> is complete (its last unit reports): when
        /// the map recolors it and its seat, if it is one, is decided.</summary>
        public int RegionReportFrame(int region) => FrameOfReported(m_RegionDone[region]);

        /// <summary>The first frame with at least <paramref name="reported"/> units in.</summary>
        private int FrameOfReported(int reported) =>
            reported <= EmptyUnits ? 0 : (reported - EmptyUnits + WaveSize - 1) / WaveSize;

        internal int FrameEnd(int frame) => m_FrameEnd[frame];
        internal int RegionDone(int region) => m_RegionDone[region];
        internal long Votes(int frame, int party) => m_Votes[frame * PartyCount + party];
        internal int Seats(int frame, int party) => m_Seats[frame * PartyCount + party];
        internal int SeatsReported(int frame) => m_SeatsReported[frame];
        internal RegionTally City(int frame) => m_City[frame];
        internal int Leader(int frame) => m_Leader[frame];

        internal bool LeadChangedAt(int frame) =>
            frame > 0 && m_Leader[frame] >= 0 && m_Leader[frame - 1] >= 0 && m_Leader[frame] != m_Leader[frame - 1];

        internal void CopyRow(int frame, long[] votes, int[] seats)
        {
            if (votes != null) Array.Copy(m_Votes, frame * PartyCount, votes, 0, PartyCount);
            if (seats != null) Array.Copy(m_Seats, frame * PartyCount, seats, 0, PartyCount);
        }

        /// <summary>
        /// Reveal position -> unit index: the units with no voters first, in frozen order, then the rest in frozen
        /// order shuffled by the seed. Integer-only, so it gives the same order on every runtime.
        /// </summary>
        private static int[] RevealOrder(ReportingUnit[] units, ulong seed, out int empty)
        {
            var order = new int[units.Length];
            empty = 0;
            for (int u = 0; u < units.Length; u++)
                if (IsEmpty(units[u])) order[empty++] = u;
            int next = empty;
            for (int u = 0; u < units.Length; u++)
                if (!IsEmpty(units[u])) order[next++] = u;

            var rng = new SplitMix64(seed ^ kOrderSalt);
            for (int i = units.Length - 1; i > empty; i--)
            {
                int j = empty + Below(ref rng, (ulong)(i - empty) + 1);
                int t = order[i];
                order[i] = order[j];
                order[j] = t;
            }

            return order;
        }

        private static bool IsEmpty(ReportingUnit unit) => IsEmpty(unit.Eligible, unit.Votes);

        /// <summary>No voters and no votes: nothing to count, so it reports at the close of polls and isn't shown as a return.</summary>
        private static bool IsEmpty(long eligible, long votes) => eligible == 0 && votes == 0;

        /// <summary>Districts of <paramref name="snapshot"/> with something to count, to size the reveal's waves.</summary>
        public static int DistrictsWithVoters(ElectionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            int n = 0;
            foreach (var d in snapshot.Districts)
                if (!IsEmpty(d.Eligible, d.Votes)) n++;
            return n;
        }

        /// <summary>Whether district <paramref name="region"/> has something to count (empty ones are left out of returns).</summary>
        public bool HasVoters(int region)
        {
            var d = Snapshot.Districts[region];
            return !IsEmpty(d.Eligible, d.Votes);
        }

        /// <summary>Uniform in [0, bound), without modulo bias.</summary>
        private static int Below(ref SplitMix64 rng, ulong bound)
        {
            ulong threshold = unchecked(0UL - bound) % bound; // 2^64 mod bound: draws below it would favor low values
            ulong r;
            do r = rng.Next(); while (r < threshold);
            return (int)(r % bound);
        }

        private static void CheckUnitsAddUp(ElectionSnapshot snapshot, ReportingUnit[] units)
        {
            int parties = snapshot.Parties.Length, regions = snapshot.Districts.Length;
            var votes = new long[regions * parties];
            var eligible = new long[regions];
            var count = new int[regions];
            foreach (var unit in units)
            {
                if (unit.RegionIndex >= regions)
                    throw new ArgumentException($"Unit {unit.Id} reports for district {unit.RegionIndex}, but the election has {regions}.", nameof(units));
                if (unit.PartyVotes.Length != parties)
                    throw new ArgumentException($"Unit {unit.Id} needs one vote count per party.", nameof(units));
                int r = unit.RegionIndex;
                count[r]++;
                eligible[r] += unit.Eligible;
                for (int p = 0; p < parties; p++) votes[r * parties + p] += unit.PartyVotes[p];
            }

            for (int r = 0; r < regions; r++)
            {
                var d = snapshot.Districts[r];
                bool same = count[r] > 0 && eligible[r] == d.Eligible;
                for (int p = 0; same && p < parties; p++) same = votes[r * parties + p] == d.PartyVotes[p];
                if (!same)
                    throw new ArgumentException($"The units of district '{d.Name}' don't add up to its archived count.", nameof(units));
            }
        }
    }

    /// <summary>
    /// The count as it stands after one frame of an <see cref="ElectionNight"/>. A view over the replay's
    /// precomputed arrays: cheap to copy, allocates nothing.
    /// </summary>
    public readonly struct ReportFrame
    {
        private readonly ElectionNight m_Night;

        internal ReportFrame(ElectionNight night, int index)
        {
            m_Night = night;
            Index = index;
        }

        public int Index { get; }

        public bool IsFinal => Index == m_Night.FrameCount - 1;

        /// <summary>Units reported so far: the first <see cref="UnitsReported"/> of the reveal order.</summary>
        public int UnitsReported => m_Night.FrameEnd(Index);

        public int UnitCount => m_Night.UnitCount;

        /// <summary>The first reveal position of this frame's wave; the wave runs up to <see cref="UnitsReported"/>.</summary>
        public int WaveStart => Index == 0 ? 0 : m_Night.FrameEnd(Index - 1);

        /// <summary>Units that reported in this frame (in frame 0, the units with no voters).</summary>
        public int WaveCount => UnitsReported - WaveStart;

        /// <summary>The k-th unit of this frame's wave, as an index into <see cref="ElectionNight.Units"/>.</summary>
        public int WaveUnit(int k)
        {
            if ((uint)k >= (uint)WaveCount) throw new ArgumentOutOfRangeException(nameof(k));
            return m_Night.RevealedUnit(WaveStart + k);
        }

        /// <summary>Seat districts whose result is in. The outside bucket is never one.</summary>
        public int SeatsReported => m_Night.SeatsReported(Index);

        public int SeatCount => m_Night.SeatCount;

        /// <summary>Seats with voters whose result is in, out of <see cref="ContestedSeatCount"/>.</summary>
        public int ContestedSeatsReported => SeatsReported - m_Night.EmptySeats;

        /// <summary>Seats with voters: the ones a "districts reported" count is out of.</summary>
        public int ContestedSeatCount => m_Night.SeatCount - m_Night.EmptySeats;

        /// <summary>The citywide count so far, outside bucket included: votes, eligible voters counted, popular leader and lead.</summary>
        public RegionTally City => m_Night.City(Index);

        public long Votes => City.Votes;

        /// <summary>Running citywide votes for <paramref name="party"/>, outside bucket included.</summary>
        public long PartyVotes(int party) => m_Night.Votes(Index, Check(party));

        /// <summary>Districts <paramref name="party"/> has won among those reported (provisional seats).</summary>
        public int Seats(int party) => m_Night.Seats(Index, Check(party));

        /// <summary>
        /// The provisional governing party under the snapshot's rule, from the votes and seats counted so far
        /// (<see cref="ElectionModel.PickGoverningParty"/>); on the final frame, the archived governing party.
        /// -1 while nobody has a vote.
        /// </summary>
        public int LeaderIndex => m_Night.Leader(Index);

        /// <summary>The provisional leader changed hands in this frame.</summary>
        public bool LeadChanged => m_Night.LeadChangedAt(Index);

        public bool IsUnitReported(int unit) => m_Night.RevealPosition(unit) < UnitsReported;

        /// <summary>Every unit of district <paramref name="region"/> is in: the map shows its result instead of grey.</summary>
        public bool IsRegionReported(int region) => m_Night.RegionDone(region) <= UnitsReported;

        /// <summary>Copies the running votes (and seats) per party into arrays of at least <see cref="ElectionNight.PartyCount"/>.</summary>
        public void CopyTo(long[] partyVotes, int[] seats = null)
        {
            if (partyVotes != null && partyVotes.Length < m_Night.PartyCount) throw new ArgumentException("Too short.", nameof(partyVotes));
            if (seats != null && seats.Length < m_Night.PartyCount) throw new ArgumentException("Too short.", nameof(seats));
            m_Night.CopyRow(Index, partyVotes, seats);
        }

        private int Check(int party)
        {
            if ((uint)party >= (uint)m_Night.PartyCount) throw new ArgumentOutOfRangeException(nameof(party));
            return party;
        }
    }
}
