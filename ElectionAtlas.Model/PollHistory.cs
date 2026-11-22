using System;
using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    /// <summary>One month's poll, as stored in the history.</summary>
    public sealed class PollRecord
    {
        public PollRecord(int monthIndex, string governingPartyId, string[] partyIds, float approval, float[] intention,
            long[] districtIds, float[] districtApproval)
        {
            MonthIndex = monthIndex;
            GoverningPartyId = governingPartyId;
            PartyIds = partyIds ?? throw new ArgumentNullException(nameof(partyIds));
            Approval = approval;
            Intention = intention ?? throw new ArgumentNullException(nameof(intention));
            DistrictIds = districtIds;
            DistrictApproval = districtApproval;
        }

        public int MonthIndex { get; }
        public string GoverningPartyId { get; }
        public string[] PartyIds { get; }
        public float Approval { get; }
        public float[] Intention { get; }

        /// <summary>Per-district approval; dropped (null) once the record is older than the district window.</summary>
        public long[] DistrictIds { get; internal set; }
        public float[] DistrictApproval { get; internal set; }

        public static PollRecord From(PollResult poll, int monthIndex, string governingPartyId, IReadOnlyList<DistrictInfo> districts)
        {
            var ids = new long[poll.Districts.Count];
            var approval = new float[poll.Districts.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = i < districts.Count ? districts[i].Id : i;
                approval[i] = poll.Districts[i].Approval;
            }

            return new PollRecord(monthIndex, governingPartyId, (string[])poll.PartyIds.Clone(), poll.Approval,
                (float[])poll.Intention.Clone(), ids, approval);
        }
    }

    /// <summary>
    /// Monthly polls, oldest first. Keeps the last <see cref="Capacity"/> citywide records and the per-district
    /// approval of the last <see cref="DistrictWindow"/> records, so the saved size stays bounded.
    /// </summary>
    public sealed class PollHistory
    {
        private readonly List<PollRecord> m_Records = new List<PollRecord>();

        public PollHistory(int capacity = 120, int districtWindow = 24)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            DistrictWindow = Math.Max(0, Math.Min(districtWindow, capacity));
        }

        public int Capacity { get; }
        public int DistrictWindow { get; }
        public int Count => m_Records.Count;
        public IReadOnlyList<PollRecord> Records => m_Records;
        public PollRecord Latest => m_Records.Count > 0 ? m_Records[m_Records.Count - 1] : null;

        public void Add(PollRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (Latest != null && record.MonthIndex <= Latest.MonthIndex)
                throw new ArgumentException($"Polls must be added in month order ({ElectionSchedule.Format(record.MonthIndex)} after {ElectionSchedule.Format(Latest.MonthIndex)}).", nameof(record));

            m_Records.Add(record);
            if (m_Records.Count > Capacity)
                m_Records.RemoveAt(0);

            int dropBefore = m_Records.Count - DistrictWindow;
            for (int i = 0; i < dropBefore; i++)
            {
                m_Records[i].DistrictIds = null;
                m_Records[i].DistrictApproval = null;
            }
        }

        public void Clear() => m_Records.Clear();
    }
}
