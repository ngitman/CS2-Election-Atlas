using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using ElectionAtlas.Model;
using Game;
using Game.Assets;
using Game.City;
using Game.Simulation;
using Unity.Collections;
using Deleted = Game.Common.Deleted;
using District = Game.Areas.District;
using Entity = Unity.Entities.Entity;
using Temp = Game.Tools.Temp;
using TimeData = Game.Common.TimeData;

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
        internal enum ResultNotice { None, Immediate, Scheduled }

        private const int kIdleInterval = 512;
        private const int kReadInterval = 16;
        private const int kSliceHouseholds = 1024;  // about 0.2 ms of worker time per slice
        private const float kCensusStart = 0.75f;  // census starts once the month is this far along

        private CityHallSystem m_CityHall;
        private CityConfigurationSystem m_CityConfiguration;
        private TimeSystem m_Time;
        private SimulationSystem m_Simulation;
        private Unity.Entities.EntityQuery m_TimeDataQuery;
        private VoterReader m_Reader;
        private readonly ModelConfig m_Config = new ModelConfig();
        private readonly ProfileConfig m_ProfileConfig = new ProfileConfig();
        private readonly Dictionary<long, DistrictProfile> m_Profiles = new Dictionary<long, DistrictProfile>();
        private Unity.Entities.EntityQuery m_DistrictQuery;

        // Format: the save's format version. Districts: its district ids already mapped (v3+), or null to match by name.
        private (Hash128 Source, ElectionLedger Ledger, int Format, DistrictRemap Districts)? m_Restore;
        private Hash128 m_LoadedSource;
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
        public bool HasLoadedCity => m_InGame;
        public string CityName => m_InGame ? m_CityConfiguration.cityName : null;
        public string LoadedSaveName { get; private set; }

        /// <summary>Bumped whenever something the panel shows changes, so the UI only rebuilds then.</summary>
        public int DataVersion { get; private set; }

        /// <summary>The latest poll (in memory only), with district names in the same order.</summary>
        public PollResult LatestPoll { get; private set; }
        public string[] LatestPollDistrictNames { get; private set; }
        public DistrictInfo[] LatestPollDistricts { get; private set; }

        /// <summary>Set when an election result comes in; the UI opens the panel and clears it.</summary>
        internal ResultNotice PendingResultNotice { get; set; }

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
        public bool IsCountingElection => IsReading && m_PendingElection;
        // The HUD clock uses elapsed simulation frames. The lighting clock can include a time-of-day offset.
        internal uint CityClockTicks => m_Simulation.frameIndex - m_TimeDataQuery.GetSingleton<TimeData>().m_FirstFrame;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => IsReading ? kReadInterval : kIdleInterval;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_CityHall = World.GetOrCreateSystemManaged<CityHallSystem>();
            m_CityConfiguration = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_Time = World.GetOrCreateSystemManaged<TimeSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeDataQuery = GetEntityQuery(Unity.Entities.ComponentType.ReadOnly<TimeData>());
            m_Reader = new VoterReader(this);
            m_DistrictQuery = EntityManager.CreateEntityQuery(new Unity.Entities.EntityQueryDesc
            {
                All = new[] { Unity.Entities.ComponentType.ReadOnly<District>() },
                None = new[] { Unity.Entities.ComponentType.ReadOnly<Temp>(), Unity.Entities.ComponentType.ReadOnly<Deleted>() },
            });
        }

        /// <summary>The city's districts as entities (not ones being drawn or deleted).</summary>
        public NativeArray<Entity> DistrictEntities(Allocator allocator) => m_DistrictQuery.ToEntityArray(allocator);

        protected override void OnDestroy()
        {
            m_Reader?.Dispose();
            base.OnDestroy();
        }

        /// <summary>
        /// Stage a ledger for the save asset the game is reading. <paramref name="districts"/> is the mapping already
        /// applied to its district ids (format v3+), or null when the save has none and districts are matched by name.
        /// </summary>
        public void RestoreFromSave(Context context, ElectionLedger ledger, int format, DistrictRemap districts)
        {
            m_Restore = ledger != null && context.purpose == Purpose.LoadGame ? (context.instigatorGuid, ledger, format, districts) : null;
        }

        /// <summary>Called by ElectionStoreSystem when the game saves.</summary>
        public ElectionLedger LedgerForSave()
        {
            Ledger.State = m_CityHall.State;
            return Ledger;
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            m_InGame = false;
            m_Restore = null;
            m_LoadedSource = Hash128.Empty;
            LoadedSaveName = null;
            Ledger = new ElectionLedger();
            m_Reader.Cancel();
            ClearCensus();
            m_Profiles.Clear();
            m_PendingPoll = m_PendingElection = false;
            DevMonthOffset = 0;
            LatestPoll = null;
            LatestPollDistrictNames = null;
            LatestPollDistricts = null;
            PendingResultNotice = ResultNotice.None;
            DataVersion++;
        }

        protected override void OnGameLoaded(Context context)
        {
            base.OnGameLoaded(context);
            if (context.purpose == Purpose.LoadGame) m_LoadedSource = context.instigatorGuid;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_InGame = mode == GameMode.Game;
            bool loadedSave = m_InGame && purpose == Purpose.LoadGame && m_LoadedSource != Hash128.Empty;
            var restore = m_Restore;
            m_Restore = null;
            bool restored = loadedSave && restore.HasValue && restore.Value.Source == m_LoadedSource;
            if (restored)
            {
                Ledger = restore.Value.Ledger;
                try
                {
                    LogDistrictRemap(restore.Value.Format, restore.Value.Districts ?? MatchDistrictsByName(Ledger));
                }
                catch (Exception e)
                {
                    // RemapDistricts swaps in the copies last, so a failure leaves the ledger as it was read.
                    Mod.Log.Error(e, "District IDs couldn't be remapped; past elections may not line up with the map");
                }
            }
            else if (restore.HasValue)
                Mod.Log.Warn($"Election data for save asset {restore.Value.Source} was ignored while loading {m_LoadedSource} ({purpose})");

            if (loadedSave &&
                AssetDatabase.global.TryGetAsset(m_LoadedSource, out IAssetData asset) && asset is SaveGameMetadata save)
                LoadedSaveName = save.name;

            m_CityHall.ResetForLoad(Ledger.State);
            m_LastGateState = Ledger.State;
            DataVersion++;

            if (!m_InGame) return;
            string world = $"city '{CityName}', loaded save '{LoadedSaveName ?? "none"}' ({m_LoadedSource})";
            if (restored)
                Mod.Log.Info($"Election data loaded: {Ledger.State}, {Ledger.Archive.Count} election(s), {Ledger.Polls.Count} poll(s), next {ElectionSchedule.Format(Ledger.NextElectionMonth)}; {world}");
            else
                Mod.Log.Info($"No election history restored for {world}");
            WarmUpModel();
        }

        /// <summary>
        /// Saves before format v3 hold district ids from entities the game has since renumbered. Matches them to the
        /// loaded districts by name (names unique on both sides only) and rewrites the ledger's ids; the next save
        /// writes the v3 district table, so this only runs once per city.
        /// </summary>
        private DistrictRemap MatchDistrictsByName(ElectionLedger ledger)
        {
            DistrictInfo[] current;
            using (var entities = DistrictEntities(Allocator.Temp))
            {
                current = new DistrictInfo[entities.Length];
                for (int i = 0; i < current.Length; i++)
                    current[i] = new DistrictInfo(DistrictKey.Of(entities[i]), m_Reader.DistrictName(entities[i]));
            }

            var remap = DistrictRemap.ByUniqueName(ledger, current);
            ledger.RemapDistricts(remap);
            return remap;
        }

        private static void LogDistrictRemap(int format, DistrictRemap remap)
        {
            string how = format >= 3 ? $"v{format} table" : $"v{format} save, name match";
            string rest = remap.Unmatched > 0 ? $"; {remap.Unmatched} unmatched, kept as former districts" : "";
            Mod.Log.Info($"District IDs remapped: {remap.Matched} of {remap.Total} ({how}){rest}");
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
                    Process(table, districts, names, m_PendingMonth, m_PendingPoll, m_PendingElection, ReadNote("on the spot"), scheduled: true);
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
                    if (!TryProcessCensus(now, tick.RunPoll, tick.RunElection, scheduled: true))
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

        private bool TryProcessCensus(int month, bool poll, bool election, bool scheduled)
        {
            if (m_Census == null || m_CensusMonth != month - 1)
                return false;

            Process(m_Census, m_CensusDistricts, m_CensusNames, month, poll, election, ReadNote("census from last month"), scheduled);
            ClearCensus();
            return true;
        }

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
        private void Process(GroupTable table, DistrictInfo[] districts, string[] names, int month, bool poll, bool election,
            string readNote, bool scheduled)
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
                PendingResultNotice = scheduled ? ResultNotice.Scheduled : ResultNotice.Immediate;

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

        private void ReadAndProcessNow(int month, bool poll, bool election)
        {
            var sw = Stopwatch.StartNew();
            var table = m_Reader.ReadAll(out var districts, out var names);
            UpdateProfiles(districts);
            Process(table, districts, names, month, poll, election, $"full read {sw.Elapsed.TotalMilliseconds:0.0} ms", scheduled: false);
        }

        /// <summary>Reads the whole city now and holds an election (plus a poll). Works while paused.</summary>
        public string RunElectionNow()
        {
            var sw = Stopwatch.StartNew();
            int before = Ledger.Archive.Count;
            int now = NowMonth;
            ReadAndProcessNow(now, poll: true, election: true);
            m_PendingPoll = m_PendingElection = false;
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
            var sw = Stopwatch.StartNew();
            ReadAndProcessNow(NowMonth, poll: true, election: false);
            if (m_PendingElection) BeginRead(m_PendingMonth);
            else m_PendingPoll = false;
            if (LatestPoll == null)
                return "No poll: there are no voting-age residents yet.";
            var parties = CurrentParties();
            return $"Poll ({ElectionSchedule.Format(NowMonth)}): approval {LatestPoll.Approval:0.0} · " +
                string.Join(" · ", parties.Select((p, i) => $"{p.Name} {LatestPoll.Intention[i]:P1}")) + $" · {sw.ElapsedMilliseconds} ms";
        }

#if DEV_TOOLS
        public string DevAdvanceMonths(int months)
        {
            if (!m_InGame) return "Load a city first.";
            SyncGate(NowMonth);
            return AdvanceTestClock(months);
        }

        public string DevSkipToElection()
        {
            if (!m_InGame) return "Load a city first.";
            int now = NowMonth;
            SyncGate(now);
            int until = ElectionSchedule.MonthsUntil(now, Ledger.NextElectionMonth);
            if (until < 0 || !GovernmentGate.PollsRun(Ledger.State))
                return "No election is scheduled (is there a City Hall?).";
            return AdvanceTestClock(until);
        }

        /// <summary>Exercises the calendar immediately, including when the game's simulation is paused.</summary>
        private string AdvanceTestClock(int months)
        {
            DevMonthOffset += months;
            DataVersion++;
            int now = NowMonth;
            var tick = Ledger.OnMonth(now, m_CityHall.IsShutDown);
            string date = $"Test date: {ElectionSchedule.Format(now)} (offset +{DevMonthOffset}).";
            if (!GovernmentGate.PollsRun(Ledger.State)) return $"{date} Elections and polls are paused without a City Hall.";

            // A previous simulation update may already have advanced the ledger to a due election.
            bool election = tick.RunElection || m_PendingElection || ElectionSchedule.IsDue(now, Ledger.NextElectionMonth);
            bool poll = tick.RunPoll || m_PendingPoll || election;
            if (!poll) return date;

            int before = Ledger.Archive.Count;
            var previousPoll = LatestPoll;
            m_PendingPoll = poll;
            m_PendingElection = election;
            m_PendingMonth = now;
            if (TryProcessCensus(now, poll, election, scheduled: false)) m_Reader.Cancel();
            else ReadAndProcessNow(now, poll, election);
            m_PendingPoll = m_PendingElection = false;

            if (Ledger.Archive.Count > before) return $"{date} Election #{Ledger.Archive.Latest.Number} held.";
            if (!ReferenceEquals(LatestPoll, previousPoll)) return $"{date} Poll complete. Approval {LatestPoll.Approval:0.0}.";
            return $"{date} There are no voting-age residents yet.";
        }

        public string DevReset()
        {
            m_Reader.Cancel();
            ClearCensus();
            m_PendingPoll = m_PendingElection = false;
            var state = m_CityHall.State;
            Ledger = new ElectionLedger();
            PendingResultNotice = ResultNotice.None;
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
