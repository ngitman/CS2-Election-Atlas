namespace ElectionAtlas.Model
{
    /// <summary>
    /// District colors for the results map: the winner's color, faded toward <see cref="Base"/> when the race
    /// was close. A margin of 0 shows at <see cref="MinStrength"/>; <see cref="FullMargin"/> or more shows the
    /// party's own color.
    /// </summary>
    public sealed class MapTint
    {
        /// <summary>The neutral color close races fade toward (0xRRGGBB).</summary>
        public uint Base = 0xF0F0F0;

        /// <summary>Color for a district where nobody voted.</summary>
        public uint NoResult = 0x8A8A8A;

        /// <summary>How much of the party color a dead heat shows, 0 to 1.</summary>
        public float MinStrength = 0.25f;

        /// <summary>The winning margin (share points, 0 to 1) at which the party color shows at full strength.</summary>
        public float FullMargin = 0.30f;

        public uint DistrictColor(int winnerIndex, uint winnerColor, float margin)
        {
            if (winnerIndex < 0)
                return NoResult;

            float t = FullMargin > 0f ? margin / FullMargin : 1f;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            float strength = MinStrength + (1f - MinStrength) * t;
            return Blend(Base, winnerColor, strength);
        }

        /// <summary>Per-channel blend from <paramref name="from"/> (strength 0) to <paramref name="to"/> (strength 1).</summary>
        public static uint Blend(uint from, uint to, float strength)
        {
            uint r = Channel(from >> 16, to >> 16, strength);
            uint g = Channel(from >> 8, to >> 8, strength);
            uint b = Channel(from, to, strength);
            return (r << 16) | (g << 8) | b;
        }

        private static uint Channel(uint from, uint to, float strength)
        {
            float a = from & 0xFF;
            float b = to & 0xFF;
            float v = a + (b - a) * strength;
            return (uint)(v + 0.5f) & 0xFF;
        }
    }
}
