using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ElectionAtlas.Model;

namespace ElectionAtlas.Lab
{
    /// <summary>Prints an <see cref="ElectionNight"/> replay frame by frame, and writes it as JSON for UI work.</summary>
    internal static class NightReport
    {
        private const int ReportingWidth = 46;

        public static void Print(ElectionNight night, string title)
        {
            var s = night.Snapshot;
            var names = s.Parties.Select(p => p.Name).ToArray();
            int col = Math.Max(14, names.Max(n => n.Length) + 1);

            Console.WriteLine();
            Console.WriteLine($"Election night · {title} · {night.UnitCount} units, {Plural(night.WaveSize, "unit")} per frame · {s.Rule} · {night.SeatCount} seats · {BuildTime(s, night.WaveSize)}");

            var header = new StringBuilder($"  {"Frame",5}  {"In",7}  {"Seats",7}  {"Reporting".PadRight(ReportingWidth)}");
            foreach (var n in names) header.Append("  ").Append(n.PadLeft(col));
            header.Append("  Seats      Leader");
            Console.WriteLine(header.ToString());

            for (int f = 0; f < night.FrameCount; f++)
            {
                var frame = night[f];
                var line = new StringBuilder();
                line.Append($"  {f,5}  {$"{frame.UnitsReported}/{frame.UnitCount}",7}  {$"{frame.SeatsReported}/{frame.SeatCount}",7}  ");
                line.Append(Fit(Reporting(night, frame), ReportingWidth));

                long votes = frame.Votes;
                for (int p = 0; p < names.Length; p++)
                {
                    long v = frame.PartyVotes(p);
                    string share = votes > 0 ? ((double)v / votes).ToString("P1") : "-";
                    line.Append("  ").Append($"{share} {v:N0}".PadLeft(col));
                }

                string seats = string.Join("-", Enumerable.Range(0, names.Length).Select(frame.Seats));
                line.Append("  ").Append(seats.PadRight(9)).Append("  ").Append(PartyName(s, frame.LeaderIndex));
                int popular = frame.City.WinnerIndex;
                if (popular != frame.LeaderIndex && popular >= 0) line.Append($" (popular vote: {PartyName(s, popular)})");
                if (frame.City.IsTie) line.Append(" (vote tied)");
                if (frame.LeadChanged) line.Append("  * lead change");
                if (frame.IsFinal) line.Append("  [final]");
                Console.WriteLine(line.ToString());
            }

            Console.WriteLine($"  Result: {PartyName(s, night.Final.LeaderIndex)} governs (archived) · {Plural(night.LeadChanges, "lead change")} · {Verify(night)}");
        }

        /// <summary>Checks the final frame against the archive, the way the tests do.</summary>
        private static string Verify(ElectionNight night)
        {
            var s = night.Snapshot;
            var final = night.Final;
            var votes = new long[night.PartyCount];
            var seats = new int[night.PartyCount];
            final.CopyTo(votes, seats);

            bool totals = votes.SequenceEqual(s.PartyVotes) && seats.SequenceEqual(s.DistrictsWon)
                && final.Votes == s.Votes && final.City.Eligible == s.Eligible;
            bool leader = ElectionModel.PickGoverningParty(s.Rule, votes, seats) == s.GoverningPartyIndex;
            return (totals ? "final totals match the archive" : "FINAL TOTALS DIFFER FROM THE ARCHIVE")
                + (leader ? "" : " · THE FULL COUNT DISAGREES WITH THE ARCHIVED GOVERNING PARTY");
        }

        private static string BuildTime(ElectionSnapshot s, int wave)
        {
            var sw = Stopwatch.StartNew();
            ElectionNight.ByDistrict(s, wave);
            double cold = sw.Elapsed.TotalMilliseconds;

            const int runs = 2000;
            sw.Restart();
            for (int i = 0; i < runs; i++) ElectionNight.ByDistrict(s, wave);
            double warm = sw.Elapsed.TotalMilliseconds * 1000.0 / runs;
            return $"built in {warm:0.0} µs warm, {cold:0.00} ms cold";
        }

