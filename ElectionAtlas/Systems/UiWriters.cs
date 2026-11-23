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

        public static void PartyList(this IJsonWriter w, string name, IReadOnlyList<Party> parties)
        {
            w.PropertyName(name);
            w.ArrayBegin((uint)parties.Count);
            foreach (var party in parties) w.Party(party.Id, party.Name, party.Color, party.Position);
            w.ArrayEnd();
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
                w.PartyResult(s.Parties[i], s.PartyVotes[i], s.Votes, s.DistrictsWon[i]);
            w.ArrayEnd();
        }

        private static void PartyResult(this IJsonWriter w, PartySnapshot party, long votes, long total, int seats)
        {
            w.TypeBegin("ElectionAtlas.PartyResult");
            w.Prop("id", party.Id);
            w.Prop("name", party.Name);
            w.PropColor("color", party.Color);
            w.Prop("share", total > 0 ? (float)((double)votes / total) : 0f);
            w.Prop("votes", votes);
            w.Prop("won", seats);
            w.TypeEnd();
        }

        public static void ElectionDayReplay(this IJsonWriter w, ElectionNightPlayback play, bool sample)
        {
            var snapshot = play.Night.Snapshot;
            var report = play.Frame;
            bool voting = play.IsVoting;
            long ballots = play.Day.BallotsCastAt(play.Hour);

            // V3: adds leadChanged (Cohtml keeps each type name's first property list, so a new shape needs a new name).
            w.TypeBegin("ElectionAtlas.DayReplayV3");
            w.Prop("number", snapshot.Number);
            w.Prop("month", ElectionSchedule.MonthIndex(snapshot.Year, snapshot.Month));
            w.Prop("rule", snapshot.Rule.ToString());
            w.Prop("voting", voting);
            w.Prop("playing", play.IsPlaying);
            w.Prop("finished", play.IsFinished);
            w.Prop("sample", sample);
            w.Prop("live", play.FollowsGameClock);
            w.Prop("beforePolls", play.BeforePolls);
            w.Prop("hour", play.Hour);
            w.Prop("ballots", ballots);
            w.Prop("turnout", snapshot.Eligible > 0 ? (float)((double)ballots / snapshot.Eligible) : 0f);
            w.Prop("counted", voting ? 0L : report.Votes);
            // Districts nobody lives in have nothing to count: they're left out of "n of k" and the voting list.
            w.Prop("districtsReported", voting ? 0 : report.ContestedSeatsReported);
            w.Prop("districtCount", report.ContestedSeatCount);
            w.Prop("leader", voting ? -1 : report.LeaderIndex);
            w.Prop("leadChanged", !voting && report.LeadChanged);

            int votingDistricts = 0;
            if (voting)
                for (int i = 0; i < snapshot.Districts.Length; i++)
                    if (play.Night.HasVoters(i)) votingDistricts++;
            w.PropertyName("districts");
            w.ArrayBegin((uint)votingDistricts);
            if (voting)
            {
                for (int i = 0; i < snapshot.Districts.Length; i++)
                {
                    if (!play.Night.HasVoters(i)) continue;
                    var district = snapshot.Districts[i];
                    long districtBallots = play.Day.DistrictBallotsCastAt(i, play.Hour);
                    w.TypeBegin("ElectionAtlas.DayDistrict");
                    w.Prop("name", district.Name);
                    w.Prop("ballots", districtBallots);
                    w.Prop("turnout", district.Eligible > 0 ? (float)((double)districtBallots / district.Eligible) : 0f);
                    w.TypeEnd();
                }
            }
            w.ArrayEnd();

            w.PropertyName("parties");
            w.ArrayBegin(voting ? 0 : (uint)snapshot.Parties.Length);
            if (!voting)
            {
                for (int p = 0; p < snapshot.Parties.Length; p++)
                    w.PartyResult(snapshot.Parties[p], report.PartyVotes(p), report.Votes, report.Seats(p));
            }
            w.ArrayEnd();

            w.PropertyName("wave");
            w.ArrayBegin(voting || report.Index == 0 ? 0 : (uint)report.WaveCount);
            if (!voting && report.Index > 0)
                for (int i = 0; i < report.WaveCount; i++) w.Write(play.Night.Units[report.WaveUnit(i)].Name ?? string.Empty);
            w.ArrayEnd();
            w.TypeEnd();
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
