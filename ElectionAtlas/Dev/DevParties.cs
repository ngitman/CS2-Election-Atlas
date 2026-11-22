#if DEV_TOOLS
using ElectionAtlas.Model;

namespace ElectionAtlas.Dev
{
    /// <summary>Party lineups for dev elections; shared with ElectionAtlas.Lab through the model library.</summary>
    internal static class DevParties
    {
        public static Party[] Default => SampleParties.Two();

        public static Party[] Get(int count) => SampleParties.Get(count);
    }
}
#endif
