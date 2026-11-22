using System;
using System.Collections.Generic;
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
    /// due, an election. Elections and polls are frozen into the <see cref="ElectionLedger"/>, which
    /// ElectionStoreSystem saves when enabled. A City Hall shutdown doesn't stop anything; it becomes a penalty
    /// for the governing party.
    ///
    /// Cost: the voters are counted by a census taken in the last quarter of each month, in Burst slices of
    /// <see cref="kSliceHouseholds"/> households scheduled on worker threads behind the game's own jobs (the main
    /// thread never waits). When the month turns, the poll and election run on that census straight away (the
    /// model takes well under a millisecond). Without a fresh census (just loaded, months skipped) the same read
    /// runs on the spot. Idle updates every 512 frames only compare the month.
    /// </summary>
    public partial class ElectionSystem : GameSystemBase
    {
        private const int kIdleInterval = 512;
        private const int kReadInterval = 16;
        private const int kSliceHouseholds = 1024;  // about 0.2 ms of worker time per slice
        private const float kCensusStart = 0.75f;  // census starts once the month is this far along

        private CityHallSystem m_CityHall;
        private TimeSystem m_Time;
        private VoterReader m_Reader;
        private readonly ModelConfig m_Config = new ModelConfig();
        private readonly ProfileConfig m_ProfileConfig = new ProfileConfig();
        private readonly Dictionary<long, DistrictProfile> m_Profiles = new Dictionary<long, DistrictProfile>();

        private ElectionLedger m_Restored;
        private bool m_InGame;
        private GovernmentState m_LastGateState;
        private bool m_PendingPoll;
        private bool m_PendingElection;
        private int m_PendingMonth;
        private int m_ReadMonth;
        private int m_ReadSlices;
        private int m_ReadHouseholds;

        // The latest census, waiting for next month's poll/election.
        private GroupTable m_Census;
        private DistrictInfo[] m_CensusDistricts;
        private string[] m_CensusNames;
        private int m_CensusMonth = int.MinValue;

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

        /// <summary>Each district's current profile (by district id), from the latest read.</summary>
        public IReadOnlyDictionary<long, DistrictProfile> Profiles => m_Profiles;

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
            m_Reader = new VoterReader(this);
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
            ClearCensus();
            m_Profiles.Clear();
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

            if (m_InGame) WarmUpModel();
        }

        /// <summary>
        /// Runs the model once on a tiny table while the loading screen is still up, so the first real election
        /// doesn't pay for compiling it (about 40 ms the first time, under 1 ms after).
        /// </summary>
        private void WarmUpModel()
        {
            var sw = Stopwatch.StartNew();
            var table = new GroupTable(1, new[] { new VoterGroup(0, AgeBand.Adult, 2, 1, JobSector.Office, 10, 50f) });
            var parties = CurrentParties();
            ElectionModel.RunElection(table, parties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, m_Config, 1);
            ElectionModel.RunPoll(table, parties, null, m_Config);
            Mod.Log.Info($"Model warmed up in {sw.ElapsedMilliseconds} ms");
        }

        protected override void OnUpdate()
        {
            if (!m_InGame)
                return;

            int now = NowMonth;
            SyncGate(now);

            if (m_Reader.InProgress)
            {
                if (!m_Reader.AllScheduled)
                {
                    Dependency = m_Reader.ScheduleStep(kSliceHouseholds, Dependency);
                    m_ReadSlices++;
                    return;
                }

                // Every slice was scheduled at least one update ago, so this doesn't wait.
                var table = m_Reader.Finish(out var districts, out var names);
                UpdateProfiles(districts);
                if (m_PendingPoll || m_PendingElection)
                {
                    Process(table, districts, names, m_PendingMonth, m_PendingPoll, m_PendingElection, ReadNote("on the spot"));
                    m_PendingPoll = m_PendingElection = false;
                }
                else
                {
                    m_Census = table;
                    m_CensusDistricts = districts;
                    m_CensusNames = names;
                    m_CensusMonth = m_ReadMonth;
                }
                return;
            }

            var tick = Ledger.OnMonth(now, m_CityHall.IsShutDown);
            if (tick.IsNewMonth)
            {
                DataVersion++;
                if (tick.RunPoll || tick.RunElection)
                {
                    if (m_Census != null && m_CensusMonth == now - 1)
                    {
                        Process(m_Census, m_CensusDistricts, m_CensusNames, now, tick.RunPoll, tick.RunElection, ReadNote("census from last month"));
                        ClearCensus();
                    }
                    else
                    {
                        m_PendingPoll = tick.RunPoll;
                        m_PendingElection = tick.RunElection;
                        m_PendingMonth = now;
                        BeginRead(now);
                    }
                }
                return;
            }

            // Late in the month: take the census next month's poll will use, in the background.
            if (m_CensusMonth != now && m_CityHall.State != GovernmentState.NoCityHall && MonthProgress >= kCensusStart)
                BeginRead(now);
        }

        private void BeginRead(int month)
        {
            m_Reader.Begin();
            m_ReadMonth = month;
            m_ReadSlices = 0;
            m_ReadHouseholds = m_Reader.HouseholdCount;
        }

        private string ReadNote(string how) =>
            $"{m_ReadHouseholds:N0} households read in {m_ReadSlices} worker slices ({how})";

        /// <summary>Refreshes district profiles from the reader's last land-use pass.</summary>
        private void UpdateProfiles(DistrictInfo[] districts)
        {
            var land = m_Reader.LandUse;
            m_Profiles.Clear();
            for (int i = 0; i < districts.Length && i < land.Length; i++)
                if (districts[i].IsSeat) m_Profiles[districts[i].Id] = DistrictProfiles.Classify(land[i], m_ProfileConfig);
            DataVersion++;
        }

        private void ClearCensus()
        {
            m_Census = null;
            m_CensusDistricts = null;
            m_CensusNames = null;
            m_CensusMonth = int.MinValue;
        }

        /// <summary>How far through the current in-game month we are, 0 to 1.</summary>
        private float MonthProgress
        {
            get
            {
                float months = m_Time.normalizedDate * 12f;
                return months - (float)Math.Floor(months);
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
        private void Process(GroupTable table, DistrictInfo[] districts, string[] names, int month, bool poll, bool election, string readNote)
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

            Mod.Log.Info($"{ElectionSchedule.Format(month)}: {(election ? "election + " : "")}{(poll ? "poll" : "")} on {table.TotalVoters:N0} voters ({table.Groups.Count} groups); {readNote}; model {sw.Elapsed.TotalMilliseconds:0.00} ms");
            DataVersion++;
        }

        private ElectionCadence Cadence => Mod.Settings?.Cadence ?? ElectionCadence.EveryYear;

        public Party[] CurrentParties() => Mod.Settings?.GetParties() ?? Setting.TwoPartyPreset;

        // ---------------------------------------------------------------- on demand (dev commands, UI)

        /// <summary>Reads the whole city now and holds an election (plus a poll). Works while paused.</summary>
        public string RunElectionNow()
        {
            m_Reader.Cancel();
            var sw = Stopwatch.StartNew();
            var table = m_Reader.ReadAll(out var districts, out var names);
            UpdateProfiles(districts);
            int before = Ledger.Archive.Count;
            int now = NowMonth;
            Process(table, districts, names, now, poll: true, election: true, readNote: $"full read {sw.Elapsed.TotalMilliseconds:0.0} ms");
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
            UpdateProfiles(districts);
            Process(table, districts, names, NowMonth, poll: true, election: false, readNote: $"full read {sw.Elapsed.TotalMilliseconds:0.0} ms");
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
            ClearCensus();
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