        private static string Reporting(ElectionNight night, ReportFrame frame)
        {
            if (frame.Index == 0)
                return frame.WaveCount == 0 ? "polls close" : $"polls close ({Plural(frame.WaveCount, "unit")} with no voters in)";
            var s = night.Snapshot;
            var first = night.Units[frame.WaveUnit(0)];
            if (frame.WaveCount > 1) return $"{first.Name} +{frame.WaveCount - 1} more";

            var t = first.Tally;
            string seat = s.Districts[first.RegionIndex].IsSeat ? "" : " (no seat)";
            if (!t.HasWinner) return $"{first.Name}: no votes{seat}";
            if (t.IsTie) return $"{first.Name}: tie{seat}";
            return $"{first.Name}: {s.Parties[t.WinnerIndex].Name} +{t.Margin * 100f:0.0}p{seat}";
        }

        private static string PartyName(ElectionSnapshot s, int index) => index >= 0 ? s.Parties[index].Name : "-";

        private static string Fit(string text, int width) =>
            text.Length <= width ? text.PadRight(width) : text.Substring(0, width - 1) + "…";

        private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

        /// <summary>
        /// The replay as JSON with fixed shapes (every object of a kind has the same properties), for building the
        /// Election Night view before the game bindings exist. Colors are "#rrggbb"; the seed is a string because
        /// JavaScript numbers can't hold every 64-bit value.
        /// </summary>
        public static void WriteJson(ElectionNight night, string name, string path, ElectionDay day = null)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var s = night.Snapshot;
            using var stream = File.Create(path);
            using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

            w.WriteStartObject();
            w.WriteString("name", name);

            w.WriteStartObject("election");
            w.WriteNumber("number", s.Number);
            w.WriteNumber("year", s.Year);
            w.WriteNumber("month", s.Month);
            w.WriteString("rule", s.Rule.ToString());
            w.WriteString("seed", s.Seed.ToString());
            w.WriteNumber("governingPartyIndex", s.GoverningPartyIndex);
            w.WriteNumber("eligible", s.Eligible);
            w.WriteNumber("votes", s.Votes);
            Longs(w, "partyVotes", s.PartyVotes);
            Ints(w, "districtsWon", s.DistrictsWon);
            w.WriteEndObject();

