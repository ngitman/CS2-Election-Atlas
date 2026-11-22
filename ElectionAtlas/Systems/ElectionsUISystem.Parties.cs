using System;
using Colossal.UI.Binding;
using ElectionAtlas.Model;

namespace ElectionAtlas.Systems
{
    public partial class ElectionsUISystem
    {
        private RawValueBinding m_PartyEditor;
        private Party[] m_EditorParties;
        private int m_EditorRevision;
        private string m_EditorMessage = "";

        private void BindPartyEditor()
        {
            m_EditorParties = m_Elections.CurrentParties();
            AddBinding(m_PartyEditor = new RawValueBinding(kGroup, "partyEditor", Guarded("partyEditor", WritePartyEditor)));
            AddBinding(new TriggerBinding<int, string>(kGroup, "saveParties", SaveParties));
        }

        private void RefreshPartyEditor()
        {
            var parties = m_Elections.CurrentParties();
            if (!ReferenceEquals(parties, m_EditorParties)) UpdatePartyEditor(parties, "");
        }

        private void UpdatePartyEditor(Party[] parties, string message)
        {
            m_EditorParties = parties;
            m_EditorRevision++;
            m_EditorMessage = message;
            m_PartyEditor.Update();
            Invalidate();
        }

        private void SaveParties(int revision, string draft)
        {
            RefreshPartyEditor();
            try
            {
                if (revision != m_EditorRevision) throw new FormatException("The lineup changed. Review the current parties and try again.");
                var settings = Mod.Settings;
                if (settings == null) throw new InvalidOperationException("Election Atlas settings are unavailable.");

                var parties = PartyLineupSettings.ReadDraft(draft, m_EditorParties);
                settings.SaveParties(parties);
                UpdatePartyEditor(settings.GetParties(), "Party lineup saved. Changes apply to future polls and elections.");
                Mod.Log.Info($"Saved party lineup with {parties.Length} parties");
            }
            catch (FormatException ex)
            {
                m_EditorMessage = ex.Message;
                m_PartyEditor.Update();
            }
            catch (Exception ex)
            {
                Mod.Log.Error(ex, "Could not save the party lineup");
                m_EditorMessage = "Could not save the party lineup. Try again.";
                m_PartyEditor.Update();
            }
        }

        private void WritePartyEditor(IJsonWriter w)
        {
            w.TypeBegin("ElectionAtlas.PartyEditor");
            w.Prop("revision", m_EditorRevision);
            w.Prop("message", m_EditorMessage);
            w.Prop("minParties", PartyLineupSettings.MinParties);
            w.Prop("maxParties", PartyLineupSettings.MaxParties);
            w.Prop("maxNameLength", PartyLineupSettings.MaxNameLength);
            w.PartyList("parties", m_EditorParties);
            w.PartyList("twoPartyPreset", Setting.TwoPartyPreset);
            w.PartyList("fourPartyPreset", Setting.FourPartyPreset);
            w.TypeEnd();
        }
    }
}
