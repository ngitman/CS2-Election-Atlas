using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// A captured city's voter groups as CSV, so the model can be run on real cities without launching the game.
    /// Format (v1):
    /// <code>
    /// # ElectionAtlas voter groups v1
    /// # source: My City, 2026-11-21 19:30
    /// # district 0: Downtown
    /// district,age,education,wealth,job,count,avgHappiness
    /// 0,Adult,2,1,Commercial,153,61.25
    /// </code>
    /// </summary>
    public static class GroupTableCsv
    {
        public const string Magic = "# ElectionAtlas voter groups v1";
        private const string Header = "district,age,education,wealth,job,count,avgHappiness";
        private const string DistrictPrefix = "# district ";
        private const string SourcePrefix = "# source: ";

        public static void Write(TextWriter writer, GroupTable table, IReadOnlyList<string> districtNames, string source)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (table == null) throw new ArgumentNullException(nameof(table));

            var inv = CultureInfo.InvariantCulture;
            writer.WriteLine(Magic);
            if (!string.IsNullOrEmpty(source))
                writer.WriteLine(SourcePrefix + OneLine(source));
            for (int d = 0; d < table.DistrictCount; d++)
            {
                string name = districtNames != null && d < districtNames.Count ? districtNames[d] : null;
                writer.WriteLine(DistrictPrefix + d.ToString(inv) + ": " + OneLine(name ?? $"District {d}"));
            }

            writer.WriteLine(Header);
            foreach (var g in table.Groups)
            {
                writer.WriteLine(string.Join(",",
                    g.District.ToString(inv), g.Age.ToString(), g.Education.ToString(inv), g.WealthBand.ToString(inv),
                    g.Job.ToString(), g.Count.ToString(inv), g.AvgHappiness.ToString("0.###", inv)));
            }
        }

        public static GroupTable Read(TextReader reader, out string[] districtNames, out string source)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            var inv = CultureInfo.InvariantCulture;
            var names = new SortedDictionary<int, string>();
            var groups = new List<VoterGroup>();
            source = null;
            int lineNo = 0;
            bool sawMagic = false;
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                lineNo++;
                if (line.Length == 0) continue;

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    if (line == Magic) sawMagic = true;
                    else if (line.StartsWith(SourcePrefix, StringComparison.Ordinal)) source = line.Substring(SourcePrefix.Length);
                    else if (line.StartsWith(DistrictPrefix, StringComparison.Ordinal))
                    {
                        string rest = line.Substring(DistrictPrefix.Length);
                        int colon = rest.IndexOf(':');
                        if (colon > 0 && int.TryParse(rest.Substring(0, colon), NumberStyles.Integer, inv, out int idx))
                            names[idx] = rest.Substring(colon + 1).Trim();
                    }

                    continue;
                }

                if (line == Header) continue;

                var f = line.Split(',');
                if (f.Length != 7)
                    throw new FormatException($"Line {lineNo}: expected 7 fields, got {f.Length}.");
                try
                {
                    groups.Add(new VoterGroup(
                        int.Parse(f[0], inv),
                        (AgeBand)Enum.Parse(typeof(AgeBand), f[1]),
                        int.Parse(f[2], inv),
                        int.Parse(f[3], inv),
                        (JobSector)Enum.Parse(typeof(JobSector), f[4]),
                        int.Parse(f[5], inv),
                        float.Parse(f[6], inv)));
                }
                catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
                {
                    throw new FormatException($"Line {lineNo}: {ex.Message}", ex);
                }
            }

            if (!sawMagic)
                throw new FormatException("Not an ElectionAtlas voter-groups file (missing the v1 header line).");

            int districtCount = 1;
            foreach (var g in groups) districtCount = Math.Max(districtCount, g.District + 1);
            foreach (var idx in names.Keys) districtCount = Math.Max(districtCount, idx + 1);

            districtNames = new string[districtCount];
            for (int d = 0; d < districtCount; d++)
                districtNames[d] = names.TryGetValue(d, out var n) ? n : $"District {d}";

            return new GroupTable(districtCount, groups);
        }

        private static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ');
    }
}
