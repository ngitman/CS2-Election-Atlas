using System;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// Turnout per group is a logistic curve: sigmoid(intercept + traits). The intercept is solved for each
    /// election so the city's average turnout lands exactly on the target, whatever the city's make-up.
    /// </summary>
    public static class TurnoutModel
    {
        private const int kBisectionSteps = 50;
        private const double kInterceptRange = 20.0;

        /// <summary>A group's log-odds of voting, before the intercept.</summary>
        public static double Score(in VoterGroup g, TurnoutWeights w, float elderlyBoost)
        {
            double s = g.Education * w.PerEducationLevel
                + g.WealthBand * w.PerWealthBand
                + (g.AvgHappiness - 50f) / 50f * w.Happiness;
            if (g.Age == AgeBand.Elderly) s += elderlyBoost;
            if (g.Job == JobSector.Unemployed) s += w.Unemployed;
            return s;
        }

        /// <summary>Solves the intercept so the count-weighted mean turnout of the table equals the target.</summary>
        public static TurnoutCurve Calibrate(GroupTable table, TurnoutWeights w, float elderlyBoost, float target)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            double goal = Math.Min(0.99, Math.Max(0.01, target));

            var groups = table.Groups;
            if (groups.Count == 0)
                return new TurnoutCurve(Logit(goal), w, elderlyBoost);

            var scores = new double[groups.Count];
            var weights = new double[groups.Count];
            double totalWeight = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                scores[i] = Score(g, w, elderlyBoost);
                weights[i] = g.Count;
                totalWeight += g.Count;
            }

            // Mean turnout rises monotonically with the intercept, so bisection always converges.
            double lo = -kInterceptRange, hi = kInterceptRange;
            for (int step = 0; step < kBisectionSteps; step++)
            {
                double mid = (lo + hi) * 0.5;
                double sum = 0;
                for (int i = 0; i < scores.Length; i++)
                    sum += weights[i] * Sigmoid(mid + scores[i]);

                if (sum / totalWeight < goal) lo = mid;
                else hi = mid;
            }

            return new TurnoutCurve((lo + hi) * 0.5, w, elderlyBoost);
        }

        internal static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));

        private static double Logit(double p) => Math.Log(p / (1.0 - p));
    }

    /// <summary>A turnout curve calibrated for one election.</summary>
    public sealed class TurnoutCurve
    {
        private readonly TurnoutWeights m_Weights;
        private readonly float m_ElderlyBoost;

        internal TurnoutCurve(double intercept, TurnoutWeights weights, float elderlyBoost)
        {
            Intercept = intercept;
            m_Weights = weights;
            m_ElderlyBoost = elderlyBoost;
        }

        public double Intercept { get; }

        /// <summary>Share of the group that votes, 0 to 1.</summary>
        public float Turnout(in VoterGroup g)
        {
            return (float)TurnoutModel.Sigmoid(Intercept + TurnoutModel.Score(g, m_Weights, m_ElderlyBoost));
        }
    }
}
