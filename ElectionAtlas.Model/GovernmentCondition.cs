using System;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// How well the government is working, from the City Hall's state. A shutdown doesn't stop elections;
    /// it costs the governing party approval and votes (design decision, 21 November 2026).
    /// The default value means "working normally".
    /// </summary>
    public readonly struct GovernmentCondition
    {
        public GovernmentCondition(float shutdownMonths, float efficiencyDeficit)
        {
            ShutdownMonths = shutdownMonths < 0f ? 0f : shutdownMonths;
            EfficiencyDeficit = efficiencyDeficit < 0f ? 0f : efficiencyDeficit > 1f ? 1f : efficiencyDeficit;
        }

        /// <summary>Months the City Hall has been shut down (switched off or at zero efficiency) this term.</summary>
        public readonly float ShutdownMonths;

        /// <summary>1 - the City Hall's efficiency while it's open: 0 = fully working, 1 = barely working.</summary>
        public readonly float EfficiencyDeficit;

        public bool IsNormal => ShutdownMonths <= 0f && EfficiencyDeficit <= 0f;

        /// <summary>Approval points the governing party loses.</summary>
        public float ApprovalPenalty(ModelConfig cfg)
        {
            return Math.Min(cfg.ShutdownApprovalCap, ShutdownMonths * cfg.ShutdownApprovalPerMonth)
                + EfficiencyDeficit * cfg.LowEfficiencyApprovalPoints;
        }

        /// <summary>Utility the governing party loses in every voter's choice (same units as the incumbent bonus).</summary>
        public float IncumbentPenalty(ModelConfig cfg)
        {
            return Math.Min(cfg.ShutdownIncumbentCap, ShutdownMonths * cfg.ShutdownIncumbentPerMonth)
                + EfficiencyDeficit * cfg.LowEfficiencyIncumbent;
        }
    }
}
