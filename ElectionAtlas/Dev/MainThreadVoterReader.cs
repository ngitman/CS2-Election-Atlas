#if DEV_TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using ElectionAtlas.Model;
using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Student = Game.Citizens.Student;

namespace ElectionAtlas.Dev
{
    /// <summary>Everything one read of the city produced.</summary>
    public sealed class VoterReading
    {
        public GroupTable Table;
        public string[] DistrictNames;          // index = district index; the last one is "Unassigned"
        public DistrictInfo[] Districts;        // same order; Id packs entity index and version (-1 = Unassigned)
        public int Households;
        public int[] WealthSorted;
        public int[] WealthCuts;                // 3 cut points (quartiles) for the 4 bands
        public Dictionary<CitizenAge, int> ByAge = new Dictionary<CitizenAge, int>();
        public Dictionary<CitizenAge, List<float>> AgeDays = new Dictionary<CitizenAge, List<float>>();
        public int Tourists, Commuters, AdultStudents, Voters;
        public int[] VotersByDistrict;
        public long[] HappinessByDistrict;
        public long ElapsedMs;
    }

    /// <summary>
    /// Dev builds only. Reads every resident into voter groups on the main thread with plain EntityManager
    /// calls: slow (tens of ms in a mid-size city) but simple. The spike logger and the dev commands share it;
    /// phase 3 replaces it with the Burst reader for real elections.
    /// </summary>
    public sealed class MainThreadVoterReader
    {
        private readonly EntityManager m_EntityManager;
        private readonly NameSystem m_NameSystem;
        private readonly SimulationSystem m_SimulationSystem;
        private readonly EntityQuery m_HouseholdQuery;
        private readonly EntityQuery m_DistrictQuery;
        private readonly EntityQuery m_TimeDataQuery;

        public MainThreadVoterReader(World world)
        {
            m_EntityManager = world.EntityManager;
            m_NameSystem = world.GetOrCreateSystemManaged<NameSystem>();
            m_SimulationSystem = world.GetOrCreateSystemManaged<SimulationSystem>();

            m_HouseholdQuery = m_EntityManager.CreateEntityQuery(new EntityQueryDesc
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
            m_DistrictQuery = m_EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<District>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_TimeDataQuery = m_EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TimeData>());
        }

        public string DistrictName(Entity district)
        {
            if (district == Entity.Null) return "(none)";
            try { return m_NameSystem.GetRenderedLabelName(district); }
            catch { return $"<district {district.Index}>"; }
        }

        public VoterReading Read(bool collectAges)
        {
            var sw = Stopwatch.StartNew();
            var em = m_EntityManager;
            var r = new VoterReading();

            uint frame = m_SimulationSystem.frameIndex;
            bool hasTime = collectAges && !m_TimeDataQuery.IsEmptyIgnoreFilter;
            TimeData timeData = hasTime ? m_TimeDataQuery.GetSingleton<TimeData>() : default;

            // Districts: index 0..n-1 by entity, index n = "Unassigned".
            var districtIndex = new Dictionary<Entity, int>();
            var names = new List<string>();
            var infos = new List<DistrictInfo>();
            using (var districts = m_DistrictQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var d in districts)
                {
                    districtIndex[d] = names.Count;
                    string name = DistrictName(d);
                    names.Add(name);
                    infos.Add(new DistrictInfo(((long)d.Version << 32) | (uint)d.Index, name));
                }
            }
            int unassigned = names.Count;
            names.Add("Unassigned");
            infos.Add(new DistrictInfo(-1, "Unassigned"));
            int districtCount = names.Count;

            r.DistrictNames = names.ToArray();
            r.Districts = infos.ToArray();
            r.VotersByDistrict = new int[districtCount];
            r.HappinessByDistrict = new long[districtCount];

            var counts = new int[districtCount * GroupKey.SlotsPerDistrict];
            var happiness = new long[counts.Length];

