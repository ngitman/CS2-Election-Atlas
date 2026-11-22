using Colossal.UI.Binding;
using ElectionAtlas.Model;
using Game.Areas;
using Game.UI.InGame;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// The "Election" section in the game's own info panel when the player selects a district in the city:
    /// the district's result in its latest election, and its approval of the government now.
    /// The UI renders it by this class's full name (registered in the UI module's selectedInfoSectionComponents).
    /// Hidden when the selection isn't a district or the district has no election or poll yet.
    /// </summary>
    public partial class DistrictElectionSection : InfoSectionBase
    {
        private ElectionSystem m_Elections;
        private DistrictView m_View;
        private ElectionSnapshot m_Election;
        private string m_GoverningName;
        private uint m_GoverningColor;

        protected override string group => nameof(DistrictElectionSection);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Elections = World.GetOrCreateSystemManaged<ElectionSystem>();
            m_InfoUISystem.AddMiddleSection(this);
        }

        protected override void Reset()
        {
            m_View = null;
            m_Election = null;
            m_GoverningName = null;
            m_GoverningColor = 0;
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            visible = selectedEntity != Unity.Entities.Entity.Null
                && EntityManager.HasComponent<District>(selectedEntity)
                && (m_Elections.Ledger.Archive.Count > 0 || m_Elections.Ledger.Polls.Count > 0);
        }

        protected override void OnProcess()
        {
            var e = selectedEntity;
            long id = DistrictKey.Of(e);
            string name = m_NameSystem != null ? m_NameSystem.GetRenderedLabelName(e) : $"District {e.Index}";

            var ledger = m_Elections.Ledger;
            m_View = DistrictViews.ForDistrict(ledger, id, name, m_Elections.Profiles, out m_Election);

            var governing = System.Array.Find(m_Elections.CurrentParties(), p => p.Id == ledger.GoverningPartyId);
            m_GoverningName = governing?.Name ?? ledger.GoverningPartyId;
            m_GoverningColor = governing?.Color ?? 0x888888;
        }

        /// <summary>Always the same properties, null where there's nothing (see the rule in UiWriters).</summary>
        public override void OnWriteProperties(IJsonWriter writer)
        {
            bool safe = false;
#if DEV_TOOLS
            // Check the nested shapes in managed code first; on a problem, write the same properties with nulls.
            safe = !Dev.ShapeGuard.Check("district section", w =>
            {
                w.TypeBegin("ElectionAtlas.SectionCheck");
                WriteProperties(w, false);
                w.TypeEnd();
            });
#endif
            WriteProperties(writer, safe);
        }

        private void WriteProperties(IJsonWriter writer, bool nullsOnly)
        {
            var view = nullsOnly ? null : m_View;
            var election = nullsOnly ? null : m_Election;
            writer.Prop("hasData", view != null);
            writer.Prop("governing", m_GoverningName ?? "");
            writer.PropColor("governingColor", m_GoverningColor);

            writer.PropertyName("district");
            if (view != null) writer.District(view); else writer.WriteNull();

            writer.PropertyName("election");
            if (election != null)
            {
                writer.TypeBegin("ElectionAtlas.Election");
                writer.ElectionFields(election);
                writer.TypeEnd();
            }
            else writer.WriteNull();
        }
    }
}
