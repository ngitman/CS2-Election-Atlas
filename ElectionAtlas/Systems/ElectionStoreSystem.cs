using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using ElectionAtlas.Model;
using Game;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// Saves the election ledger into the city's save file (phase 5), through the same system-serialization path
    /// 40 vanilla systems use. Registered only when "Save election data" is on, because it's not yet confirmed
    /// that a save written with it loads cleanly after the mod is removed (spike 3). Versioned: a newer format
    /// than this build knows is ignored rather than misread.
    /// </summary>
    public partial class ElectionStoreSystem : GameSystemBase, IDefaultSerializable, ISerializable
    {
        private ElectionSystem m_Elections;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Elections = World.GetOrCreateSystemManaged<ElectionSystem>();
            Mod.Log.Info("Election data saving is ON (experimental)");
        }

        protected override void OnUpdate()
        {
        }

        public void SetDefaults(Context context)
        {
            m_Elections.RestoreFromSave(null);
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var l = m_Elections.LedgerForSave();
            writer.Write(ElectionLedger.CurrentVersion);
            writer.Write((int)l.State);
            writer.Write(l.UnlockedMonth);
            writer.Write(l.NextElectionMonth);
            writer.Write(l.LastMonthSeen);
            WriteString(writer, l.GoverningPartyId);
            writer.Write(l.ShutdownMonths);

            writer.Write(l.Archive.Count);
            foreach (var s in l.Archive.All)
            {
                writer.Write(s.Number);
                writer.Write(s.Year);
                writer.Write(s.Month);
                writer.Write((int)s.Rule);
                WriteLong(writer, (long)s.Seed);
                writer.Write(s.TargetTurnout);
                writer.Write(s.GoverningPartyIndex);

                writer.Write(s.Parties.Length);
                foreach (var p in s.Parties)
                {
                    WriteString(writer, p.Id);
                    WriteString(writer, p.Name);
                    writer.Write(unchecked((int)p.Color));
                    writer.Write(p.Economic);
                    writer.Write(p.Social);
                }

                writer.Write(s.Districts.Length);
                foreach (var d in s.Districts)
                {
                    WriteLong(writer, d.DistrictId);
                    WriteString(writer, d.Name);
                    WriteLong(writer, d.Eligible);
                    writer.Write(d.WinnerIndex);
                    writer.Write(d.Margin);
                    writer.Write(unchecked((int)d.MapColor));
                    writer.Write(d.PartyVotes.Length);
                    foreach (long v in d.PartyVotes) WriteLong(writer, v);
                }
            }

            writer.Write(l.Polls.Count);
            foreach (var r in l.Polls.Records)
            {
                writer.Write(r.MonthIndex);
                WriteString(writer, r.GoverningPartyId);
                writer.Write(r.PartyIds.Length);
                foreach (var id in r.PartyIds) WriteString(writer, id);
                writer.Write(r.Approval);
                writer.Write(r.Intention.Length);
                foreach (float f in r.Intention) writer.Write(f);
                int n = r.DistrictIds != null && r.DistrictApproval != null ? r.DistrictIds.Length : -1;
                writer.Write(n);
                for (int i = 0; i < n; i++)
                {
                    WriteLong(writer, r.DistrictIds[i]);
                    writer.Write(r.DistrictApproval[i]);
                }
            }

            Mod.Log.Info($"Election data saved: {l.State}, {l.Archive.Count} election(s), {l.Polls.Count} poll(s)");
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            if (version < 1 || version > ElectionLedger.CurrentVersion)
            {
                Mod.Log.Warn($"Election data has format version {version}; this build reads up to {ElectionLedger.CurrentVersion}. Starting fresh.");
                m_Elections.RestoreFromSave(null);
                return;
            }

            var l = new ElectionLedger();
            reader.Read(out int state); l.State = (GovernmentState)state;
            reader.Read(out l.UnlockedMonth);
            reader.Read(out l.NextElectionMonth);
            reader.Read(out l.LastMonthSeen);
            l.GoverningPartyId = ReadString(reader);
            reader.Read(out l.ShutdownMonths);

            reader.Read(out int elections);
            for (int e = 0; e < elections; e++)
            {
                reader.Read(out int number);
                reader.Read(out int year);
                reader.Read(out int month);
                reader.Read(out int rule);
                ulong seed = (ulong)ReadLong(reader);
                reader.Read(out float target);
                reader.Read(out int governing);

                reader.Read(out int partyCount);
                var parties = new PartySnapshot[partyCount];
                for (int p = 0; p < partyCount; p++)
                {
                    string id = ReadString(reader);
                    string name = ReadString(reader);
                    reader.Read(out int color);
                    reader.Read(out float economic);
                    reader.Read(out float social);
                    parties[p] = new PartySnapshot(id, name, unchecked((uint)color), economic, social);
                }

                reader.Read(out int districtCount);
                var districts = new DistrictSnapshot[districtCount];
                for (int d = 0; d < districtCount; d++)
                {
                    long districtId = ReadLong(reader);
                    string name = ReadString(reader);
                    long eligible = ReadLong(reader);
                    reader.Read(out int winner);
                    reader.Read(out float margin);
                    reader.Read(out int color);
                    reader.Read(out int voteCount);
                    var votes = new long[voteCount];
                    for (int v = 0; v < voteCount; v++) votes[v] = ReadLong(reader);
                    districts[d] = new DistrictSnapshot(districtId, name, eligible, votes, winner, margin, unchecked((uint)color));
                }

                l.Archive.Add(new ElectionSnapshot(number, year, month, (GoverningRule)rule, seed, target, governing, parties, districts));
            }

            reader.Read(out int polls);
            for (int i = 0; i < polls; i++)
            {
                reader.Read(out int monthIndex);
                string governingId = ReadString(reader);
                reader.Read(out int idCount);
                var ids = new string[idCount];
                for (int k = 0; k < idCount; k++) ids[k] = ReadString(reader);
                reader.Read(out float approval);
                reader.Read(out int intentionCount);
                var intention = new float[intentionCount];
                for (int k = 0; k < intentionCount; k++) reader.Read(out intention[k]);
                reader.Read(out int n);
                long[] districtIds = null;
                float[] districtApproval = null;
                if (n >= 0)
                {
                    districtIds = new long[n];
                    districtApproval = new float[n];
                    for (int k = 0; k < n; k++)
                    {
                        districtIds[k] = ReadLong(reader);
                        reader.Read(out districtApproval[k]);
                    }
                }

                l.Polls.Add(new PollRecord(monthIndex, governingId, ids, approval, intention, districtIds, districtApproval));
            }

            m_Elections.RestoreFromSave(l);
        }

        // Strings: a presence flag then the text, so null survives a round trip.
        private static void WriteString<TWriter>(TWriter writer, string s) where TWriter : IWriter
        {
            writer.Write(s != null);
            if (s != null) writer.Write(s);
        }

        private static string ReadString<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out bool present);
            if (!present) return null;
            reader.Read(out string s);
            return s;
        }

        private static void WriteLong<TWriter>(TWriter writer, long v) where TWriter : IWriter
        {
            writer.Write(unchecked((int)(v & 0xFFFFFFFF)));
            writer.Write(unchecked((int)(v >> 32)));
        }

        private static long ReadLong<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int lo);
            reader.Read(out int hi);
            return ((long)hi << 32) | (uint)lo;
        }
    }
}