            using (var households = m_HouseholdQuery.ToEntityArray(Allocator.Temp))
            {
                r.Households = households.Length;

                // Pass 1: wealth per household, cut into quartiles for the 4 bands.
                var wealth = new int[households.Length];
                for (int i = 0; i < households.Length; i++)
                {
                    var hh = em.GetComponentData<Household>(households[i]);
                    var resources = em.GetBuffer<Resources>(households[i], true);
                    wealth[i] = EconomyUtils.GetHouseholdTotalWealth(hh, resources);
                }

                r.WealthSorted = (int[])wealth.Clone();
                Array.Sort(r.WealthSorted);
                r.WealthCuts = new[] { Percentile(r.WealthSorted, 0.25), Percentile(r.WealthSorted, 0.50), Percentile(r.WealthSorted, 0.75) };
                var cuts = r.WealthCuts;

                // Pass 2: every resident.
                for (int i = 0; i < households.Length; i++)
                {
                    var property = em.GetComponentData<PropertyRenter>(households[i]).m_Property;
                    int district = unassigned;
                    if (property != Entity.Null && em.HasComponent<CurrentDistrict>(property))
                    {
                        var d = em.GetComponentData<CurrentDistrict>(property).m_District;
                        if (d != Entity.Null && districtIndex.TryGetValue(d, out int idx)) district = idx;
                    }

                    int band = wealth[i] <= cuts[0] ? 0 : wealth[i] <= cuts[1] ? 1 : wealth[i] <= cuts[2] ? 2 : 3;

                    var members = em.GetBuffer<HouseholdCitizen>(households[i], true);
                    for (int m = 0; m < members.Length; m++)
                    {
                        var citizenEntity = members[m].m_Citizen;
                        if (!em.Exists(citizenEntity) || !em.HasComponent<Citizen>(citizenEntity)) continue;

                        var c = em.GetComponentData<Citizen>(citizenEntity);
                        if ((c.m_State & CitizenFlags.Tourist) != 0) { r.Tourists++; continue; }
                        if ((c.m_State & CitizenFlags.Commuter) != 0) { r.Commuters++; continue; }

                        var age = c.GetAge();
                        r.ByAge.TryGetValue(age, out int n);
                        r.ByAge[age] = n + 1;
                        if (hasTime)
                        {
                            if (!r.AgeDays.TryGetValue(age, out var list)) r.AgeDays[age] = list = new List<float>();
                            list.Add(c.GetAgeInDays(frame, timeData));
                        }

                        if (age != CitizenAge.Adult && age != CitizenAge.Elderly) continue;

                        r.Voters++;
                        r.VotersByDistrict[district]++;
                        r.HappinessByDistrict[district] += c.Happiness;

                        JobSector job;
                        if (em.HasComponent<Worker>(citizenEntity))
                            job = SectorOf(em.GetComponentData<Worker>(citizenEntity).m_Workplace);
                        else if (age == CitizenAge.Elderly)
                            job = JobSector.Retired;
                        else
                        {
                            if (em.HasComponent<Student>(citizenEntity)) r.AdultStudents++;
                            job = JobSector.Unemployed;
                        }

                        int edu = Math.Min(4, Math.Max(0, c.GetEducationLevel()));
                        var ageBand = age == CitizenAge.Elderly ? AgeBand.Elderly : AgeBand.Adult;
                        int key = GroupKey.Encode(district, ageBand, edu, band, job);
                        counts[key]++;
                        happiness[key] += c.Happiness;
                    }
                }
            }

            r.Table = GroupTable.FromSlots(districtCount, counts, happiness);
            r.ElapsedMs = sw.ElapsedMilliseconds;
            return r;
        }

        private JobSector SectorOf(Entity workplace)
        {
            var em = m_EntityManager;
            if (workplace == Entity.Null || !em.Exists(workplace)) return JobSector.Unemployed;
            if (em.HasComponent<OfficeCompany>(workplace)) return JobSector.Office;
            if (em.HasComponent<IndustrialCompany>(workplace)) return JobSector.Industrial;
            return JobSector.Commercial; // commercial companies, and for now city-service jobs (spike 4 decides)
        }

        private static int Percentile(int[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            return sorted[(int)Math.Round(p * (sorted.Length - 1))];
        }
    }
}
#endif
