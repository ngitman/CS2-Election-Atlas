using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ElectionAtlas.Model;

namespace ElectionAtlas.Lab
{
    /// <summary>
    /// Runs elections on a city exported from the game (Developer > Export city CSV), so the model can be
    /// tuned without relaunching Cities: Skylines II.
    /// </summary>
    internal static class Program
    {
        private const string Usage = @"Usage: dotnet run --project ElectionAtlas.Lab -- <city.csv | folder> [options]

  <city.csv>           a file from Developer > Export city CSV
  <folder>             runs the newest .csv in that folder
  --parties 2|4        test party lineup (default 2)
  --preset NAME        UsMunicipal | UsGeneral | UkGeneral | Nordic (default UsGeneral)
  --rule NAME          PopularVote | MostDistricts (default PopularVote)
  --incumbent ID       governing party id, e.g. left (default: nobody)
  --elections N        run N elections in a row, each incumbent the last winner (default 1)
  --seed N             seed for the first election (default 1)
  --profiles FILE      compare elections with district-profile effects
                      use the export's # district indices, e.g. 0=UrbanCore
                      profiles: UrbanCore | Urban | Suburban | Rural | Industrial | Business | Unknown
                      both runs use the baseline incumbent and citywide turnout target";

        private static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is IOException)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            if (args.Length == 0 || args[0] == "-h" || args[0] == "--help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
            }

            if (args[0] == "--sample")
            {
                string outPath = args.Length > 1 ? args[1] : "sample-city.csv";
                WriteSampleCity(outPath);
                Console.WriteLine($"Wrote a synthetic 6-district city to {Path.GetFullPath(outPath)}");
                return 0;
            }

            string path = ResolveCity(args[0]);
            var opts = ParseOptions(args.Skip(1).ToArray());
            int partyCount = int.Parse(Get(opts, "parties", "2"), CultureInfo.InvariantCulture);
            var preset = (TurnoutPreset)Enum.Parse(typeof(TurnoutPreset), Get(opts, "preset", "UsGeneral"), true);
            var rule = (GoverningRule)Enum.Parse(typeof(GoverningRule), Get(opts, "rule", "PopularVote"), true);
            string incumbent = Get(opts, "incumbent", null);
            int elections = int.Parse(Get(opts, "elections", "1"), CultureInfo.InvariantCulture);
            ulong seed = ulong.Parse(Get(opts, "seed", "1"), CultureInfo.InvariantCulture);

            GroupTable table;
            string[] names;
            string source;
            using (var reader = File.OpenText(path))
                table = GroupTableCsv.Read(reader, out names, out source);

            var parties = SampleParties.Get(partyCount);
            var cfg = new ModelConfig();
            int outside = names[^1] == DistrictInfo.OutsideName ? names.Length - 1 : -1;
            string profilePath = Get(opts, "profiles", null);
            var experiment = profilePath == null ? null : new ProfileExperiment(table, profilePath, outside);

            Console.WriteLine($"City: {Path.GetFileName(path)}{(source != null ? $" ({source})" : "")}");
            Console.WriteLine($"{table.TotalVoters:N0} voters in {table.Groups.Count:N0} groups, {table.DistrictCount} districts");
            Console.WriteLine($"Parties: {string.Join(", ", parties.Select(p => $"{p.Name} {p.Position}"))} · {preset} · {rule}");

            var poll = ElectionModel.RunPoll(table, parties, incumbent, cfg);
            Console.WriteLine($"Poll before: approval {poll.Approval:0.0} · {string.Join(" · ", parties.Select((p, i) => $"{p.Name} {poll.Intention[i]:P1}"))}");

            for (int e = 0; e < elections; e++)
            {
                var result = ElectionModel.RunElection(table, parties, incumbent, rule, preset, cfg, seed + (ulong)e, outsideDistrict: outside);
                PrintElection(e + 1, result, parties, names);
                if (experiment != null)
                {
                    var adjusted = experiment.Run(result, parties, incumbent, rule, preset);
                    PrintProfileComparison(result, adjusted, experiment.Profiles, parties, names, outside);
                }
                incumbent = result.GoverningPartyId;
            }

