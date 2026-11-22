using System.Collections.Generic;
using ElectionAtlas.Model;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Purpose = Colossal.Serialization.Entities.Purpose;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// Phase 2: the City Hall gate. Watches for a placed City Hall and moves the government through the
    /// states in <see cref="GovernmentGate"/>, and reports how well the City Hall is working (a shutdown
    /// doesn't stop elections; the election system turns it into a political penalty).
    /// Cost: one chunk count every 32 UI frames; the one or two City Halls are read when the count changes
    /// and every 256 frames (switching a building off doesn't change any count).
    /// Runs in the UI phase so it also reacts while the game is paused.
    /// </summary>
    public partial class CityHallSystem : GameSystemBase
    {
        private const int kFullCheckEvery = 8; // updates (x 32 frames)

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_AdminQuery;
        private readonly Dictionary<Entity, bool> m_IsCityHallPrefab = new Dictionary<Entity, bool>();

        private bool m_InGame;
        private bool m_FirstCheck;
        private int m_LastAdminCount = -1;
        private int m_Tick;

        public GovernmentState State { get; private set; } = GovernmentState.NoCityHall;

        public int CityHallCount { get; private set; }

        /// <summary>Best efficiency among City Halls, 0 to 1, ignoring fire. 0 when there are none.</summary>
        public float Efficiency { get; private set; }

        /// <summary>Every City Hall is at zero efficiency (switched off, or nothing working). Fire doesn't count.</summary>
        public bool IsShutDown { get; private set; }

        /// <summary>At least one City Hall was switched off by the player.</summary>
        public bool SwitchedOff { get; private set; }

        /// <summary>Set when elections unlock during play (never on load). The UI clears it after opening the panel.</summary>
        public bool PendingUnlockNotice { get; set; }

        public override int GetUpdateInterval(SystemUpdatePhase phase) => 32;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // AdminBuilding is also on the Central Bank, so the prefab name narrows it down (spike 1).
            m_AdminQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<AdminBuilding>(), ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Destroyed>() },
            });
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            // State is not reset here: ElectionSystem restores it (from the save, or NoCityHall) via ResetForLoad,
            // and the order of OnGameLoadingComplete calls between systems isn't guaranteed.
            m_InGame = mode == GameMode.Game;
            m_FirstCheck = true;
            m_LastAdminCount = -1;
            m_Tick = 0;
            m_IsCityHallPrefab.Clear();
            CityHallCount = 0;
            Efficiency = 0f;
            IsShutDown = false;
            SwitchedOff = false;
            PendingUnlockNotice = false;
        }

        /// <summary>Called by ElectionSystem when a city loads, with the saved state (or NoCityHall).</summary>
        public void ResetForLoad(GovernmentState restored)
        {
            State = restored;
            m_FirstCheck = true;
            m_LastAdminCount = -1;
        }

        protected override void OnUpdate()
        {
            if (!m_InGame)
                return;

            m_Tick++;
            int adminCount = m_AdminQuery.CalculateEntityCount();
            bool countChanged = adminCount != m_LastAdminCount;
            if (!countChanged && m_Tick % kFullCheckEvery != 0)
                return;
            m_LastAdminCount = adminCount;

            ReadCityHalls();
            bool pause = Mod.Settings?.PauseWithoutCityHall ?? true;
            var next = GovernmentGate.Next(State, CityHallCount > 0, pause);

            if (next != State)
            {
                Mod.Log.Info($"Government: {State} -> {next} ({CityHallCount} City Hall(s){(m_FirstCheck ? ", on load" : "")})");
                if (!m_FirstCheck && State == GovernmentState.NoCityHall && next == GovernmentState.AwaitingFounding)
                    PendingUnlockNotice = true;
                State = next;
            }

            m_FirstCheck = false;
        }

        /// <summary>Call after the founding election: Awaiting founding becomes Active.</summary>
        public void ApplyFoundingElection()
        {
            var next = GovernmentGate.AfterFoundingElection(State);
            if (next == State)
                return;
            Mod.Log.Info($"Government: {State} -> {next} (founding election held)");
            State = next;
        }

#if DEV_TOOLS
        /// <summary>Dev command: force a state. The next City Hall check re-applies the gate rules from it.</summary>
        public void DevSetState(GovernmentState state)
        {
            Mod.Log.Info($"[dev] Government forced: {State} -> {state}");
            State = state;
        }
#endif

        private void ReadCityHalls()
        {
            int count = 0;
            float best = 0f;
            bool switchedOff = false;

            using (var buildings = m_AdminQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var building in buildings)
                {
                    if (!IsCityHall(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab))
                        continue;

                    count++;
                    float efficiency = 1f;
                    if (EntityManager.HasBuffer<Efficiency>(building))
                    {
                        var factors = EntityManager.GetBuffer<Efficiency>(building, true);
                        efficiency = BuildingUtils.GetEfficiencyExcludingFactor(factors, EfficiencyFactor.Fire);
                        for (int i = 0; i < factors.Length; i++)
                        {
                            if (factors[i].m_Factor == EfficiencyFactor.Disabled && factors[i].m_Efficiency <= 0.001f)
                                switchedOff = true;
                        }
                    }

                    if (efficiency > best)
                        best = efficiency;
                }
            }

            bool shutDown = count > 0 && best <= 0.01f;
            if (count > 0 && (shutDown != IsShutDown || switchedOff != SwitchedOff))
                Mod.Log.Info($"City Hall: efficiency {best:P0}{(switchedOff ? ", switched off" : "")}{(shutDown ? " -> government shutdown" : "")}");

            CityHallCount = count;
            Efficiency = best;
            IsShutDown = shutDown;
            SwitchedOff = switchedOff;
        }

        private bool IsCityHall(Entity prefab)
        {
            if (!m_IsCityHallPrefab.TryGetValue(prefab, out bool isCityHall))
            {
                string name = m_PrefabSystem.TryGetPrefab<PrefabBase>(prefab, out var p) && p != null ? p.name : null;
                isCityHall = GovernmentGate.IsCityHallPrefabName(name);
                m_IsCityHallPrefab[prefab] = isCityHall;
            }

            return isCityHall;
        }
    }
}
