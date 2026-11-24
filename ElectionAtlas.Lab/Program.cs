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
       dotnet run --project ElectionAtlas.Lab -- --sample [out.csv]
       dotnet run --project ElectionAtlas.Lab -- --day-samples [folder]

  <city.csv>           a file from Developer > Export city CSV
  <folder>             runs the newest .csv in that folder
  --sample [out.csv]   writes a synthetic 6-district city
  --day-samples [folder]
                       shows voting hours and district returns for the built-in
                       sample elections (close race, four parties, outside
                       residents); with a folder, also writes <name>.json fixtures
  --day [N]            after each election, shows its Election Day: hourly
                       turnout, then the count, N districts per wave (default 1)
  --day-json FILE      with --day: writes the last Election Day as JSON
  --parties 2|4        test party lineup (default 2)
  --preset NAME        UsMunicipal | UsGeneral | UkGeneral | Nordic (default UsGeneral)
  --rule NAME          PopularVote | MostDistricts (default PopularVote)
  --incumbent ID       governing party id, e.g. left (default: nobody)
  --elections N        run N elections in a row, each incumbent the last winner (default 1)
  --seed N             seed for the first election (default 1)
  --demographics       show the voter mix by district
  --lean-weights FILE  compare the default lean with name=value overrides
                      both runs use the same parties, incumbent, preset and seed
  --profiles FILE      compare elections with district-profile effects
                      use the export's # district indices, e.g. 0=UrbanCore
  --profile-strength S with --profiles: multiplies the lean offsets (default 1)
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

            if (args[0] == "--day-samples")
            {
                string outDir = args.Length > 1 ? args[1] : null;
                foreach (var sample in SampleNights.All())
                {
                    var replay = ElectionNight.ByDistrict(sample.Value);
                    var day = new ElectionDay(sample.Value);
                    NightReport.PrintDay(day, sample.Key);
                    NightReport.Print(replay, sample.Key);
                    if (outDir == null) continue;
                    string file = Path.Combine(outDir, sample.Key + ".json");
                    NightReport.WriteJson(replay, sample.Key, file, day);
                    Console.WriteLine($"  Wrote {Path.GetFullPath(file)}");
                }

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
            string dayOpt = Get(opts, "day", null);
            int wave = dayOpt == null ? 0 : int.Parse(dayOpt, CultureInfo.InvariantCulture);
            string nightJson = Get(opts, "day-json", null);
            if (dayOpt != null && wave < 1) throw new ArgumentException("--day needs 1 or more districts per wave.");
            if (nightJson != null && dayOpt == null) throw new ArgumentException("--day-json needs --day.");

            GroupTable table;
            string[] names;
            string source;
            using (var reader = File.OpenText(path))
                table = GroupTableCsv.Read(reader, out names, out source);

            var parties = SampleParties.Get(partyCount);
            var cfg = new ModelConfig();
            string leanPath = Get(opts, "lean-weights", null);
            var tuned = leanPath == null ? null : new ModelConfig { Lean = DemographicCalibration.ReadWeights(leanPath) };
            int outside = names[^1] == DistrictInfo.OutsideName ? names.Length - 1 : -1;
            string profilePath = Get(opts, "profiles", null);
            float strength = float.Parse(Get(opts, "profile-strength", "1"), CultureInfo.InvariantCulture);
            var experiment = profilePath == null ? null : new ProfileExperiment(table, profilePath, outside, strength);

            Console.WriteLine($"City: {Path.GetFileName(path)}{(source != null ? $" ({source})" : "")}");
            Console.WriteLine($"{table.TotalVoters:N0} voters in {table.Groups.Count:N0} groups, {table.DistrictCount} districts");
            Console.WriteLine($"Parties: {string.Join(", ", parties.Select(p => $"{p.Name} {p.Position}"))} · {preset} · {rule}");
            if (opts.ContainsKey("demographics")) DemographicCalibration.PrintMix(table, names);

            var poll = ElectionModel.RunPoll(table, parties, incumbent, cfg);
            Console.WriteLine($"Poll before: approval {poll.Approval:0.0} · {string.Join(" · ", parties.Select((p, i) => $"{p.Name} {poll.Intention[i]:P1}"))}");

            // Lab district ids: the index + 1 (the game packs entities instead), and the outside bucket's own id.
            var districtInfos = names.Select((n, i) => i == outside ? DistrictInfo.Outside : new DistrictInfo(i + 1, n)).ToArray();
            ElectionNight lastNight = null;
            ElectionDay lastDay = null;

            for (int e = 0; e < elections; e++)
            {
                var result = ElectionModel.RunElection(table, parties, incumbent, rule, preset, cfg, seed + (ulong)e, outsideDistrict: outside);
                if (wave > 0)
                {
                    var snapshot = ElectionSnapshot.Capture(result, parties, districtInfos, e + 1, 2026 + e, 3, cfg.Map);
                    lastNight = ElectionNight.ByDistrict(snapshot, wave);
                    lastDay = new ElectionDay(snapshot);
                    NightReport.PrintDay(lastDay, $"election {e + 1}");
                    NightReport.Print(lastNight, $"election {e + 1}");
                }
                PrintElection(e + 1, result, parties, names);
                if (tuned != null)
                {
                    var adjusted = ElectionModel.RunElection(table, parties, incumbent, rule, preset, tuned, result.Seed, outsideDistrict: outside);
                    DemographicCalibration.PrintComparison(result, adjusted, parties, names);
                }
                if (experiment != null)
                {
                    var adjusted = experiment.Run(result, parties, incumbent, rule, preset);
                    PrintProfileComparison(result, adjusted, experiment.Profiles, parties, names, outside);
                }
                incumbent = result.GoverningPartyId;
            }

            if (nightJson != null && lastNight != null)
            {
                NightReport.WriteJson(lastNight, Path.GetFileNameWithoutExtension(path), nightJson, lastDay);
                Console.WriteLine($"  Wrote {Path.GetFullPath(nightJson)}");
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
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Unexpected argument '{args[i]}'. Run with --help.");
                string key = args[i].Substring(2);
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                if (hasValue) opts[key] = args[++i];
                else if (OptionalValues.TryGetValue(key, out string fallback)) opts[key] = fallback;
                else throw new ArgumentException($"'{args[i]}' needs a value. Run with --help.");
            }

            return opts;
        }

        /// <summary>Options that may be given without a value, and the value they then take.</summary>
        private static readonly Dictionary<string, string> OptionalValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["day"] = "1",
            ["demographics"] = "true",
        };

        private static string Get(Dictionary<string, string> opts, string key, string fallback) =>
            opts.TryGetValue(key, out var v) ? v : fallback;
    }
}
