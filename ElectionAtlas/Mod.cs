using ElectionAtlas.Systems;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace ElectionAtlas
{
    public sealed class Mod : IMod
    {
        public const string Id = "ElectionAtlas";

        public static readonly ILog Log = LogManager.GetLogger($"{Id}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static Setting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{Id} loading");

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                Log.Info($"Mod asset at {asset.path}");

            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(Id, Settings, new Setting(this));

            // The City Hall gate (phase 2) runs in the UI phase so it reacts while paused; it only counts
            // chunks every 32 frames. Election and poll systems (phases 3-5) will run on a long interval.
            updateSystem.UpdateAt<CityHallSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ElectionsUISystem>(SystemUpdatePhase.UIUpdate);

#if DEBUG
            // In-game spikes: one play session answers what phases 2-5 depend on. Never in Release builds.
            updateSystem.UpdateAt<Diagnostics.SpikeLoggerSystem>(SystemUpdatePhase.GameSimulation);
            if (Settings.SaveProbe)
            {
                updateSystem.UpdateAt<Diagnostics.SpikeSaveProbeSystem>(SystemUpdatePhase.GameSimulation);
                Log.Info("Save probe (spike 3) enabled");
            }
#endif

            Log.Info($"{Id} loaded");
        }

        public void OnDispose()
        {
            Log.Info($"{Id} disposing");
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
