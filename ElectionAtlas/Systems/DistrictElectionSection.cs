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
            long id = ((long)e.Version << 32) | (uint)e.Index;
            string name = m_NameSystem != null ? m_NameSystem.GetRenderedLabelName(e) : $"District {e.Index}";

            var ledger = m_Elections.Ledger;
            m_View = DistrictViews.ForDistrict(ledger, id, name, out m_Election);

            var governing = System.Array.Find(m_Elections.CurrentParties(), p => p.Id == ledger.GoverningPartyId);
            m_GoverningName = governing?.Name ?? ledger.GoverningPartyId;
            m_GoverningColor = governing?.Color ?? 0x888888;
        }

        public override void OnWriteProperties(IJsonWriter writer)
        {
            var v = m_View;
            writer.PropertyName("hasData");
            writer.Write(v != null);
            if (v == null)
                return;

            writer.PropertyName("governing");
            writer.Write(m_GoverningName ?? "");
            writer.PropertyName("governingColor");
            writer.Write(Hex(m_GoverningColor));

            writer.PropertyName("approval");
            if (v.HasApproval) writer.Write(v.Approval); else writer.WriteNull();

            writer.PropertyName("trend");
            writer.ArrayBegin((uint)(v.Trend?.Length ?? 0));
            if (v.Trend != null)
                foreach (float f in v.Trend)
                {
                    if (float.IsNaN(f)) writer.WriteNull(); else writer.Write(f);
                }
            writer.ArrayEnd();

            writer.PropertyName("election");
            if (m_Election == null)
            {
                writer.WriteNull();
                return;
            }

            writer.TypeBegin("ElectionAtlas.DistrictResult");
            writer.PropertyName("number"); writer.Write(m_Election.Number);
            writer.PropertyName("month"); writer.Write(ElectionSchedule.MonthIndex(m_Election.Year, m_Election.Month));
            writer.PropertyName("turnout"); writer.Write(v.Turnout);
            writer.PropertyName("eligible"); writer.Write(v.Eligible);
            writer.PropertyName("votes"); writer.Write(v.Votes);
            writer.PropertyName("winner"); writer.Write(v.Winner);
            writer.PropertyName("margin"); writer.Write(v.Margin);
            writer.PropertyName("color"); writer.Write(Hex(v.Color));

            writer.PropertyName("parties");
            writer.ArrayBegin((uint)m_Election.Parties.Length);
            for (int i = 0; i < m_Election.Parties.Length; i++)
            {
                var p = m_Election.Parties[i];
                writer.TypeBegin("ElectionAtlas.PartyShare");
                writer.PropertyName("name"); writer.Write(p.Name);
                writer.PropertyName("color"); writer.Write(Hex(p.Color));
                writer.PropertyName("share"); writer.Write(v.Share(i));
                writer.PropertyName("votes"); writer.Write(v.PartyVotes != null && i < v.PartyVotes.Length ? v.PartyVotes[i] : 0L);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
            writer.TypeEnd();
        }

        private static string Hex(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("x6");
    }
}
