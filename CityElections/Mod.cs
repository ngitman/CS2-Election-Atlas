using CityElections.Systems;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CityElections
{
    public sealed class Mod : IMod
    {
        public const string Id = "CityElections";

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

            // UI bindings only. Election and poll systems arrive in phases 2-5 and will run
            // on a long update interval, never every frame (see "Keeping it lightweight").
            updateSystem.UpdateAt<ElectionsUISystem>(SystemUpdatePhase.UIUpdate);

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
