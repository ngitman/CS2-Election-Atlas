namespace ElectionAtlas.Model
{
    /// <summary>Test party lineups for dev elections, ElectionAtlas.Lab and replay tests, until the party editor exists.</summary>
    public static class SampleParties
    {
        public static Party[] Two()
        {
            return new[]
            {
                new Party("left", "Left", 0xCC3333, new Position(-0.6f, -0.4f)),
                new Party("right", "Right", 0x3355CC, new Position(0.6f, 0.4f)),
            };
        }

        public static Party[] Four()
        {
            return new[]
            {
                new Party("greens", "Greens", 0x2E9E5B, new Position(-0.4f, -0.8f)),
                new Party("labour", "Labour", 0xCC3333, new Position(-0.6f, 0.1f)),
                new Party("liberals", "Liberals", 0xE5A50A, new Position(0.4f, -0.4f)),
                new Party("conservatives", "Conservatives", 0x3355CC, new Position(0.6f, 0.5f)),
            };
        }

        /// <summary>2 or 4 parties; anything else gives 2.</summary>
        public static Party[] Get(int count) => count == 4 ? Four() : Two();
    }
}
