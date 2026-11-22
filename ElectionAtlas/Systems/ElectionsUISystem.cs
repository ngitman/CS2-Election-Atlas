using System;
using System.Linq;
using Colossal.UI.Binding;
using ElectionAtlas.Model;
using Game.UI;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// Bindings between C# and the Elections panel. Panel data goes out as JSON strings (overview, districts,
    /// history, polls) rebuilt only while the panel is open and only when something they show has changed.
    /// ValueBindings only push to the UI when their value changes, so an idle panel costs nothing.
    /// </summary>
    public partial class ElectionsUISystem : UISystemBase
    {
        public const string kGroup = "electionAtlas";
        private const int kPollRows = 24;

        private CityHallSystem m_CityHall;
        private ElectionSystem m_Elections;

        private ValueBinding<bool> m_PanelOpen;
        private ValueBinding<string> m_GovernmentState;
        private ValueBinding<string> m_Overview;
        private ValueBinding<string> m_Districts;
        private ValueBinding<string> m_History;
        private ValueBinding<string> m_Polls;

        private GovernmentState m_LastState = GovernmentState.NoCityHall;
        private int m_ElectionIndex = -1; // -1 = latest
        private long m_LastKey = long.MinValue;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CityHall = World.GetOrCreateSystemManaged<CityHallSystem>();
            m_Elections = World.GetOrCreateSystemManaged<ElectionSystem>();

            AddBinding(m_PanelOpen = new ValueBinding<bool>(kGroup, "panelOpen", false));
            AddBinding(m_GovernmentState = new ValueBinding<string>(kGroup, "governmentState", nameof(GovernmentState.NoCityHall)));
            AddBinding(m_Overview = new ValueBinding<string>(kGroup, "overview", "{}"));
            AddBinding(m_Districts = new ValueBinding<string>(kGroup, "districts", "{}"));
            AddBinding(m_History = new ValueBinding<string>(kGroup, "history", "{}"));
            AddBinding(m_Polls = new ValueBinding<string>(kGroup, "polls", "{}"));

            AddBinding(new TriggerBinding(kGroup, "togglePanel", TogglePanel));
            AddBinding(new TriggerBinding(kGroup, "closePanel", ClosePanel));
            AddBinding(new TriggerBinding<int>(kGroup, "stepElection", StepElection));
            AddBinding(new TriggerBinding<int>(kGroup, "showElection", ShowElection));

            Mod.Log.Info("Elections UI bindings registered");
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            // Only touch the binding when the state changes (Enum.ToString allocates).
            var state = m_CityHall.State;
            if (state != m_LastState)
            {
                m_LastState = state;
                m_GovernmentState.Update(state.ToString());
            }

            if (m_CityHall.PendingUnlockNotice)
            {
                m_CityHall.PendingUnlockNotice = false;
                m_PanelOpen.Update(true);
            }

            if (m_Elections.PendingResultNotice)
            {
                m_Elections.PendingResultNotice = false;
                m_ElectionIndex = -1;
                Invalidate();
                m_PanelOpen.Update(true);
            }

            if (!m_PanelOpen.value)
                return;

            // Rebuild only when something shown has changed.
            long key = m_Elections.DataVersion;
            key = key * 31 + (int)state;
            key = key * 31 + (int)(m_CityHall.Efficiency * 100f);
            key = key * 31 + (m_CityHall.IsShutDown ? 1 : 0) + (m_CityHall.SwitchedOff ? 2 : 0);
            key = key * 31 + m_CityHall.CityHallCount;
            key = key * 31 + m_Elections.NowMonth;
            key = key * 31 + m_ElectionIndex;
            if (key == m_LastKey)
                return;
            m_LastKey = key;

            m_Overview.Update(BuildOverview());
            m_Districts.Update(BuildDistricts());
            m_History.Update(BuildHistory());
            m_Polls.Update(BuildPolls());
        }

        private void Invalidate() => m_LastKey = long.MinValue;

        private void TogglePanel()
        {
            m_PanelOpen.Update(!m_PanelOpen.value);
            Invalidate();
        }

        private void ClosePanel() => m_PanelOpen.Update(false);

        private void StepElection(int delta)
        {
            int count = m_Elections.Ledger.Archive.Count;
            if (count == 0) return;
            int current = m_ElectionIndex < 0 ? count - 1 : m_ElectionIndex;
            ShowElection(current + delta);
        }

        private void ShowElection(int index)
        {
            int count = m_Elections.Ledger.Archive.Count;
            if (count == 0) return;
            index = Math.Max(0, Math.Min(count - 1, index));
            m_ElectionIndex = index == count - 1 ? -1 : index;
            Invalidate();
        }

        // ---------------------------------------------------------------- JSON

        private string BuildOverview()
        {
            var ledger = m_Elections.Ledger;
            int now = m_Elections.NowMonth;
            var parties = m_Elections.CurrentParties();
            var governing = parties.FirstOrDefault(p => p.Id == ledger.GoverningPartyId);
            var poll = ledger.Polls.Latest;

            var j = new Json().Begin()
                .Prop("state", m_CityHall.State.ToString())
                .Prop("today", now)
                .Prop("cityHalls", m_CityHall.CityHallCount)
                .Prop("efficiency", m_CityHall.Efficiency)
                .Prop("shutDown", m_CityHall.IsShutDown)
                .Prop("switchedOff", m_CityHall.SwitchedOff)
                .Prop("shutdownMonths", ledger.ShutdownMonths)
                .Prop("nextElection", ledger.NextElectionMonth == ElectionSchedule.NotScheduled ? -1 : ledger.NextElectionMonth)
                .Prop("monthsUntil", ElectionSchedule.MonthsUntil(now, ledger.NextElectionMonth))
                .Prop("elections", ledger.Archive.Count)
                .Prop("reading", m_Elections.IsReading)
                .Prop("devMonthOffset", m_Elections.DevMonthOffset);

            j.Key("governing");
            if (governing != null) PartyJson(j, governing.Id, governing.Name, governing.Color);
            else if (ledger.GoverningPartyId != null) j.Begin().Prop("id", ledger.GoverningPartyId).Prop("name", ledger.GoverningPartyId).Prop("color", "#888888").End();
            else j.Null();

            j.Key("parties").BeginArray();
            foreach (var p in parties)
                PartyJson(j, p.Id, p.Name, p.Color, p.Position.Economic, p.Position.Social);
            j.EndArray();

            j.Key("poll");
            if (poll != null)
            {
                j.Begin().Prop("month", poll.MonthIndex).Prop("approval", poll.Approval);
                IntentionJson(j, poll, parties);
                j.End();
            }
            else j.Null();

            return j.End().ToString();
        }

        /// <summary>The shown election (latest by default) and every district in it.</summary>
        private string BuildDistricts()
        {
            var ledger = m_Elections.Ledger;
            var archive = ledger.Archive;
            var j = new Json().Begin().Prop("count", archive.Count);
            if (archive.Count == 0)
                return j.End().ToString();

            int index = m_ElectionIndex < 0 ? archive.Count - 1 : Math.Min(m_ElectionIndex, archive.Count - 1);
            var s = archive[index];
            j.Prop("index", index);
            j.Prop("approvalMonth", ledger.Polls.Latest?.MonthIndex ?? -1);
            SummaryFields(j, s);

            j.Key("districts").BeginArray();
            foreach (var d in DistrictViews.ForElection(ledger, s))
                DistrictJson(j, d);
            j.EndArray();

            return j.End().ToString();
        }

        private string BuildHistory()
        {
            var archive = m_Elections.Ledger.Archive;
            var j = new Json().Begin();
            j.Key("elections").BeginArray();
            for (int i = archive.Count - 1; i >= 0; i--)
            {
                j.Begin().Prop("index", i);
                SummaryFields(j, archive[i]);
                j.End();
            }

            return j.EndArray().End().ToString();
        }

        private string BuildPolls()
        {
            var ledger = m_Elections.Ledger;
            var parties = m_Elections.CurrentParties();
            var j = new Json().Begin();

            j.Key("parties").BeginArray();
            foreach (var p in parties) PartyJson(j, p.Id, p.Name, p.Color);
            j.EndArray();

            j.Key("records").BeginArray();
            foreach (var r in ledger.Polls.Records.Skip(Math.Max(0, ledger.Polls.Count - kPollRows)))
            {
                j.Begin().Prop("month", r.MonthIndex).Prop("approval", r.Approval).Prop("governing", r.GoverningPartyId);
                IntentionJson(j, r, parties);
                j.End();
            }

            return j.EndArray().End().ToString();
        }

        internal static void DistrictJson(Json j, DistrictView d)
        {
            j.Begin()
                .Prop("id", d.Id)
                .Prop("index", d.EntityIndex)
                .Prop("version", d.EntityVersion)
                .Prop("selectable", d.CanSelect)
                .Prop("name", d.Name)
                .Prop("eligible", d.Eligible)
                .Prop("votes", d.Votes)
                .Prop("turnout", d.Turnout)
                .Prop("winner", d.Winner)
                .Prop("winnerShare", d.WinnerShare)
                .Prop("margin", d.Margin)
                .Color("color", d.Color);

            j.Key("approval");
            if (d.HasApproval) j.Value(d.Approval); else j.Null();

            j.Key("partyVotes").BeginArray();
            if (d.PartyVotes != null) foreach (long v in d.PartyVotes) j.Value(v);
            j.EndArray();

            j.Key("trend").BeginArray();
            if (d.Trend != null) foreach (float f in d.Trend) j.Value(f);
            j.EndArray();

            j.End();
        }

        private static void PartyJson(Json j, string id, string name, uint color, float? economic = null, float? social = null)
        {
            j.Begin().Prop("id", id).Prop("name", name).Color("color", color);
            if (economic.HasValue) j.Prop("economic", economic.Value);
            if (social.HasValue) j.Prop("social", social.Value);
            j.End();
        }

        /// <summary>Intention mapped onto the current lineup (0 for parties the poll didn't include).</summary>
        private static void IntentionJson(Json j, PollRecord r, Party[] parties)
        {
            j.Key("intention").BeginArray();
            foreach (var p in parties)
            {
                int i = Array.IndexOf(r.PartyIds, p.Id);
                j.Value(i >= 0 ? r.Intention[i] : 0f);
            }
            j.EndArray();
        }

        private static void SummaryFields(Json j, ElectionSnapshot s)
        {
            j.Prop("number", s.Number)
                .Prop("month", ElectionSchedule.MonthIndex(s.Year, s.Month))
                .Prop("rule", s.Rule.ToString())
                .Prop("turnout", s.Turnout)
                .Prop("target", s.TargetTurnout)
                .Prop("eligible", s.Eligible)
                .Prop("votes", s.Votes)
                .Prop("governing", s.GoverningPartyIndex);

            j.Key("parties").BeginArray();
            for (int i = 0; i < s.Parties.Length; i++)
            {
                var p = s.Parties[i];
                j.Begin().Prop("id", p.Id).Prop("name", p.Name).Color("color", p.Color)
                    .Prop("share", s.Share(i)).Prop("votes", s.PartyVotes[i]).Prop("won", s.DistrictsWon[i]).End();
            }
            j.EndArray();
        }
    }
}