            return 0;
        }

        private static void PrintElection(int number, ElectionResult r, IReadOnlyList<Party> parties, string[] names)
        {
            Console.WriteLine();
            Console.WriteLine($"Election {number}: turnout {r.Turnout:P1} (target {r.TargetTurnout:P1}) · governing: {r.GoverningPartyId ?? "nobody"}");
            Console.WriteLine("  " + string.Join(" · ", parties.Select((p, i) => $"{p.Name} {r.Shares[i]:P1} ({r.DistrictsWon[i]} districts)")));

            int nameWidth = Math.Max(10, names.Max(n => n.Length));
            Console.WriteLine($"  {"District".PadRight(nameWidth)}  {"Voters",8}  {"Turnout",7}  {"Winner",-14}  {"Margin",7}");
            foreach (var d in r.Districts.Where(d => d.Eligible > 0).OrderBy(d => d.Margin))
            {
                string winner = d.WinnerIndex >= 0 ? parties[d.WinnerIndex].Name : "-";
                Console.WriteLine($"  {names[d.District].PadRight(nameWidth)}  {d.Eligible,8:N0}  {d.Turnout,7:P1}  {winner,-14}  {d.Margin * 100f,6:0.0}p");
            }
        }

        private static void PrintProfileComparison(ElectionResult baseline, DistrictResult[] adjusted, DistrictProfile[] profiles,
            IReadOnlyList<Party> parties, string[] names, int outside)
        {
            long total = adjusted.Sum(d => d.Votes);
            double turnout = baseline.Eligible > 0 ? (double)total / baseline.Eligible : 0;
            Console.WriteLine($"  With profiles: citywide turnout {turnout:P2} ({PointChange(baseline.Turnout, turnout)})");
            for (int p = 0; p < parties.Count; p++)
            {
                int party = p;
                double share = total > 0 ? (double)adjusted.Sum(d => d.PartyVotes[party]) / total : 0;
                int seats = adjusted.Count(d => d.District != outside && d.WinnerIndex == party);
                Console.WriteLine($"    {parties[p].Name}: {baseline.Shares[p]:P2} → {share:P2} ({PointChange(baseline.Shares[p], share)}), districts {baseline.DistrictsWon[p]} → {seats}");
            }

            int nameWidth = Math.Max(10, names.Max(n => n.Length));
            Console.WriteLine($"    {"District".PadRight(nameWidth)}  {"Profile",-12}  {"Turnout Δ",11}  Winner and margin");
            foreach (var d in adjusted.Where(d => d.Eligible > 0 && d.District != outside))
            {
                var before = baseline.Districts[d.District];
                var profile = profiles[d.District];
                string label = profile == DistrictProfile.Unknown ? "Unknown" : DistrictProfiles.Label(profile);
                string priorWinner = before.WinnerIndex >= 0 ? parties[before.WinnerIndex].Name : "—";
                string winner = d.WinnerIndex >= 0 ? parties[d.WinnerIndex].Name : "—";
                string winners = priorWinner == winner ? winner : $"{priorWinner} → {winner}";
                Console.WriteLine($"    {names[d.District].PadRight(nameWidth)}  {label,-12}  {PointChange(before.Turnout, d.Turnout),11}  {winners} · {before.Margin:P2} → {d.Margin:P2}");
            }
        }

        private static string PointChange(double before, double after) => $"{(after - before) * 100:+0.00;-0.00;0.00} pp";

        /// <summary>A made-up city with poor industrial districts and rich office districts, for trying the tool.</summary>
        private static void WriteSampleCity(string path)
        {
            var names = new[] { "Docklands", "Mill Row", "Old Town", "Midtown", "Riverside", "The Heights" };
            var rng = new Random(7);
            var groups = new List<VoterGroup>();
            for (int d = 0; d < names.Length; d++)
            {
                double richness = d / (double)(names.Length - 1); // 0 = poorest district, 1 = richest
                foreach (AgeBand age in Enum.GetValues(typeof(AgeBand)))
                for (int edu = 0; edu < GroupKey.EducationLevels; edu++)
                for (int wealth = 0; wealth < GroupKey.WealthBands; wealth++)
                foreach (JobSector job in Enum.GetValues(typeof(JobSector)))
                {
                    if ((age == AgeBand.Elderly) != (job == JobSector.Retired)) continue;
                    double fit = 1.0 - Math.Abs(wealth / 3.0 - richness) - 0.5 * Math.Abs(edu / 4.0 - richness);
                    if (job == JobSector.Industrial) fit += 0.4 * (1 - richness);
                    if (job == JobSector.Office) fit += 0.4 * richness;
                    double scale = job == JobSector.Student ? 0.4 : 1.0;
                    int count = (int)Math.Max(0, scale * (120 * fit + rng.Next(0, 40)));
                    if (count > 0) groups.Add(new VoterGroup(d, age, edu, wealth, job, count, (float)(35 + 40 * rng.NextDouble())));
                }
            }

            using (var writer = File.CreateText(path))
                GroupTableCsv.Write(writer, new GroupTable(names.Length, groups), names, "synthetic sample city");
        }

        private static string ResolveCity(string arg)
        {
            if (Directory.Exists(arg))
            {
                var newest = new DirectoryInfo(arg).GetFiles("*.csv").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (newest == null) throw new IOException($"No .csv files in {arg}.");
                return newest.FullName;
            }

            if (!File.Exists(arg)) throw new IOException($"File not found: {arg}");
            return arg;
        }

        private static Dictionary<string, string> ParseOptions(string[] args)
        {
            var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
                    throw new ArgumentException($"Unexpected argument '{args[i]}'. Run with --help.");
                opts[args[i].Substring(2)] = args[++i];
            }

            return opts;
        }

        private static string Get(Dictionary<string, string> opts, string key, string fallback) =>
            opts.TryGetValue(key, out var v) ? v : fallback;
    }
}
