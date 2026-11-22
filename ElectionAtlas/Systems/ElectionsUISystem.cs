using System;
using System.Linq;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using ElectionAtlas.Model;
using Game;
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
        private ValueBinding<int> m_CitySession;
        private ValueBinding<string> m_GovernmentState;
        // Raw bindings: written straight into the UI with the game's JSON writer, no strings to build or parse.
        private RawValueBinding m_Overview;
        private RawValueBinding m_Districts;
        private RawValueBinding m_History;
        private RawValueBinding m_Polls;
        private ValueBinding<bool> m_MapView;

        private GovernmentState m_LastState = GovernmentState.NoCityHall;
        private int m_ElectionIndex = -1; // -1 = latest
        private long m_LastKey = long.MinValue;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CityHall = World.GetOrCreateSystemManaged<CityHallSystem>();
            m_Elections = World.GetOrCreateSystemManaged<ElectionSystem>();

            AddBinding(m_PanelOpen = new ValueBinding<bool>(kGroup, "panelOpen", false));
            AddBinding(m_CitySession = new ValueBinding<int>(kGroup, "citySession", 0));
            AddBinding(m_GovernmentState = new ValueBinding<string>(kGroup, "governmentState", nameof(GovernmentState.NoCityHall)));
            AddBinding(m_Overview = new RawValueBinding(kGroup, "overview", Guarded("overview", WriteOverview)));
            AddBinding(m_Districts = new RawValueBinding(kGroup, "districts", Guarded("districts", WriteDistricts)));
            AddBinding(m_History = new RawValueBinding(kGroup, "history", Guarded("history", WriteHistory)));
            AddBinding(m_Polls = new RawValueBinding(kGroup, "polls", Guarded("polls", WritePolls)));
            BindPartyEditor();

            AddBinding(new TriggerBinding(kGroup, "togglePanel", TogglePanel));
            AddBinding(new TriggerBinding(kGroup, "closePanel", ClosePanel));
            AddBinding(new TriggerBinding<int>(kGroup, "stepElection", StepElection));
            AddBinding(new TriggerBinding<int>(kGroup, "showElection", ShowElection));
            AddBinding(m_MapView = new ValueBinding<bool>(kGroup, "mapView", false));
            AddBinding(new TriggerBinding(kGroup, "showMap", () => m_MapView.Update(true)));
            AddBinding(new TriggerBinding(kGroup, "hideMap", () => m_MapView.Update(false)));

            Mod.Log.Info("Elections UI bindings registered");
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            ClosePanel();
            m_ElectionIndex = -1;
            Invalidate();
            m_CitySession.Update(m_CitySession.value + 1);
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (!m_Elections.HasLoadedCity) return;

            // Only touch the binding when the state changes (Enum.ToString allocates).
            var state = m_CityHall.State;
            if (state != m_LastState)
            {
                m_LastState = state;
                m_GovernmentState.Update(state.ToString());
            }

            bool opening = m_CityHall.PendingUnlockNotice || m_Elections.PendingResultNotice;
            m_CityHall.PendingUnlockNotice = false;
            if (m_Elections.PendingResultNotice)
            {
                m_Elections.PendingResultNotice = false;
                m_ElectionIndex = -1;
                Invalidate();
            }

            if (!m_PanelOpen.value && !opening) return;
            RefreshPanel();
            if (opening) m_PanelOpen.Update(true);
        }

        private void RefreshPanel()
        {
            RefreshPartyEditor();

            // Rebuild only when something shown has changed.
            long key = m_Elections.DataVersion;
            key = key * 31 + (int)m_CityHall.State;
            key = key * 31 + (int)(m_CityHall.Efficiency * 100f);
            key = key * 31 + (m_CityHall.IsShutDown ? 1 : 0) + (m_CityHall.SwitchedOff ? 2 : 0);
            key = key * 31 + m_CityHall.CityHallCount;
            key = key * 31 + m_Elections.NowMonth;
            key = key * 31 + m_ElectionIndex;
            if (key == m_LastKey)
                return;
            m_LastKey = key;

            m_Overview.Update();
            m_Districts.Update();
            m_History.Update();
            m_Polls.Update();
        }

        private void Invalidate() => m_LastKey = long.MinValue;

        public bool PanelOpen => m_Elections.HasLoadedCity && m_PanelOpen.value;

        /// <summary>The panel is collapsed to the map legend and districts are colored by result.</summary>
        public bool MapView => PanelOpen && m_MapView.value;

        /// <summary>The election the Districts tab is showing (the latest unless the player stepped back).</summary>
        public ElectionSnapshot ShownElection
        {
            get
            {
                if (!m_Elections.HasLoadedCity) return null;
                var archive = m_Elections.Ledger.Archive;
                if (archive.Count == 0) return null;
                return m_ElectionIndex < 0 || m_ElectionIndex >= archive.Count ? archive.Latest : archive[m_ElectionIndex];
            }
        }


        private void TogglePanel()
        {
            if (!m_Elections.HasLoadedCity) return;
            if (m_PanelOpen.value)
            {
                ClosePanel();
                return;
            }

            Invalidate();
            RefreshPanel();
            m_PanelOpen.Update(true);
        }

        /// <summary>Closing the panel (or the map legend) also ends the map view, so the overlay goes with it.</summary>
        private void ClosePanel()
        {
            m_PanelOpen.Update(false);
            m_MapView.Update(false);
        }

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

        // ---------------------------------------------------------------- bindings (shapes in UiWriters)

        /// <summary>Dev builds check every write's shapes in managed code first (see Dev/ShapeGuard); release builds write directly.</summary>
        private static Action<IJsonWriter> Guarded(string name, Action<IJsonWriter> write)
        {
#if DEV_TOOLS
            return Dev.ShapeGuard.Wrap(name, write);
#else
            return write;
#endif
        }

        private void WriteOverview(IJsonWriter w)
        {
            var ledger = m_Elections.Ledger;
            int now = m_Elections.NowMonth;
            var parties = m_Elections.CurrentParties();
            var governing = parties.FirstOrDefault(p => p.Id == ledger.GoverningPartyId);
            var poll = ledger.Polls.Latest;

            w.TypeBegin("ElectionAtlas.CityOverview");
            w.Prop("cityName", m_Elections.CityName);
            w.Prop("loadedSaveName", m_Elections.LoadedSaveName);
            w.Prop("state", m_CityHall.State.ToString());
            w.Prop("today", now);
            w.Prop("cityHalls", m_CityHall.CityHallCount);
            w.Prop("efficiency", m_CityHall.Efficiency);
            w.Prop("shutDown", m_CityHall.IsShutDown);
            w.Prop("switchedOff", m_CityHall.SwitchedOff);
            w.Prop("shutdownMonths", ledger.ShutdownMonths);
            w.Prop("nextElection", ledger.NextElectionMonth == ElectionSchedule.NotScheduled ? -1 : ledger.NextElectionMonth);
            w.Prop("monthsUntil", ElectionSchedule.MonthsUntil(now, ledger.NextElectionMonth));
            w.Prop("elections", ledger.Archive.Count);
            w.Prop("reading", m_Elections.IsCountingElection);
            w.Prop("devMonthOffset", m_Elections.DevMonthOffset);

            w.PropertyName("governing");
            if (governing != null) w.Party(governing.Id, governing.Name, governing.Color);
            else if (ledger.Archive.Latest?.GoverningParty is PartySnapshot elected) w.Party(elected.Id, elected.Name, elected.Color);
            else w.WriteNull();

            w.PartyList("parties", parties);

            w.PropertyName("poll");
            if (poll != null)
            {
                w.TypeBegin("ElectionAtlas.Poll");
                w.Prop("month", poll.MonthIndex);
                w.Prop("approval", poll.Approval);
                w.PropertyName("intention");
                w.Intention(poll, parties);
                w.TypeEnd();
            }
            else w.WriteNull();

            w.TypeEnd();
        }

        /// <summary>The shown election (latest by default) and every district in it.</summary>
        private void WriteDistricts(IJsonWriter w)
        {
            var ledger = m_Elections.Ledger;
            var archive = ledger.Archive;
            int index = archive.Count == 0 ? -1 : m_ElectionIndex < 0 ? archive.Count - 1 : Math.Min(m_ElectionIndex, archive.Count - 1);
            var s = index >= 0 ? archive[index] : null;
            var districts = DistrictViews.ForElection(ledger, s, m_Elections.Profiles); // empty when there's no election

            // Same properties whether or not there's an election (see the rule in UiWriters).
            w.TypeBegin("ElectionAtlas.Districts");
            w.Prop("count", archive.Count);
            w.Prop("index", index);
            w.Prop("approvalMonth", ledger.Polls.Latest?.MonthIndex ?? -1);
            w.ElectionFields(s);
            w.PropertyName("districts");
            w.ArrayBegin((uint)districts.Count);
            foreach (var d in districts) w.District(d);
            w.ArrayEnd();
            w.TypeEnd();
        }

        private void WriteHistory(IJsonWriter w)
        {
            var archive = m_Elections.Ledger.Archive;
            w.TypeBegin("ElectionAtlas.History");
            w.PropertyName("elections");
            w.ArrayBegin((uint)archive.Count);
            for (int i = archive.Count - 1; i >= 0; i--)
            {
                w.TypeBegin("ElectionAtlas.HistoryEntry");
                w.Prop("index", i);
                w.ElectionFields(archive[i]);
                w.TypeEnd();
            }
            w.ArrayEnd();
            w.TypeEnd();
        }

        private void WritePolls(IJsonWriter w)
        {
            var ledger = m_Elections.Ledger;
            var parties = m_Elections.CurrentParties();
            var records = ledger.Polls.Records;
            int start = Math.Max(0, records.Count - kPollRows);

            w.TypeBegin("ElectionAtlas.Polls");
            w.PropertyName("parties");
            w.ArrayBegin((uint)parties.Length);
            foreach (var p in parties) w.Party(p.Id, p.Name, p.Color);
            w.ArrayEnd();

            w.PropertyName("records");
            w.ArrayBegin((uint)(records.Count - start));
            for (int i = start; i < records.Count; i++)
            {
                var r = records[i];
                w.TypeBegin("ElectionAtlas.PollRecord");
                w.Prop("month", r.MonthIndex);
                w.Prop("approval", r.Approval);
                w.Prop("governing", r.GoverningPartyId);
                w.PropertyName("intention");
                w.Intention(r, parties);
                w.TypeEnd();
            }
            w.ArrayEnd();
            w.TypeEnd();
        }
    }
}
