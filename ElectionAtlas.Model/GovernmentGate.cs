namespace ElectionAtlas.Model
{
    /// <summary>Where the city's government stands. Elections and polls only run while <see cref="Active"/>.</summary>
    public enum GovernmentState
    {
        /// <summary>No City Hall yet: no elections.</summary>
        NoCityHall,

        /// <summary>A City Hall stands; the founding election is still ahead. Polls run.</summary>
        AwaitingFounding,

        /// <summary>The founding election has been held: monthly polls, elections on the cadence.</summary>
        Active,

        /// <summary>The City Hall was lost after the founding election: no polls or votes, history kept.</summary>
        Suspended,
    }

    /// <summary>The City Hall gate's state rules, kept here (not in the mod) so they can be unit-tested.</summary>
    public static class GovernmentGate
    {
        /// <summary>The state after a check for a City Hall.</summary>
        public static GovernmentState Next(GovernmentState current, bool hasCityHall, bool pauseWithoutCityHall)
        {
            switch (current)
            {
                case GovernmentState.NoCityHall:
                    return hasCityHall ? GovernmentState.AwaitingFounding : GovernmentState.NoCityHall;

                case GovernmentState.AwaitingFounding:
                    // Demolished before the first vote: the founding clock starts over.
                    return hasCityHall ? GovernmentState.AwaitingFounding : GovernmentState.NoCityHall;

                case GovernmentState.Active:
                    return hasCityHall || !pauseWithoutCityHall ? GovernmentState.Active : GovernmentState.Suspended;

                case GovernmentState.Suspended:
                    // Rebuilt, or the player turned pausing off: elections resume from the next date.
                    return hasCityHall || !pauseWithoutCityHall ? GovernmentState.Active : GovernmentState.Suspended;

                default:
                    return current;
            }
        }

        /// <summary>The state after the founding election has been held.</summary>
        public static GovernmentState AfterFoundingElection(GovernmentState current)
        {
            return current == GovernmentState.AwaitingFounding ? GovernmentState.Active : current;
        }

        /// <summary>Whether monthly polls run in this state.</summary>
        public static bool PollsRun(GovernmentState state)
        {
            return state == GovernmentState.AwaitingFounding || state == GovernmentState.Active;
        }

        /// <summary>
        /// Whether a building prefab is a City Hall. Spike 1 found AdminBuilding on CityHall01, EE_Cityhall
        /// (Eastern Europe pack) and CentralBank01, so the component alone isn't enough.
        /// </summary>
        public static bool IsCityHallPrefabName(string prefabName)
        {
            return !string.IsNullOrEmpty(prefabName)
                && prefabName.IndexOf("cityhall", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
