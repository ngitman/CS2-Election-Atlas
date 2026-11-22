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

        [SettingsUISection(kSection, kRulesGroup)]
        public GoverningRule GoverningRule { get; set; }

        [SettingsUISection(kSection, kRulesGroup)]
        public TurnoutPreset TurnoutPreset { get; set; }

        /// <summary>Test lineup until the party editor exists. Changing it starts a fresh lineup at the next election.</summary>
        [SettingsUISection(kSection, kRulesGroup)]
        public PartyLineup PartyLineup { get; set; }

        /// <summary>
        /// Experimental: store election history in the save file. Read once at mod load (restart to change).
        /// Off by default until it's confirmed that saves still load after the mod is removed (spike 3).
        /// </summary>
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
            GoverningRule = GoverningRule.PopularVote;
            TurnoutPreset = TurnoutPreset.UsGeneral;
            PartyLineup = PartyLineup.TwoParties;
            SaveElectionData = true;
#if DEV_TOOLS
            SaveProbe = false;
#endif
        }
    }
}
