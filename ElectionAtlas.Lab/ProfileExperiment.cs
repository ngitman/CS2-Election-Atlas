using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ElectionAtlas.Model;

namespace ElectionAtlas.Lab
{
    /// <summary>Compares a baseline election with small, provisional district-profile effects.</summary>
    internal sealed class ProfileExperiment
    {
        private readonly GroupTable m_City;
        private readonly (GroupTable Table, float Turnout, float Social)[] m_Adjustments;
        private readonly double m_TurnoutCenter;

        public DistrictProfile[] Profiles { get; }

        public ProfileExperiment(GroupTable city, string path, int outsideDistrict)
        {
            m_City = city;
            Profiles = ReadProfiles(city.DistrictCount, path, outsideDistrict);
            var adjustments = new List<(GroupTable Table, float Turnout, float Social)>();
            foreach (var groups in city.Groups.GroupBy(g => Profiles[g.District]))
            {
                var (turnout, social) = Effects(groups.Key);
                if (turnout == 0f && social == 0f) continue;
                var table = new GroupTable(city.DistrictCount, groups);
                adjustments.Add((table, turnout, social));
            }

            m_Adjustments = adjustments.ToArray();
            long voters = m_Adjustments.Sum(a => a.Table.TotalVoters);
            m_TurnoutCenter = voters > 0 ? m_Adjustments.Sum(a => (double)a.Turnout * a.Table.TotalVoters) / voters : 0;
        }

        public DistrictResult[] Run(ElectionResult baseline, IReadOnlyList<Party> parties, string incumbent,
            GoverningRule rule, TurnoutPreset preset)
        {
            var districts = baseline.Districts.ToArray();
            if (m_Adjustments.Length == 0) return districts;

            // The Lab uses default model weights in both runs. Only the profile run changes these local weights.
            var cfg = new ModelConfig { TurnoutVariation = 0f };
            var data = cfg.GetPreset(preset);
            var curve = TurnoutModel.Calibrate(m_City, cfg.Turnout, data.ElderlyBoost, baseline.TargetTurnout);
            float adultSocial = cfg.Lean.AdultSocial;
            float elderlySocial = cfg.Lean.ElderlySocial;

            foreach (var (table, turnout, social) in m_Adjustments)
            {
                double expected = table.Groups.Sum(g => g.Count * (double)curve.Turnout(g));
                data.Target = (float)(expected / table.TotalVoters + turnout - m_TurnoutCenter);
                cfg.Lean.AdultSocial = adultSocial + social;
                cfg.Lean.ElderlySocial = elderlySocial + social;

                var result = ElectionModel.RunElection(table, parties, incumbent, rule, preset, cfg, baseline.Seed);
                foreach (var district in result.Districts)
                    if (district.Eligible > 0) districts[district.District] = district;
            }

            return districts;
        }

        // Turnout offsets are shares, applied to each profile's mean and centered on the city's preset.
        private static (float Turnout, float Social) Effects(DistrictProfile profile) => profile switch
        {
            DistrictProfile.UrbanCore => (-0.006f, -0.035f),
            DistrictProfile.Urban => (-0.003f, -0.020f),
            DistrictProfile.Suburban => (0.005f, 0.010f),
            DistrictProfile.Rural => (0.004f, 0.030f),
            _ => (0f, 0f),
        };

        private static DistrictProfile[] ReadProfiles(int districtCount, string path, int outsideDistrict)
        {
            var profiles = new DistrictProfile[districtCount];
            var seen = new HashSet<int>();
            int lineNumber = 0;
            foreach (string rawLine in File.ReadLines(path))
            {
                lineNumber++;
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int district)
                    || district >= districtCount || !Enum.TryParse(parts[1], true, out DistrictProfile profile)
                    || !string.Equals(parts[1], Enum.GetName(profile), StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"{path}:{lineNumber}: expected an exported district index and a profile, e.g. 0=UrbanCore.");
                if (district == outsideDistrict)
                    throw new FormatException($"{path}:{lineNumber}: residents outside districts have no district profile.");
                if (!seen.Add(district))
                    throw new FormatException($"{path}:{lineNumber}: district {district} already has a profile.");

                profiles[district] = profile;
            }

            return profiles;
        }
    }
}
