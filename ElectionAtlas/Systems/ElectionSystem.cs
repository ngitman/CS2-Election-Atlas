using System;
using System.Diagnostics;
using System.Linq;
using ElectionAtlas.Model;
using Game;
using Game.Simulation;
using Purpose = Colossal.Serialization.Entities.Purpose;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// Phases 4-5: the calendar. On each new in-game month it runs a poll (while a City Hall stands) and, when
    /// due, an election; the work is spread over 16 updates by <see cref="SlicedVoterReader"/>. Elections and
    /// polls are frozen into the <see cref="ElectionLedger"/>, which ElectionStoreSystem saves when enabled.
    /// A City Hall shutdown doesn't stop anything; it becomes a penalty for the governing party.
    /// Cost: idle updates every 512 frames only compare the month; a read runs one slice every 16 frames.
    /// </summary>
    public partial class ElectionSystem : GameSystemBase
    {
        private const int kIdleInterval = 512;
        private const int kReadInterval = 16;
        private const int kSlices = 16;

        private CityHallSystem m_CityHall;
        private TimeSystem m_Time;
        private SlicedVoterReader m_Reader;
        private readonly ModelConfig m_Config = new ModelConfig();

        private ElectionLedger m_Restored;
        private bool m_InGame;
        private GovernmentState m_LastGateState;
        private bool m_PendingPoll;
        private bool m_PendingElection;
        private int m_PendingMonth;
        private int m_StepSize;
        private Stopwatch m_ReadTimer;

        public ElectionLedger Ledger { get; private set; } = new ElectionLedger();

        /// <summary>Bumped whenever something the panel shows changes, so the UI only rebuilds then.</summary>
        public int DataVersion { get; private set; }

        /// <summary>The latest poll (in memory only), with district names in the same order.</summary>
        public PollResult LatestPoll { get; private set; }
        public string[] LatestPollDistrictNames { get; private set; }
        public DistrictInfo[] LatestPollDistricts { get; private set; }

        /// <summary>Set when an election result comes in; the UI opens the panel and clears it.</summary>
        public bool PendingResultNotice { get; set; }

        /// <summary>Dev builds: months added to the game date, to test the schedule without waiting.</summary>
        public int DevMonthOffset { get; private set; }

        public ModelConfig Config => m_Config;

        public int NowMonth
        {
            get
            {
                int month = Math.Min(11, Math.Max(0, (int)Math.Floor(m_Time.normalizedDate * 12f)));
                return ElectionSchedule.MonthIndex(m_Time.year, month) + DevMonthOffset;
            }
        }

        public bool IsReading => m_Reader != null && m_Reader.InProgress;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => IsReading ? kReadInterval : kIdleInterval;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CityHall = World.GetOrCreateSystemManaged<CityHallSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Reader = new SlicedVoterReader(World);
        }

        protected override void OnDestroy()
        {
            m_Reader?.Dispose();
            base.OnDestroy();
        }

        /// <summary>Called by ElectionStoreSystem while a save loads (null = no saved data).</summary>
        public void RestoreFromSave(ElectionLedger ledger) => m_Restored = ledger;

        /// <summary>Called by ElectionStoreSystem when the game saves.</summary>
        public ElectionLedger LedgerForSave()
        {
            Ledger.State = m_CityHall.State;
            return Ledger;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_InGame = mode == GameMode.Game;
            m_Reader.Cancel();
            m_PendingPoll = m_PendingElection = false;
            DevMonthOffset = 0;
            LatestPoll = null;
            LatestPollDistrictNames = null;
            PendingResultNotice = false;

            Ledger = m_Restored ?? new ElectionLedger();
            if (m_Restored != null)
                Mod.Log.Info($"Election data loaded: {Ledger.State}, {Ledger.Archive.Count} election(s), {Ledger.Polls.Count} poll(s), next {ElectionSchedule.Format(Ledger.NextElectionMonth)}");
            m_Restored = null;

            m_CityHall.ResetForLoad(Ledger.State);
            m_LastGateState = Ledger.State;
            DataVersion++;
        }

        protected override void OnUpdate()
        {
            if (!m_InGame)
                return;

            int now = NowMonth;
            SyncGate(now);

            if (m_Reader.InProgress)
            {
                if (m_Reader.Step(m_StepSize))
                {
                    var table = m_Reader.Finish(out var districts, out var names);
                    Process(table, districts, names, m_PendingMonth, m_PendingPoll, m_PendingElection, m_ReadTimer?.ElapsedMilliseconds ?? 0);
                    m_PendingPoll = m_PendingElection = false;
                }

                return;
            }

            var tick = Ledger.OnMonth(now, m_CityHall.IsShutDown);
            if (!tick.IsNewMonth)
                return;

            DataVersion++;
            if (tick.RunPoll || tick.RunElection)
            {
                m_PendingPoll = tick.RunPoll;
                m_PendingElection = tick.RunElection;
                m_PendingMonth = now;
                m_ReadTimer = Stopwatch.StartNew();
                m_Reader.Begin();
                m_StepSize = Math.Max(64, m_Reader.HouseholdCount / kSlices + 1);
            }
        }

        private void SyncGate(int now)
        {
            var state = m_CityHall.State;
            if (state != m_LastGateState)
            {
                Ledger.OnGovernmentChanged(m_LastGateState, state, now, Cadence);
                m_LastGateState = state;
                DataVersion++;
            }

            Ledger.State = state;
            Ledger.EnsureScheduled(now, Cadence);
        }

        /// <summary>Runs the poll and/or election on a finished read and records them.</summary>
        private void Process(GroupTable table, DistrictInfo[] districts, string[] names, int month, bool poll, bool election, long readMs)
        {
            if (table.TotalVoters == 0)
            {
                Mod.Log.Info($"{ElectionSchedule.Format(month)}: no voting-age residents yet; poll and election skipped");
                return;
            }

            var parties = CurrentParties();
            var settings = Mod.Settings;
            var sw = Stopwatch.StartNew();

            if (election)
            {
                var condition = Ledger.Condition(m_CityHall.CityHallCount > 0, m_CityHall.IsShutDown, m_CityHall.Efficiency);
                int number = Ledger.Archive.NextNumber;
                var result = ElectionModel.RunElection(table, parties, Ledger.GoverningPartyId,
                    settings?.GoverningRule ?? GoverningRule.PopularVote, settings?.TurnoutPreset ?? TurnoutPreset.UsGeneral,
                    m_Config, ElectionModel.MakeSeed(Ledger.UnlockedMonth, number), condition,
                    DistrictInfo.IndexOfOutside(districts));
                var snapshot = ElectionSnapshot.Capture(result, parties, districts, number,
                    ElectionSchedule.YearOf(month), ElectionSchedule.MonthOf(month), m_Config.Map);

                Ledger.RecordElection(snapshot, month, Cadence);
                m_CityHall.ApplyFoundingElection();
                m_LastGateState = m_CityHall.State;
                PendingResultNotice = true;

                Mod.Log.Info($"Election #{number} ({ElectionSchedule.Format(month)}): turnout {snapshot.Turnout:P1}, " +
                    string.Join(", ", snapshot.Parties.Select((p, i) => $"{p.Name} {snapshot.Share(i):P1}")) +
                    $"; governing {snapshot.GoverningParty?.Name ?? "nobody"}; next {ElectionSchedule.Format(Ledger.NextElectionMonth)}" +
                    (condition.IsNormal ? "" : $"; shutdown months {condition.ShutdownMonths}, efficiency gap {condition.EfficiencyDeficit:P0}"));
            }

            if (poll)
            {
                var condition = Ledger.Condition(m_CityHall.CityHallCount > 0, m_CityHall.IsShutDown, m_CityHall.Efficiency);
                var result = ElectionModel.RunPoll(table, parties, Ledger.GoverningPartyId, m_Config, condition);
                LatestPoll = result;
                LatestPollDistrictNames = names;
                LatestPollDistricts = districts;
                if (Ledger.Polls.Latest == null || Ledger.Polls.Latest.MonthIndex < month)
                    Ledger.Polls.Add(PollRecord.From(result, month, Ledger.GoverningPartyId, districts));
            }

            Mod.Log.Info($"{ElectionSchedule.Format(month)}: {(election ? "election + " : "")}{(poll ? "poll" : "")} on {table.TotalVoters:N0} voters ({table.Groups.Count} groups); read {readMs} ms over {kSlices} slices, model {sw.ElapsedMilliseconds} ms");
            DataVersion++;
        }

        private ElectionCadence Cadence => Mod.Settings?.Cadence ?? ElectionCadence.EveryYear;

        public Party[] CurrentParties() =>
            SampleParties.Get(Mod.Settings?.PartyLineup == PartyLineup.FourParties ? 4 : 2);

        // ---------------------------------------------------------------- on demand (dev commands, UI)

        /// <summary>Reads the whole city now and holds an election (plus a poll). Works while paused.</summary>
        public string RunElectionNow()
        {
            m_Reader.Cancel();
            var sw = Stopwatch.StartNew();
            var table = m_Reader.ReadAll(out var districts, out var names);
            int before = Ledger.Archive.Count;
            int now = NowMonth;
            Process(table, districts, names, now, poll: true, election: true, readMs: sw.ElapsedMilliseconds);
            var s = Ledger.Archive.Latest;
            if (Ledger.Archive.Count == before || s == null)
                return "No election: there are no voting-age residents yet.";
            return $"Election #{s.Number} ({ElectionSchedule.Format(now)}): turnout {s.Turnout:P1} · " +
                string.Join(" · ", s.Parties.Select((p, i) => $"{p.Name} {s.Share(i):P1}")) +
                $"\nGoverning: {s.GoverningParty?.Name ?? "nobody"} · next election {ElectionSchedule.Format(Ledger.NextElectionMonth)} · read+model {sw.ElapsedMilliseconds} ms";
        }

        /// <summary>Reads the whole city now and runs a poll (recorded if it's a new month).</summary>
        public string RunPollNow()
        {
            m_Reader.Cancel();
            var sw = Stopwatch.StartNew();
            var table = m_Reader.ReadAll(out var districts, out var names);
            Process(table, districts, names, NowMonth, poll: true, election: false, readMs: sw.ElapsedMilliseconds);
            if (LatestPoll == null)
                return "No poll: there are no voting-age residents yet.";
            var parties = CurrentParties();
            return $"Poll ({ElectionSchedule.Format(NowMonth)}): approval {LatestPoll.Approval:0.0} · " +
                string.Join(" · ", parties.Select((p, i) => $"{p.Name} {LatestPoll.Intention[i]:P1}")) + $" · {sw.ElapsedMilliseconds} ms";
        }

