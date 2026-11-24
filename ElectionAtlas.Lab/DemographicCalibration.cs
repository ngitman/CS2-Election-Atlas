using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ElectionAtlas.Model;

namespace ElectionAtlas.Lab
{
    internal static class DemographicCalibration
    {
        public static LeanWeights ReadWeights(string path)
        {
            var weights = new LeanWeights();
            var fields = typeof(LeanWeights).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(float))
                .ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lineNumber = 0;
            foreach (var raw in File.ReadLines(path))
            {
                lineNumber++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !fields.TryGetValue(parts[0], out FieldInfo field)
                    || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    || !float.IsFinite(value))
                    throw new FormatException($"{path}:{lineNumber}: expected a LeanWeights field and a finite value, e.g. OfficeSocial=-0.2.");
                if (!seen.Add(field.Name))
                    throw new FormatException($"{path}:{lineNumber}: {field.Name} is specified more than once.");

                field.SetValue(weights, value);
            }

            if (seen.Count == 0) throw new FormatException($"{path}: no lean weights were specified.");
            return weights;
        }

        public static void PrintMix(GroupTable table, string[] names)
        {
            Console.WriteLine();
            Console.WriteLine("Voter mix · education levels 3–4 and the highest household wealth band");
            int width = Math.Max(8, names.Append("Citywide").Max(name => name.Length));
            Console.WriteLine($"  {"District".PadRight(width)}  {"Voters",8}  {"Elderly",7}  {"Edu 3–4",7}  {"Wealth 3",8}  {"Industry",8}  {"Office",7}  {"Unemployed",10}  {"Student",7}");
            PrintMixRow("Citywide", table.Groups, width);
            foreach (var district in table.Groups.GroupBy(group => group.District).OrderBy(group => group.Key))
                PrintMixRow(names[district.Key], district, width);
        }

        private static void PrintMixRow(string name, IEnumerable<VoterGroup> groups, int width)
        {
            long voters = 0, elderly = 0, educated = 0, wealth = 0, industry = 0, office = 0, unemployed = 0, students = 0;
            foreach (var group in groups)
            {
                voters += group.Count;
                if (group.Age == AgeBand.Elderly) elderly += group.Count;
                if (group.Education >= 3) educated += group.Count;
                if (group.WealthBand == VoterGroup.WealthBands - 1) wealth += group.Count;
                if (group.Job == JobSector.Industrial) industry += group.Count;
                if (group.Job == JobSector.Office) office += group.Count;
                if (group.Job == JobSector.Unemployed) unemployed += group.Count;
                if (group.Job == JobSector.Student) students += group.Count;
            }

            if (voters == 0) return;
            double share(long count) => (double)count / voters;
            Console.WriteLine($"  {name.PadRight(width)}  {voters,8:N0}  {share(elderly),7:P1}  {share(educated),7:P1}  {share(wealth),8:P1}  {share(industry),8:P1}  {share(office),7:P1}  {share(unemployed),10:P1}  {share(students),7:P1}");
        }

        public static void PrintComparison(ElectionResult baseline, ElectionResult tuned, IReadOnlyList<Party> parties, string[] names)
        {
            Console.WriteLine($"  With lean weights · turnout {tuned.Turnout:P2} · governing: {tuned.GoverningPartyId ?? "nobody"}");
            int width = Math.Max(8, names.Append("Citywide").Max(name => name.Length));
            var labels = parties.Select(party => party.Name).ToArray();
            var headers = labels.Select(name => name.PadLeft(Math.Max(name.Length, 16)));
            Console.WriteLine($"    {"District".PadRight(width)}  {string.Join("  ", headers)}");
            PrintComparisonRow("Citywide", baseline.Shares, tuned.Shares, labels, width);
            for (int d = 0; d < baseline.Districts.Count; d++)
            {
                var before = baseline.Districts[d];
                var after = tuned.Districts[d];
                if (before.Votes == 0 && after.Votes == 0) continue;

                var beforeShares = before.PartyVotes.Select(votes => before.Votes > 0 ? (float)((double)votes / before.Votes) : 0).ToArray();
                var afterShares = after.PartyVotes.Select(votes => after.Votes > 0 ? (float)((double)votes / after.Votes) : 0).ToArray();
                PrintComparisonRow(names[d], beforeShares, afterShares, labels, width);
            }
        }

        private static void PrintComparisonRow(string name, float[] before, float[] after, string[] labels, int width)
        {
            var cells = labels.Select((label, index) =>
                $"{before[index]:P1} → {after[index]:P1}".PadLeft(Math.Max(label.Length, 16)));
            Console.WriteLine($"    {name.PadRight(width)}  {string.Join("  ", cells)}");
        }
    }
}
