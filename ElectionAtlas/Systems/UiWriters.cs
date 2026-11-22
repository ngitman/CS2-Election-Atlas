using System;
using System.Collections.Generic;
using Colossal.UI.Binding;
using ElectionAtlas.Model;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// Every shape the UI receives, written with the game's own <see cref="IJsonWriter"/> (the same writer its info
    /// panels use). The Elections panel and the in-city district section share these, so a district reads the
    /// same everywhere. The TypeScript side mirrors them in UI/src/bindings.ts.
    ///
    /// RULE: every TypeBegin name must always get exactly the same properties, in the same order. The UI engine
    /// (Cohtml) remembers a type's shape the first time it sees it, and a later object of that type with more
    /// properties crashes the game in native code. Write null for anything missing; give different shapes
    /// different type names.
    /// </summary>
    internal static class UiWriters
    {
        public static void Color(this IJsonWriter w, uint rgb) => w.Write("#" + (rgb & 0xFFFFFF).ToString("x6"));

        /// <summary>NaN and infinity aren't valid JSON; they go out as null.</summary>
        public static void Number(this IJsonWriter w, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) w.WriteNull(); else w.Write(v);
        }

        public static void Prop(this IJsonWriter w, string name, string v) { w.PropertyName(name); if (v == null) w.WriteNull(); else w.Write(v); }
        public static void Prop(this IJsonWriter w, string name, int v) { w.PropertyName(name); w.Write(v); }
        public static void Prop(this IJsonWriter w, string name, long v) { w.PropertyName(name); w.Write(v); }
        public static void Prop(this IJsonWriter w, string name, float v) { w.PropertyName(name); w.Number(v); }
        public static void Prop(this IJsonWriter w, string name, bool v) { w.PropertyName(name); w.Write(v); }
        public static void PropColor(this IJsonWriter w, string name, uint rgb) { w.PropertyName(name); w.Color(rgb); }

        public static void Numbers(this IJsonWriter w, IReadOnlyList<float> values)
        {
            w.ArrayBegin((uint)(values?.Count ?? 0));
            if (values != null) foreach (float v in values) w.Number(v);
            w.ArrayEnd();
        }

        public static void Numbers(this IJsonWriter w, IReadOnlyList<long> values)
        {
            w.ArrayBegin((uint)(values?.Count ?? 0));
            if (values != null) foreach (long v in values) w.Write(v);
            w.ArrayEnd();
        }

        /// <summary>A party as the lineup knows it (id, name, color, optionally its position).</summary>
        public static void Party(this IJsonWriter w, string id, string name, uint color, Position? position = null)
        {
            w.TypeBegin("ElectionAtlas.Party");
            w.Prop("id", id);
            w.Prop("name", name);
            w.PropColor("color", color);
            w.PropertyName("economic");
            if (position.HasValue) w.Number(position.Value.Economic); else w.WriteNull();
            w.PropertyName("social");
            if (position.HasValue) w.Number(position.Value.Social); else w.WriteNull();
            w.TypeEnd();
        }

        /// <summary>One district: its result in an election and how it feels about the government now.</summary>
        public static void District(this IJsonWriter w, DistrictView d)
        {
            var entity = DistrictKey.ToEntity(d.Id);
            w.TypeBegin("ElectionAtlas.District");
            w.Prop("id", d.Id);
            w.Prop("index", entity.Index);
            w.Prop("version", entity.Version);
            w.Prop("seat", d.IsSeat);
            w.Prop("name", d.Name);
            w.Prop("profile", DistrictProfiles.Label(d.Profile)); // "" until known
            w.Prop("eligible", d.Eligible);
            w.Prop("votes", d.Votes);
            w.Prop("turnout", d.Turnout);
            w.Prop("winner", d.Winner);
            w.Prop("winnerShare", d.WinnerShare);
            w.Prop("margin", d.Margin);
            w.PropColor("color", d.Color);
            w.PropertyName("approval");
            if (d.HasApproval) w.Write(d.Approval); else w.WriteNull();
            w.PropertyName("partyVotes");
            w.Numbers(d.PartyVotes);
            w.PropertyName("trend");
            w.Numbers(d.Trend);
            w.TypeEnd();
        }

        /// <summary>
        /// An election's citywide figures, written as properties of an object the caller has open. Always the same
        /// properties: with no election (null) they are zeros and an empty party list.
        /// </summary>
        public static void ElectionFields(this IJsonWriter w, ElectionSnapshot s)
        {
            w.Prop("number", s?.Number ?? 0);
            w.Prop("month", s != null ? ElectionSchedule.MonthIndex(s.Year, s.Month) : -1);
            w.Prop("rule", s?.Rule.ToString() ?? "");
            w.Prop("turnout", s?.Turnout ?? 0f);
            w.Prop("target", s?.TargetTurnout ?? 0f);
            w.Prop("eligible", s?.Eligible ?? 0L);
            w.Prop("votes", s?.Votes ?? 0L);
            w.Prop("governing", s?.GoverningPartyIndex ?? -1);

            w.PropertyName("parties");
            int count = s?.Parties.Length ?? 0;
            w.ArrayBegin((uint)count);
            for (int i = 0; i < count; i++)
            {
                var p = s.Parties[i];
                w.TypeBegin("ElectionAtlas.PartyResult");
                w.Prop("id", p.Id);
                w.Prop("name", p.Name);
                w.PropColor("color", p.Color);
                w.Prop("share", s.Share(i));
                w.Prop("votes", s.PartyVotes[i]);
                w.Prop("won", s.DistrictsWon[i]);
                w.TypeEnd();
            }
            w.ArrayEnd();
        }

        /// <summary>Poll intention mapped onto the current lineup (0 for parties the poll didn't include).</summary>
        public static void Intention(this IJsonWriter w, PollRecord r, Party[] parties)
        {
            w.ArrayBegin((uint)parties.Length);
            foreach (var p in parties)
            {
                int i = Array.IndexOf(r.PartyIds, p.Id);
                w.Number(i >= 0 ? r.Intention[i] : 0f);
            }
            w.ArrayEnd();
        }
    }
}
