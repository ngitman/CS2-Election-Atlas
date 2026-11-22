#if DEV_TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Colossal.Logging;
using ElectionAtlas.Dev;
using ElectionAtlas.Systems;
using ElectionAtlas.Model;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Purpose = Colossal.Serialization.Entities.Purpose;

namespace ElectionAtlas.Diagnostics
{
    /// <summary>
    /// Dev builds only. Answers the in-game spikes in one play session and writes everything to
    /// ElectionAtlas.Spikes.log. Runs on the main thread with plain EntityManager reads: slow, but it only
    /// runs at load, every 6 in-game months, and on demand from the dev commands.
    /// </summary>
    public partial class SpikeLoggerSystem : GameSystemBase
    {
        internal static readonly ILog Log = LogManager.GetLogger("ElectionAtlas.Spikes").SetShowsErrorsInUI(false);

        private const int kWorkerSample = 5000;
        private const int kFullReportEveryMonths = 6;

        private PrefabSystem m_PrefabSystem;
        private TimeSystem m_TimeSystem;
        private SimulationSystem m_SimulationSystem;
        private VoterReader m_Reader; // its own instance: never the one the census is using

        private EntityQuery m_AdminQuery;
        private EntityQuery m_AdminPrefabQuery;
        private EntityQuery m_WorkerQuery;
        private EntityQuery m_CitizenQuery;
        private EntityQuery m_TimeDataQuery;

        private bool m_InGame;
        private bool m_PendingFullReport;
        private int m_LastYear = int.MinValue;
        private int m_LastMonth = -1;
        private int m_LastAdminCount = -1;
        private int m_MonthsSeen;
        private bool m_Failed;

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 1024;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_Reader = new VoterReader(this);

