#if DEV_TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Colossal.Logging;
using ElectionAtlas.Dev;
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
        private MainThreadVoterReader m_Reader;

        private EntityQuery m_AdminQuery;
        private EntityQuery m_AdminPrefabQuery;
        private EntityQuery m_WorkerQuery;

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
            m_Reader = new MainThreadVoterReader(World);

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
        private void ReportCitizensAndPreviewElection()
        {
            var r = m_Reader.Read(collectAges: true);
            var w = r.WealthSorted;

            Log.Info($"[spike 6] households={r.Households} wealth min={P(w, 0)} p10={P(w, 0.10)} p25={r.WealthCuts[0]} p50={r.WealthCuts[1]} p75={r.WealthCuts[2]} p90={P(w, 0.90)} max={P(w, 1)}");
            Log.Info($"[spike 6] suggested wealth band cut points (quartiles): {r.WealthCuts[0]}, {r.WealthCuts[1]}, {r.WealthCuts[2]}");

            Log.Info($"[spike 7] residents by age: {string.Join(", ", r.ByAge.Select(kv => $"{kv.Key}={kv.Value}"))}; skipped tourists={r.Tourists} commuters={r.Commuters}; adult students without a job={r.AdultStudents}");
            foreach (var kv in r.AgeDays.OrderBy(k => k.Key))
            {
                var days = kv.Value;
                days.Sort();
                Log.Info($"[spike 7]   {kv.Key} age in days: min={P(days, 0):0.0} p5={P(days, 0.05):0.0} p50={P(days, 0.5):0.0} p95={P(days, 0.95):0.0} max={P(days, 1):0.0}");
            }

            Log.Info($"[phase 3] voting-age residents={r.Voters} in {r.DistrictNames.Length - 1} district(s) + Unassigned");
            for (int d = 0; d < r.DistrictNames.Length; d++)
            {
                if (r.VotersByDistrict[d] == 0) continue;
                Log.Info($"[phase 3]   {r.DistrictNames[d]}: voters={r.VotersByDistrict[d]} avgHappiness={(double)r.HappinessByDistrict[d] / r.VotersByDistrict[d]:0.0}");
            }

            PreviewElection(r);
            Log.Info($"[timing] citizen pass took {r.ElapsedMs} ms (main thread, unoptimized; the real reader is a Burst job)");
        }

        private static void PreviewElection(VoterReading r)
        {
            var parties = DevParties.Default;
            var cfg = new ModelConfig();

            var sw = Stopwatch.StartNew();
            var result = ElectionModel.RunElection(r.Table, parties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, cfg, 1);
            var poll = ElectionModel.RunPoll(r.Table, parties, null, cfg);
            long ms = sw.ElapsedMilliseconds;

            Log.Info($"[preview] {r.Table.Groups.Count} voter groups, {r.Table.TotalVoters} voters; model ran in {ms} ms");
            Log.Info($"[preview] citywide turnout={result.Turnout:P1} {parties[0].Name}={result.Shares[0]:P1} {parties[1].Name}={result.Shares[1]:P1} governing={result.GoverningPartyId} approval(happiness)={poll.Approval:0.0}");
            foreach (var d in result.Districts)
            {
                if (d.Eligible == 0) continue;
                string winner = d.WinnerIndex >= 0 ? parties[d.WinnerIndex].Name : "-";
                Log.Info($"[preview]   {r.DistrictNames[d.District]}: winner={winner} margin={d.Margin * 100f:0.0} pts turnout={d.Turnout:P1} votes {parties[0].Name}={d.PartyVotes[0]} {parties[1].Name}={d.PartyVotes[1]}");
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
