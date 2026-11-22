using System;

namespace ElectionAtlas.Model
{
    /// <summary>What kind of place a district is, from what's built in it.</summary>
    public enum DistrictProfile
    {
        /// <summary>Too little built yet to say.</summary>
        Unknown,
        UrbanCore,
        Urban,
        Suburban,
        Rural,
        Industrial,
        Business,
    }

    /// <summary>
    /// What the census saw in one district: its homes, how densely they're built, and the jobs located there.
    /// Gathered in the same pass that reads voters, so profiles cost no extra scan of the city.
    /// </summary>
    public struct DistrictLandUse
    {
        /// <summary>District surface area in square meters.</summary>
        public float AreaM2;

        /// <summary>Households living in the district.</summary>
        public int Households;

        /// <summary>
        /// Sum over those households of their building's homes per lot cell (residential properties / lot cells).
        /// Detached houses are well under 1, row houses around 1, towers several.
        /// </summary>
        public float HomeDensitySum;

        /// <summary>Jobs located in the district (by workplace), split by sector.</summary>
        public int IndustrialJobs, OfficeJobs, CommercialJobs;

        public int Jobs => IndustrialJobs + OfficeJobs + CommercialJobs;

        /// <summary>Average homes per lot cell of the buildings people live in (0 with no households).</summary>
        public float HomeDensity => Households > 0 ? HomeDensitySum / Households : 0f;

        /// <summary>Households per hectare of district area (0 with no area).</summary>
        public float HouseholdsPerHectare => AreaM2 > 0f ? Households / (AreaM2 / 10000f) : 0f;
    }

    /// <summary>Thresholds for <see cref="DistrictProfiles.Classify"/>. First guesses; calibrated on real cities.</summary>
    public sealed class ProfileConfig
    {
        /// <summary>Below this many households and jobs combined, a district is Unknown.</summary>
        public int MinActivity = 20;

        /// <summary>A district is a workplace (Industrial or Business) when it has this many jobs per household or more.</summary>
        public float WorkplaceJobsPerHousehold = 2f;

        /// <summary>Share of a workplace district's jobs that must be industrial for it to be Industrial (else Business).</summary>
        public float IndustrialShare = 0.5f;

        /// <summary>Average homes per lot cell at or above which homes are towers (Urban core) or row houses (Urban).</summary>
        public float UrbanCoreHomeDensity = 1.5f;
        public float UrbanHomeDensity = 0.5f;

        /// <summary>Households per hectare at or above which a district is Urban core whatever its lots look like
        /// (calibrated on a test city: tower districts ran 73-108/ha, suburbs 7-11/ha).</summary>
        public float UrbanCoreHouseholdsPerHectare = 60f;

        /// <summary>Households per hectare below which a low-density district is Rural rather than Suburban.</summary>
        public float RuralHouseholdsPerHectare = 2f;
    }

    public static class DistrictProfiles
    {
        public static DistrictProfile Classify(in DistrictLandUse d, ProfileConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            int jobs = d.Jobs;
            if (d.Households + jobs < cfg.MinActivity) return DistrictProfile.Unknown;

            if (jobs >= cfg.WorkplaceJobsPerHousehold * Math.Max(1, d.Households))
                return d.IndustrialJobs >= cfg.IndustrialShare * jobs ? DistrictProfile.Industrial : DistrictProfile.Business;

            float density = d.HomeDensity;
            if (density >= cfg.UrbanCoreHomeDensity || d.HouseholdsPerHectare >= cfg.UrbanCoreHouseholdsPerHectare) return DistrictProfile.UrbanCore;
            if (density >= cfg.UrbanHomeDensity) return DistrictProfile.Urban;
            return d.HouseholdsPerHectare < cfg.RuralHouseholdsPerHectare ? DistrictProfile.Rural : DistrictProfile.Suburban;
        }

        /// <summary>The label players see.</summary>
        public static string Label(DistrictProfile p)
        {
            switch (p)
            {
                case DistrictProfile.UrbanCore: return "Urban core";
                case DistrictProfile.Urban: return "Urban";
                case DistrictProfile.Suburban: return "Suburban";
                case DistrictProfile.Rural: return "Rural";
                case DistrictProfile.Industrial: return "Industrial";
                case DistrictProfile.Business: return "Business";
                default: return "";
            }
        }
    }
}
