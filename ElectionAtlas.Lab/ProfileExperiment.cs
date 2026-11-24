using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ElectionAtlas.Model;

namespace ElectionAtlas.Lab
{
    /// <summary>
    /// Compares a baseline election with provisional district-profile effects on both axes: where a district sits
    /// (urban core, suburb, countryside, industry) moves its voters' social and economic lean, on top of who they are.
    /// </summary>
    internal sealed class ProfileExperiment
    {
        private readonly GroupTable m_City;
        private readonly (GroupTable Table, float Turnout, float Social, float Economic)[] m_Adjustments;
        private readonly double m_TurnoutCenter;

        public DistrictProfile[] Profiles { get; }

        /// <param name="strength">Multiplies the lean offsets (not turnout), for calibration sweeps; 1 is the proposal.</param>
        public ProfileExperiment(GroupTable city, string path, int outsideDistrict, float strength = 1f)
        {
            m_City = city;
            Profiles = ReadProfiles(city.DistrictCount, path, outsideDistrict);
            var adjustments = new List<(GroupTable Table, float Turnout, float Social, float Economic)>();
            foreach (var groups in city.Groups.GroupBy(g => Profiles[g.District]))
            {
                var (turnout, social, economic) = Effects(groups.Key);
                social *= strength;
                economic *= strength;
                if (turnout == 0f && social == 0f && economic == 0f) continue;
                var table = new GroupTable(city.DistrictCount, groups);
                adjustments.Add((table, turnout, social, economic));
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
            float poorest = cfg.Lean.WealthEconomicPoorest;
            float richest = cfg.Lean.WealthEconomicRichest;

            foreach (var (table, turnout, social, economic) in m_Adjustments)
            {
                double expected = table.Groups.Sum(g => g.Count * (double)curve.Turnout(g));
                data.Target = (float)(expected / table.TotalVoters + turnout - m_TurnoutCenter);
                cfg.Lean.AdultSocial = adultSocial + social;
                cfg.Lean.ElderlySocial = elderlySocial + social;
                // Shifting both wealth endpoints moves every voter's economic lean by the same amount.
                cfg.Lean.WealthEconomicPoorest = poorest + economic;
                cfg.Lean.WealthEconomicRichest = richest + economic;

                var result = ElectionModel.RunElection(table, parties, incumbent, rule, preset, cfg, baseline.Seed);
                foreach (var district in result.Districts)
                    if (district.Eligible > 0) districts[district.District] = district;
            }

            return districts;
        }

        // Turnout offsets are shares, applied to each profile's mean and centered on the city's preset. Lean offsets
        // are on the -1..+1 axes (negative = left / progressive).
        // Third proposal (2026-11-22). The owner: suburbs are not a monolith (some lean far left, some right), and
        // the lean should come from who lives there (income, careers, education, age). So place keeps only a modest
        // residual (the urban-rural gap that remains after demographics), and suburbs get none. The polarization
        // itself is to come from stronger demographic weights and a per-household housing-density trait (HANDOFF).
        private static (float Turnout, float Social, float Economic) Effects(DistrictProfile profile) => profile switch
        {
            DistrictProfile.UrbanCore => (-0.006f, -0.05f, -0.10f),
            DistrictProfile.Urban => (-0.003f, -0.03f, -0.05f),
            DistrictProfile.Suburban => (0.005f, 0f, 0f),
            DistrictProfile.Rural => (0.004f, 0.10f, 0.06f),
            DistrictProfile.Industrial => (0f, 0.05f, 0f),
            DistrictProfile.Business => (0f, 0f, 0.05f),
            _ => (0f, 0f, 0f),
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