            var notTempOrDeleted = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() };
            m_AdminQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<AdminBuilding>(), ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = notTempOrDeleted,
            });
            m_AdminPrefabQuery = GetEntityQuery(ComponentType.ReadOnly<AdminBuildingData>(), ComponentType.ReadOnly<PrefabData>());
            m_WorkerQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Worker>(), ComponentType.ReadOnly<Citizen>() },
                None = notTempOrDeleted,
            });

            m_CitizenQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<HouseholdMember>() },
                None = notTempOrDeleted,
            });
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());

            Log.Info("Spike logger created (dev build). Load a city and let it run for an in-game year.");
            try { LogModelAssembly(); }
            catch (Exception ex) { Log.Error($"[spike 8] ElectionAtlas.Model failed to load: {ex.GetType().Name}: {ex.Message}"); }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_InGame = mode == GameMode.Game;
            m_PendingFullReport = m_InGame;
            m_LastYear = int.MinValue;
            m_LastMonth = -1;
            m_LastAdminCount = -1;
            m_MonthsSeen = 0;
            m_Failed = false;
            Log.Info($"Game loading complete: purpose={purpose} mode={mode}");
        }

        protected override void OnUpdate()
        {
            if (!m_InGame || m_Failed)
                return;

            try
            {
                if (m_PendingFullReport)
                {
                    m_PendingFullReport = false;
                    FullReport("city loaded");
                }

                CheckCalendar();
                CheckAdminBuildings();
            }
            catch (Exception ex)
            {
                m_Failed = true; // stop after the first failure instead of flooding the log
                Log.Error($"Spike logger stopped after an error: {ex}");
            }
        }

        /// <summary>Runs a full report immediately, even while paused. Returns how long it took.</summary>
        public long RunFullReportNow(string reason)
        {
            var sw = Stopwatch.StartNew();
            FullReport(reason);
            return sw.ElapsedMilliseconds;
        }

        // ---------------------------------------------------------------- reports

        private void FullReport(string reason)
        {
            var sw = Stopwatch.StartNew();
            Log.Info($"===== Full report ({reason}) =====");
            ReportAdminPrefabs();
            ReportAdminBuildings();
            ReportWorkplaces();
            ReportCitizensAndPreviewElection();
            Log.Info($"===== Full report done in {sw.ElapsedMilliseconds} ms =====");
        }

        private void LogModelAssembly()
        {
            var asm = typeof(ElectionModel).Assembly;
            Log.Info($"[spike 8] ElectionAtlas.Model {asm.GetName().Version} loaded from {asm.Location}");
        }

        // Spike 5: does normalizedDate give 12 distinct months per year?
        private void CheckCalendar()
        {
            int year = m_TimeSystem.year;
            float date = m_TimeSystem.normalizedDate;
            int month = Math.Min(11, Math.Max(0, (int)Math.Floor(date * 12f)));
            if (year == m_LastYear && month == m_LastMonth)
                return;

            bool first = m_LastMonth < 0;
            m_LastYear = year;
            m_LastMonth = month;
            Log.Info($"[spike 5] calendar year={year} month={month} normalizedDate={date:0.0000} daysPerYear={m_TimeSystem.daysPerYear} frame={m_SimulationSystem.frameIndex}");

            if (first)
                return;

            m_MonthsSeen++;
            if (m_MonthsSeen % kFullReportEveryMonths == 0)
                FullReport($"{m_MonthsSeen} months in");
        }

        // Spike 1: which placed buildings carry AdminBuilding?
        private void CheckAdminBuildings()
        {
            int count = m_AdminQuery.CalculateEntityCount();
            if (count == m_LastAdminCount)
                return;

            m_LastAdminCount = count;
            ReportAdminBuildings();
        }

        private void ReportAdminBuildings()
        {
            using (var entities = m_AdminQuery.ToEntityArray(Allocator.Temp))
            {
                Log.Info($"[spike 1] {entities.Length} placed building(s) carry AdminBuilding");
                foreach (var e in entities)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    string district = EntityManager.HasComponent<Game.Areas.CurrentDistrict>(e)
                        ? m_Reader.DistrictName(EntityManager.GetComponentData<Game.Areas.CurrentDistrict>(e).m_District)
                        : "(no CurrentDistrict)";
                    Log.Info($"[spike 1]   entity {e.Index}:{e.Version} prefab='{PrefabName(prefab)}' district={district}");
                }
            }
        }

        private void ReportAdminPrefabs()
        {
            using (var prefabs = m_AdminPrefabQuery.ToEntityArray(Allocator.Temp))
            {
                var names = new List<string>();
                foreach (var p in prefabs) names.Add(PrefabName(p));
                names.Sort(StringComparer.Ordinal);
                Log.Info($"[spike 1] {names.Count} prefab(s) have AdminBuildingData: {string.Join(", ", names)}");
            }
        }

        // Spike 4: which company components do workplaces carry?
        private void ReportWorkplaces()
        {
            var kinds = new Dictionary<string, int>();
            var noCompanyPrefabs = new Dictionary<string, int>();
            int missing = 0, sampled = 0;

            using (var workers = m_WorkerQuery.ToEntityArray(Allocator.Temp))
            {
                int stride = Math.Max(1, workers.Length / kWorkerSample);
                for (int i = 0; i < workers.Length; i += stride)
                {
                    sampled++;
                    var workplace = EntityManager.GetComponentData<Worker>(workers[i]).m_Workplace;
                    if (workplace == Entity.Null || !EntityManager.Exists(workplace))
                    {
                        missing++;
                        continue;
                    }

                    string kind = WorkplaceKind(workplace);
                    Increment(kinds, kind);
                    if (kind == "none")
                    {
                        string prefab = EntityManager.HasComponent<PrefabRef>(workplace)
                            ? PrefabName(EntityManager.GetComponentData<PrefabRef>(workplace).m_Prefab)
                            : "(no PrefabRef)";
                        string building = EntityManager.HasComponent<Building>(workplace) ? "building" : "not a building";
                        Increment(noCompanyPrefabs, $"{prefab} [{building}]");
                    }
                }

                Log.Info($"[spike 4] workers={workers.Length} sampled={sampled} missingWorkplace={missing}");
            }

            foreach (var kv in kinds.OrderByDescending(k => k.Value))
                Log.Info($"[spike 4]   workplace {kv.Key}: {kv.Value}");
            foreach (var kv in noCompanyPrefabs.OrderByDescending(k => k.Value).Take(20))
                Log.Info($"[spike 4]   no company component: {kv.Value} x {kv.Key}");
        }

        private string WorkplaceKind(Entity workplace)
        {
            var parts = new List<string>(3);
            if (EntityManager.HasComponent<CommercialCompany>(workplace)) parts.Add("commercial");
            if (EntityManager.HasComponent<IndustrialCompany>(workplace)) parts.Add("industrial");
            if (EntityManager.HasComponent<OfficeCompany>(workplace)) parts.Add("office");
            return parts.Count == 0 ? "none" : string.Join("+", parts);
        }

        // Spikes 6 and 7, district sanity checks for phase 3, and a preview election on the real city.
        // Uses the same reader as real elections, so what it reports is what elections see.
        private void ReportCitizensAndPreviewElection()
        {
            var sw = Stopwatch.StartNew();
            var table = m_Reader.ReadAll(out _, out string[] names);
            long readMs = sw.ElapsedMilliseconds;
            var st = m_Reader.Stats;
            var w = st.WealthSorted;

            Log.Info($"[spike 6] households={st.Households} wealth min={P(w, 0)} p10={P(w, 0.10)} p25={P(w, 0.25)} p50={P(w, 0.5)} p75={P(w, 0.75)} p90={P(w, 0.90)} max={P(w, 1)}");
            Log.Info($"[spike 7] residents by age: Child={st.Children}, Teen={st.Teens}, Adult={st.Adults}, Elderly={st.Elderly}; skipped tourists={st.Tourists} commuters={st.Commuters}");
            ReportAgeSample();

            var voters = new long[table.DistrictCount];
            var happiness = new double[table.DistrictCount];
            foreach (var g in table.Groups)
            {
                voters[g.District] += g.Count;
                happiness[g.District] += (double)g.AvgHappiness * g.Count;
            }
            Log.Info($"[phase 3] voting-age residents={table.TotalVoters} in {names.Length - 1} district(s) + {DistrictInfo.OutsideName}");
            for (int d = 0; d < names.Length; d++)
            {
                if (voters[d] == 0) continue;
                Log.Info($"[phase 3]   {names[d]}: voters={voters[d]} avgHappiness={happiness[d] / voters[d]:0.0}");
            }

            var land = m_Reader.LandUse;
            var cfg = new ProfileConfig();
            for (int d = 0; d < names.Length - 1 && d < land.Length; d++)
            {
                var l = land[d];
                Log.Info($"[profiles] {names[d]}: {DistrictProfiles.Classify(l, cfg)} | households={l.Households} area={l.AreaM2 / 10000f:0.0} ha " +
                    $"({l.HouseholdsPerHectare:0.0}/ha) homes/cell={l.HomeDensity:0.00} jobs={l.Jobs} (industrial {l.IndustrialJobs}, office {l.OfficeJobs}, commercial {l.CommercialJobs})");
            }

            PreviewElection(table, names);
            Log.Info($"[timing] full read took {readMs} ms in one go (scheduled reads are spread over time)");
        }

        /// <summary>
        /// Spike 7's age-threshold question (where the game's Teen/Adult/Elderly bands start, in days) and how many
        /// adults are students without a job. A sample, not a census: this is calibration, not voting.
        /// </summary>
        private void ReportAgeSample()
        {
            if (m_TimeDataQuery.IsEmptyIgnoreFilter) return;
            var timeData = m_TimeDataQuery.GetSingleton<TimeData>();
            uint frame = m_SimulationSystem.frameIndex;
            var days = new Dictionary<CitizenAge, List<float>>();
            int adultStudents = 0;

            using (var citizens = m_CitizenQuery.ToEntityArray(Allocator.Temp))
            {
                int step = Math.Max(1, citizens.Length / kWorkerSample);
                for (int i = 0; i < citizens.Length; i += step)
                {
                    var c = EntityManager.GetComponentData<Citizen>(citizens[i]);
                    if ((c.m_State & (CitizenFlags.Tourist | CitizenFlags.Commuter)) != 0) continue;
                    var age = c.GetAge();
                    if (!days.TryGetValue(age, out var list)) days[age] = list = new List<float>();
                    list.Add(c.GetAgeInDays(frame, timeData));
                    if (age == CitizenAge.Adult && !EntityManager.HasComponent<Worker>(citizens[i])
                        && EntityManager.HasComponent<Game.Citizens.Student>(citizens[i]))
                        adultStudents++;
                }
            }

            foreach (var kv in days.OrderBy(k => k.Key))
            {
                var list = kv.Value;
                list.Sort();
                Log.Info($"[spike 7]   {kv.Key} age in days (sample): min={P(list, 0):0.0} p5={P(list, 0.05):0.0} p50={P(list, 0.5):0.0} p95={P(list, 0.95):0.0} max={P(list, 1):0.0}");
            }
            Log.Info($"[spike 7] adult students without a job in the sample: {adultStudents}");
        }

        private static void PreviewElection(GroupTable table, string[] names)
        {
            var parties = DevParties.Default;
            var cfg = new ModelConfig();

            var sw = Stopwatch.StartNew();
            var result = ElectionModel.RunElection(table, parties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, cfg, 1);
            var poll = ElectionModel.RunPoll(table, parties, null, cfg);
            long ms = sw.ElapsedMilliseconds;

            Log.Info($"[preview] {table.Groups.Count} voter groups, {table.TotalVoters} voters; model ran in {ms} ms");
            Log.Info($"[preview] citywide turnout={result.Turnout:P1} {parties[0].Name}={result.Shares[0]:P1} {parties[1].Name}={result.Shares[1]:P1} governing={result.GoverningPartyId} approval(happiness)={poll.Approval:0.0}");
            foreach (var d in result.Districts)
            {
                if (d.Eligible == 0) continue;
                string winner = d.WinnerIndex >= 0 ? parties[d.WinnerIndex].Name : "-";
                Log.Info($"[preview]   {names[d.District]}: winner={winner} margin={d.Margin * 100f:0.0} pts turnout={d.Turnout:P1} votes {parties[0].Name}={d.PartyVotes[0]} {parties[1].Name}={d.PartyVotes[1]}");
            }
        }

        // ---------------------------------------------------------------- helpers

        private string PrefabName(Entity prefab)
        {
            return m_PrefabSystem.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null ? p.name : $"<prefab {prefab.Index}>";
        }

        private static void Increment<T>(Dictionary<T, int> map, T key)
        {
            map.TryGetValue(key, out int n);
            map[key] = n + 1;
        }

        private static int P(int[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[(int)Math.Round(p * (sorted.Length - 1))];

        private static float P(List<float> sorted, double p) => sorted.Count == 0 ? 0f : sorted[(int)Math.Round(p * (sorted.Count - 1))];
    }
}
#endif
