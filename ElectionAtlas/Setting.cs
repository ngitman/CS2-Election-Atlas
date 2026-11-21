using ElectionAtlas.Model;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace ElectionAtlas
{
    // GoverningRule and TurnoutPreset live in ElectionAtlas.Model, which reads them.
    public enum ElectionCadence
    {
        EveryYear,
        EveryTwoYears,
        EveryFourYears,
    }

    // Phase 1 only shows these in the options menu;
    // the scheduler and vote model start reading them in phases 4-5.
    [FileLocation(Mod.Id)]
    [SettingsUIGroupOrder(kScheduleGroup, kRulesGroup)]
    [SettingsUIShowGroupName(kScheduleGroup, kRulesGroup)]
    public sealed class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kScheduleGroup = "Schedule";
        public const string kRulesGroup = "Rules";

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

        public override void SetDefaults()
        {
            Cadence = ElectionCadence.EveryYear;
            PauseWithoutCityHall = true;
            GoverningRule = GoverningRule.PopularVote;
            TurnoutPreset = TurnoutPreset.UsGeneral;
        }
    }
}
