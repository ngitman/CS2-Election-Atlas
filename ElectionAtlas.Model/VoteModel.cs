using System;
using System.Collections.Generic;

namespace ElectionAtlas.Model
{
    /// <summary>
    /// Vote choice for one group: P(party i) = exp(-k * d_i + b_i) / sum_j exp(-k * d_j + b_j),
    /// where d is the distance to the party and b is the incumbent bonus (governing party only).
    /// </summary>
    public static class VoteModel
    {
        /// <summary>Bonus for the governing party: positive when the group is happy, negative when not.</summary>
        public static float IncumbentBonus(float avgHappiness, ModelConfig cfg)
        {
            return (avgHappiness - 50f) / 50f * cfg.IncumbentWeight;
        }

        /// <summary>
        /// Writes each party's share of the group's vote into <paramref name="sharesOut"/> (same order as
        /// <paramref name="parties"/>). The shares sum to 1. <paramref name="incumbentIndex"/> is -1 when nobody governs.
        /// </summary>
        public static void Shares(Position voter, float avgHappiness, IReadOnlyList<Party> parties, int incumbentIndex, ModelConfig cfg, float[] sharesOut)
        {
            if (parties == null) throw new ArgumentNullException(nameof(parties));
            if (sharesOut == null || sharesOut.Length < parties.Count)
                throw new ArgumentException("sharesOut must have room for every party.", nameof(sharesOut));

            int n = parties.Count;
            float bonus = IncumbentBonus(avgHappiness, cfg);

            // Softmax, shifted by the largest utility so exp() never overflows.
            float max = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                float u = -cfg.DistanceWeight * voter.DistanceTo(parties[i].Position);
                if (i == incumbentIndex) u += bonus;
                sharesOut[i] = u;
                if (u > max) max = u;
            }

            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double e = Math.Exp(sharesOut[i] - max);
                sharesOut[i] = (float)e;
                sum += e;
            }

            for (int i = 0; i < n; i++)
                sharesOut[i] = (float)(sharesOut[i] / sum);
        }
    }
}
