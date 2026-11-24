using System;
using ElectionAtlas.Model;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace ElectionAtlas
{
    // ElectionCadence, GoverningRule, TurnoutPreset and PartyLineup live in ElectionAtlas.Model, which reads them.
    [FileLocation(Mod.Id)]
#if DEV_TOOLS
    [SettingsUIGroupOrder(kScheduleGroup, kRulesGroup, kSaveGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kScheduleGroup, kRulesGroup, kSaveGroup, kDebugGroup)]
#else
    [SettingsUIGroupOrder(kScheduleGroup, kRulesGroup, kSaveGroup)]
    [SettingsUIShowGroupName(kScheduleGroup, kRulesGroup, kSaveGroup)]
#endif
    public sealed class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kScheduleGroup = "Schedule";
        public const string kRulesGroup = "Rules";
        public const string kSaveGroup = "Saving";
        public const string kDebugGroup = "Debug";

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kScheduleGroup)]
        public ElectionCadence Cadence { get; set; }

        [SettingsUISection(kSection, kScheduleGroup)]
        public bool PauseWithoutCityHall { get; set; }

        [SettingsUISection(kSection, kScheduleGroup)]
        public ResultReveal ResultReveal { get; set; }

        [SettingsUISection(kSection, kScheduleGroup)]
        public ElectionDayPace ElectionDayPace { get; set; }

        [SettingsUISection(kSection, kScheduleGroup)]
        public bool ElectionNotifications { get; set; }

        [SettingsUISection(kSection, kRulesGroup)]
        public GoverningRule GoverningRule { get; set; }

        [SettingsUISection(kSection, kRulesGroup)]
        public TurnoutPreset TurnoutPreset { get; set; }

        // Retain the old setting so existing players start the editor from their selected preset.
        [SettingsUIHidden]
        public PartyLineup PartyLineup { get; set; }

        [SettingsUIHidden]
        public string CustomPartyLineup { get; set; }

        internal static readonly Party[] TwoPartyPreset = SampleParties.Two();
        internal static readonly Party[] FourPartyPreset = SampleParties.Four();
        private string m_ReadPartyLineup;
        private Party[] m_CustomParties;

        internal Party[] GetParties()
        {
            if (!string.Equals(CustomPartyLineup, m_ReadPartyLineup, StringComparison.Ordinal))
            {
                m_ReadPartyLineup = CustomPartyLineup;
                m_CustomParties = null;
                if (!string.IsNullOrEmpty(m_ReadPartyLineup))
                {
                    try { m_CustomParties = PartyLineupSettings.ReadSaved(m_ReadPartyLineup); }
                    catch (FormatException ex) { Mod.Log.Warn($"Using the default party lineup. {ex.Message}"); }
                }
            }
            return m_CustomParties ?? (PartyLineup == PartyLineup.FourParties ? FourPartyPreset : TwoPartyPreset);
        }

        internal void SaveParties(Party[] parties)
        {
            string previous = CustomPartyLineup;
            CustomPartyLineup = PartyLineupSettings.Write(parties);
            try { ApplyAndSave(); }
            catch { CustomPartyLineup = previous; throw; }
        }

        /// <summary>Whether the next city save includes election history. Defaults to on.</summary>
        [SettingsUISection(kSection, kSaveGroup)]
        public bool SaveElectionData { get; set; }

#if DEV_TOOLS
        /// <summary>Spike 3. Read once at mod load, so changing it needs a game restart.</summary>
        [SettingsUISection(kSection, kDebugGroup)]
        public bool SaveProbe { get; set; }
#endif

        public override void SetDefaults()
        {
            Cadence = ElectionCadence.EveryYear;
            PauseWithoutCityHall = true;
            ResultReveal = ResultReveal.ElectionDay;
            ElectionDayPace = ElectionDayPace.CityClock;
            ElectionNotifications = true;
            GoverningRule = GoverningRule.PopularVote;
            TurnoutPreset = TurnoutPreset.UsGeneral;
            PartyLineup = PartyLineup.TwoParties;
            CustomPartyLineup = null;
            SaveElectionData = true;
#if DEV_TOOLS
            SaveProbe = false;
#endif
        }
    }
}
