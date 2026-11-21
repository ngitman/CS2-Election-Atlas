using System;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// Every tunable number in the model. Defaults are starting values for playtesting, not real-world data.
    /// Public fields so the mod can fill them from a JSON file without a rebuild.
    /// </summary>
    public sealed class ModelConfig
    {
        public LeanWeights Lean = new LeanWeights();
        public TurnoutWeights Turnout = new TurnoutWeights();
        public MapTint Map = new MapTint();

        /// <summary>How strongly voters stick to the nearest party (k in the vote-share formula).</summary>
        public float DistanceWeight = 3f;

        /// <summary>Incumbent bonus at 100 happiness (and penalty at 0): (happiness - 50) / 50 x this.</summary>
        public float IncumbentWeight = 0.8f;

        /// <summary>Approval points added when a group clearly prefers the governing party, or removed when it never would vote for it.</summary>
        public float ApprovalAlignmentPoints = 15f;

        /// <summary>Largest random shift of an election's citywide turnout, as a fraction (0.03 = 3 points).</summary>
        public float TurnoutVariation = 0.03f;

        // Approximate real-world averages; check against official sources before release.
        public TurnoutPresetData UsMunicipal = new TurnoutPresetData(0.25f, 1.2f);
        public TurnoutPresetData UsGeneral = new TurnoutPresetData(0.65f, 1.0f);
        public TurnoutPresetData UkGeneral = new TurnoutPresetData(0.67f, 0.7f);
        public TurnoutPresetData Nordic = new TurnoutPresetData(0.80f, 0.4f);

        public TurnoutPresetData GetPreset(TurnoutPreset preset)
        {
            switch (preset)
            {
                case TurnoutPreset.UsMunicipal: return UsMunicipal;
                case TurnoutPreset.UsGeneral: return UsGeneral;
                case TurnoutPreset.UkGeneral: return UkGeneral;
                case TurnoutPreset.Nordic: return Nordic;
                default: throw new ArgumentOutOfRangeException(nameof(preset));
            }
        }
    }

    /// <summary>How each trait moves a voter group on the two axes. Wealth and education are interpolated across their bands.</summary>
    public sealed class LeanWeights
    {
        public float WealthEconomicPoorest = -0.4f;
        public float WealthEconomicRichest = 0.4f;

        public float EducationEconomicNone = 0.1f;
        public float EducationEconomicTop = -0.1f;
        public float EducationSocialNone = 0.3f;
        public float EducationSocialTop = -0.4f;

        public float AdultSocial = 0f;
        public float ElderlySocial = 0.3f;

        public float CommercialEconomic = 0.05f;
        public float CommercialSocial = 0f;
        public float IndustrialEconomic = -0.15f;
        public float IndustrialSocial = 0.1f;
        public float OfficeEconomic = 0.1f;
        public float OfficeSocial = -0.1f;
        public float UnemployedEconomic = -0.2f;
        public float UnemployedSocial = 0f;
        public float RetiredEconomic = 0f;
        public float RetiredSocial = 0f;
    }

    /// <summary>Terms of the turnout curve, in log-odds. The intercept is solved per election to hit the preset's target.</summary>
    public sealed class TurnoutWeights
    {
        public float PerEducationLevel = 0.25f;
        public float PerWealthBand = 0.2f;

        /// <summary>Applied to (happiness - 50) / 50: unhappy voters stay home a little more.</summary>
        public float Happiness = 0.5f;

        public float Unemployed = -0.3f;
    }

    public sealed class TurnoutPresetData
    {
        public TurnoutPresetData(float target, float elderlyBoost)
        {
            Target = target;
            ElderlyBoost = elderlyBoost;
        }

        /// <summary>Citywide average turnout the curve is calibrated to, 0 to 1.</summary>
        public float Target;

        /// <summary>Extra log-odds of voting for the Elderly band: the age gap.</summary>
        public float ElderlyBoost;
    }
}
