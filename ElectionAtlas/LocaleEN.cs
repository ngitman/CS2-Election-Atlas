using System.Collections.Generic;
using ElectionAtlas.Model;
using Colossal;

namespace ElectionAtlas
{
    public sealed class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var entries = new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Election Atlas" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kScheduleGroup), "Schedule" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kRulesGroup), "Rules" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Cadence)), "Election cadence" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Cadence)), "How often the city holds an election, in in-game years." },
                { m_Setting.GetEnumValueLocaleID(ElectionCadence.EveryYear), "Every year" },
                { m_Setting.GetEnumValueLocaleID(ElectionCadence.EveryTwoYears), "Every 2 years" },
                { m_Setting.GetEnumValueLocaleID(ElectionCadence.EveryFourYears), "Every 4 years" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PauseWithoutCityHall)), "Pause elections without a City Hall" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PauseWithoutCityHall)), "Elections only run while the city has a City Hall. History is kept while paused." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.GoverningRule)), "Who governs" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.GoverningRule)), "Popular vote: the largest citywide share governs. Most districts: the party that wins the most districts governs; a tie goes to the popular vote." },
                { m_Setting.GetEnumValueLocaleID(GoverningRule.PopularVote), "Popular vote" },
                { m_Setting.GetEnumValueLocaleID(GoverningRule.MostDistricts), "Most districts" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TurnoutPreset)), "Turnout preset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TurnoutPreset)), "The real-world turnout pattern the simulation is calibrated to." },
                { m_Setting.GetEnumValueLocaleID(TurnoutPreset.UsMunicipal), "US municipal (~25%)" },
                { m_Setting.GetEnumValueLocaleID(TurnoutPreset.UsGeneral), "US general (~65%)" },
                { m_Setting.GetEnumValueLocaleID(TurnoutPreset.UkGeneral), "UK general (~67%)" },
                { m_Setting.GetEnumValueLocaleID(TurnoutPreset.Nordic), "Nordic (~80%)" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PartyLineup)), "Parties" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PartyLineup)), "Test party lineup until the party editor arrives. A change applies from the next poll." },
                { m_Setting.GetEnumValueLocaleID(PartyLineup.TwoParties), "Two parties (Left, Right)" },
                { m_Setting.GetEnumValueLocaleID(PartyLineup.FourParties), "Four parties (Greens, Labour, Liberals, Conservatives)" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kSaveGroup), "Saving" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.SaveElectionData)), "Save election data in the city (experimental)" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.SaveElectionData)), "Keeps elections, polls and the government's state in your save. Experimental: use a copy of a city, because a save written with this may not load once the mod is removed. Takes effect after a restart." },
            };

#if DEV_TOOLS
            entries[m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup)] = "Debug (development builds only)";
            entries[m_Setting.GetOptionLabelLocaleID(nameof(Setting.SaveProbe))] = "Save probe (spike 3)";
            entries[m_Setting.GetOptionDescLocaleID(nameof(Setting.SaveProbe))] =
                "Tests whether the game saves data from a mod's own system. Takes effect after a restart. Use only on a copy of a city: the save may not load once the probe is gone.";
#endif
            return entries;
        }

        public void Unload()
        {
        }
    }
}