#if DEV_TOOLS
        public string DevAdvanceMonths(int months)
        {
            DevMonthOffset += months;
            DataVersion++;
            return $"Advanced {months} month(s): the mod now sees {ElectionSchedule.Format(NowMonth)} (offset +{DevMonthOffset}). Unpause for the month tick to run.";
        }

        public string DevSkipToElection()
        {
            int until = ElectionSchedule.MonthsUntil(NowMonth, Ledger.NextElectionMonth);
            if (until < 0) return "No election is scheduled (is there a City Hall?).";
            if (until == 0) return "An election is already due; unpause for the month tick to run it.";
            return DevAdvanceMonths(until);
        }

        public string DevReset()
        {
            m_Reader.Cancel();
            var state = m_CityHall.State;
            Ledger = new ElectionLedger();
            Ledger.OnGovernmentChanged(GovernmentState.NoCityHall, state == GovernmentState.Active || state == GovernmentState.Suspended ? GovernmentState.AwaitingFounding : state, NowMonth, Cadence);
            if (state != GovernmentState.NoCityHall) m_CityHall.DevSetState(GovernmentState.AwaitingFounding);
            m_LastGateState = m_CityHall.State;
            LatestPoll = null;
            DevMonthOffset = 0;
            DataVersion++;
            return "Election data reset: no elections, no polls, founding election rescheduled.";
        }
#endif
    }
}
