namespace ElectionAtlas.Model
{
    /// <summary>
    /// Tiny seeded random generator. Used instead of System.Random so a seed gives the same numbers
    /// in the game (Unity Mono) and in the tests (.NET 8).
    /// </summary>
    internal struct SplitMix64
    {
        private ulong m_State;

        public SplitMix64(ulong seed)
        {
            m_State = seed;
        }

        public ulong Next()
        {
            ulong z = m_State += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));
    }
}
