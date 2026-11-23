namespace ElectionAtlas.Model
{
    /// <summary>The game's adult and elderly age bands are eligible to vote.</summary>
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
        Student,
    }

    /// <summary>Who governs after an election.</summary>
    public enum GoverningRule
    {
        /// <summary>The party with the largest citywide share.</summary>
        PopularVote,

        /// <summary>The party that wins the most districts; a tie goes to the popular vote.</summary>
        MostDistricts,
    }

    /// <summary>How a new election's result is shown (a player setting). Any past election can still be watched or skipped.</summary>
    public enum ResultReveal
    {
        /// <summary>The final result appears at once, as in 0.1.</summary>
        AllAtOnce,

        /// <summary>Watch voting hours and district returns before opening the final result.</summary>
        ElectionDay,
    }

    /// <summary>How fast a new election's Election Day plays (a player setting).</summary>
    public enum ElectionDayPace
    {
        /// <summary>Realistic: follows the city clock. Polls open at 07:00, close at 19:00, and the count runs to midnight.</summary>
        CityClock,

        /// <summary>A few real seconds per hour and per report, whatever the city's speed.</summary>
        Fast,
    }

    /// <summary>The real-world turnout pattern the simulation is calibrated to.</summary>
    public enum TurnoutPreset
    {
        UsMunicipal,
        UsGeneral,
        UkGeneral,
        Nordic,
    }

    /// <summary>How often the city holds an election, in in-game years.</summary>
    public enum ElectionCadence
    {
        EveryYear,
        EveryTwoYears,
        EveryFourYears,
    }

    /// <summary>The test party lineup used until the party editor exists.</summary>
    public enum PartyLineup
    {
        TwoParties,
        FourParties,
    }
}
