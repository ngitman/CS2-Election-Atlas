namespace ElectionAtlas.Model
{
    /// <summary>Voting-age bands. The game only tracks Adult and Elderly for anyone old enough to vote.</summary>
    public enum AgeBand
    {
        Adult,
        Elderly,
    }

    public enum JobSector
    {
        Commercial,
        Industrial,
        Office,
        Unemployed,
        Retired,
    }

    /// <summary>Who governs after an election.</summary>
    public enum GoverningRule
    {
        /// <summary>The party with the largest citywide share.</summary>
        PopularVote,

        /// <summary>The party that wins the most districts; a tie goes to the popular vote.</summary>
        MostDistricts,
    }

    /// <summary>The real-world turnout pattern the simulation is calibrated to.</summary>
    public enum TurnoutPreset
    {
        UsMunicipal,
        UsGeneral,
        UkGeneral,
        Nordic,
    }
}
