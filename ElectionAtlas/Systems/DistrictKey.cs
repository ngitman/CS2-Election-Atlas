using Unity.Entities;

namespace ElectionAtlas.Systems
{
    /// <summary>
    /// District ids as stored in snapshots and polls: the district entity's version and index packed into a long,
    /// so results survive in the model (which knows nothing about entities) and can be matched back to the map.
    /// The only place that packs or unpacks them.
    /// </summary>
    internal static class DistrictKey
    {
        public static long Of(Entity district) => ((long)district.Version << 32) | (uint)district.Index;

        public static Entity ToEntity(long id) => new Entity { Index = (int)(uint)(id & 0xFFFFFFFFL), Version = (int)(id >> 32) };
    }
}
