using System;
using System.Collections.Generic;
using ElectionAtlas.Model;
using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Tools;
using Game.UI;
using Unity.Collections;
using Unity.Entities;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// Reads every voting-age resident into voter groups, spread over several steps so no single frame pays
    /// for the whole city (phase 3). Main thread, plain EntityManager reads: correct and simple; a Burst job can
    /// replace the inside of <see cref="Step"/> later without changing callers.
    /// Households snapshot at <see cref="Begin"/>; ones removed mid-read are skipped. Lists are reused between
    /// reads, so a read allocates almost nothing.
    /// </summary>
    public sealed class SlicedVoterReader : IDisposable
    {
        private struct Member
        {
            public int Household;
            public byte Age;        // AgeBand
            public byte Education;  // 0-4
            public byte Job;        // JobSector
            public byte Happiness;  // 0-100
        }

        private readonly EntityManager m_EntityManager;
        private readonly NameSystem m_NameSystem;
        private readonly EntityQuery m_HouseholdQuery;
        private readonly EntityQuery m_DistrictQuery;

        private NativeArray<Entity> m_Households;
        private readonly List<int> m_HouseholdWealth = new List<int>();
        private readonly List<int> m_HouseholdDistrict = new List<int>();
        private readonly List<Member> m_Members = new List<Member>();
        private readonly Dictionary<Entity, int> m_DistrictIndex = new Dictionary<Entity, int>();
        private readonly List<string> m_DistrictNames = new List<string>();
        private readonly List<DistrictInfo> m_Districts = new List<DistrictInfo>();
        private int m_Next;

        public SlicedVoterReader(World world)
        {
            m_EntityManager = world.EntityManager;
            m_NameSystem = world.GetOrCreateSystemManaged<NameSystem>();
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
        }

        public bool InProgress { get; private set; }

        public int HouseholdCount => m_Households.IsCreated ? m_Households.Length : 0;

        /// <summary>Snapshots the households and districts. Call <see cref="Step"/> until it returns true.</summary>
        public void Begin()
        {
            DisposeSnapshot();
            m_Households = m_HouseholdQuery.ToEntityArray(Allocator.Persistent);
            m_Next = 0;
            m_HouseholdWealth.Clear();
            m_HouseholdDistrict.Clear();
            m_Members.Clear();

            m_DistrictIndex.Clear();
            m_DistrictNames.Clear();
            m_Districts.Clear();
            using (var districts = m_DistrictQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var d in districts)
                {
                    m_DistrictIndex[d] = m_DistrictNames.Count;
                    string name = DistrictName(d);
                    m_DistrictNames.Add(name);
                    m_Districts.Add(new DistrictInfo(((long)d.Version << 32) | (uint)d.Index, name));
                }
            }

            m_DistrictNames.Add("Unassigned");
            m_Districts.Add(new DistrictInfo(-1, "Unassigned"));
            InProgress = true;
        }

        /// <summary>Reads up to <paramref name="maxHouseholds"/> households. Returns true when every household has been read.</summary>
        public bool Step(int maxHouseholds)
        {
            if (!InProgress) return true;

            var em = m_EntityManager;
            int unassigned = m_DistrictNames.Count - 1;
            int end = (int)Math.Min((long)m_Next + Math.Max(1, maxHouseholds), m_Households.Length);

            for (; m_Next < end; m_Next++)
            {
                var hh = m_Households[m_Next];
                if (!em.Exists(hh) || !em.HasComponent<Household>(hh) || !em.HasComponent<PropertyRenter>(hh))
                    continue;

                int householdIndex = m_HouseholdWealth.Count;
                m_HouseholdWealth.Add(EconomyUtils.GetHouseholdTotalWealth(em.GetComponentData<Household>(hh), em.GetBuffer<Resources>(hh, true)));

                int district = unassigned;
                var property = em.GetComponentData<PropertyRenter>(hh).m_Property;
                if (property != Entity.Null && em.HasComponent<CurrentDistrict>(property))
                {
                    var d = em.GetComponentData<CurrentDistrict>(property).m_District;
                    if (d != Entity.Null && m_DistrictIndex.TryGetValue(d, out int idx)) district = idx;
                }
                m_HouseholdDistrict.Add(district);

                var members = em.GetBuffer<HouseholdCitizen>(hh, true);
                for (int m = 0; m < members.Length; m++)
                {
                    var citizenEntity = members[m].m_Citizen;
                    if (!em.Exists(citizenEntity) || !em.HasComponent<Citizen>(citizenEntity)) continue;

                    var c = em.GetComponentData<Citizen>(citizenEntity);
                    if ((c.m_State & (CitizenFlags.Tourist | CitizenFlags.Commuter)) != 0) continue;

                    var age = c.GetAge();
                    if (age != CitizenAge.Adult && age != CitizenAge.Elderly) continue;

                    JobSector job;
                    if (em.HasComponent<Worker>(citizenEntity))
                        job = SectorOf(em.GetComponentData<Worker>(citizenEntity).m_Workplace);
                    else
                        job = age == CitizenAge.Elderly ? JobSector.Retired : JobSector.Unemployed;

                    m_Members.Add(new Member
                    {
                        Household = householdIndex,
                        Age = (byte)(age == CitizenAge.Elderly ? AgeBand.Elderly : AgeBand.Adult),
                        Education = (byte)Math.Min(4, Math.Max(0, c.GetEducationLevel())),
                        Job = (byte)job,
                        Happiness = (byte)Math.Min(100, Math.Max(0, c.Happiness)),
                    });
                }
            }

            return m_Next >= m_Households.Length;
        }

        /// <summary>Turns what was read into a table. Wealth bands are the quartiles of this read.</summary>
        public GroupTable Finish(out DistrictInfo[] districts, out string[] districtNames)
        {
            InProgress = false;
            int districtCount = m_DistrictNames.Count;

            var sorted = m_HouseholdWealth.ToArray();
            Array.Sort(sorted);
            int q1 = Percentile(sorted, 0.25), q2 = Percentile(sorted, 0.50), q3 = Percentile(sorted, 0.75);

            var counts = new int[districtCount * GroupKey.SlotsPerDistrict];
            var happiness = new long[counts.Length];
            foreach (var m in m_Members)
            {
                int w = m_HouseholdWealth[m.Household];
                int band = w <= q1 ? 0 : w <= q2 ? 1 : w <= q3 ? 2 : 3;
                int key = GroupKey.Encode(m_HouseholdDistrict[m.Household], (AgeBand)m.Age, m.Education, band, (JobSector)m.Job);
                counts[key]++;
                happiness[key] += m.Happiness;
            }

            districts = m_Districts.ToArray();
            districtNames = m_DistrictNames.ToArray();
            DisposeSnapshot();
            return GroupTable.FromSlots(districtCount, counts, happiness);
        }

        /// <summary>Reads the whole city at once (dev commands and on-demand runs).</summary>
        public GroupTable ReadAll(out DistrictInfo[] districts, out string[] districtNames)
        {
            Begin();
            Step(int.MaxValue);
            return Finish(out districts, out districtNames);
        }

        public void Cancel()
        {
            InProgress = false;
            DisposeSnapshot();
        }

        public void Dispose() => Cancel();

        private void DisposeSnapshot()
        {
            if (m_Households.IsCreated)
                m_Households.Dispose();
        }

        private JobSector SectorOf(Entity workplace)
        {
            var em = m_EntityManager;
            if (workplace == Entity.Null || !em.Exists(workplace)) return JobSector.Unemployed;
            if (em.HasComponent<OfficeCompany>(workplace)) return JobSector.Office;
            if (em.HasComponent<IndustrialCompany>(workplace)) return JobSector.Industrial;
            return JobSector.Commercial; // commercial companies, and for now city-service jobs (spike 4 decides)
        }

        private string DistrictName(Entity district)
        {
            try { return m_NameSystem.GetRenderedLabelName(district); }
            catch { return $"District {district.Index}"; }
        }

        private static int Percentile(int[] sorted, double p) =>
            sorted.Length == 0 ? 0 : sorted[(int)Math.Round(p * (sorted.Length - 1))];
    }
}
