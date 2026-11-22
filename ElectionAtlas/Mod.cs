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

            // The City Hall gate runs in the UI phase so it reacts while paused; it only counts chunks every
            // 32 frames. The election calendar runs with the simulation (no month ticks while paused).
            updateSystem.UpdateAt<CityHallSystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ElectionSystem>(SystemUpdatePhase.GameSimulation);
            // Always registered; the "Save election data" setting decides at save time whether the data is written.
            updateSystem.UpdateAt<ElectionStoreSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ElectionsUISystem>(SystemUpdatePhase.UIUpdate);
            // "Election" section in the game's own info panel when a district is selected in the city.
            updateSystem.UpdateAt<DistrictElectionSection>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<ElectionMapSystem>(SystemUpdatePhase.UIUpdate);

#if DEV_TOOLS
            // Developer tools: never in Release builds (DEV_TOOLS is off there, and publishing refuses a build with it).
            Log.Info("Developer tools are compiled in (DEV_TOOLS)");
            updateSystem.UpdateAt<Dev.DevCommandsSystem>(SystemUpdatePhase.UIUpdate);
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
