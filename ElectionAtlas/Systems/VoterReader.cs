using System;
using ElectionAtlas.Model;
using Game.Agents;
using Game.Areas;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
using Game.Economy;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// The one voter reader. Turns every voting-age resident into voter-group slots (district x 200) with Burst jobs
    /// and flat native data: a slice job over a snapshot of households (wealth, home district, members), then an
    /// aggregate job that sorts wealth into quartile bands and sums members into slots.
    ///
    /// Two ways to run it, same code:
    ///  - Sliced and scheduled (<see cref="Begin"/>, <see cref="ScheduleStep"/>, <see cref="Finish"/>): the election
    ///    system's monthly census. Each slice runs on a worker thread after the game's jobs that write what we read,
    ///    so the main thread never waits.
    ///  - All at once on the calling thread (<see cref="ReadAll"/>): dev commands, CSV export, and fallbacks.
    ///
    /// The same pass also gathers each district's land use (homes, how densely they're built, jobs located there)
    /// for <see cref="DistrictProfiles"/>, so profiles cost no extra scan of the city.
    /// Native containers are allocated once and reused across reads; a read allocates almost nothing.
    /// Lookups come from the owning system, so ECS knows the jobs read those components.
    /// </summary>
    public sealed class VoterReader : IDisposable
    {
        /// <summary>What the last read saw besides the voter table: counts for logs and dev diagnostics.</summary>
        public sealed class ReadStats
        {
            public int Households;
            public int Children, Teens, Adults, Elderly; // residents of the households read
            public int Tourists, Commuters;              // skipped: not residents
            public int[] WealthSorted = Array.Empty<int>(); // household wealth, ascending
        }

        private struct Member
        {
            public int Household;
            public byte Age;       // AgeBand
            public byte Education; // 0-4
            public byte Job;       // JobSector
            public byte Happiness; // 0-100
        }

        private const int kChildren = 0, kTeens = 1, kAdults = 2, kElderly = 3, kTourists = 4, kCommuters = 5, kStatCount = 6;

        [BurstCompile]
        private struct ReadSliceJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> Snapshot;
            public int Start, End;

            [ReadOnly] public ComponentLookup<Household> Households;
            [ReadOnly] public ComponentLookup<PropertyRenter> Renters;
            [ReadOnly] public ComponentLookup<MovingAway> MovingAway;
            [ReadOnly] public ComponentLookup<Deleted> Deleted;
            [ReadOnly] public BufferLookup<HouseholdCitizen> Members;
            [ReadOnly] public BufferLookup<Resources> Resources;
            [ReadOnly] public ComponentLookup<CurrentDistrict> CurrentDistricts;
            [ReadOnly] public ComponentLookup<Citizen> Citizens;
            [ReadOnly] public ComponentLookup<Worker> Workers;
            [ReadOnly] public ComponentLookup<OfficeCompany> Offices;
            [ReadOnly] public ComponentLookup<IndustrialCompany> Industries;
            [ReadOnly] public ComponentLookup<PrefabRef> Prefabs;
            [ReadOnly] public ComponentLookup<BuildingPropertyData> BuildingProperties;
            [ReadOnly] public ComponentLookup<BuildingData> Buildings;
            [ReadOnly] public NativeParallelHashMap<Entity, int> DistrictIndex;
            public int Outside;

            public NativeList<int> Wealth;
            public NativeList<int> HouseholdDistrict;
            public NativeList<Member> Voters;
            public NativeArray<int> Stats;

            // Land use per district index: households, sum of homes-per-lot-cell, jobs by sector (x3).
            public NativeArray<int> LandHouseholds;
            public NativeArray<float> LandDensity;
            public NativeArray<int> LandJobs;

            public void Execute()
            {
                for (int i = Start; i < End; i++)
                {
                    var hh = Snapshot[i];
                    // Households can change between slices: skip ones that left, were deleted or lost their home.
                    if (!Households.TryGetComponent(hh, out var household) || !Renters.TryGetComponent(hh, out var renter)
                        || MovingAway.HasComponent(hh) || Deleted.HasComponent(hh)
                        || !Members.TryGetBuffer(hh, out var members) || !Resources.TryGetBuffer(hh, out var resources))
                        continue;

                    int index = Wealth.Length;
                    Wealth.Add(EconomyUtils.GetHouseholdTotalWealth(household, resources));

                    int district = Outside;
                    if (CurrentDistricts.TryGetComponent(renter.m_Property, out var current) && DistrictIndex.TryGetValue(current.m_District, out int d))
                        district = d;
                    HouseholdDistrict.Add(district);
                    LandHouseholds[district]++;
                    LandDensity[district] += HomesPerCell(renter.m_Property);

                    for (int m = 0; m < members.Length; m++)
                    {
                        var entity = members[m].m_Citizen;
                        if (!Citizens.TryGetComponent(entity, out var c)) continue;
                        if ((c.m_State & CitizenFlags.Tourist) != 0) { Stats[kTourists]++; continue; }
                        if ((c.m_State & CitizenFlags.Commuter) != 0) { Stats[kCommuters]++; continue; }

                        var age = c.GetAge();
                        if (age == CitizenAge.Child) { Stats[kChildren]++; continue; }
                        if (age == CitizenAge.Teen) { Stats[kTeens]++; continue; }
                        bool elderly = age != CitizenAge.Adult;
                        Stats[elderly ? kElderly : kAdults]++;

                        JobSector job;
                        if (Workers.TryGetComponent(entity, out var worker))
                        {
                            job = SectorOf(worker.m_Workplace);
                            CountJob(worker.m_Workplace, job);
                        }
                        else job = elderly ? JobSector.Retired : JobSector.Unemployed;

                        int education = c.GetEducationLevel();
                        int happiness = c.Happiness;
                        Voters.Add(new Member
                        {
                            Household = index,
                            Age = (byte)(elderly ? AgeBand.Elderly : AgeBand.Adult),
                            Education = (byte)(education < 0 ? 0 : education > 4 ? 4 : education),
                            Job = (byte)job,
                            Happiness = (byte)(happiness < 0 ? 0 : happiness > 100 ? 100 : happiness),
                        });
                    }
                }
            }

            /// <summary>Homes per lot cell of the building a household lives in (towers several, houses well under 1).</summary>
            private float HomesPerCell(Entity building)
            {
                if (!Prefabs.TryGetComponent(building, out var prefab)) return 0f;
                if (!BuildingProperties.TryGetComponent(prefab.m_Prefab, out var properties) || !Buildings.TryGetComponent(prefab.m_Prefab, out var data))
                    return 0f;
                int cells = data.m_LotSize.x * data.m_LotSize.y;
                return cells > 0 ? (float)properties.m_ResidentialProperties / cells : 0f;
            }

            /// <summary>Counts a job in the district its workplace stands in.</summary>
            private void CountJob(Entity workplace, JobSector sector)
            {
                int slot = sector == JobSector.Industrial ? 0 : sector == JobSector.Office ? 1 : sector == JobSector.Commercial ? 2 : -1;
                if (slot < 0 || !Renters.TryGetComponent(workplace, out var site)) return;
                if (CurrentDistricts.TryGetComponent(site.m_Property, out var current) && DistrictIndex.TryGetValue(current.m_District, out int d))
                    LandJobs[d * 3 + slot]++;
            }

            private JobSector SectorOf(Entity workplace)
            {
                if (workplace == Entity.Null || !Citizens.EntityExists(workplace)) return JobSector.Unemployed;
                if (Offices.HasComponent(workplace)) return JobSector.Office;
                if (Industries.HasComponent(workplace)) return JobSector.Industrial;
                return JobSector.Commercial; // commercial companies, and for now city-service jobs (spike 4 decides)
            }
        }

        [BurstCompile]
        private struct AggregateJob : IJob
        {
            [ReadOnly] public NativeList<int> Wealth;
            [ReadOnly] public NativeList<int> HouseholdDistrict;
            [ReadOnly] public NativeList<Member> Voters;
            public NativeArray<int> SortedWealth;
            public NativeArray<int> Counts;
            public NativeArray<long> Happiness;
            public NativeArray<bool> RanInBurst;

            public void Execute()
            {
                RanInBurst[0] = true;
                MarkManaged(ref RanInBurst);

                for (int i = 0; i < Wealth.Length; i++) SortedWealth[i] = Wealth[i];
                SortedWealth.Sort();
                int q1 = Percentile(SortedWealth, 0.25), q2 = Percentile(SortedWealth, 0.50), q3 = Percentile(SortedWealth, 0.75);

                for (int i = 0; i < Voters.Length; i++)
                {
                    var m = Voters[i];
                    int w = Wealth[m.Household];
                    int band = w <= q1 ? 0 : w <= q2 ? 1 : w <= q3 ? 2 : 3;
                    int key = GroupKey.Encode(HouseholdDistrict[m.Household], (AgeBand)m.Age, m.Education, band, (JobSector)m.Job);
                    Counts[key]++;
                    Happiness[key] += m.Happiness;
                }
            }

            private static int Percentile(NativeArray<int> sorted, double p) =>
                sorted.Length == 0 ? 0 : sorted[(int)Math.Round(p * (sorted.Length - 1))];

            /// <summary>Burst strips this call, so the flag stays true only when the job really ran as Burst code.</summary>
            [BurstDiscard]
            private static void MarkManaged(ref NativeArray<bool> flag) => flag[0] = false;
        }

        private readonly SystemBase m_Owner;
        private readonly EntityManager m_EntityManager;
        private readonly NameSystem m_NameSystem;
        private readonly EntityQuery m_HouseholdQuery;
        private readonly EntityQuery m_DistrictQuery;

        // Reused across reads.
        private NativeArray<Entity> m_Snapshot;
        private NativeList<int> m_Wealth;
        private NativeList<int> m_HouseholdDistrict;
        private NativeList<Member> m_Voters;
        private NativeArray<int> m_Stats;
        private NativeParallelHashMap<Entity, int> m_DistrictIndex;
        private NativeArray<int> m_LandHouseholds;
        private NativeArray<float> m_LandDensity;
        private NativeArray<int> m_LandJobs;
        private float[] m_DistrictArea;
        private DistrictInfo[] m_Districts;
        private string[] m_DistrictNames;
        private int m_Next;
        private JobHandle m_Pending;

        public VoterReader(SystemBase owner)
        {
            m_Owner = owner;
            m_EntityManager = owner.EntityManager;
            m_NameSystem = owner.World.GetOrCreateSystemManaged<NameSystem>();
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

            m_Wealth = new NativeList<int>(1024, Allocator.Persistent);
            m_HouseholdDistrict = new NativeList<int>(1024, Allocator.Persistent);
            m_Voters = new NativeList<Member>(4096, Allocator.Persistent);
            m_Stats = new NativeArray<int>(kStatCount, Allocator.Persistent);
            m_DistrictIndex = new NativeParallelHashMap<Entity, int>(64, Allocator.Persistent);
        }

        public bool InProgress { get; private set; }

        /// <summary>True once every slice of the current read has been scheduled (it may still be running).</summary>
        public bool AllScheduled => !InProgress || m_Next >= m_Snapshot.Length;

        public int HouseholdCount => m_Snapshot.IsCreated ? m_Snapshot.Length : 0;

        public ReadStats Stats { get; } = new ReadStats();

        /// <summary>True when the last read's jobs ran as Burst code (false means a managed fallback).</summary>
        public bool LastReadUsedBurst { get; private set; }

        /// <summary>Land use per district from the last finished read, same order as its districts.</summary>
        public DistrictLandUse[] LandUse { get; private set; } = Array.Empty<DistrictLandUse>();

        /// <summary>Snapshots the households and districts and clears the buffers. Then schedule steps, then finish.</summary>
        public void Begin()
        {
            Cancel();
            m_Snapshot = m_HouseholdQuery.ToEntityArray(Allocator.Persistent);
            m_Next = 0;
            m_Wealth.Clear();
            m_HouseholdDistrict.Clear();
            m_Voters.Clear();
            for (int i = 0; i < kStatCount; i++) m_Stats[i] = 0;

            // District names come from the (managed) NameSystem, so they're read here; districts are few.
            m_DistrictIndex.Clear();
            using (var districts = m_DistrictQuery.ToEntityArray(Allocator.Temp))
            {
                m_Districts = new DistrictInfo[districts.Length + 1];
                m_DistrictNames = new string[districts.Length + 1];
                m_DistrictArea = new float[districts.Length + 1];
                for (int i = 0; i < districts.Length; i++)
                {
                    var d = districts[i];
                    m_DistrictIndex[d] = i;
                    m_DistrictNames[i] = DistrictName(d);
                    m_Districts[i] = new DistrictInfo(DistrictKey.Of(d), m_DistrictNames[i]);
                    if (m_EntityManager.HasComponent<Geometry>(d))
                        m_DistrictArea[i] = m_EntityManager.GetComponentData<Geometry>(d).m_SurfaceArea;
                }
                m_Districts[districts.Length] = DistrictInfo.Outside;
                m_DistrictNames[districts.Length] = DistrictInfo.OutsideName;
            }

            DisposeLand();
            m_LandHouseholds = new NativeArray<int>(m_Districts.Length, Allocator.Persistent);
            m_LandDensity = new NativeArray<float>(m_Districts.Length, Allocator.Persistent);
            m_LandJobs = new NativeArray<int>(m_Districts.Length * 3, Allocator.Persistent);

            InProgress = true;
        }

        /// <summary>
        /// Schedules the next slice of up to <paramref name="maxHouseholds"/> households after <paramref name="dependency"/>
        /// (the owning system's Dependency) and returns the handle to assign back to it. Slices run in order.
        /// </summary>
        public JobHandle ScheduleStep(int maxHouseholds, JobHandle dependency)
        {
            if (!InProgress || m_Next >= m_Snapshot.Length) return dependency;
            var job = SliceJob(maxHouseholds);
            m_Pending = job.Schedule(JobHandle.CombineDependencies(dependency, m_Pending));
            return m_Pending;
        }

        /// <summary>Completes the read (waiting for any running slice) and turns it into a table.</summary>
        public GroupTable Finish(out DistrictInfo[] districts, out string[] districtNames)
        {
            m_Pending.Complete();
            InProgress = false;
            districts = m_Districts;
            districtNames = m_DistrictNames;

            var counts = new NativeArray<int>(districts.Length * GroupKey.SlotsPerDistrict, Allocator.TempJob);
            var happiness = new NativeArray<long>(counts.Length, Allocator.TempJob);
            var sorted = new NativeArray<int>(m_Wealth.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var ranInBurst = new NativeArray<bool>(1, Allocator.TempJob);
            try
            {
                new AggregateJob
                {
                    Wealth = m_Wealth,
                    HouseholdDistrict = m_HouseholdDistrict,
                    Voters = m_Voters,
                    SortedWealth = sorted,
                    Counts = counts,
                    Happiness = happiness,
                    RanInBurst = ranInBurst,
                }.Run();

                LastReadUsedBurst = ranInBurst[0];
                Stats.Households = m_Wealth.Length;
                Stats.Children = m_Stats[kChildren];
                Stats.Teens = m_Stats[kTeens];
                Stats.Adults = m_Stats[kAdults];
                Stats.Elderly = m_Stats[kElderly];
                Stats.Tourists = m_Stats[kTourists];
                Stats.Commuters = m_Stats[kCommuters];
                Stats.WealthSorted = sorted.ToArray();

                var land = new DistrictLandUse[districts.Length];
                for (int i = 0; i < land.Length; i++)
                {
                    land[i] = new DistrictLandUse
                    {
                        AreaM2 = m_DistrictArea[i],
                        Households = m_LandHouseholds[i],
                        HomeDensitySum = m_LandDensity[i],
                        IndustrialJobs = m_LandJobs[i * 3],
                        OfficeJobs = m_LandJobs[i * 3 + 1],
                        CommercialJobs = m_LandJobs[i * 3 + 2],
                    };
                }
                LandUse = land;

                return GroupTable.FromSlots(districts.Length, counts.ToArray(), happiness.ToArray());
            }
            finally
            {
                counts.Dispose();
                happiness.Dispose();
                sorted.Dispose();
                ranInBurst.Dispose();
                DisposeSnapshot();
                DisposeLand();
            }
        }

        /// <summary>
        /// Reads the whole city now, on the calling thread (waits only for jobs writing what it reads).
        /// Use a reader of your own, not the one a scheduled census is using.
        /// </summary>
        public GroupTable ReadAll(out DistrictInfo[] districts, out string[] districtNames)
        {
            Begin();
            var em = m_EntityManager;
            em.CompleteDependencyBeforeRO<Household>();
            em.CompleteDependencyBeforeRO<PropertyRenter>();
            em.CompleteDependencyBeforeRO<HouseholdCitizen>();
            em.CompleteDependencyBeforeRO<Resources>();
            em.CompleteDependencyBeforeRO<CurrentDistrict>();
            em.CompleteDependencyBeforeRO<Citizen>();
            em.CompleteDependencyBeforeRO<Worker>();
            em.CompleteDependencyBeforeRO<PrefabRef>();
            SliceJob(int.MaxValue).Run();
            return Finish(out districts, out districtNames);
        }

        public void Cancel()
        {
            m_Pending.Complete();
            m_Pending = default;
            InProgress = false;
            DisposeSnapshot();
            DisposeLand();
        }

        public void Dispose()
        {
            Cancel();
            m_Wealth.Dispose();
            m_HouseholdDistrict.Dispose();
            m_Voters.Dispose();
            m_Stats.Dispose();
            m_DistrictIndex.Dispose();
        }

        public string DistrictName(Entity district)
        {
            if (district == Entity.Null) return "(none)";
            try { return m_NameSystem.GetRenderedLabelName(district); }
            catch { return $"District {district.Index}"; }
        }

        /// <summary>The next slice's job, with fresh lookups from the owning system.</summary>
        private ReadSliceJob SliceJob(int maxHouseholds)
        {
            int start = m_Next;
            int end = (int)Math.Min((long)start + Math.Max(1, maxHouseholds), m_Snapshot.Length);
            m_Next = end;
            return new ReadSliceJob
            {
                Snapshot = m_Snapshot,
                Start = start,
                End = end,
                Households = m_Owner.GetComponentLookup<Household>(true),
                Renters = m_Owner.GetComponentLookup<PropertyRenter>(true),
                MovingAway = m_Owner.GetComponentLookup<MovingAway>(true),
                Deleted = m_Owner.GetComponentLookup<Deleted>(true),
                Members = m_Owner.GetBufferLookup<HouseholdCitizen>(true),
                Resources = m_Owner.GetBufferLookup<Resources>(true),
                CurrentDistricts = m_Owner.GetComponentLookup<CurrentDistrict>(true),
                Citizens = m_Owner.GetComponentLookup<Citizen>(true),
                Workers = m_Owner.GetComponentLookup<Worker>(true),
                Offices = m_Owner.GetComponentLookup<OfficeCompany>(true),
                Industries = m_Owner.GetComponentLookup<IndustrialCompany>(true),
                Prefabs = m_Owner.GetComponentLookup<PrefabRef>(true),
                BuildingProperties = m_Owner.GetComponentLookup<BuildingPropertyData>(true),
                Buildings = m_Owner.GetComponentLookup<BuildingData>(true),
                DistrictIndex = m_DistrictIndex,
                Outside = m_Districts.Length - 1,
                Wealth = m_Wealth,
                HouseholdDistrict = m_HouseholdDistrict,
                Voters = m_Voters,
                Stats = m_Stats,
                LandHouseholds = m_LandHouseholds,
                LandDensity = m_LandDensity,
                LandJobs = m_LandJobs,
            };
        }

        private void DisposeSnapshot()
        {
            if (m_Snapshot.IsCreated) m_Snapshot.Dispose();
        }

        private void DisposeLand()
        {
            if (m_LandHouseholds.IsCreated) m_LandHouseholds.Dispose();
            if (m_LandDensity.IsCreated) m_LandDensity.Dispose();
            if (m_LandJobs.IsCreated) m_LandJobs.Dispose();
        }
    }
}
