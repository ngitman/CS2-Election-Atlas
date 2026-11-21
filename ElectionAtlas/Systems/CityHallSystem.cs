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
    /// states in <see cref="GovernmentGate"/>.
    /// Cost: one chunk count every 32 UI frames; the buildings are only read when that count changes.
    /// Runs in the UI phase so it also reacts while the game is paused.
    /// Not saved yet: on load the state is worked out again from the map (saving arrives with phase 5).
    /// </summary>
    public partial class CityHallSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_AdminQuery;
        private readonly Dictionary<Entity, bool> m_IsCityHallPrefab = new Dictionary<Entity, bool>();

        private bool m_InGame;
        private bool m_FirstCheck;
        private int m_LastAdminCount = -1;

        public GovernmentState State { get; private set; } = GovernmentState.NoCityHall;

        public int CityHallCount { get; private set; }

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
            m_InGame = mode == GameMode.Game;
            m_FirstCheck = true;
            m_LastAdminCount = -1;
            m_IsCityHallPrefab.Clear();
            State = GovernmentState.NoCityHall;
            CityHallCount = 0;
            PendingUnlockNotice = false;
        }

        protected override void OnUpdate()
        {
            if (!m_InGame)
                return;

            int adminCount = m_AdminQuery.CalculateEntityCount();
            if (adminCount == m_LastAdminCount)
                return;
            m_LastAdminCount = adminCount;

            CityHallCount = CountCityHalls();
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

        private int CountCityHalls()
        {
            int count = 0;
            using (var buildings = m_AdminQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var building in buildings)
                {
                    if (IsCityHall(EntityManager.GetComponentData<PrefabRef>(building).m_Prefab))
                        count++;
                }
            }

            return count;
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
