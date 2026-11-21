#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Colossal.Logging;
using Colossal.Serialization.Entities;
using ElectionAtlas.Model;
using Game;
using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Purpose = Colossal.Serialization.Entities.Purpose;
using Student = Game.Citizens.Student;

namespace ElectionAtlas.Diagnostics
{
    /// <summary>
    /// Debug builds only. Answers the in-game spikes in one play session and writes everything to
    /// ElectionAtlas.Spikes.log. Runs on the main thread with plain EntityManager reads: slow, but it only
    /// runs at load, on month ticks and every 6 months, and never ships in Release builds.
    /// </summary>
    public partial class SpikeLoggerSystem : GameSystemBase
    {
        internal static readonly ILog Log = LogManager.GetLogger("ElectionAtlas.Spikes").SetShowsErrorsInUI(false);

        private const int kWorkerSample = 5000;
        private const int kFullReportEveryMonths = 6;

        private PrefabSystem m_PrefabSystem;
        private NameSystem m_NameSystem;
        private SimulationSystem m_SimulationSystem;
        private TimeSystem m_TimeSystem;

        private EntityQuery m_AdminQuery;
        private EntityQuery m_AdminPrefabQuery;
        private EntityQuery m_WorkerQuery;
        private EntityQuery m_HouseholdQuery;
        private EntityQuery m_DistrictQuery;
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
            m_NameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();

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
            m_HouseholdQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Household>(), ComponentType.ReadOnly<PropertyRenter>(),
                    ComponentType.ReadOnly<HouseholdCitizen>(), ComponentType.ReadOnly<Resources>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<TouristHousehold>(), ComponentType.ReadOnly<CommuterHousehold>(),
                    ComponentType.ReadOnly<MovingAway>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(),
                },
            });
            m_DistrictQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<District>() },
                None = notTempOrDeleted,
            });
            m_TimeDataQuery = GetEntityQuery(ComponentType.ReadOnly<TimeData>());

            Log.Info("Spike logger created (Debug build). Load a city and let it run for an in-game year.");
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
                    string district = EntityManager.HasComponent<CurrentDistrict>(e)
                        ? DistrictName(EntityManager.GetComponentData<CurrentDistrict>(e).m_District)
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

        private JobSector SectorOf(Entity workplace)
        {
            if (workplace == Entity.Null || !EntityManager.Exists(workplace)) return JobSector.Unemployed;
            if (EntityManager.HasComponent<OfficeCompany>(workplace)) return JobSector.Office;
            if (EntityManager.HasComponent<IndustrialCompany>(workplace)) return JobSector.Industrial;
            return JobSector.Commercial; // commercial companies, and for now city-service jobs (spike 4 decides)
        }

        // Spikes 6 and 7, district sanity checks for phase 3, and a preview election on the real city.
        private void ReportCitizensAndPreviewElection()
        {
            var sw = Stopwatch.StartNew();
            uint frame = m_SimulationSystem.frameIndex;
            bool hasTime = !m_TimeDataQuery.IsEmptyIgnoreFilter;
            TimeData timeData = hasTime ? m_TimeDataQuery.GetSingleton<TimeData>() : default;

            // Districts: index 0..n-1 by entity, index n = "Unassigned".
            var districtIndex = new Dictionary<Entity, int>();
            var districtNames = new List<string>();
            using (var districts = m_DistrictQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var d in districts)
                {
                    districtIndex[d] = districtNames.Count;
                    districtNames.Add(DistrictName(d));
                }
            }
            int unassigned = districtNames.Count;
            districtNames.Add("Unassigned");
            int districtCount = districtNames.Count;

            using (var households = m_HouseholdQuery.ToEntityArray(Allocator.Temp))
            {
                // Pass 1: wealth per household (spike 6), cut into quartiles for the bands.
                var wealth = new int[households.Length];
                for (int i = 0; i < households.Length; i++)
                {
                    var hh = EntityManager.GetComponentData<Household>(households[i]);
                    var resources = EntityManager.GetBuffer<Resources>(households[i], true);
                    wealth[i] = EconomyUtils.GetHouseholdTotalWealth(hh, resources);
                }

                var sorted = (int[])wealth.Clone();
                Array.Sort(sorted);
                int[] cuts = { Percentile(sorted, 0.25), Percentile(sorted, 0.50), Percentile(sorted, 0.75) };
                Log.Info($"[spike 6] households={sorted.Length} wealth min={Percentile(sorted, 0)} p10={Percentile(sorted, 0.10)} p25={cuts[0]} p50={cuts[1]} p75={cuts[2]} p90={Percentile(sorted, 0.90)} max={Percentile(sorted, 1)}");
                Log.Info($"[spike 6] suggested wealth band cut points (quartiles): {cuts[0]}, {cuts[1]}, {cuts[2]}");

                // Pass 2: every resident (spike 7) and the voter-group slots for the preview election.
                var counts = new int[districtCount * GroupKey.SlotsPerDistrict];
                var happiness = new long[counts.Length];
                var ageDays = new Dictionary<CitizenAge, List<float>>();
                var byAge = new Dictionary<CitizenAge, int>();
                var residentsByDistrict = new int[districtCount];
                var happinessByDistrict = new long[districtCount];
                int tourists = 0, commuters = 0, students = 0, voters = 0;

                for (int i = 0; i < households.Length; i++)
                {
                    var property = EntityManager.GetComponentData<PropertyRenter>(households[i]).m_Property;
                    int district = unassigned;
                    if (property != Entity.Null && EntityManager.HasComponent<CurrentDistrict>(property))
                    {
                        var d = EntityManager.GetComponentData<CurrentDistrict>(property).m_District;
                        if (d != Entity.Null && districtIndex.TryGetValue(d, out int idx)) district = idx;
                    }

                    int band = wealth[i] <= cuts[0] ? 0 : wealth[i] <= cuts[1] ? 1 : wealth[i] <= cuts[2] ? 2 : 3;

                    var members = EntityManager.GetBuffer<HouseholdCitizen>(households[i], true);
                    for (int m = 0; m < members.Length; m++)
                    {
                        var citizenEntity = members[m].m_Citizen;
                        if (!EntityManager.Exists(citizenEntity) || !EntityManager.HasComponent<Citizen>(citizenEntity)) continue;

                        var c = EntityManager.GetComponentData<Citizen>(citizenEntity);
                        if ((c.m_State & CitizenFlags.Tourist) != 0) { tourists++; continue; }
                        if ((c.m_State & CitizenFlags.Commuter) != 0) { commuters++; continue; }

                        var age = c.GetAge();
                        Increment(byAge, age);
                        if (hasTime)
                        {
                            if (!ageDays.TryGetValue(age, out var list)) ageDays[age] = list = new List<float>();
                            list.Add(c.GetAgeInDays(frame, timeData));
                        }

                        if (age != CitizenAge.Adult && age != CitizenAge.Elderly) continue;

                        voters++;
                        residentsByDistrict[district]++;
                        happinessByDistrict[district] += c.Happiness;

                        JobSector job;
                        if (EntityManager.HasComponent<Worker>(citizenEntity))
                            job = SectorOf(EntityManager.GetComponentData<Worker>(citizenEntity).m_Workplace);
                        else if (age == CitizenAge.Elderly)
                            job = JobSector.Retired;
                        else
                        {
                            if (EntityManager.HasComponent<Student>(citizenEntity)) students++;
                            job = JobSector.Unemployed;
                        }

                        int edu = Math.Min(4, Math.Max(0, c.GetEducationLevel()));
                        var ageBand = age == CitizenAge.Elderly ? AgeBand.Elderly : AgeBand.Adult;
                        int key = GroupKey.Encode(district, ageBand, edu, band, job);
                        counts[key]++;
                        happiness[key] += c.Happiness;
                    }
                }

                Log.Info($"[spike 7] residents by age: {string.Join(", ", byAge.Select(kv => $"{kv.Key}={kv.Value}"))}; skipped tourists={tourists} commuters={commuters}; adult students without a job={students}");
                foreach (var kv in ageDays.OrderBy(k => k.Key))
                {
                    var days = kv.Value;
                    days.Sort();
                    Log.Info($"[spike 7]   {kv.Key} age in days: min={P(days, 0):0.0} p5={P(days, 0.05):0.0} p50={P(days, 0.5):0.0} p95={P(days, 0.95):0.0} max={P(days, 1):0.0}");
                }

                Log.Info($"[phase 3] voting-age residents={voters} in {districtCount - 1} district(s) + Unassigned");
                for (int d = 0; d < districtCount; d++)
                {
                    if (residentsByDistrict[d] == 0) continue;
                    Log.Info($"[phase 3]   {districtNames[d]}: voters={residentsByDistrict[d]} avgHappiness={(double)happinessByDistrict[d] / residentsByDistrict[d]:0.0}");
                }

                PreviewElection(districtCount, districtNames, counts, happiness);
            }

            Log.Info($"[timing] citizen pass took {sw.ElapsedMilliseconds} ms (main thread, unoptimized; the real reader is a Burst job)");
        }

        private void PreviewElection(int districtCount, List<string> districtNames, int[] counts, long[] happiness)
        {
            var table = GroupTable.FromSlots(districtCount, counts, happiness);
            var parties = new[]
            {
                new Party("left", "Left", 0xCC3333, new Position(-0.6f, -0.4f)),
                new Party("right", "Right", 0x3355CC, new Position(0.6f, 0.4f)),
            };
            var cfg = new ModelConfig();

            var sw = Stopwatch.StartNew();
            var result = ElectionModel.RunElection(table, parties, null, GoverningRule.PopularVote, TurnoutPreset.UsGeneral, cfg, 1);
            var poll = ElectionModel.RunPoll(table, parties, null, cfg);
            long ms = sw.ElapsedMilliseconds;

            Log.Info($"[preview] {table.Groups.Count} voter groups, {table.TotalVoters} voters; model ran in {ms} ms");
            Log.Info($"[preview] citywide turnout={result.Turnout:P1} Left={result.Shares[0]:P1} Right={result.Shares[1]:P1} governing={result.GoverningPartyId} approval(happiness)={poll.Approval:0.0}");
            foreach (var d in result.Districts)
            {
                if (d.Eligible == 0) continue;
                string winner = d.WinnerIndex >= 0 ? parties[d.WinnerIndex].Name : "-";
                Log.Info($"[preview]   {districtNames[d.District]}: winner={winner} margin={d.Margin * 100f:0.0} pts turnout={d.Turnout:P1} votes L={d.PartyVotes[0]} R={d.PartyVotes[1]}");
            }
        }

        // ---------------------------------------------------------------- helpers

        private string PrefabName(Entity prefab)
        {
            return m_PrefabSystem.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null ? p.name : $"<prefab {prefab.Index}>";
        }

        private string DistrictName(Entity district)
        {
            if (district == Entity.Null) return "(none)";
            try { return m_NameSystem.GetRenderedLabelName(district); }
            catch { return $"<district {district.Index}>"; }
        }

        private static void Increment<T>(Dictionary<T, int> map, T key)
        {
            map.TryGetValue(key, out int n);
            map[key] = n + 1;
        }

        private static int Percentile(int[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            return sorted[(int)Math.Round(p * (sorted.Length - 1))];
        }

        private static float P(List<float> sorted, double p)
        {
            if (sorted.Count == 0) return 0f;
            return sorted[(int)Math.Round(p * (sorted.Count - 1))];
        }
    }
}
#endif