            w.WriteStartArray("parties");
            foreach (var p in s.Parties)
            {
                w.WriteStartObject();
                w.WriteString("id", p.Id);
                w.WriteString("name", p.Name);
                w.WriteString("color", Color(p.Color));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("regions");
            for (int r = 0; r < s.Districts.Length; r++)
            {
                var d = s.Districts[r];
                w.WriteStartObject();
                w.WriteNumber("index", r);
                w.WriteNumber("id", d.DistrictId);
                w.WriteString("name", d.Name);
                w.WriteBoolean("isSeat", d.IsSeat);
                w.WriteNumber("eligible", d.Eligible);
                w.WriteNumber("votes", d.Votes);
                Longs(w, "partyVotes", d.PartyVotes);
                w.WriteNumber("winnerIndex", d.WinnerIndex);
                w.WriteNumber("margin", Math.Round(d.Margin, 4));
                w.WriteString("mapColor", Color(d.MapColor));
                w.WriteNumber("reportFrame", night.RegionReportFrame(r));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("units");
            for (int u = 0; u < night.UnitCount; u++)
            {
                var unit = night.Units[u];
                w.WriteStartObject();
                w.WriteNumber("index", u);
                w.WriteNumber("id", unit.Id);
                w.WriteString("name", unit.Name);
                w.WriteNumber("regionIndex", unit.RegionIndex);
                w.WriteNumber("eligible", unit.Eligible);
                w.WriteNumber("votes", unit.Votes);
                Longs(w, "partyVotes", unit.PartyVotes);
                w.WriteNumber("winnerIndex", unit.Tally.WinnerIndex);
                w.WriteNumber("revealPosition", night.RevealPosition(u));
                w.WriteNumber("reportFrame", night.UnitReportFrame(u));
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteNumber("waveSize", night.WaveSize);
            w.WriteNumber("seatCount", night.SeatCount);
            w.WriteNumber("leadChanges", night.LeadChanges);

            var votes = new long[night.PartyCount];
            var seats = new int[night.PartyCount];
            w.WriteStartArray("frames");
            for (int f = 0; f < night.FrameCount; f++)
            {
                var frame = night[f];
                frame.CopyTo(votes, seats);
                var city = frame.City;
                w.WriteStartObject();
                w.WriteNumber("index", f);
                w.WriteBoolean("isFinal", frame.IsFinal);
                w.WriteStartArray("wave");
                for (int k = 0; k < frame.WaveCount; k++) w.WriteNumberValue(frame.WaveUnit(k));
                w.WriteEndArray();
                w.WriteNumber("unitsReported", frame.UnitsReported);
                w.WriteNumber("seatsReported", frame.SeatsReported);
                w.WriteNumber("eligible", city.Eligible);
                w.WriteNumber("votes", city.Votes);
                Longs(w, "partyVotes", votes);
                Ints(w, "seats", seats);
                w.WriteNumber("leaderIndex", frame.LeaderIndex);
                w.WriteNumber("popularLeaderIndex", city.WinnerIndex);
                w.WriteNumber("runnerUpIndex", city.RunnerUpIndex);
                w.WriteNumber("leadVotes", city.LeadVotes);
                w.WriteBoolean("leadChanged", frame.LeadChanged);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            if (day != null) WriteDay(w, day);
            w.WriteEndObject();
        }

        public static void PrintDay(ElectionDay day, string title)
        {
            Console.WriteLine();
            Console.WriteLine($"Election Day · {title}");
            Console.WriteLine($"  {"Time",-5}  {"Polls",-6}  {"Ballots cast",12}  {"Turnout",8}");
            for (int hour = ElectionDay.OpeningHour; hour <= ElectionDay.ClosingHour; hour++)
            {
                long ballots = day.BallotsCastAt(hour);
                double turnout = day.Snapshot.Eligible > 0 ? (double)ballots / day.Snapshot.Eligible : 0;
                string status = hour == ElectionDay.ClosingHour ? "Closed" : "Open";
                Console.WriteLine($"  {hour:00}:00  {status,-6}  {ballots,12:N0}  {turnout,8:P1}");
            }
        }

        private static void WriteDay(Utf8JsonWriter w, ElectionDay day)
        {
            w.WriteStartArray("dayFrames");
            for (int hour = ElectionDay.OpeningHour; hour <= ElectionDay.ClosingHour; hour++)
            {
                w.WriteStartObject();
                w.WriteNumber("hour", hour);
                w.WriteBoolean("pollsOpen", hour < ElectionDay.ClosingHour);
                w.WriteNumber("ballotsCast", day.BallotsCastAt(hour));
                w.WriteStartArray("districtBallotsCast");
                for (int d = 0; d < day.Snapshot.Districts.Length; d++)
                    w.WriteNumberValue(day.DistrictBallotsCastAt(d, hour));
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        private static void Longs(Utf8JsonWriter w, string name, long[] values)
        {
            w.WriteStartArray(name);
            foreach (long v in values) w.WriteNumberValue(v);
            w.WriteEndArray();
        }

        private static void Ints(Utf8JsonWriter w, string name, int[] values)
        {
            w.WriteStartArray(name);
            foreach (int v in values) w.WriteNumberValue(v);
            w.WriteEndArray();
        }

        private static string Color(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("x6");
    }
}
