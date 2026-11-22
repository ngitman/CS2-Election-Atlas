using System;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// Calendar arithmetic for elections. Dates are month indexes: year x 12 + month (month 0-11), so the
    /// scheduler only compares integers.
    /// </summary>
    public static class ElectionSchedule
    {
        public const int NotScheduled = int.MinValue;

        public static int MonthIndex(int year, int month) => year * 12 + month;

        public static int YearOf(int monthIndex) => (int)Math.Floor(monthIndex / 12.0);

        public static int MonthOf(int monthIndex) => ((monthIndex % 12) + 12) % 12;

        public static int MonthsBetween(ElectionCadence cadence)
        {
            switch (cadence)
            {
                case ElectionCadence.EveryYear: return 12;
                case ElectionCadence.EveryTwoYears: return 24;
                case ElectionCadence.EveryFourYears: return 48;
                default: throw new ArgumentOutOfRangeException(nameof(cadence));
            }
        }

        /// <summary>The founding election comes one cadence period after the City Hall goes up.</summary>
        public static int FoundingElection(int unlockedMonthIndex, ElectionCadence cadence) =>
            unlockedMonthIndex + MonthsBetween(cadence);

        /// <summary>The next election after one held in <paramref name="heldMonthIndex"/>.</summary>
        public static int NextAfter(int heldMonthIndex, ElectionCadence cadence) =>
            heldMonthIndex + MonthsBetween(cadence);

        public static bool IsDue(int nowMonthIndex, int dueMonthIndex) =>
            dueMonthIndex != NotScheduled && nowMonthIndex >= dueMonthIndex;

        /// <summary>Whole months from now until the due date (0 when due or overdue; -1 when nothing is scheduled).</summary>
        public static int MonthsUntil(int nowMonthIndex, int dueMonthIndex) =>
            dueMonthIndex == NotScheduled ? -1 : Math.Max(0, dueMonthIndex - nowMonthIndex);

        public static string Format(int monthIndex)
        {
            if (monthIndex == NotScheduled) return "-";
            return $"{YearOf(monthIndex)}-{MonthOf(monthIndex) + 1:00}";
        }
    }
}
