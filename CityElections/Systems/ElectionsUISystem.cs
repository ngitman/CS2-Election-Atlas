using Colossal.UI.Binding;
using Game.UI;

namespace CityElections.Systems
{
    /// <summary>
    /// Bindings between C# and the Elections panel. ValueBindings only push to the UI
    /// when their value changes, so this costs nothing while the panel is idle.
    /// </summary>
    public partial class ElectionsUISystem : UISystemBase
    {
        public const string kGroup = "cityElections";

        private ValueBinding<bool> m_PanelOpen;

        protected override void OnCreate()
        {
            base.OnCreate();

            AddBinding(m_PanelOpen = new ValueBinding<bool>(kGroup, "panelOpen", false));
            AddBinding(new TriggerBinding(kGroup, "togglePanel", TogglePanel));
            AddBinding(new TriggerBinding(kGroup, "closePanel", ClosePanel));

            Mod.Log.Info("Elections UI bindings registered");
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
