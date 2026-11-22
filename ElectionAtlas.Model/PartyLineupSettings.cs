using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace ElectionAtlas.Model
{
    /// <summary>Validates editor submissions and stores lineups using stable party IDs.</summary>
    public static class PartyLineupSettings
    {
        public const int MinParties = 2;
        // Keep the editor within the bounds the election save reader accepts.
        public const int MaxParties = 64;
        public const int MaxNameLength = 40;
        private const int kMaxJsonLength = 32768;

        public static Party[] ReadSaved(string json) => Read(json, null);

        public static Party[] ReadDraft(string json, IEnumerable<Party> current)
        {
            var knownIds = new HashSet<string>(current.Select(p => p.Id), StringComparer.Ordinal);
            foreach (var party in SampleParties.Two().Concat(SampleParties.Four())) knownIds.Add(party.Id);
            return Read(json, knownIds);
        }

        public static string Write(IEnumerable<Party> parties)
        {
            var rows = parties.Select(p => new Row
            {
                Id = p.Id,
                Name = p.Name,
                Color = p.Color.ToString("x6", CultureInfo.InvariantCulture),
                Economic = p.Position.Economic,
                Social = p.Position.Social,
            }).ToArray();
            using var stream = new MemoryStream();
            new DataContractJsonSerializer(typeof(Row[])).WriteObject(stream, rows);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static Party[] Read(string json, HashSet<string> knownIds)
        {
            if (string.IsNullOrEmpty(json) || json.Length > kMaxJsonLength)
                throw new FormatException("Invalid party lineup data.");

            Row[] rows;
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                rows = (Row[])new DataContractJsonSerializer(typeof(Row[])).ReadObject(stream);
            }
            catch (Exception ex) when (ex is SerializationException || ex is XmlException)
            {
                throw new FormatException("Invalid party lineup data.", ex);
            }

            if (rows == null || rows.Length < MinParties || rows.Length > MaxParties)
                throw new FormatException($"A lineup needs {MinParties}–{MaxParties} parties.");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parties = new Party[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                string name = row?.Name?.Trim();
                if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || name.Any(char.IsControl))
                    throw new FormatException($"Party {i + 1} needs a name of 1–{MaxNameLength} characters.");
                if (!names.Add(name)) throw new FormatException($"Party names must be unique. “{name}” is used twice.");

                string color = row.Color?.Trim();
                if (color?.StartsWith("#", StringComparison.Ordinal) == true) color = color.Substring(1);
                if (color?.Length != 6 || !uint.TryParse(color, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
                    throw new FormatException($"Enter a six-digit hex color for “{name}”, such as #3366CC.");
                if (!ValidPosition(row.Economic) || !ValidPosition(row.Social))
                    throw new FormatException($"The positions for “{name}” must be between -1 and +1.");

                string id = row.Id;
                if (knownIds != null)
                {
                    if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
                    else if (!knownIds.Contains(id)) throw new FormatException("A party in this draft is no longer available.");
                }
                if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !ids.Add(id))
                    throw new FormatException("The lineup contains an invalid or duplicate party ID.");

                parties[i] = new Party(id, name, rgb, new Position(row.Economic, row.Social));
            }

            return parties;
        }

        private static bool ValidPosition(float value) => value >= Position.Min && value <= Position.Max;

        [DataContract]
        private sealed class Row
        {
            [DataMember(Name = "id")] public string Id { get; set; }
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "color")] public string Color { get; set; }
            [DataMember(Name = "economic", IsRequired = true)] public float Economic { get; set; }
            [DataMember(Name = "social", IsRequired = true)] public float Social { get; set; }
        }
    }
}
