#if DEV_TOOLS
using Colossal.Serialization.Entities;
using Game;

namespace ElectionAtlas.Diagnostics
{
    /// <summary>
    /// Spike 3: does the game's serializer save and load a system that a mod adds? Writes a counter on every
    /// save and logs what it reads back. Opt-in (Options > Election Atlas > Debug) because a save written with
    /// the probe may not load cleanly once the probe is gone: only use it on a copy of a city.
    /// </summary>
    public partial class SpikeSaveProbeSystem : GameSystemBase, IDefaultSerializable, ISerializable
    {
        private const int kVersion = 1;

        private int m_Counter;
        private bool m_Deserialized;

        protected override void OnCreate()
        {
            base.OnCreate();
            SpikeLoggerSystem.Log.Info("[spike 3] save probe created");
        }

        protected override void OnUpdate()
        {
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode == GameMode.Game)
                SpikeLoggerSystem.Log.Info($"[spike 3] after load: counter={m_Counter} deserialized={m_Deserialized} (purpose={purpose})");
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            m_Counter++;
            writer.Write(kVersion);
            writer.Write(m_Counter);
            SpikeLoggerSystem.Log.Info($"[spike 3] Serialize called: wrote counter={m_Counter}");
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out int counter);
            m_Counter = counter;
            m_Deserialized = true;
            SpikeLoggerSystem.Log.Info($"[spike 3] Deserialize called: version={version} counter={counter}");
        }

        public void SetDefaults(Context context)
        {
            m_Counter = 0;
            m_Deserialized = false;
            SpikeLoggerSystem.Log.Info("[spike 3] SetDefaults called (new city, or a save without probe data)");
        }
    }
}
#endif
