using System;
using System.Collections.Generic;
using ElectionAtlas.Model;

namespace ElectionAtlas.Systems
{
    /// <summary>One district as the UI shows it: its result in an election, and how it feels now.</summary>
    internal sealed class DistrictView
    {
        public long Id;
        public string Name;
        public long Eligible;
        public long Votes;
        public float Turnout;
        public int Winner = -1;
        public float WinnerShare;
        public float Margin;
        public uint Color;
        public long[] PartyVotes;
        public bool HasApproval;
        public float Approval;
        public float[] Trend; // oldest first; NaN where the district wasn't polled

        public bool CanSelect => Id >= 0;
        public int EntityIndex => (int)(Id & 0xFFFFFFFF);
        public int EntityVersion => (int)(Id >> 32);

        public float Share(int party) => Votes > 0 && PartyVotes != null && party < PartyVotes.Length ? (float)((double)PartyVotes[party] / Votes) : 0f;
    }

    /// <summary>Builds <see cref="DistrictView"/>s from the ledger: snapshot results plus poll approval by district id.</summary>
    internal static class DistrictViews
    {
        public const int TrendMonths = 12;

        public static List<DistrictView> ForElection(ElectionLedger ledger, ElectionSnapshot election)
        {
            var list = new List<DistrictView>();
            if (election == null) return list;

            foreach (var d in election.Districts)
            {
                if (d.Eligible <= 0) continue;
                var view = FromSnapshot(d);
                AddApproval(view, ledger);
                list.Add(view);
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        /// <summary>The district's result in the latest election that included it, plus its approval. Null if it has neither.</summary>
        public static DistrictView ForDistrict(ElectionLedger ledger, long id, string fallbackName, out ElectionSnapshot election)
        {
            election = null;
            DistrictView view = null;

            var all = ledger.Archive.All;
            for (int i = all.Count - 1; i >= 0 && view == null; i--)
            {
                foreach (var d in all[i].Districts)
                {
                    if (d.DistrictId != id) continue;
                    view = FromSnapshot(d);
                    election = all[i];
                    break;
                }
            }

            if (view == null)
                view = new DistrictView { Id = id, Name = fallbackName };

            AddApproval(view, ledger);
            return view.HasApproval || election != null ? view : null;
        }

        private static DistrictView FromSnapshot(DistrictSnapshot d)
        {
            var view = new DistrictView
            {
                Id = d.DistrictId,
                Name = d.Name,
                Eligible = d.Eligible,
                Votes = d.Votes,
                Turnout = d.Turnout,
                Winner = d.WinnerIndex,
                Margin = d.Margin,
                Color = d.MapColor,
                PartyVotes = d.PartyVotes,
            };
            view.WinnerShare = view.Winner >= 0 ? view.Share(view.Winner) : 0f;
            return view;
        }

        private static void AddApproval(DistrictView view, ElectionLedger ledger)
        {
            var records = ledger.Polls.Records;
            int start = Math.Max(0, records.Count - TrendMonths);
            view.Trend = new float[records.Count - start];

            for (int i = start; i < records.Count; i++)
            {
                float value = float.NaN;
                var r = records[i];
                if (r.DistrictIds != null && r.DistrictApproval != null)
                {
                    int k = Array.IndexOf(r.DistrictIds, view.Id);
                    if (k >= 0) value = r.DistrictApproval[k];
                }

                view.Trend[i - start] = value;
                if (i == records.Count - 1 && !float.IsNaN(value))
                {
                    view.HasApproval = true;
                    view.Approval = value;
                }
            }
        }
    }
}
