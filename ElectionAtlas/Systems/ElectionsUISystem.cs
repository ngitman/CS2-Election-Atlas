using Colossal.UI.Binding;
using ElectionAtlas.Model;
using Game.UI;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// Bindings between C# and the Elections panel. ValueBindings only push to the UI
    /// when their value changes, so this costs nothing while the panel is idle.
    /// </summary>
    public partial class ElectionsUISystem : UISystemBase
    {
        public const string kGroup = "electionAtlas";

        private CityHallSystem m_CityHallSystem;
        private ValueBinding<bool> m_PanelOpen;
        private ValueBinding<string> m_GovernmentState;
        private GovernmentState m_LastState = GovernmentState.NoCityHall;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CityHallSystem = World.GetOrCreateSystemManaged<CityHallSystem>();

            AddBinding(m_PanelOpen = new ValueBinding<bool>(kGroup, "panelOpen", false));
            AddBinding(m_GovernmentState = new ValueBinding<string>(kGroup, "governmentState", nameof(GovernmentState.NoCityHall)));
            AddBinding(new TriggerBinding(kGroup, "togglePanel", TogglePanel));
            AddBinding(new TriggerBinding(kGroup, "closePanel", ClosePanel));

            Mod.Log.Info("Elections UI bindings registered");
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            // Only touch the binding when the state changes (Enum.ToString allocates).
            var state = m_CityHallSystem.State;
            if (state != m_LastState)
            {
                m_LastState = state;
                m_GovernmentState.Update(state.ToString());
            }

            if (m_CityHallSystem.PendingUnlockNotice)
            {
                m_CityHallSystem.PendingUnlockNotice = false;
                m_PanelOpen.Update(true);
            }
        }

        private void TogglePanel()
        {
            m_PanelOpen.Update(!m_PanelOpen.value);
        }

        private void ClosePanel()
        {
            m_PanelOpen.Update(false);
        }
    }
}
